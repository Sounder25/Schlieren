using System.Numerics;
using Scrutor.Core.Primitives;

namespace Scrutor.Core.State;

/// <summary>
/// Interface for Global State access and modification.
/// All getters are async (ValueTask) to allow for network-based forking.
/// </summary>
public interface IGlobalState
{
    ValueTask<BigInteger> GetBalanceAsync(Address address, CancellationToken ct = default);
    void SetBalance(Address address, BigInteger amount);
    
    ValueTask<ulong> GetNonceAsync(Address address, CancellationToken ct = default);
    void SetNonce(Address address, ulong nonce);
    
    ValueTask<byte[]> GetCodeAsync(Address address, CancellationToken ct = default);
    void SetCode(Address address, byte[] code);

    ValueTask<BigInteger> GetStorageAtAsync(Address address, BigInteger key, CancellationToken ct = default);
    void SetStorageAt(Address address, BigInteger key, BigInteger value);

    void Reset();
    ValueTask<bool> AccountExistsAsync(Address address, CancellationToken ct = default);
    IDictionary<Address, Account> Snapshot();
}
