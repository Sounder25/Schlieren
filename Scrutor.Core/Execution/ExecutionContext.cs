using System.Numerics;
using System.Linq;
using Scrutor.Core.Primitives;
using Scrutor.Core.Models;
using Scrutor.Core.State;

namespace Scrutor.Core.Execution
{
    /// <summary>
    /// Execution context for opcode execution
    /// </summary>
    public sealed class ExecutionContext
    {
        public EvmStack Stack { get; } = new();
        public EvmMemory Memory { get; } = new();
        public IEvmStorage Storage { get; init; } = new InMemoryStorage();
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
        public ulong GasUsed { get; set; }
        public ulong GasLimit { get; init; } = 30_000_000;
        public bool IsStatic { get; init; }
        public bool CaptureTrace { get; init; }
        public int CallDepth { get; init; } = 1;
        public byte[] Code { get; init; } = Array.Empty<byte>();
        public int ProgramCounter { get; set; }
        public List<ExecutionTraceStep> TraceSteps { get; } = new();
        private readonly Dictionary<string, string> _traceStorage = new(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>
        /// Callback to execute a sub-call (internal transaction).
        /// Args: Transaction, isStatic, creationAddress (if CREATE), codeAddress (if DELEGATECALL/CALLCODE)
        /// </summary>
        public Func<Transaction, bool, Address?, Address?, Task<ExecutionResult>>? SubCall { get; set; }

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
