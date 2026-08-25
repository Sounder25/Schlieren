using Schlieren.Core.Execution.Causal;
using Schlieren.Harvest.Domain;

namespace Schlieren.Harvest.Clustering;

/// <summary>
/// Input entry for Harvest failure clustering.
///
/// The summary field is human-readable annotation only — it must never appear
/// in the cluster key or fingerprint.
/// </summary>
public sealed record HarvestFailureEntry(
    string                   CaseId,
    string                   Fork,
    IReadOnlyList<FieldDelta> Deltas,
    string?                  Summary = null);

/// <summary>
/// One Harvest failure cluster — all cases sharing the same typed causal geometry.
/// </summary>
public sealed record HarvestFailureCluster(
    string                   FamilyKey,
    FailureFingerprint       Fingerprint,
    int                      Count,
    IReadOnlyList<string>    CaseIds,
    IReadOnlyList<string>    Forks);

/// <summary>
/// Clusters Harvest failure entries by typed causal geometry (FieldDelta layer + kind + fork).
///
/// Uses FailureClusteringService.ClusterByKey&lt;T&gt; from Core so the grouping
/// primitive is shared — no duplication, no string parsing, no rendering.
///
/// Contracts:
///   - Family key = fork + primary discrepancy layer + primary discrepancy kind.
///   - Primary delta = first delta in the list (comparator emits in stable order).
///   - Input ordering does not affect family identity.
///   - Human summary never enters the key.
///   - Different forks are always separate families.
///   - Journal-only entries without typed deltas get a sentinel key (never merged
///     with typed-delta entries).
///   - Results ordered by count descending, then key ascending.
/// </summary>
public static class FailureFamilyClusterer
{
    public static IReadOnlyList<HarvestFailureCluster> Cluster(
        IEnumerable<HarvestFailureEntry> entries)
    {
        // Build key from typed facts only — summary is explicitly excluded
        static string KeyOf(HarvestFailureEntry e)
            => FailureFingerprint.FromDeltas(e.Fork, e.Deltas).Key;

        var groups = FailureClusteringService.ClusterByKey(entries, KeyOf);

        return groups.Select(g =>
        {
            var first      = g.Members[0];
            var fingerprint = FailureFingerprint.FromDeltas(first.Fork, first.Deltas);
            var caseIds    = g.Members.Select(m => m.CaseId).ToList();
            var forks      = g.Members.Select(m => m.Fork).Distinct().OrderBy(f => f).ToList();

            return new HarvestFailureCluster(
                FamilyKey:   g.Key,
                Fingerprint: fingerprint,
                Count:       g.Count,
                CaseIds:     caseIds,
                Forks:       forks);
        }).ToList();
    }
}
