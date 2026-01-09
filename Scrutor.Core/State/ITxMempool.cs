using Scrutor.Core.Primitives;

namespace Scrutor.Core.State;

public interface ITxMempool
{
    void Add(Transaction tx);
    Transaction? PeekBest();
    Transaction? PopBest();
    ulong GetNextNonce(Address address, ulong currentNonce);
    ValueTask<ulong> ReserveNonceAsync(Address from, ulong currentChainNonce);
    void Clear();
    int Count { get; }
}
