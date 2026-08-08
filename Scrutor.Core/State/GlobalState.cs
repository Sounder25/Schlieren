using System.Collections.Concurrent;
using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Primitives;

namespace Scrutor.Core.State;

public sealed class GlobalState : IGlobalState
{
    private readonly ConcurrentDictionary<Address, Account> _accounts = new();
    private readonly ReaderWriterLockSlim _snapshotGate = new();

    public ValueTask<BigInteger> GetBalanceAsync(Address address, CancellationToken ct = default)
    {
        if (_accounts.TryGetValue(address, out var account))
            return new ValueTask<BigInteger>(account.Balance);
        return new ValueTask<BigInteger>(BigInteger.Zero);
    }

    public void SetBalance(Address address, BigInteger amount)
    {
        _snapshotGate.EnterReadLock();
        try
        {
            var account = GetOrCreateAccount(address);
            lock (account) { account.Balance = amount; }
        }
        finally { _snapshotGate.ExitReadLock(); }
    }

    public ValueTask<ulong> GetNonceAsync(Address address, CancellationToken ct = default)
        => new(_accounts.TryGetValue(address, out var account) ? account.Nonce : 0);

    public void SetNonce(Address address, ulong nonce)
    {
        _snapshotGate.EnterReadLock();
        try
        {
            var account = GetOrCreateAccount(address);
            lock (account) { account.Nonce = nonce; }
        }
        finally { _snapshotGate.ExitReadLock(); }
    }

    public ValueTask<byte[]> GetCodeAsync(Address address, CancellationToken ct = default)
        => new(_accounts.TryGetValue(address, out var account) ? account.Code : Array.Empty<byte>());

    public void SetCode(Address address, byte[] code)
    {
        _snapshotGate.EnterReadLock();
        try
        {
            var account = GetOrCreateAccount(address);
            lock (account) { account.Code = code; }
        }
        finally { _snapshotGate.ExitReadLock(); }
    }

    public ValueTask<BigInteger> GetStorageAtAsync(Address address, BigInteger key, CancellationToken ct = default)
    {
        if (_accounts.TryGetValue(address, out var account))
        {
            lock (account)
                return new ValueTask<BigInteger>(account.Storage.GetValueOrDefault(key, BigInteger.Zero));
        }
        return new ValueTask<BigInteger>(BigInteger.Zero);
    }

    public ValueTask<IReadOnlyCollection<BigInteger>> GetStorageKeysAsync(Address address, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_accounts.TryGetValue(address, out var account))
        {
            lock (account)
                return new ValueTask<IReadOnlyCollection<BigInteger>>(account.Storage.Keys.ToList().AsReadOnly());
        }
        return new ValueTask<IReadOnlyCollection<BigInteger>>(Array.Empty<BigInteger>());
    }

    public ValueTask<StoragePresence> GetStoragePresenceAsync(Address address, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_accounts.TryGetValue(address, out var account))
        {
            lock (account)
                return new ValueTask<StoragePresence>(
                    account.Storage.Values.Any(v => v != BigInteger.Zero)
                        ? StoragePresence.NonEmpty
                        : StoragePresence.Empty);
        }
        return new ValueTask<StoragePresence>(StoragePresence.Empty);
    }

    public async ValueTask<bool> HasStorageAsync(Address address, CancellationToken ct = default)
        => await GetStoragePresenceAsync(address, ct) == StoragePresence.NonEmpty;

    public void SetStorageAt(Address address, BigInteger key, BigInteger value)
    {
        _snapshotGate.EnterReadLock();
        try
        {
            var account = GetOrCreateAccount(address);
            lock (account) { account.Storage[key] = value; }
        }
        finally { _snapshotGate.ExitReadLock(); }
    }

    public void Reset()
    {
        _snapshotGate.EnterWriteLock();
        try { _accounts.Clear(); }
        finally { _snapshotGate.ExitWriteLock(); }
    }

    public void DeleteAccount(Address address)
    {
        _snapshotGate.EnterReadLock();
        try { _accounts.TryRemove(address, out _); }
        finally { _snapshotGate.ExitReadLock(); }
    }

    public void MarkCreated(Address address) { }
    public bool WasCreatedInTransaction(Address address) => false;
    public void MarkForDeletion(Address address) { }
    public bool IsMarkedForDeletion(Address address) => false;
    public IEnumerable<Address> GetAccountsMarkedForDeletion() => Array.Empty<Address>();

    public ValueTask<bool> AccountExistsAsync(Address address, CancellationToken ct = default)
        => new(_accounts.ContainsKey(address));

    public ValueTask TouchAccountAsync(Address address, CancellationToken ct = default)
    {
        // GlobalState: create an empty account if it doesn't already exist.
        if (!_accounts.ContainsKey(address))
            SetBalance(address, BigInteger.Zero);
        return ValueTask.CompletedTask;
    }

    public IDictionary<Address, Account> Snapshot()
    {
        _snapshotGate.EnterWriteLock();
        try
        {
            var snapshot = new Dictionary<Address, Account>();
            foreach (var kvp in _accounts)
            {
                lock (kvp.Value)
                    snapshot[kvp.Key] = kvp.Value.Clone();
            }
            return snapshot;
        }
        finally { _snapshotGate.ExitWriteLock(); }
    }

    private Account GetOrCreateAccount(Address address)
        => _accounts.GetOrAdd(address, _ => new Account());
}
