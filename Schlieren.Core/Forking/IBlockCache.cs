using Schlieren.Core.Models;

namespace Schlieren.Core.Forking;

public interface IBlockCache
{
    bool TryGetBlock(ulong number, out Block? block);
    void CacheBlock(Block block);
}
