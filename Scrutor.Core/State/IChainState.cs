using Scrutor.Core.Models;

namespace Scrutor.Core.State;

public interface IChainState
{
    Block CurrentBlock { get; }
    IBlockStore BlockStore { get; }
    ulong ChainId { get; }
    bool Automine { get; set; }
    int? BlockTimeSeconds { get; set; }
    long TimeOffset { get; set; }
    ulong? NextBlockTimestamp { get; set; }
    void UpdateHead(Block block);
}
