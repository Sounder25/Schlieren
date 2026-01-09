using System.Collections.Concurrent;
using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Primitives;

namespace Scrutor.Core.State;

public sealed class GlobalState : IGlobalState
{
    private readonly ConcurrentDictionary<Address, Account> _accounts = new();

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
        var account = GetOrCreateAccount(address);
        lock (account)
        {
            account.Balance = amount;
        }
    }

    public ValueTask<ulong> GetNonceAsync(Address address, CancellationToken ct = default)
    {
        return new ValueTask<ulong>(_accounts.TryGetValue(address, out var account) ? account.Nonce : 0);
    }

    public void SetNonce(Address address, ulong nonce)
    {
        var account = GetOrCreateAccount(address);
        lock (account)
        {
            account.Nonce = nonce;
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
        var account = GetOrCreateAccount(address);
        lock (account)
        {
            account.Code = code;
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
        var account = GetOrCreateAccount(address);
        lock (account)
        {
            account.Storage[key] = value;
        }
    }

    public void Reset()
    {
        _accounts.Clear();
    }

    public ValueTask<bool> AccountExistsAsync(Address address, CancellationToken ct = default)
    {
        return new ValueTask<bool>(_accounts.ContainsKey(address));
    }

    public IDictionary<Address, Account> Snapshot()
    {
        return new Dictionary<Address, Account>(_accounts);
    }

    private Account GetOrCreateAccount(Address address)
    {
        return _accounts.GetOrAdd(address, _ => new Account());
    }
}
