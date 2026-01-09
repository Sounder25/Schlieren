using System.Collections.Concurrent;
using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Primitives;

namespace Scrutor.Core.State;

public sealed class GlobalState : IGlobalState
{
    private readonly ConcurrentDictionary<Address, Account> _accounts = new();
    private readonly ReaderWriterLockSlim _consistencyLock = new();

    public ValueTask<BigInteger> GetBalanceAsync(Address address, CancellationToken ct = default)
    {
        if (_accounts.TryGetValue(address, out var account))
        {
            return new ValueTask<BigInteger>(account.Balance);
        }
        return new ValueTask<BigInteger>(BigInteger.Zero);
    }

    public void SetBalance(Address address, BigInteger amount)
    {
        _consistencyLock.EnterReadLock();
        try
        {
            var account = GetOrCreateAccount(address);
            lock (account)
            {
                account.Balance = amount;
            }
        }
        finally
        {
            _consistencyLock.ExitReadLock();
        }
    }

    public ValueTask<ulong> GetNonceAsync(Address address, CancellationToken ct = default)
    {
        return new ValueTask<ulong>(_accounts.TryGetValue(address, out var account) ? account.Nonce : 0);
    }

    public void SetNonce(Address address, ulong nonce)
    {
        _consistencyLock.EnterReadLock();
        try
        {
            var account = GetOrCreateAccount(address);
            lock (account)
            {
                account.Nonce = nonce;
            }
        }
        finally
        {
            _consistencyLock.ExitReadLock();
        }
    }

    public ValueTask<byte[]> GetCodeAsync(Address address, CancellationToken ct = default)
    {
        return new ValueTask<byte[]>(_accounts.TryGetValue(address, out var account) 
            ? account.Code 
            : Array.Empty<byte>());
    }

    public void SetCode(Address address, byte[] code)
    {
        _consistencyLock.EnterReadLock();
        try
        {
            var account = GetOrCreateAccount(address);
            lock (account)
            {
                account.Code = code;
            }
        }
        finally
        {
            _consistencyLock.ExitReadLock();
        }
    }

    public ValueTask<BigInteger> GetStorageAtAsync(Address address, BigInteger key, CancellationToken ct = default)
    {
        if (_accounts.TryGetValue(address, out var account))
        {
            lock (account)
            {
                return new ValueTask<BigInteger>(account.Storage.GetValueOrDefault(key, BigInteger.Zero));
            }
        }
        return new ValueTask<BigInteger>(BigInteger.Zero);
    }

    public void SetStorageAt(Address address, BigInteger key, BigInteger value)
    {
        _consistencyLock.EnterReadLock();
        try
        {
            var account = GetOrCreateAccount(address);
            lock (account)
            {
                account.Storage[key] = value;
            }
        }
        finally
        {
            _consistencyLock.ExitReadLock();
        }
    }

    public void Reset()
    {
        _consistencyLock.EnterWriteLock();
        try
        {
            _accounts.Clear();
        }
        finally
        {
            _consistencyLock.ExitWriteLock();
        }
    }

    public ValueTask<bool> AccountExistsAsync(Address address, CancellationToken ct = default)
    {
        return new ValueTask<bool>(_accounts.ContainsKey(address));
    }

    public IDictionary<Address, Account> Snapshot()
    {
        _consistencyLock.EnterWriteLock();
        try
        {
            var snapshot = new Dictionary<Address, Account>();
            foreach (var kvp in _accounts)
            {
                // We assume _accounts structure doesn't change during iteration because we hold WriteLock?
                // Wait, GetOrCreateAccount adds to _accounts. SetBalance calls GetOrCreateAccount.
                // SetBalance holds ReadLock. Snapshot holds WriteLock.
                // So no GetOrCreateAccount can happen during Snapshot.
                // So _accounts keys are stable.
                
                // We still lock account for internal consistency, although strictly 
                // if all setters take consistencyLock, and Snapshot takes WriteLock, 
                // no one can be holding the per-account lock either (because they'd need ReadLock first).
                // But let's keep the inner lock for safety/correctness if accessed elsewhere.
                
                lock (kvp.Value)
                {
                    snapshot[kvp.Key] = kvp.Value.Clone();
                }
            }
            return snapshot;
        }
        finally
        {
            _consistencyLock.ExitWriteLock();
        }
    }

    private Account GetOrCreateAccount(Address address)
    {
        return _accounts.GetOrAdd(address, _ => new Account());
    }
}
