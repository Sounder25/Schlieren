using System.Collections.ObjectModel;

namespace Schlieren.Core.Gas;

/// <summary>The rule IDs that must exist in a complete schedule slice.</summary>
public sealed class GasCoverageManifest
{
    public static GasCoverageManifest Empty { get; } = new(Array.Empty<GasRuleId>());

    public GasCoverageManifest(IEnumerable<GasRuleId> requiredRuleIds)
    {
        ArgumentNullException.ThrowIfNull(requiredRuleIds);

        var source = requiredRuleIds.ToArray();
        var duplicate = source
            .GroupBy(id => id)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate required gas rule '{duplicate.Key}'.", nameof(requiredRuleIds));

        RequiredRuleIds = new ReadOnlyCollection<GasRuleId>(source
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToArray());
    }

    public IReadOnlyList<GasRuleId> RequiredRuleIds { get; }
}