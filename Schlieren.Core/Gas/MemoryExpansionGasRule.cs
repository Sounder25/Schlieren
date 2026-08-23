using System.Numerics;
using Schlieren.Core.Forks;

namespace Schlieren.Core.Gas;

/// <summary>
/// Canonical EVM memory expansion curve. Host allocation limits are enforced
/// separately, after this protocol calculation has used the full operands.
/// </summary>
public sealed class MemoryExpansionGasRule : IGasRule<MemoryGasContext>
{
    public static readonly GasRuleId Id = new("MEMORY.EXPANSION");

    public GasRuleMetadata Metadata { get; } = new(
        Id,
        "Memory",
        Fork.Frontier,
        "Ethereum Yellow Paper, Appendix G: C_mem(a) = 3a + floor(a^2 / 512)",
        "EvmMemory.CalculateGasCost");

    public GasCalculation Calculate(MemoryGasContext context, Fork fork)
    {
        if (context.CurrentSizeBytes < BigInteger.Zero)
            throw new ArgumentOutOfRangeException(nameof(context), "Current memory size cannot be negative.");
        if (context.Offset < BigInteger.Zero)
            throw new ArgumentOutOfRangeException(nameof(context), "Memory offset cannot be negative.");
        if (context.Length < BigInteger.Zero)
            throw new ArgumentOutOfRangeException(nameof(context), "Memory length cannot be negative.");
        if (context.CurrentSizeBytes % 32 != BigInteger.Zero)
            throw new ArgumentException("Current EVM memory size must be word aligned.", nameof(context));

        var oldWords = GasMath.WordCount(context.CurrentSizeBytes);
        var requestedEnd = context.Length.IsZero
            ? BigInteger.Zero
            : context.Offset + context.Length;
        var newSize = BigInteger.Max(context.CurrentSizeBytes, requestedEnd);
        var newWords = GasMath.WordCount(newSize);
        var oldCost = MemoryCost(oldWords);
        var newCost = MemoryCost(newWords);
        var delta = newCost - oldCost;
        var chargedGas = checked((ulong)delta);
        var expanded = newWords > oldWords;

        return GasCalculation.Create(
            Metadata,
            fork,
            chargedGas,
            0,
            GasDisposition.Charge,
            new[]
            {
                new GasComponent("new-memory-cost", "Cost at requested memory size", GasComponentKind.Informational, newCost, "3*w + floor(w^2/512)"),
                new GasComponent("old-memory-cost", "Cost at current memory size", GasComponentKind.Informational, oldCost, "3*w + floor(w^2/512)"),
                new GasComponent("expansion-delta", "Memory expansion charged", GasComponentKind.Charge, delta, "new-memory-cost - old-memory-cost")
            },
            new[]
            {
                new GasDecision(
                    "memory-expanded",
                    "Requested end exceeds current allocated words",
                    expanded.ToString(),
                    expanded ? "expanded" : "unchanged",
                    new[] { "expanded", "unchanged" })
            });
    }

    private static BigInteger MemoryCost(BigInteger words) =>
        3 * words + (words * words) / 512;
}