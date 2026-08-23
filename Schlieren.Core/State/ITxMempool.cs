using Schlieren.Core.Primitives;

namespace Schlieren.Core.State;

public interface ITxMempool
{
    /// <summary>Returns false if rejected (pool full, duplicate, or lost a same-nonce replacement).</summary>
    bool Add(Transaction tx);
    Transaction? PeekBest();
    Transaction? PopBest();
    /// <summary>Snapshot of currently pending transactions (for eth_getTransactionByHash).</summary>
    IReadOnlyList<Transaction> GetPending();
    ulong GetNextNonce(Address address, ulong currentNonce);
    ValueTask<ulong> ReserveNonceAsync(Address from, ulong currentChainNonce);
    void ResetReservation(Address from);
    void Clear();
    int Count { get; }
}
