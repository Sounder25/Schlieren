using System.Text.Json;
using Schlieren.Harvest.Comparison;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Ledger;

namespace Schlieren.Harvest.Tests.Comparison;

/// <summary>
/// Proves RunComparator contracts:
///   - Rejects different manifest hashes.
///   - Classifies eliminated, reduced, expanded, introduced, unchanged families.
///   - Detects regressions (was Pass → now non-Pass).
///   - Reports runtime deltas.
/// </summary>
public class RunComparatorTests
{
    private static ContentEnvelope<RunRecord> MakeEnvelope(
        string runId,
        string manifestHash,
        IReadOnlyList<CaseOutcome> outcomes,
        DateTime? started = null,
        DateTime? completed = null)
    {
        var s = started ?? new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var c = completed ?? s.AddMinutes(5);
        int pass = 0, div = 0;
        foreach (var o in outcomes)
        {
            if (o.Status == CaseStatus.Pass) pass++;
            else if (o.Status == CaseStatus.Divergence) div++;
        }
        var record = new RunRecord(runId, "c1", "1", manifestHash,
            RunKind.Inspection, RunState.Completed, s, c,
            new EnvironmentIdentity("Win", "8", "h", 4),
            new ToolIdentity("s", "1", "a", null), null,
            new RunCaseSummary(pass, div, 0, 0, 0, 0), outcomes);
        return new ContentEnvelope<RunRecord>("1", DateTime.UtcNow, "hash", record);
    }

    private static CaseOutcome PassCase(string id) =>
        new(id, CaseStatus.Pass, Array.Empty<FieldDelta>(), "r1", DateTime.UtcNow);

    private static CaseOutcome DivCase(string id) =>
        new(id, CaseStatus.Divergence, new[]
        {
            new FieldDelta(DiscrepancyLayer.Gas, DiscrepancyKind.GasUsed,
                JsonSerializer.SerializeToElement(1), JsonSerializer.SerializeToElement(2))
        }, "r1", DateTime.UtcNow);

    // ── Test 1: Rejects different manifest hashes ─────────────────────────

    [Fact]
    public void Compare_DifferentManifestHashes_Throws()
    {
        var before = MakeEnvelope("r1", "hashA", new[] { PassCase("c1") });
        var after  = MakeEnvelope("r2", "hashB", new[] { PassCase("c1") });

        Assert.Throws<InvalidOperationException>(() => RunComparator.Compare(before, after));
    }

    // ── Test 2: All pass both → no changes, no regressions ────────────────

    [Fact]
    public void Compare_BothAllPass_NoChanges()
    {
        var before = MakeEnvelope("r1", "h1", new[] { PassCase("c1"), PassCase("c2") });
        var after  = MakeEnvelope("r2", "h1", new[] { PassCase("c1"), PassCase("c2") });

        var result = RunComparator.Compare(before, after);

        Assert.Empty(result.FamilyChanges);
        Assert.Empty(result.Regressions);
    }

    // ── Test 3: Family eliminated ─────────────────────────────────────────

    [Fact]
    public void Compare_FamilyEliminated_ReportsEliminated()
    {
        var before = MakeEnvelope("r1", "h1", new[] { DivCase("c1"), PassCase("c2") });
        var after  = MakeEnvelope("r2", "h1", new[] { PassCase("c1"), PassCase("c2") });

        var result = RunComparator.Compare(before, after);

        Assert.Single(result.FamilyChanges);
        Assert.Equal(FamilyChangeKind.Eliminated, result.FamilyChanges[0].Change);
    }

    // ── Test 4: Family introduced ─────────────────────────────────────────

    [Fact]
    public void Compare_FamilyIntroduced_ReportsIntroduced()
    {
        var before = MakeEnvelope("r1", "h1", new[] { PassCase("c1"), PassCase("c2") });
        var after  = MakeEnvelope("r2", "h1", new[] { DivCase("c1"), PassCase("c2") });

        var result = RunComparator.Compare(before, after);

        Assert.Single(result.FamilyChanges);
        Assert.Equal(FamilyChangeKind.Introduced, result.FamilyChanges[0].Change);
    }

    // ── Test 5: Regression detected ──────────────────────────────────────

    [Fact]
    public void Compare_PassToDiv_ReportsRegression()
    {
        var before = MakeEnvelope("r1", "h1", new[] { PassCase("c1"), PassCase("c2") });
        var after  = MakeEnvelope("r2", "h1", new[] { PassCase("c1"), DivCase("c2") });

        var result = RunComparator.Compare(before, after);

        Assert.Single(result.Regressions);
        Assert.Equal("c2", result.Regressions[0].CaseId);
        Assert.Equal(CaseStatus.Pass, result.Regressions[0].BeforeStatus);
        Assert.Equal(CaseStatus.Divergence, result.Regressions[0].AfterStatus);
    }

    // ── Test 6: Runtime deltas ────────────────────────────────────────────

    [Fact]
    public void Compare_IncludesRuntimeDeltas()
    {
        var t1 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var before = MakeEnvelope("r1", "h1", new[] { PassCase("c1") }, t1, t1.AddMinutes(10));
        var after  = MakeEnvelope("r2", "h1", new[] { PassCase("c1") }, t1, t1.AddMinutes(5));

        var result = RunComparator.Compare(before, after);

        Assert.Equal(TimeSpan.FromMinutes(10), result.BeforeDuration);
        Assert.Equal(TimeSpan.FromMinutes(5), result.AfterDuration);
    }
}
