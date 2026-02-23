using Scrutor.Core.Models;

namespace Scrutor.Core.Forking;

public interface IBlockCache
{
    bool TryGetBlock(ulong number, out Block? block);
    void CacheBlock(Block block);
}
