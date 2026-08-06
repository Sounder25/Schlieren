using System.Numerics;
using System;

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
    /// <summary>EIP-7623 (Prague): calldata token floor cost is enforced post-execution.</summary>
    public bool Eip7623Enabled { get; init; }
    /// <summary>EIP-7702 (Prague): set-code transaction (type 4) authorization processing.</summary>
    public bool Eip7702Enabled { get; init; }
    public ulong ExcessBlobGas { get; init; }

    public static BlockContext Genesis => new()
    {
        Number = 0,
        Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        GasLimit = 30_000_000,
        BaseFeePerGas = 1_000_000_000 // 1 gwei
    };

    public BigInteger GetBlobBaseFee()
    {
        return FakeExponential(
            BigInteger.One,
            new BigInteger(ExcessBlobGas),
            new BigInteger(3_338_477));
    }

    private static BigInteger FakeExponential(
        BigInteger factor,
        BigInteger numerator,
        BigInteger denominator)
    {
        var i = BigInteger.One;
        var output = BigInteger.Zero;
        var numeratorAccumulator = factor * denominator;
        while (numeratorAccumulator > BigInteger.Zero)
        {
            output += numeratorAccumulator;
            numeratorAccumulator =
                numeratorAccumulator * numerator / (denominator * i);
            i++;
        }

        return output / denominator;
    }
}
