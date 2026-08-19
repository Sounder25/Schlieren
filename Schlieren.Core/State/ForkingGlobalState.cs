using System.Collections.Concurrent;
using System.Numerics;
using Schlieren.Core.Forking;
using Schlieren.Core.Primitives;

namespace Schlieren.Core.State;

public sealed class ForkingGlobalState : IGlobalState
{
    private readonly IGlobalState _localState;
    private readonly IForkProvider? _forkProvider;
    private readonly ulong? _forkBlockNumber;
    private readonly ConcurrentDictionary<Address, bool> _fetchedAccounts = new();
    private readonly ConcurrentDictionary<(Address, BigInteger), Task<BigInteger>> _pendingStorageFetches = new();
    private readonly ConcurrentDictionary<Address, Task> _pendingAccountFetches = new();

    public ForkingGlobalState(IGlobalState localState, IForkProvider? forkProvider = null, ulong? forkBlockNumber = null)
    {
        _localState = localState;
        _forkProvider = forkProvider;
        _forkBlockNumber = forkBlockNumber;
    }

    public async ValueTask<BigInteger> GetBalanceAsync(Address address, CancellationToken ct = default)
    {
        await EnsureAccountFetchedAsync(address, ct);
        return await _localState.GetBalanceAsync(address, ct);
    }

    public void SetBalance(Address address, BigInteger amount)
    {
        MarkAsFetched(address);
        _localState.SetBalance(address, amount);
    }

    public async ValueTask<ulong> GetNonceAsync(Address address, CancellationToken ct = default)
    {
        await EnsureAccountFetchedAsync(address, ct);
        return await _localState.GetNonceAsync(address, ct);
    }

    public void SetNonce(Address address, ulong nonce)
    {
        MarkAsFetched(address);
        _localState.SetNonce(address, nonce);
    }

    public async ValueTask<byte[]> GetCodeAsync(Address address, CancellationToken ct = default)
    {
        await EnsureAccountFetchedAsync(address, ct);
        return await _localState.GetCodeAsync(address, ct);
    }

    public void SetCode(Address address, byte[] code)
    {
        MarkAsFetched(address);
        _localState.SetCode(address, code);
    }

    public async ValueTask<BigInteger> GetStorageAtAsync(Address address, BigInteger key, CancellationToken ct = default)
    {
        await EnsureAccountFetchedAsync(address, ct);
        
        var localValue = await _localState.GetStorageAtAsync(address, key, ct);
        if (localValue != BigInteger.Zero) return localValue;

        if (_forkProvider != null)
        {
            var fetchTask = _pendingStorageFetches.GetOrAdd((address, key), _ => FetchStorageFromRemoteAsync(address, key, ct));
            return await fetchTask;
        }

        return localValue;
    }

    public async ValueTask<IReadOnlyCollection<BigInteger>> GetStorageKeysAsync(Address address, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await EnsureAccountFetchedAsync(address, ct);
        return await _localState.GetStorageKeysAsync(address, ct);
    }

    public async ValueTask<StoragePresence> GetStoragePresenceAsync(Address address, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await EnsureAccountFetchedAsync(address, ct);

        // Check if local cached state already knows about non-zero storage
        var localPresence = await _localState.GetStoragePresenceAsync(address, ct);
        if (localPresence == StoragePresence.NonEmpty)
        {
            return StoragePresence.NonEmpty;
        }

        // For remote forked accounts where key enumeration is unsupported, return Unknown
        if (_forkProvider != null)
        {
            return StoragePresence.Unknown;
        }

        return localPresence;
    }

    public async ValueTask<bool> HasStorageAsync(Address address, CancellationToken ct = default)
    {
        return await GetStoragePresenceAsync(address, ct) == StoragePresence.NonEmpty;
    }

    private async Task<BigInteger> FetchStorageFromRemoteAsync(Address address, BigInteger key, CancellationToken ct)
    {
        try
        {
            // Apply a hard timeout for remote calls
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            var remoteValue = await _forkProvider!.GetStorageAtAsync(address, key, _forkBlockNumber, timeoutCts.Token);
            if (remoteValue != BigInteger.Zero)
            {
                _localState.SetStorageAt(address, key, remoteValue);
            }
            return remoteValue;
        }
        catch
        {
            _pendingStorageFetches.TryRemove((address, key), out _);
            throw;
        }
        finally
        {
            _pendingStorageFetches.TryRemove((address, key), out _);
        }
    }

    public void SetStorageAt(Address address, BigInteger key, BigInteger value)
    {
        MarkAsFetched(address);
        _localState.SetStorageAt(address, key, value);
    }

    public void Reset()
    {
        _localState.Reset();
        _fetchedAccounts.Clear();
        _pendingAccountFetches.Clear();
        _pendingStorageFetches.Clear();
    }

    public async ValueTask<bool> AccountExistsAsync(Address address, CancellationToken ct = default)
    {
        if (await _localState.AccountExistsAsync(address, ct)) return true;
        if (_forkProvider == null) return false;
        
        await EnsureAccountFetchedAsync(address, ct);
        return await _localState.AccountExistsAsync(address, ct);
    }

    public async ValueTask TouchAccountAsync(Address address, CancellationToken ct = default)
    {
        var exists = await AccountExistsAsync(address, ct);
        if (!exists)
            _localState.SetBalance(address, BigInteger.Zero);
    }

    private Task EnsureAccountFetchedAsync(Address address, CancellationToken ct)
    {
        if (_forkProvider == null || _fetchedAccounts.ContainsKey(address)) return Task.CompletedTask;

        return _pendingAccountFetches.GetOrAdd(address, async addr =>
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

                var balance = await _forkProvider.GetBalanceAsync(addr, _forkBlockNumber, timeoutCts.Token);
                var nonce = await _forkProvider.GetTransactionCountAsync(addr, _forkBlockNumber, timeoutCts.Token);
                var code = await _forkProvider.GetCodeAsync(addr, _forkBlockNumber, timeoutCts.Token);

                // GlobalState.SetBalance/SetNonce/SetCode all materialize a dictionary entry
                // for the address (GetOrCreateAccount), even when called with a zero/empty
                // value. Calling them unconditionally for a genuinely nonexistent remote
                // account would make AccountExistsAsync report true after a single query
                // ever touches it, and distort EIP-161 empty-account/new-account-gas-surcharge
                // accounting. Only materialize the local entry when the remote data proves the
                // account actually exists.
                if (balance != BigInteger.Zero || nonce != 0 || code.Length != 0)
                {
                    _localState.SetBalance(addr, balance);
                    _localState.SetNonce(addr, nonce);
                    _localState.SetCode(addr, code);
                }

                _fetchedAccounts.TryAdd(addr, true);
            }
            catch
            {
                _pendingAccountFetches.TryRemove(addr, out _);
                throw;
            }
            finally
            {
                _pendingAccountFetches.TryRemove(addr, out _);
            }
        });
    }

    private void MarkAsFetched(Address address)
    {
        _fetchedAccounts.TryAdd(address, true);
    }

    public IDictionary<Address, Account> Snapshot()
    {
        return _localState.Snapshot();
    }

    public void RestoreSnapshot(IDictionary<Address, Account> snapshot)
    {
        _localState.RestoreSnapshot(snapshot);
        // Also clear the fetch-tracking so re-reads after restore go to the restored state.
        _fetchedAccounts.Clear();
        _pendingAccountFetches.Clear();
        _pendingStorageFetches.Clear();
    }

    public void MarkCreated(Address address) => _localState.MarkCreated(address);
    public bool WasCreatedInTransaction(Address address) => _localState.WasCreatedInTransaction(address);
    public void MarkForDeletion(Address address) => _localState.MarkForDeletion(address);
    public bool IsMarkedForDeletion(Address address) => _localState.IsMarkedForDeletion(address);
    public IEnumerable<Address> GetAccountsMarkedForDeletion() => _localState.GetAccountsMarkedForDeletion();
    public void DeleteAccount(Address address) => _localState.DeleteAccount(address);
}