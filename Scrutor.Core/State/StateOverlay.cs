using System.Collections.Concurrent;
using System.Numerics;
using Scrutor.Core.Primitives;

namespace Scrutor.Core.State;

public sealed class StateOverlay : IGlobalState
{
    private readonly IGlobalState _parent;
    private readonly ConcurrentDictionary<Address, OverlayAccount> _buffer = new();

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

    public async ValueTask<ulong> GetNonceAsync(Address address, CancellationToken ct = default)
    {
        if (_buffer.TryGetValue(address, out var acc) && acc.Nonce.HasValue)
            return acc.Nonce.Value;
        
        return await _parent.GetNonceAsync(address, ct);
    }

    public void SetNonce(Address address, ulong nonce)
    {
        GetOrCreateOverlayAccount(address).Nonce = nonce;
    }

    public async ValueTask<byte[]> GetCodeAsync(Address address, CancellationToken ct = default)
    {
        if (_buffer.TryGetValue(address, out var acc) && acc.Code != null)
            return acc.Code;
        
        return await _parent.GetCodeAsync(address, ct);
    }

    public void SetCode(Address address, byte[] code)
    {
        GetOrCreateOverlayAccount(address).Code = code;
    }

    public async ValueTask<BigInteger> GetStorageAtAsync(Address address, BigInteger key, CancellationToken ct = default)
    {
        if (_buffer.TryGetValue(address, out var acc) && acc.Storage.TryGetValue(key, out var val))
            return val;
        
        return await _parent.GetStorageAtAsync(address, key, ct);
    }

    public void SetStorageAt(Address address, BigInteger key, BigInteger value)
    {
        GetOrCreateOverlayAccount(address).Storage[key] = value;
    }

    public async ValueTask<bool> AccountExistsAsync(Address address, CancellationToken ct = default)
    {
        if (_buffer.ContainsKey(address)) return true;
        return await _parent.AccountExistsAsync(address, ct);
    }

    public void Reset()
    {
        _buffer.Clear();
    }

    public void Commit()
    {
        foreach (var (address, acc) in _buffer)
        {
            if (acc.Balance.HasValue) _parent.SetBalance(address, acc.Balance.Value);
            if (acc.Nonce.HasValue) _parent.SetNonce(address, acc.Nonce.Value);
            if (acc.Code != null) _parent.SetCode(address, acc.Code);
            
            foreach (var (key, val) in acc.Storage)
            {
                _parent.SetStorageAt(address, key, val);
            }
        }
    }

    private OverlayAccount GetOrCreateOverlayAccount(Address address)
    {
        return _buffer.GetOrAdd(address, _ => new OverlayAccount());
    }

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
                acc = acc.Clone(); // Clone to avoid mutating parent snapshot if it was returned directly
                snapshot[address] = acc;
            }

            if (overlayAcc.Balance.HasValue) acc.Balance = overlayAcc.Balance.Value;
            if (overlayAcc.Nonce.HasValue) acc.Nonce = overlayAcc.Nonce.Value;
            if (overlayAcc.Code != null) acc.Code = overlayAcc.Code;
            foreach (var (k, v) in overlayAcc.Storage)
            {
                acc.Storage[k] = v;
            }
        }
        return snapshot;
    }
}