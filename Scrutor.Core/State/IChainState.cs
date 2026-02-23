using Scrutor.Core.Models;

namespace Scrutor.Core.State;

public interface IChainState
{
    Block CurrentBlock { get; }
    IBlockStore BlockStore { get; }
    ulong ChainId { get; }
    long TimeOffset { get; set; }
    ulong? NextBlockTimestamp { get; set; }
    void UpdateHead(Block block);
}
