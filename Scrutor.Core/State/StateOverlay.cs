using System.Collections.Concurrent;
using System.Numerics;
using System.Linq;
using Scrutor.Core.Primitives;

namespace Scrutor.Core.State;

public sealed class StateOverlay : IGlobalState
{
    private readonly IGlobalState _parent;
    private readonly ConcurrentDictionary<Address, OverlayAccount> _buffer = new();
    private readonly HashSet<Address> _createdAccounts = new();
    private readonly HashSet<Address> _accountsMarkedForDeletion = new();

    public StateOverlay(IGlobalState parent)
    {
        _parent = parent;
    }

    public async ValueTask<BigInteger> GetBalanceAsync(Address address, CancellationToken ct = default)
    {
        if (_buffer.TryGetValue(address, out var acc) && acc.Balance.HasValue)
            return acc.Balance.Value;
        return await _parent.GetBalanceAsync(address, ct);
    }

    public void SetBalance(Address address, BigInteger amount)
    {
        GetOrCreateOverlayAccount(address).Balance = amount;
    }

    public void SetNonce(Address address, ulong nonce)
    {
        GetOrCreateOverlayAccount(address).Nonce = nonce;
    }

    public void SetCode(Address address, byte[] code)
    {
        GetOrCreateOverlayAccount(address).Code = code;
    }

    public async ValueTask<ulong> GetNonceAsync(Address address, CancellationToken ct = default)
    {
        if (_buffer.TryGetValue(address, out var acc) && acc.Nonce.HasValue)
            return acc.Nonce.Value;
        return await _parent.GetNonceAsync(address, ct);
    }

    public async ValueTask<byte[]> GetCodeAsync(Address address, CancellationToken ct = default)
    {
        if (_buffer.TryGetValue(address, out var acc) && acc.Code != null)
            return acc.Code;
        return await _parent.GetCodeAsync(address, ct);
    }

    public async ValueTask<BigInteger> GetStorageAtAsync(Address address, BigInteger key, CancellationToken ct = default)
    {
        if (_buffer.TryGetValue(address, out var acc) && acc.Storage.TryGetValue(key, out var val))
            return val;
        return await _parent.GetStorageAtAsync(address, key, ct);
    }

    public async ValueTask<IReadOnlyCollection<BigInteger>> GetStorageKeysAsync(Address address, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var parentKeys = await _parent.GetStorageKeysAsync(address, ct);
        if (_buffer.TryGetValue(address, out var acc))
        {
            var set = new HashSet<BigInteger>(parentKeys);
            foreach (var key in acc.Storage.Keys)
                set.Add(key);
            return set;
        }
        return parentKeys;
    }

    public async ValueTask<StoragePresence> GetStoragePresenceAsync(Address address, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_buffer.TryGetValue(address, out var acc) && acc.Storage.Values.Any(v => v != BigInteger.Zero))
            return StoragePresence.NonEmpty;

        var parentPresence = await _parent.GetStoragePresenceAsync(address, ct);
        if (parentPresence == StoragePresence.Unknown)
            return StoragePresence.Unknown;

        var keys = await GetStorageKeysAsync(address, ct);
        foreach (var key in keys)
        {
            ct.ThrowIfCancellationRequested();
            var val = await GetStorageAtAsync(address, key, ct);
            if (val != BigInteger.Zero)
                return StoragePresence.NonEmpty;
        }
        return StoragePresence.Empty;
    }

    public async ValueTask<bool> HasStorageAsync(Address address, CancellationToken ct = default)
        => await GetStoragePresenceAsync(address, ct) == StoragePresence.NonEmpty;

    public void SetStorageAt(Address address, BigInteger key, BigInteger value)
    {
        GetOrCreateOverlayAccount(address).Storage[key] = value;
    }

    public async ValueTask<bool> AccountExistsAsync(Address address, CancellationToken ct = default)
    {
        if (_buffer.ContainsKey(address))
            return true;
        return await _parent.AccountExistsAsync(address, ct);
    }

    public void Reset()
    {
        _buffer.Clear();
    }

    /// <summary>
    /// Remove all buffered mutations for a single address.
    /// Used to roll back a failed top-level CREATE so the creation address
    /// doesn't leak into the committed state (EELS: restore_tx_state semantics).
    /// </summary>
    public void Reset(Address address)
    {
        _buffer.TryRemove(address, out _);
        _createdAccounts.Remove(address);
    }

    public void Commit()
    {
        foreach (var (address, acc) in _buffer)
        {
            if (acc.Balance.HasValue) _parent.SetBalance(address, acc.Balance.Value);
            if (acc.Nonce.HasValue) _parent.SetNonce(address, acc.Nonce.Value);
            if (acc.Code != null) _parent.SetCode(address, acc.Code);
            foreach (var (key, val) in acc.Storage)
                _parent.SetStorageAt(address, key, val);
        }
        foreach (var addr in _createdAccounts) _parent.MarkCreated(addr);
        foreach (var addr in _accountsMarkedForDeletion) _parent.MarkForDeletion(addr);
    }

    public void MarkCreated(Address address) => _createdAccounts.Add(address);
    public bool WasCreatedInTransaction(Address address) => _createdAccounts.Contains(address) || _parent.WasCreatedInTransaction(address);
    public void MarkForDeletion(Address address) => _accountsMarkedForDeletion.Add(address);
    public bool IsMarkedForDeletion(Address address) => _accountsMarkedForDeletion.Contains(address) || _parent.IsMarkedForDeletion(address);
    public IEnumerable<Address> GetAccountsMarkedForDeletion() => _accountsMarkedForDeletion.Concat(_parent.GetAccountsMarkedForDeletion()).Distinct();
    public void DeleteAccount(Address address) => _parent.DeleteAccount(address);

    private OverlayAccount GetOrCreateOverlayAccount(Address address)
        => _buffer.GetOrAdd(address, _ => new OverlayAccount());

    private class OverlayAccount
    {
        public BigInteger? Balance { get; set; }
        public ulong? Nonce { get; set; }
        public byte[]? Code { get; set; }
        public ConcurrentDictionary<BigInteger, BigInteger> Storage { get; } = new();
    }

    public IDictionary<Address, Account> Snapshot()
    {
        var snapshot = _parent.Snapshot();
        foreach (var (address, overlayAcc) in _buffer)
        {
            if (!snapshot.TryGetValue(address, out var acc))
            {
                acc = new Account();
                snapshot[address] = acc;
            }
            else
            {
                acc = acc.Clone();
                snapshot[address] = acc;
            }

            if (overlayAcc.Balance.HasValue) acc.Balance = overlayAcc.Balance.Value;
            if (overlayAcc.Nonce.HasValue) acc.Nonce = overlayAcc.Nonce.Value;
            if (overlayAcc.Code != null) acc.Code = overlayAcc.Code;
            foreach (var (k, v) in overlayAcc.Storage)
                acc.Storage[k] = v;
        }
        foreach (var addr in GetAccountsMarkedForDeletion())
            snapshot.Remove(addr);
        return snapshot;
    }
}
