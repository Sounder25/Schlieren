using System.Numerics;
using Schlieren.Core.Forks;

namespace Schlieren.Core.Gas;

/// <summary>Fork-aware EXP base and exponent-byte charge.</summary>
public sealed class ExponentGasRule : IGasRule<ExponentGasContext>
{
    public static readonly GasRuleId Id = new("OP.EXP");

    public GasRuleMetadata Metadata { get; } = new(
        Id,
        "Opcode",
        Fork.Frontier,
        "Yellow Paper G_exp/G_expbyte; EIP-160",
        "OpcodeExp.ExecuteAsync");

    public GasCalculation Calculate(ExponentGasContext context, Fork fork)
    {
        if (context.Exponent < BigInteger.Zero)
            throw new ArgumentOutOfRangeException(nameof(context));

        var byteCount = context.Exponent.IsZero
            ? 0UL
            : checked((ulong)((context.Exponent.GetBitLength() + 7) / 8));
        var perByte = fork >= Fork.SpuriousDragon ? 50UL : 10UL;
        var byteCharge = GasMath.MultiplyChecked(byteCount, perByte);
        var total = GasMath.AddChecked(10, byteCharge);

        return GasCalculation.Create(
            Metadata,
            fork,
            total,
            0,
            GasDisposition.Charge,
            new[]
            {
                new GasComponent("base", "EXP base", GasComponentKind.Charge, 10),
                new GasComponent("exponent-byte-charge", "Exponent byte charge", GasComponentKind.Charge, byteCharge, "bytes * per-byte-price"),
                new GasComponent("exponent-bytes", "Exponent byte count", GasComponentKind.Informational, byteCount)
            },
            new[]
            {
                new GasDecision(
                    "exp-byte-era",
                    "Fork is Spurious Dragon or later",
                    (fork >= Fork.SpuriousDragon).ToString(),
                    perByte == 50 ? "50-per-byte" : "10-per-byte",
                    new[] { "10-per-byte", "50-per-byte" })
            });
    }
}