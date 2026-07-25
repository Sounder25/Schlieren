using System.Numerics;

namespace Scrutor.Core.Primitives;

/// <summary>
/// Block information used during execution
/// </summary>
public sealed class BlockContext
{
    public ulong ChainId { get; init; }
    public ulong Number { get; init; }
    public ulong Timestamp { get; init; }
    public ulong GasLimit { get; init; } = 30_000_000;
    public Address Coinbase { get; init; } = Address.Zero;
    public BigInteger Difficulty { get; init; }
    public ulong BaseFeePerGas { get; init; }
    public byte[] Hash { get; init; } = new byte[32];
    public bool BlobHashEnabled { get; init; } = true;
    public ulong ExcessBlobGas { get; init; }

    public static BlockContext Genesis => new()
    {
        Number = 0,
        Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        GasLimit = 30_000_000,
        BaseFeePerGas = 1_000_000_000 // 1 gwei
    };
}
