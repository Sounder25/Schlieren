using System.Collections.Concurrent;
using Scrutor.Core.Models;

namespace Scrutor.Core.Forking;

public class BlockCache : IBlockCache
{
    private readonly ConcurrentDictionary<ulong, Block> _cache = new();

    public bool TryGetBlock(ulong number, out Block? block)
    {
        return _cache.TryGetValue(number, out block);
    }

    public void CacheBlock(Block block)
    {
        _cache.TryAdd(block.Number, block);
    }
}
