using System.Numerics;
using Schlieren.Core.Models;
using Schlieren.Core.Primitives;

namespace Schlieren.Core.Forking;

public interface IForkProvider
{
    Task<ulong> GetChainIdAsync(CancellationToken ct = default);
    Task<ulong> GetLatestBlockNumberAsync(CancellationToken ct = default);
    Task<Block?> GetBlockByNumberAsync(ulong number, CancellationToken ct = default);
    /// <summary>Fetches the latest block in a single RPC call (eth_getBlockByNumber "latest").</summary>
    Task<Block?> GetLatestBlockAsync(CancellationToken ct = default);
    Task<Block?> GetBlockByHashAsync(string hash, CancellationToken ct = default);
    Task<BigInteger> GetBalanceAsync(Address address, ulong? blockNumber = null, CancellationToken ct = default);
    Task<ulong> GetTransactionCountAsync(Address address, ulong? blockNumber = null, CancellationToken ct = default);
    Task<byte[]> GetCodeAsync(Address address, ulong? blockNumber = null, CancellationToken ct = default);
    Task<BigInteger> GetStorageAtAsync(Address address, BigInteger key, ulong? blockNumber = null, CancellationToken ct = default);
}
