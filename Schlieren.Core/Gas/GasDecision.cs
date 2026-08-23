namespace Schlieren.Core.Gas;

/// <summary>A recorded condition and formula branch used by a gas rule.</summary>
public sealed record GasDecision(
    string Id,
    string Condition,
    string ObservedValue,
    string SelectedBranch,
    IReadOnlyList<string> Alternatives);