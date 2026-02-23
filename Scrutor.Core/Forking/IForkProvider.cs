using System.Numerics;
using Scrutor.Core.Models;
using Scrutor.Core.Primitives;

namespace Scrutor.Core.Forking;

public interface IForkProvider
{
    Task<ulong> GetLatestBlockNumberAsync(CancellationToken ct = default);
    Task<Block?> GetBlockByNumberAsync(ulong number, CancellationToken ct = default);
    Task<Block?> GetBlockByHashAsync(string hash, CancellationToken ct = default);
    Task<BigInteger> GetBalanceAsync(Address address, ulong? blockNumber = null, CancellationToken ct = default);
    Task<ulong> GetTransactionCountAsync(Address address, ulong? blockNumber = null, CancellationToken ct = default);
    Task<byte[]> GetCodeAsync(Address address, ulong? blockNumber = null, CancellationToken ct = default);
    Task<BigInteger> GetStorageAtAsync(Address address, BigInteger key, ulong? blockNumber = null, CancellationToken ct = default);
}
