using Scrutor.Core.Primitives;

namespace Scrutor.Core.State;

public interface ITxMempool
{
    void Add(Transaction tx);
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
