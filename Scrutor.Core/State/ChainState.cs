using Scrutor.Core.Models;
using Scrutor.Core.Configuration;

namespace Scrutor.Core.State;

public class ChainState : IChainState
{
    private readonly object _lock = new();
    private readonly IBlockStore _blockStore;
    private Block _head;
    
    public ChainState(NodeConfiguration config, IBlockStore blockStore)
    {
        _blockStore = blockStore;
        ChainId = config.ChainId;
        _head = new Block 
        { 
            Number = 0, 
            Timestamp = config.GenesisTimestamp ?? (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            GasLimit = config.GasLimit,
            Difficulty = System.Numerics.BigInteger.Zero,
            Hash = "0x" + Convert.ToHexString(new byte[32]).ToLowerInvariant()
        };
        _blockStore.AddBlock(_head);
    }

    public ChainState(ulong chainId, IBlockStore blockStore)
    {
        _blockStore = blockStore;
        ChainId = chainId;
        _head = new Block
        {
            Number = 0,
            Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            GasLimit = 30_000_000,
            Difficulty = System.Numerics.BigInteger.Zero,
            Hash = "0x" + Convert.ToHexString(new byte[32]).ToLowerInvariant()
        };
        _blockStore.AddBlock(_head);
    }

    public ulong ChainId { get; }
    public long TimeOffset { get; set; }
    public ulong? NextBlockTimestamp { get; set; }

    public IBlockStore BlockStore => _blockStore;

    public Block CurrentBlock
    {
        get { lock(_lock) return _head; }
    }

    public void UpdateHead(Block block)
    {
        lock(_lock)
        {
            if (block.Number > _head.Number)
            {
                _head = block;
                _blockStore.AddBlock(block);
            }
        }
    }
}
