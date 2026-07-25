using System.Numerics;
using System.Linq;
using Scrutor.Core.Primitives;
using Scrutor.Core.Models;
using Scrutor.Core.State;

namespace Scrutor.Core.Execution
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

        /// <summary>Pre-warm an address (charges the 2400 cost externally via EIP-2930 intrinsic gas).</summary>
        public void WarmAddress(Address address) => _warmAddresses.Add(address);

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
        /// <summary>
        /// When set, the EVM will record per-opcode (op, gasCost) into this frame journal.
        /// Used by the gas causality tree — lighter-weight than full CaptureTrace.
        /// </summary>
        public GasFrameNode? GasFrame { get; init; }
        public int CallDepth { get; init; } = 1;
        public byte[] Code { get; init; } = Array.Empty<byte>();
        public int ProgramCounter { get; set; }
        public List<ExecutionTraceStep> TraceSteps { get; } = new();
        private readonly Dictionary<string, string> _traceStorage = new(StringComparer.OrdinalIgnoreCase);
        private HashSet<int>? _validJumpDestinations;
        
        /// <summary>
        /// Callback to execute a sub-call (internal transaction).
        /// Args: Transaction, isStatic, creationAddress (if CREATE), codeAddress (if DELEGATECALL/CALLCODE)
        /// </summary>
        public Func<Transaction, bool, Address?, Address?, Task<ExecutionResult>>? SubCall { get; set; }
        public Func<Address, BigInteger, BigInteger>? TransientLoad { get; init; }
        public Action<Address, BigInteger, BigInteger>? TransientStore { get; init; }

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

        public void ConsumeGas(ulong amount)
        {
            GasUsed += amount;
            if (GasUsed > GasLimit)
                throw new EvmOutOfGasException($"Out of gas: used {GasUsed}, limit {GasLimit}");
        }

        public void RefundGas(ulong amount)
        {
            if (amount > GasUsed) GasUsed = 0; // Should ideally not happen if logic is correct
            else GasUsed -= amount;
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

        public void AddTraceStep(int pc, string op, ulong gasBefore, ulong gasCost)
        {
            if (!CaptureTrace) return;

            var stack = Stack.SnapshotTopFirst()
                .Select(v => "0x" + v.ToString("x"))
                .ToList();

            TraceSteps.Add(new ExecutionTraceStep
            {
                Pc = pc,
                Op = op,
                Gas = $"0x{gasBefore:x}",
                GasCost = $"0x{gasCost:x}",
                Depth = CallDepth,
                Stack = stack,
                Memory = Memory.SnapshotWordsHex(),
                Storage = new Dictionary<string, string>(_traceStorage, StringComparer.OrdinalIgnoreCase)
            });
        }

        public void TraceStorageRead(BigInteger key, BigInteger value)
        {
            if (!CaptureTrace) return;
            _traceStorage[ToWordHex(key)] = ToWordHex(value);
        }

        public void TraceStorageWrite(BigInteger key, BigInteger value)
        {
            if (!CaptureTrace) return;
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
