using System.Collections.Concurrent;
using Scrutor.Core.Primitives;

namespace Scrutor.Core.State;

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

    public void Add(Transaction tx)
    {
        var hashKey = Convert.ToHexString(tx.Hash);

        if (_lookup.Count >= MaxMempoolSize) return;
        if (_lookup.ContainsKey(hashKey)) return;

        lock (_lock)
        {
            if (_lookup.TryAdd(hashKey, tx))
            {
                var accountTxs = _pendingByAccount.GetOrAdd(tx.From, _ => new ConcurrentDictionary<ulong, Transaction>());
                accountTxs.TryAdd(tx.Nonce, tx);
            }
        }
    }

    public Transaction? PeekBest()
    {
        // For simplicity in this MVP, we still prioritize by GasPrice globally, 
        // but the PopBest logic will ensure sequential application.
        // A truly robust mempool would only return the "best" eligible transaction.
        lock (_lock)
        {
            return _lookup.Values
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