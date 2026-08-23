using System.Numerics;
using System.Linq;
using Schlieren.Core.Primitives;
using Schlieren.Core.Models;
using Schlieren.Core.State;
using Schlieren.Core.Execution.Journal;

namespace Schlieren.Core.Execution
{
    /// <summary>
    /// EIP-2929 warm/cold storage access tracker.
    /// Addresses and storage slots are "warm" when they have been touched this transaction.
    /// Pre-warmed via tx.from, tx.to, and the EIP-2930 access list before execution starts.
    /// </summary>
    public sealed class AccessTracker
    {
        private readonly HashSet<Address> _warmAddresses = new();
        private readonly Dictionary<Address, HashSet<BigInteger>> _warmSlots = new();

        public sealed record Checkpoint(
            HashSet<Address> WarmAddresses,
            Dictionary<Address, HashSet<BigInteger>> WarmSlots);

        public Checkpoint CreateCheckpoint() =>
            new(
                new HashSet<Address>(_warmAddresses),
                _warmSlots.ToDictionary(
                    entry => entry.Key,
                    entry => new HashSet<BigInteger>(entry.Value)));

        public void Restore(Checkpoint checkpoint)
        {
            _warmAddresses.Clear();
            _warmAddresses.UnionWith(checkpoint.WarmAddresses);

            _warmSlots.Clear();
            foreach (var (address, slots) in checkpoint.WarmSlots)
            {
                _warmSlots[address] = new HashSet<BigInteger>(slots);
            }
        }

        /// <summary>Pre-warm an address (charges the 2400 cost externally via EIP-2930 intrinsic gas).</summary>
        public void WarmAddress(Address address) => _warmAddresses.Add(address);
        public bool IsWarm(Address address) => _warmAddresses.Contains(address);

        /// <summary>Pre-warm a specific storage slot (charges the 1900 cost externally via EIP-2930 intrinsic gas).</summary>
        public void WarmSlot(Address address, BigInteger slot)
        {
            _warmAddresses.Add(address);
            if (!_warmSlots.TryGetValue(address, out var slots))
            {
                slots = new HashSet<BigInteger>();
                _warmSlots[address] = slots;
            }
            slots.Add(slot);
        }

        /// <summary>
        /// Returns whether an address is warm, then marks it warm.
        /// Cold first access returns false; subsequent accesses return true.
        /// </summary>
        public bool TouchAddress(Address address)
        {
            return !_warmAddresses.Add(address); // Add returns false if already present
        }

        /// <summary>
        /// Returns whether a storage slot is warm, then marks it warm.
        /// Cost: 2100 (cold) or 100 (warm) for SLOAD; similar for SSTORE.
        /// </summary>
        public bool TouchSlot(Address address, BigInteger slot)
        {
            _warmAddresses.Add(address);
            if (!_warmSlots.TryGetValue(address, out var slots))
            {
                slots = new HashSet<BigInteger>();
                _warmSlots[address] = slots;
            }
            return !slots.Add(slot); // Add returns false if already present → cold
        }
    }

    /// <summary>
    /// Execution context for opcode execution
    /// </summary>
    public sealed class ExecutionContext
    {
        /// <summary>
        /// Unique execution ID for diagnostic tracing.
        /// </summary>
        public string ExecutionId { get; init; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        
        public EvmStack Stack { get; } = new();
        public EvmMemory Memory { get; } = new();
        public IEvmStorage Storage { get; init; } = new InMemoryStorage();

        /// <summary>
        /// [AI-EDIT 2026-01-10] EIP-2929 access tracker — shared across the entire top-level
        /// transaction (including sub-calls). Must be set by StateTransition before execution.
        /// </summary>
        public AccessTracker Access { get; init; } = new();
        
        /// <summary>
        /// [AI-EDIT 2026-07-24] EIP-2200 original storage values snapshot — tracks storage slot
        /// values at transaction start for proper net-metering refund calculations. Shared across
        /// entire transaction (including sub-calls). Populated lazily on first SLOAD/SSTORE per slot.
        /// </summary>
        public Dictionary<(Address, BigInteger), BigInteger> OriginalStorageValues { get; init; } = new();
        
        public List<TransactionLog> Logs { get; } = new();
        
        public IGlobalState State { get; set; } = default!;
        
        public IGlobalState GlobalState
        {
            get => State;
            set => State = value;
        }

        public BlockContext Block { get; init; } = BlockContext.Genesis;
        public Address Caller { get; init; }
        public Address Origin { get; init; }
        public BigInteger GasPrice { get; init; }
        public BigInteger CallValue { get; init; }
        public byte[] CallData { get; init; } = Array.Empty<byte>();
        public IReadOnlyList<byte[]> BlobVersionedHashes { get; init; } =
            Array.Empty<byte[]>();
        public byte[] LastReturnData { get; set; } = Array.Empty<byte>();
        public Address ContractAddress { get; init; }
        
        /// <summary>
        /// Storage owner address — determines which account's storage is accessed by SLOAD/SSTORE.
        /// For CALL/STATICCALL: equals ContractAddress (callee's storage).
        /// For DELEGATECALL/CALLCODE: equals caller's address (caller's storage).
        /// </summary>
        public Address StorageAddress { get; init; }
        public ulong GasUsed { get; set; }
        public ulong GasLimit { get; init; } = 30_000_000;
        /// <summary>
        /// EIP-3529 gas refund counter. Accumulated during SSTORE and applied post-execution.
        /// </summary>
        public long GasRefundCounter { get; set; }
        public bool IsStatic { get; init; }
        public bool CaptureTrace { get; init; }
        public ExecutionJournal? Journal { get; init; }
        public long? JournalFrameId { get; init; }
        public long? JournalParentFrameId { get; init; }
        public long? CurrentInstructionId { get; internal set; }
        public int CallDepth { get; init; } = 1;
        public byte[] Code { get; init; } = Array.Empty<byte>();
        public int ProgramCounter { get; set; }
        public List<ExecutionTraceStep> TraceSteps { get; } = new();
        private readonly Dictionary<string, string> _traceStorage = new(StringComparer.OrdinalIgnoreCase);
        private HashSet<int>? _validJumpDestinations;
        
        // Security analysis tracking fields
        private CallType? _callType;
        private Address? _callerAddress;
        private Address? _codeAddress;
        private int? _activeOpcodePc;
        private byte? _activeOpcode;
        private string? _activeOpcodeName;

        internal void SetActiveOpcode(int pc, byte opcode, string name)
        {
            _activeOpcodePc = pc;
            _activeOpcode = opcode;
            _activeOpcodeName = name;
        }

        internal void ClearActiveOpcode()
        {
            _activeOpcodePc = null;
            _activeOpcode = null;
            _activeOpcodeName = null;
        }

        internal JournalMachineSnapshot CaptureJournalMachineState(
            IReadOnlyList<BigInteger>? preStack,
            string opcode)
        {
            var stack = (preStack ?? Stack.SnapshotTopFirst())
                .Select(value => "0x" + value.ToString("x"))
                .ToArray();
            var memory = Memory.SnapshotWordsHex().ToArray();
            var storage = new Dictionary<string, string>(
                _traceStorage,
                StringComparer.OrdinalIgnoreCase);
            var output = IsCallLikeOp(opcode) && LastReturnData.Length > 0
                ? LastReturnData.ToArray()
                : Array.Empty<byte>();

            return new JournalMachineSnapshot(
                stack,
                memory,
                storage,
                output,
                _callType,
                ContractAddress == Address.Zero ? null : ContractAddress.ToString(),
                _callerAddress?.ToString(),
                _codeAddress?.ToString());
        }
        
        /// <summary>
        /// Set the call context for security analysis. Called when entering a new call frame.
        /// </summary>
        public void SetCallContext(CallType callType, Address? caller = null, Address? codeAddress = null)
        {
            _callType = callType;
            _callerAddress = caller;
            _codeAddress = codeAddress;
        }
        
        /// <summary>
        /// Callback to execute a sub-call (internal transaction).
        /// Args: Transaction, callType, isStatic, creationAddress (if CREATE), codeAddress (if DELEGATECALL/CALLCODE)
        /// </summary>
        public Func<Transaction, CallType, bool, Address?, Address?, Task<ExecutionResult>>? SubCall { get; set; }

        internal void ResolveCreatedFrame(ExecutionResult result, bool committed)
        {
            if (result.IsSuccess)
            {
                Journal?.ResolveFrame(
                    result.JournalFrameId,
                    JournalFrameId,
                    committed ? FrameStateResolution.Commit : FrameStateResolution.Rollback);
            }
        }
        public Func<Address, BigInteger, BigInteger>? TransientLoad { get; init; }
        public Action<Address, BigInteger, BigInteger>? TransientStore { get; init; }

        /// <summary>
        /// Set by StateTransition before a CREATE sub-call is dispatched.
        /// The opcode calls this when EIP-170 / deposit-OOG / EIP-3541 fires to
        /// roll back any transient-storage writes from the failed CREATE frame.
        /// Reset to null after each sub-call.
        /// </summary>
        public Action? RollbackLastCreateTransient { get; set; }

        /// <summary>
        /// Set by StateTransition before a CREATE sub-call is dispatched.
        /// The opcode calls this on the CREATE success path to propagate
        /// transient-storage writes from the staging overlay to the parent frame.
        /// Reset to null after each sub-call.
        /// </summary>
        public Action? CommitLastCreateTransient { get; set; }

        /// <summary>
        /// EIP-6780: set of addresses created during this transaction.
        /// Shared across the entire call tree so sub-calls can register new contracts.
        /// SELFDESTRUCT only fully deletes an account if it appears in this set.
        /// </summary>
        public HashSet<Address> CreatedInThisTransaction { get; init; } = new();

        public bool IsValidJumpDestination(int destination)
        {
            if (destination < 0 || destination >= Code.Length)
            {
                return false;
            }

            _validJumpDestinations ??= BuildValidJumpDestinations();
            return _validJumpDestinations.Contains(destination);
        }

        private HashSet<int> BuildValidJumpDestinations()
        {
            var destinations = new HashSet<int>();
            var index = 0;

            while (index < Code.Length)
            {
                var op = Code[index];
                if (op == 0x5B)
                {
                    destinations.Add(index);
                    index++;
                    continue;
                }

                if (op is >= 0x60 and <= 0x7F)
                {
                    var pushBytes = op - 0x5F;
                    index += 1 + pushBytes;
                    continue;
                }

                index++;
            }

            return destinations;
        }

        public void ConsumeGas(
            ulong amount,
            GasSemantics semantics = GasSemantics.ExclusiveCharge,
            string? component = null,
            GasComponentScope scope = GasComponentScope.Opcode)
        {
            GasUsed += amount;
            if (GasUsed > GasLimit)
                throw new EvmOutOfGasException($"Out of gas: used {GasUsed}, limit {GasLimit}");
            if (component is not null)
                RecordGasComponent(scope, component, amount, semantics);
        }

        public void RefundGas(
            ulong amount,
            GasSemantics semantics = GasSemantics.Return,
            string? component = null,
            GasComponentScope scope = GasComponentScope.Opcode)
        {
            if (amount > GasUsed)
            {
                // [DIAGNOSTIC] Over-refund masks a gas-accounting bug (the caller
                // tried to give back more than the frame has consumed). Behavior is
                // preserved (clamp to 0) so existing conformance stays green, but the
                // event is surfaced because it should never legitimately occur.
                Console.Error.WriteLine(
                    $"[OVER_REFUND] amount={amount} gasUsed={GasUsed} " +
                    $"opcode-frame contract={ContractAddress} depth={CallDepth} gasLimit={GasLimit}");
                GasUsed = 0;
            }
            else
            {
                GasUsed -= amount;
            }
            if (component is not null)
                RecordGasComponent(scope, component, amount, semantics);
        }

        internal void RecordGasComponent(
            GasComponentScope scope,
            string component,
            ulong amount,
            GasSemantics semantics)
        {
            if (Journal is not { } journal || amount == 0)
                return;

            journal.Record(new GasComponentEvent
            {
                InstructionId = CurrentInstructionId,
                FrameId = JournalFrameId,
                ParentFrameId = JournalParentFrameId,
                Scope = scope,
                Component = component,
                Amount = amount,
                Semantics = semantics,
                Pc = _activeOpcodePc,
                Opcode = _activeOpcode,
                OpcodeName = _activeOpcodeName
            });
        }

        internal void RecordStorageRead(BigInteger slot, BigInteger value, bool isWarm)
        {
            if (Journal is not { } journal || JournalFrameId is null)
                return;
            journal.Record(new StorageReadEvent
            {
                Scope = StateEffectScope.Frame,
                FrameId = JournalFrameId,
                ParentFrameId = JournalParentFrameId,
                InstructionId = CurrentInstructionId,
                Pc = _activeOpcodePc,
                Opcode = _activeOpcode,
                StorageAddress = StorageAddress,
                Slot = slot,
                Value = value,
                IsWarm = isWarm
            });
        }

        internal void RecordStorageWrite(
            BigInteger slot,
            BigInteger original,
            BigInteger previous,
            BigInteger value,
            bool isWarm)
        {
            if (Journal is not { } journal || JournalFrameId is null)
                return;
            journal.Record(new StorageWriteEvent
            {
                Scope = StateEffectScope.Frame,
                FrameId = JournalFrameId,
                ParentFrameId = JournalParentFrameId,
                InstructionId = CurrentInstructionId,
                Pc = _activeOpcodePc,
                Opcode = _activeOpcode,
                StorageAddress = StorageAddress,
                Slot = slot,
                OriginalValue = original,
                PreviousValue = previous,
                Value = value,
                IsWarm = isWarm
            });
        }

        internal void RecordTransientStorageRead(BigInteger slot, BigInteger value)
        {
            if (Journal is not { } journal || JournalFrameId is null)
                return;
            journal.Record(new TransientStorageReadEvent
            {
                Scope = StateEffectScope.Frame,
                FrameId = JournalFrameId,
                ParentFrameId = JournalParentFrameId,
                InstructionId = CurrentInstructionId,
                Pc = _activeOpcodePc,
                Opcode = _activeOpcode,
                StorageAddress = StorageAddress,
                Slot = slot,
                Value = value
            });
        }

        internal void RecordTransientStorageWrite(BigInteger slot, BigInteger previous, BigInteger value)
        {
            if (Journal is not { } journal || JournalFrameId is null)
                return;
            journal.Record(new TransientStorageWriteEvent
            {
                Scope = StateEffectScope.Frame,
                FrameId = JournalFrameId,
                ParentFrameId = JournalParentFrameId,
                InstructionId = CurrentInstructionId,
                Pc = _activeOpcodePc,
                Opcode = _activeOpcode,
                StorageAddress = StorageAddress,
                Slot = slot,
                PreviousValue = previous,
                Value = value
            });
        }

        public BigInteger LoadTransientStorage(BigInteger key)
        {
            if (TransientLoad is null)
            {
                return BigInteger.Zero;
            }

            return TransientLoad(ContractAddress, key);
        }

        public void StoreTransientStorage(BigInteger key, BigInteger value)
        {
            TransientStore?.Invoke(ContractAddress, key, value);
        }

        /// <summary>
        /// Records one EIP-3155 structLog step.
        ///
        /// EELS trace semantics (ethereum/trace.py + fork interpreter):
        ///   evm_trace(evm, OpStart(op))   ← snapshot BEFORE opcode executes
        ///   op_implementation[op](evm)
        ///   evm_trace(evm, OpEnd())
        ///
        /// So <paramref name="preStack"/> must be captured **before** calling
        /// <see cref="IOpcode.ExecuteAsync"/> — pass <c>Stack.SnapshotTopFirst()</c>
        /// taken before execution. When <paramref name="preStack"/> is null (legacy callers),
        /// the stack is snapshotted at call time (post-execution, less accurate).
        /// </summary>
        public void AddTraceStep(int pc, string op, ulong gasBefore, ulong gasCost,
            IReadOnlyList<BigInteger>? preStack = null)
        {
            if (!CaptureTrace) return;

            // Prefer the pre-execution snapshot (OpStart semantics). Fall back to
            // current stack if not provided (backwards-compat with existing callers).
            var stackItems = (preStack ?? Stack.SnapshotTopFirst())
                .Select(v => "0x" + v.ToString("x"))
                .ToList();

            TraceSteps.Add(new ExecutionTraceStep
            {
                Pc = pc,
                Op = op,
                Gas = $"0x{gasBefore:x}",
                GasCost = $"0x{gasCost:x}",
                Depth = CallDepth,
                Stack = stackItems,
                Memory = Memory.SnapshotWordsHex(),
                Storage = new Dictionary<string, string>(_traceStorage, StringComparer.OrdinalIgnoreCase),
                // Security analysis fields
                CallType = _callType,
                ContractAddress = ContractAddress == Address.Zero ? null : ContractAddress.ToString(),
                CallerAddress = _callerAddress?.ToString(),
                CodeAddress = _codeAddress?.ToString(),
                OutputData = IsCallLikeOp(op) && LastReturnData is { Length: > 0 }
                    ? LastReturnData.ToArray()
                    : null
            });
        }

        private static bool IsCallLikeOp(string op) =>
            op is "CALL" or "STATICCALL" or "DELEGATECALL" or "CALLCODE" or "CREATE" or "CREATE2";

        public void TraceStorageRead(BigInteger key, BigInteger value)
        {
            if (!CaptureTrace && Journal is null) return;
            _traceStorage[ToWordHex(key)] = ToWordHex(value);
        }

        public void TraceStorageWrite(BigInteger key, BigInteger value)
        {
            if (!CaptureTrace && Journal is null) return;
            _traceStorage[ToWordHex(key)] = ToWordHex(value);
        }

        private static string ToWordHex(BigInteger value)
        {
            var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
            var padded = new byte[32];
            if (bytes.Length > 32) Array.Copy(bytes, bytes.Length - 32, padded, 0, 32);
            else Array.Copy(bytes, 0, padded, 32 - bytes.Length, bytes.Length);
            return "0x" + Convert.ToHexString(padded).ToLowerInvariant();
        }
    }

    internal sealed record JournalMachineSnapshot(
        IReadOnlyList<string> Stack,
        IReadOnlyList<string> Memory,
        IReadOnlyDictionary<string, string> Storage,
        IReadOnlyList<byte> Output,
        CallType? CallType,
        string? ContractAddress,
        string? CallerAddress,
        string? CodeAddress);

    /// <summary>
    /// Simple in-memory storage implementation
    /// </summary>
    internal sealed class InMemoryStorage : IEvmStorage
    {
        private readonly Dictionary<BigInteger, BigInteger> _storage = new();

        public ValueTask<BigInteger> LoadAsync(BigInteger key) => 
            new ValueTask<BigInteger>(_storage.TryGetValue(key, out var value) ? value : BigInteger.Zero);

        public void Store(BigInteger key, BigInteger value) => 
            _storage[key] = value;
    }
}
