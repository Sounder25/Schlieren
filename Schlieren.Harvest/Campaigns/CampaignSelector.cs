using Schlieren.Harvest.Fixtures;

namespace Schlieren.Harvest.Campaigns;

/// <summary>
/// Result of a campaign selection attempt.
/// Exactly one of <see cref="Cases"/> or <see cref="InsufficientReport"/> is non-null.
/// </summary>
public sealed record SelectionResult(
    bool IsSuccess,
    IReadOnlyList<FixtureCaseMetadata>? Cases,
    InsufficientCoverageReport? InsufficientReport);

/// <summary>Typed report when the corpus cannot satisfy the requested count.</summary>
public sealed record InsufficientCoverageReport(
    int  RequestedCount,
    int  AvailableCount,
    string Reason);

/// <summary>
/// Deterministic storage-lifecycle campaign selector.
///
/// Algorithm (per Task 5 spec):
///   1. Score each admitted case by how many uncovered <see cref="StorageDimension"/>
///      values it would add (greedy set-cover).
///   2. Tie-break by CaseId ordinal string comparison (ascending).
///   3. Select exactly <paramref name="requestedCount"/> cases or return
///      <see cref="InsufficientCoverageReport"/> — no random seed, no unrelated fill.
///
/// Selection is pure-functional: the same inputs always produce the same output.
/// </summary>
public sealed class CampaignSelector
{
    public SelectionResult TrySelect(
        IReadOnlyList<FixtureCaseMetadata> admittedCases,
        int requestedCount)
    {
        // Only admitted cases participate
        var pool = admittedCases
            .Where(m => m.Admission == AdmissionReasonCode.Admitted)
            .ToList();

        if (pool.Count < requestedCount)
        {
            return new SelectionResult(
                IsSuccess: false,
                Cases: null,
                InsufficientReport: new InsufficientCoverageReport(
                    RequestedCount: requestedCount,
                    AvailableCount: pool.Count,
                    Reason: $"Requested {requestedCount} cases but only {pool.Count} admitted cases are available. " +
                            "Manifest creation requires exactly the requested count; no unrelated fill is allowed."));
        }

        var selected        = new List<FixtureCaseMetadata>(requestedCount);
        var covered         = new HashSet<StorageDimension>();
        var remaining       = new List<FixtureCaseMetadata>(pool);

        while (selected.Count < requestedCount && remaining.Count > 0)
        {
            // Score: count of new dimensions each remaining case would add
            // Tie-break: CaseId ordinal ascending
            var best = remaining
                .OrderByDescending(c => c.Dimensions.Count(d => !covered.Contains(d)))
                .ThenBy(c => c.CaseId, StringComparer.Ordinal)
                .First();

            selected.Add(best);
            foreach (var d in best.Dimensions)
                covered.Add(d);
            remaining.Remove(best);
        }

        return new SelectionResult(
            IsSuccess: true,
            Cases: selected,
            InsufficientReport: null);
    }
}
