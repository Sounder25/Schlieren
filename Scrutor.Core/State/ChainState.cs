using Scrutor.Core.Models;
using Scrutor.Core.Configuration;

namespace Scrutor.Core.State;

public class ChainState : IChainState
{
    private readonly object _lock = new();
    private readonly IBlockStore _blockStore;
    private Block _head;
    private bool _automine;
    private int? _blockTimeSeconds;
    
    public ChainState(NodeConfiguration config, IBlockStore blockStore)
    {
        _blockStore = blockStore;
        ChainId = config.ChainId;
        _automine = config.Automine;
        _blockTimeSeconds = config.BlockTime.HasValue && config.BlockTime.Value > 0
            ? config.BlockTime.Value
            : null;
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
        _automine = true;
        _blockTimeSeconds = null;
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
    public bool Automine
    {
        get { lock (_lock) return _automine; }
        set { lock (_lock) _automine = value; }
    }
    public int? BlockTimeSeconds
    {
        get { lock (_lock) return _blockTimeSeconds; }
        set
        {
            lock (_lock)
            {
                _blockTimeSeconds = value.HasValue && value.Value > 0 ? value.Value : null;
            }
        }
    }
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
