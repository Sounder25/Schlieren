using System.Collections.Concurrent;
using Scrutor.Core.Execution;
using Scrutor.Core.Models;

namespace Scrutor.Core.State;

public interface IBlockStore
{
    void AddBlock(Block block);
    Block? GetBlockByNumber(ulong number);
    Block? GetBlockByHash(string hash);
    void AddReceipt(TransactionReceipt receipt);
    TransactionReceipt? GetReceiptByHash(string transactionHash);
    IEnumerable<TransactionReceipt> GetReceiptsByBlockNumber(ulong blockNumber);
    IEnumerable<TransactionReceipt> GetAllReceipts();
    IEnumerable<Block> GetAllBlocks();
    void AddTrace(string transactionHash, List<ExecutionTraceStep> trace);
    List<ExecutionTraceStep>? GetTraceByHash(string transactionHash);
}

public sealed class BlockStore : IBlockStore
{
    private readonly ConcurrentDictionary<ulong, Block> _blocksByNumber = new();
    private readonly ConcurrentDictionary<string, Block> _blocksByHash = new();
    private readonly ConcurrentDictionary<string, TransactionReceipt> _receiptsByHash = new();
    private readonly ConcurrentDictionary<ulong, ConcurrentBag<TransactionReceipt>> _receiptsByBlockNumber = new();

    public void AddBlock(Block block)
    {
        _blocksByNumber[block.Number] = block;
        if (!string.IsNullOrEmpty(block.Hash))
        {
            _blocksByHash[block.Hash] = block;
        }
    }

    public Block? GetBlockByNumber(ulong number) => 
        _blocksByNumber.TryGetValue(number, out var block) ? block : null;

    public Block? GetBlockByHash(string hash) => 
        _blocksByHash.TryGetValue(hash, out var block) ? block : null;

    public void AddReceipt(TransactionReceipt receipt)
    {
        _receiptsByHash[receipt.TransactionHash] = receipt;
        _receiptsByBlockNumber.GetOrAdd(receipt.BlockNumber, _ => new ConcurrentBag<TransactionReceipt>()).Add(receipt);
    }

    public TransactionReceipt? GetReceiptByHash(string transactionHash) =>
        _receiptsByHash.TryGetValue(transactionHash, out var receipt) ? receipt : null;

    public IEnumerable<TransactionReceipt> GetReceiptsByBlockNumber(ulong blockNumber) =>
        _receiptsByBlockNumber.TryGetValue(blockNumber, out var receipts) ? receipts : Enumerable.Empty<TransactionReceipt>();

    public IEnumerable<TransactionReceipt> GetAllReceipts() => _receiptsByHash.Values;
    public IEnumerable<Block> GetAllBlocks() => _blocksByNumber.Values;

    private readonly ConcurrentDictionary<string, List<ExecutionTraceStep>> _tracesByHash = new();

    public void AddTrace(string transactionHash, List<ExecutionTraceStep> trace)
        {
            if (trace.Count > 0)
            {
                var key = transactionHash.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? transactionHash.ToLowerInvariant()
                    : "0x" + transactionHash.ToLowerInvariant();
                _tracesByHash[key] = trace;
            }
        }

    public List<ExecutionTraceStep>? GetTraceByHash(string transactionHash)
    {
        var key = transactionHash.StartsWith("0x", StringComparison.OrdinalIgnoreCase) 
            ? transactionHash.ToLowerInvariant() 
            : "0x" + transactionHash.ToLowerInvariant();
        return _tracesByHash.TryGetValue(key, out var trace) ? trace : null;
    }
}
