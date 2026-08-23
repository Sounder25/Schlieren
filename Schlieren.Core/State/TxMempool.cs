using System.Collections.Concurrent;
using Schlieren.Core.Primitives;

namespace Schlieren.Core.State;

/// <summary>
/// Transaction Mempool with Priority Queue
/// Orders by GasPrice (Highest First)
/// </summary>
public sealed class TxMempool : ITxMempool
{
    private class AtomicNonce
    {
        public long Value;
        public AtomicNonce(long value) => Value = value;
    }

    private readonly ConcurrentDictionary<Address, ConcurrentDictionary<ulong, Transaction>> _pendingByAccount = new();
    private readonly ConcurrentDictionary<string, Transaction> _lookup = new();
    private readonly ConcurrentDictionary<Address, AtomicNonce> _reservedNonces = new();
    private readonly object _lock = new();
    private const int MaxMempoolSize = 5000;

    public TxMempool()
    {
    }

    public int Count => _lookup.Count;

    /// <summary>
    /// Attempts to add a transaction. Returns false if the pool is full, the transaction
    /// is a duplicate, or it loses a same-nonce replacement (needs a strictly higher gas
    /// price than the transaction it would replace) — callers that need to report a
    /// rejection back to their caller (e.g. eth_sendRawTransaction) should check this.
    /// </summary>
    public bool Add(Transaction tx)
    {
        var hashKey = Convert.ToHexString(tx.Hash);

        // Size/duplicate checks and the mutation itself all happen under one lock so a
        // concurrent Add can't slip past the cap or double-add the same hash (TOCTOU).
        lock (_lock)
        {
            if (_lookup.Count >= MaxMempoolSize) return false;
            if (_lookup.ContainsKey(hashKey)) return false;

            var accountTxs = _pendingByAccount.GetOrAdd(tx.From, _ => new ConcurrentDictionary<ulong, Transaction>());

            if (accountTxs.TryGetValue(tx.Nonce, out var existing))
            {
                // Same-nonce resubmission (speed-up). Require a strictly higher gas price;
                // otherwise reject outright. On acceptance, the old hash must be evicted from
                // _lookup too — leaving it there orphans it from _pendingByAccount bookkeeping,
                // and it would still get popped later and fail with NonceTooLow post-replacement.
                if (tx.GasPrice <= existing.GasPrice) return false;
                _lookup.TryRemove(Convert.ToHexString(existing.Hash), out _);
            }

            accountTxs[tx.Nonce] = tx;
            return _lookup.TryAdd(hashKey, tx);
        }
    }

    public Transaction? PeekBest()
    {
        lock (_lock)
        {
            // Only the lowest-nonce pending transaction per sender is eligible. Offering a
            // higher-nonce transaction while a lower one from the same sender is still
            // pending fails with NonceTooHigh on apply — and since the requeued transaction
            // keeps winning this comparison on every retry, that starves the eligible
            // lower-nonce transaction into a persistent livelock. Price ordering still
            // applies, but only across senders' respective front-of-queue transactions.
            return _lookup.Values
                .GroupBy(t => t.From)
                .Select(g => g.OrderBy(t => t.Nonce).First())
                .OrderByDescending(t => t.GasPrice)
                .ThenBy(t => t.Nonce)
                .FirstOrDefault();
        }
    }

    public IReadOnlyList<Transaction> GetPending()
    {
        lock (_lock)
        {
            return _lookup.Values.ToList();
        }
    }

    public Transaction? PopBest()
    {
        lock (_lock)
        {
            var best = PeekBest();
            if (best != null)
            {
                var hashKey = Convert.ToHexString(best.Hash);
                if (_lookup.TryRemove(hashKey, out _))
                {
                    if (_pendingByAccount.TryGetValue(best.From, out var accTxs))
                    {
                        accTxs.TryRemove(best.Nonce, out _);
                        if (accTxs.IsEmpty) _pendingByAccount.TryRemove(best.From, out _);
                    }
                    return best;
                }
            }
            return null;
        }
    }

    public ulong GetNextNonce(Address address, ulong currentNonce)
    {
        lock (_lock)
        {
            if (_pendingByAccount.TryGetValue(address, out var accTxs))
            {
                var maxInMempool = accTxs.Keys.Count > 0 ? accTxs.Keys.Max() : (ulong)0;
                return Math.Max(currentNonce, maxInMempool + 1);
            }
            return currentNonce;
        }
    }

    public ValueTask<ulong> ReserveNonceAsync(Address from, ulong currentChainNonce)
    {
        var wrapper = _reservedNonces.GetOrAdd(from, _ => new AtomicNonce((long)currentChainNonce - 1));
        
        // Ratchet up if chain nonce moved ahead of reservation
        long current = Interlocked.Read(ref wrapper.Value);
        if (current < (long)currentChainNonce - 1)
        {
            Interlocked.Exchange(ref wrapper.Value, (long)currentChainNonce - 1);
        }

        var next = Interlocked.Increment(ref wrapper.Value);
        return new ValueTask<ulong>((ulong)next);
    }

    public void ResetReservation(Address from)
    {
        _reservedNonces.TryRemove(from, out _);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _lookup.Clear();
            _pendingByAccount.Clear();
            _reservedNonces.Clear();
        }
    }
}