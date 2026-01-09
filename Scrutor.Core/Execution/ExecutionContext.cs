using System.Numerics;
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
        public byte[] Code { get; init; } = Array.Empty<byte>();
        public int ProgramCounter { get; set; }
        
        /// <summary>
        /// Callback to execute a sub-call (internal transaction).
        /// Args: Transaction, isStatic, creationAddress (if CREATE)
        /// </summary>
        public Func<Transaction, bool, Address?, Task<ExecutionResult>>? SubCall { get; set; }

        public void ConsumeGas(ulong amount)
        {
            GasUsed += amount;
            if (GasUsed > GasLimit)
                throw new EvmOutOfGasException($"Out of gas: used {GasUsed}, limit {GasLimit}");
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
