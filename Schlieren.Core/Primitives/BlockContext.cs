using System.Numerics;
using System;
using Schlieren.Core.Forks;

namespace Schlieren.Core.Primitives;

/// <summary>
/// Block information used during execution.
/// Fork-variant behaviour is entirely encapsulated in <see cref="Rules"/> —
/// no more scattered boolean flags.
/// </summary>
public sealed class BlockContext
{
    public ulong ChainId      { get; init; }
    public ulong Number       { get; init; }
    public ulong Timestamp    { get; init; }
    public ulong GasLimit     { get; init; } = 30_000_000;
    public Address Coinbase   { get; init; } = Address.Zero;
    public BigInteger Difficulty { get; init; }
    public ulong BaseFeePerGas { get; init; }
    public byte[] Hash        { get; init; } = new byte[32];
    public ulong ExcessBlobGas { get; init; }

    /// <summary>
    /// All fork-variant rules: gas schedules, opcode availability, precompile set.
    /// Defaults to <see cref="PragueRules"/> so existing code is unaffected.
    /// </summary>
    public IForkRules Rules { get; init; } = ForkRulesFactory.Latest;

    // ── Convenience shims (delegates to Rules) ─────────────────────────────
    // These let existing call sites compile without changes during migration.
    // They will be removed once all call sites are updated to use Rules directly.

    /// <inheritdoc cref="IForkRules.HasBlobHash"/>
    public bool BlobHashEnabled => Rules.HasBlobHash;

    /// <inheritdoc cref="IForkRules.HasEip7623CalldataFloor"/>
    public bool Eip7623Enabled  => Rules.HasEip7623CalldataFloor;

    /// <inheritdoc cref="IForkRules.HasEip7702SetCode"/>
    public bool Eip7702Enabled  => Rules.HasEip7702SetCode;

    // ── Factory ────────────────────────────────────────────────────────────

    public static BlockContext Genesis => new()
    {
        Number       = 0,
        Timestamp    = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        GasLimit     = 30_000_000,
        BaseFeePerGas = 1_000_000_000, // 1 gwei
        Rules        = ForkRulesFactory.Latest,
    };

    // ── Blob base fee ──────────────────────────────────────────────────────

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
