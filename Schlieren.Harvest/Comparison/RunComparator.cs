using Schlieren.Harvest.Clustering;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Ledger;

namespace Schlieren.Harvest.Comparison;

/// <summary>
/// Family change classification between two runs of the same manifest.
/// </summary>
public enum FamilyChangeKind
{
    Eliminated,
    Reduced,
    Expanded,
    Introduced,
    Unchanged,
    Regressed
}

/// <summary>One family's change record between two runs.</summary>
public sealed record FamilyChange(
    string           FamilyKey,
    FamilyChangeKind Change,
    int              BeforeCount,
    int              AfterCount);

/// <summary>A case that previously passed but now has a non-pass status.</summary>
public sealed record RegressionEntry(
    string     CaseId,
    CaseStatus BeforeStatus,
    CaseStatus AfterStatus);

/// <summary>Complete comparison result between two runs.</summary>
public sealed record RunComparisonResult(
    string                         BeforeRunId,
    string                         AfterRunId,
    string                         ManifestHash,
    IReadOnlyList<FamilyChange>    FamilyChanges,
    IReadOnlyList<RegressionEntry> Regressions,
    TimeSpan                       BeforeDuration,
    TimeSpan                       AfterDuration);

/// <summary>
/// Compares two runs of the same manifest and classifies family-level changes.
///
/// Contracts:
///   - Rejects comparisons whose manifest hashes differ.
///   - Reports: eliminated, reduced, expanded, introduced, unchanged, regressed families.
///   - A formerly passing case that becomes anything else is a regression.
///   - Runtime/throughput deltas are included for visibility.
/// </summary>
public static class RunComparator
{
    /// <summary>
    /// Compares two finalized runs and produces the classification.
    /// Throws <see cref="InvalidOperationException"/> if manifest hashes differ.
    /// </summary>
    public static RunComparisonResult Compare(
        ContentEnvelope<RunRecord> before,
        ContentEnvelope<RunRecord> after)
    {
        var b = before.Payload;
        var a = after.Payload;

        if (!string.Equals(b.ManifestHash, a.ManifestHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Cannot compare runs with different manifest hashes. " +
                $"Before: '{b.ManifestHash}', After: '{a.ManifestHash}'.");

        // Build per-case status maps
        var beforeStatuses = b.Outcomes.ToDictionary(o => o.CaseId, o => o.Status);
        var afterStatuses  = a.Outcomes.ToDictionary(o => o.CaseId, o => o.Status);

        // Detect regressions: was Pass, now isn't
        var regressions = new List<RegressionEntry>();
        foreach (var (caseId, beforeStatus) in beforeStatuses)
        {
            if (beforeStatus == CaseStatus.Pass &&
                afterStatuses.TryGetValue(caseId, out var afterStatus) &&
                afterStatus != CaseStatus.Pass)
            {
                regressions.Add(new RegressionEntry(caseId, beforeStatus, afterStatus));
            }
        }

        // Build family groups from divergences
        var beforeFamilies = GroupByFamily(b.Outcomes.Where(o => o.Status == CaseStatus.Divergence));
        var afterFamilies  = GroupByFamily(a.Outcomes.Where(o => o.Status == CaseStatus.Divergence));

        var allKeys = beforeFamilies.Keys.Union(afterFamilies.Keys).OrderBy(k => k);
        var changes = new List<FamilyChange>();

        foreach (var key in allKeys)
        {
            beforeFamilies.TryGetValue(key, out var beforeCount);
            afterFamilies.TryGetValue(key, out var afterCount);

            var change = (beforeCount, afterCount) switch
            {
                (> 0, 0)                         => FamilyChangeKind.Eliminated,
                (> 0, > 0) when afterCount < beforeCount => FamilyChangeKind.Reduced,
                (> 0, > 0) when afterCount > beforeCount => FamilyChangeKind.Expanded,
                (> 0, > 0)                       => FamilyChangeKind.Unchanged,
                (0, > 0)                         => FamilyChangeKind.Introduced,
                _                                => FamilyChangeKind.Unchanged,
            };

            changes.Add(new FamilyChange(key, change, beforeCount, afterCount));
        }

        return new RunComparisonResult(
            BeforeRunId:   b.RunId,
            AfterRunId:    a.RunId,
            ManifestHash:  b.ManifestHash,
            FamilyChanges: changes,
            Regressions:   regressions,
            BeforeDuration: b.CompletedUtc - b.StartedUtc,
            AfterDuration:  a.CompletedUtc - a.StartedUtc);
    }

    private static Dictionary<string, int> GroupByFamily(IEnumerable<CaseOutcome> divergences)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var o in divergences)
        {
            if (o.Deltas.Count == 0) continue;
            var key = FailureFingerprint.FromDeltas("Unknown", o.Deltas).Key;
            result[key] = result.GetValueOrDefault(key) + 1;
        }
        return result;
    }
}
