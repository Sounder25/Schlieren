using System.Numerics;

namespace Scrutor.Core.Gas;

public enum GasComponentKind
{
    Charge,
    RefundCounter,
    Informational
}

/// <summary>One named arithmetic component of a gas calculation.</summary>
public sealed record GasComponent(
    string Id,
    string Label,
    GasComponentKind Kind,
    BigInteger Amount,
    string? Expression = null);
