using System.Collections.ObjectModel;
using System.Numerics;
using Scrutor.Core.Forks;

namespace Scrutor.Core.Gas;

public enum GasDisposition
{
    Charge,
    TransferOut,
    TransferIn,
    Return,
    RefundCounterDelta,
    Burn,
    Settlement,
    Validation
}

/// <summary>
/// Immutable, validated output of one executable gas rule.
/// </summary>
public sealed class GasCalculation
{
    private GasCalculation(
        GasRuleMetadata metadata,
        Fork fork,
        ulong chargedGas,
        long refundCounterDelta,
        GasDisposition disposition,
        ReadOnlyCollection<GasComponent> components,
        ReadOnlyCollection<GasDecision> decisions)
    {
        Metadata = metadata;
        RuleId = metadata.RuleId;
        Fork = fork;
        ChargedGas = chargedGas;
        RefundCounterDelta = refundCounterDelta;
        Disposition = disposition;
        Components = components;
        Decisions = decisions;
    }

    public GasRuleId RuleId { get; }
    public GasRuleMetadata Metadata { get; }
    public Fork Fork { get; }
    public ulong ChargedGas { get; }
    public long RefundCounterDelta { get; }
    public GasDisposition Disposition { get; }
    public IReadOnlyList<GasComponent> Components { get; }
    public IReadOnlyList<GasDecision> Decisions { get; }

    public static GasCalculation Create(
        GasRuleMetadata metadata,
        Fork fork,
        ulong chargedGas,
        long refundCounterDelta,
        GasDisposition disposition,
        IEnumerable<GasComponent> components,
        IEnumerable<GasDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(decisions);

        var componentSnapshot = components.ToArray();
        var decisionSnapshot = decisions
            .Select(CopyDecision)
            .ToArray();

        RejectBlankOrDuplicateIds(componentSnapshot.Select(component => component.Id), "component");
        RejectBlankOrDuplicateIds(decisionSnapshot.Select(decision => decision.Id), "decision");

        if (componentSnapshot.Any(component =>
                component.Kind == GasComponentKind.Charge && component.Amount < BigInteger.Zero))
        {
            throw new ArgumentException("Charge components cannot be negative.", nameof(components));
        }

        var chargeTotal = componentSnapshot
            .Where(component => component.Kind == GasComponentKind.Charge)
            .Aggregate(BigInteger.Zero, (total, component) => total + component.Amount);
        if (chargeTotal != new BigInteger(chargedGas))
        {
            throw new ArgumentException(
                $"Component charged gas total {chargeTotal} does not equal charged gas {chargedGas}.",
                nameof(components));
        }

        var refundTotal = componentSnapshot
            .Where(component => component.Kind == GasComponentKind.RefundCounter)
            .Aggregate(BigInteger.Zero, (total, component) => total + component.Amount);
        if (refundTotal != new BigInteger(refundCounterDelta))
        {
            throw new ArgumentException(
                $"Component refund total {refundTotal} does not equal refund counter delta {refundCounterDelta}.",
                nameof(components));
        }

        return new GasCalculation(
            metadata,
            fork,
            chargedGas,
            refundCounterDelta,
            disposition,
            Array.AsReadOnly(componentSnapshot),
            Array.AsReadOnly(decisionSnapshot));
    }

    private static GasDecision CopyDecision(GasDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(decision.Alternatives);
        return decision with { Alternatives = Array.AsReadOnly(decision.Alternatives.ToArray()) };
    }

    private static void RejectBlankOrDuplicateIds(IEnumerable<string> ids, string label)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException($"Gas {label} ID cannot be blank.");
            if (!seen.Add(id))
                throw new ArgumentException($"Duplicate gas {label} ID '{id}'.");
        }
    }
}
