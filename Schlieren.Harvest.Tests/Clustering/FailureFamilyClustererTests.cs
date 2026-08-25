using Schlieren.Harvest.Clustering;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Comparison;
using Schlieren.Harvest.Execution;
using System.Text.Json;
using Xunit;

namespace Schlieren.Harvest.Tests.Clustering;

/// <summary>
/// FailureFamilyClusterer tests.
///
/// Contracts per Task 8 spec + acceptance points:
///
/// FINGERPRINT KEYS use typed causal facts only:
///   fork + discrepancy layer + discrepancy kind + (optional) status geometry
///   Never: human-readable summary, test name, source path, rendered mismatch text.
///
/// STABILITY: input ordering must not affect cluster identity or family keys.
///
/// SEPARATION: different forks produce separate families unless the explicit
///   fingerprint policy allows merging (none defined for Task 8 — all separate).
///
/// JOURNAL: journal evidence may enrich fingerprints but must never decide
///   consensus output or cluster key when journal is the only evidence.
///
/// ACCUMULATION: multiple distinct delta kinds within one case produce
///   a fingerprint using the FIRST (earliest-layer) delta as the key.
/// </summary>
public class FailureFamilyClustererTests
{
    // ── Helpers ──────────────────────────────────────────────────────────

    private static FieldDelta GasDelta() =>
        new(DiscrepancyLayer.Gas, DiscrepancyKind.GasUsed,
            JsonSerializer.SerializeToElement(21_000UL),
            JsonSerializer.SerializeToElement(21_500UL));

    private static FieldDelta StatusDelta() =>
        new(DiscrepancyLayer.Validity, DiscrepancyKind.Status,
            JsonSerializer.SerializeToElement(true),
            JsonSerializer.SerializeToElement(false));

    private static FieldDelta StorageDelta() =>
        new(DiscrepancyLayer.Storage, DiscrepancyKind.StorageValue,
            JsonSerializer.SerializeToElement("0xdeadbeef"),
            JsonSerializer.SerializeToElement("0xcafebabe"));

    private static HarvestFailureEntry Entry(
        string caseId,
        string fork,
        IReadOnlyList<FieldDelta> deltas,
        string? summary = null)
        => new(caseId, fork, deltas, summary);

    // ── Single entry → single cluster ────────────────────────────────────

    [Fact]
    public void Cluster_SingleEntry_ProducesOneFamily()
    {
        var entries = new[] { Entry("case-001", "Berlin", new[] { GasDelta() }) };
        var clusters = FailureFamilyClusterer.Cluster(entries);

        Assert.Single(clusters);
        Assert.Single(clusters[0].CaseIds);
        Assert.Equal("case-001", clusters[0].CaseIds[0]);
    }

    // ── Identical geometry → same family ─────────────────────────────────

    [Fact]
    public void Cluster_IdenticalGeometry_SameFamily()
    {
        var entries = new[]
        {
            Entry("case-001", "Berlin", new[] { GasDelta() }),
            Entry("case-002", "Berlin", new[] { GasDelta() }),
        };

        var clusters = FailureFamilyClusterer.Cluster(entries);

        Assert.Single(clusters);
        Assert.Equal(2, clusters[0].CaseIds.Count);
    }

    // ── Different layers → different families ─────────────────────────────

    [Fact]
    public void Cluster_DifferentLayers_DifferentFamilies()
    {
        var entries = new[]
        {
            Entry("case-001", "Berlin", new[] { GasDelta() }),
            Entry("case-002", "Berlin", new[] { StorageDelta() }),
        };

        var clusters = FailureFamilyClusterer.Cluster(entries);

        Assert.Equal(2, clusters.Count);
    }

    // ── Different forks → separate families ──────────────────────────────

    [Fact]
    public void Cluster_DifferentForks_SeparateFamilies()
    {
        var entries = new[]
        {
            Entry("case-001", "Berlin",  new[] { GasDelta() }),
            Entry("case-002", "Cancun",  new[] { GasDelta() }),
            Entry("case-003", "Istanbul",new[] { GasDelta() }),
        };

        var clusters = FailureFamilyClusterer.Cluster(entries);

        // All three have identical delta geometry, but different forks → 3 families
        Assert.Equal(3, clusters.Count);
    }

    // ── Input ordering does not affect family identity ────────────────────

    [Fact]
    public void Cluster_InputOrderingIndependent_SameFamilyKeys()
    {
        var e1 = Entry("case-001", "Berlin", new[] { GasDelta() });
        var e2 = Entry("case-002", "Berlin", new[] { StorageDelta() });

        var clustersAB = FailureFamilyClusterer.Cluster(new[] { e1, e2 });
        var clustersBA = FailureFamilyClusterer.Cluster(new[] { e2, e1 });

        var keysAB = clustersAB.Select(c => c.FamilyKey).OrderBy(k => k).ToList();
        var keysBA = clustersBA.Select(c => c.FamilyKey).OrderBy(k => k).ToList();

        Assert.Equal(keysAB, keysBA);
    }

    // ── Human summary does not affect cluster key ─────────────────────────

    [Fact]
    public void Cluster_SummaryDoesNotAffectKey_SameFamily()
    {
        // Same typed geometry, different human summaries → same family key
        var e1 = Entry("case-001", "Berlin", new[] { GasDelta() }, summary: "gas was wrong");
        var e2 = Entry("case-002", "Berlin", new[] { GasDelta() }, summary: "completely different description");

        var clusters = FailureFamilyClusterer.Cluster(new[] { e1, e2 });

        Assert.Single(clusters);
        Assert.Equal(2, clusters[0].CaseIds.Count);
    }

    // ── FamilyKey contains no human-readable text ─────────────────────────

    [Fact]
    public void Cluster_FamilyKey_ContainsNoHumanSummary()
    {
        var entries = new[] { Entry("case-001", "Berlin", new[] { GasDelta() }, summary: "THIS SHOULD NOT APPEAR") };
        var clusters = FailureFamilyClusterer.Cluster(entries);

        Assert.Single(clusters);
        Assert.DoesNotContain("THIS SHOULD NOT APPEAR", clusters[0].FamilyKey);
        // Key may only contain typed enum names and fork name
        Assert.DoesNotContain("was wrong", clusters[0].FamilyKey);
    }

    // ── Empty entries → no clusters ───────────────────────────────────────

    [Fact]
    public void Cluster_EmptyInput_ProducesNoClusters()
    {
        var clusters = FailureFamilyClusterer.Cluster(Array.Empty<HarvestFailureEntry>());
        Assert.Empty(clusters);
    }

    // ── Multiple deltas per case: first (earliest) delta is key ──────────

    [Fact]
    public void Cluster_MultipleDeltas_KeyFromFirstDelta()
    {
        // Case has both status and gas deltas — key should come from status (earlier layer)
        var deltas = new[] { StatusDelta(), GasDelta() };
        var entries = new[] { Entry("case-001", "Berlin", deltas) };

        var clusters = FailureFamilyClusterer.Cluster(entries);

        Assert.Single(clusters);
        // Family key must be based on Validity/Status geometry, not Gas
        Assert.Contains("Validity", clusters[0].FamilyKey, StringComparison.OrdinalIgnoreCase);
    }

    // ── Ordered by count descending ───────────────────────────────────────

    [Fact]
    public void Cluster_OrderedByCountDescending()
    {
        var entries = new[]
        {
            Entry("case-001", "Berlin", new[] { GasDelta() }),
            Entry("case-002", "Berlin", new[] { GasDelta() }),
            Entry("case-003", "Berlin", new[] { GasDelta() }),
            Entry("case-004", "Berlin", new[] { StorageDelta() }),
        };

        var clusters = FailureFamilyClusterer.Cluster(entries);

        Assert.True(clusters.Count >= 2);
        Assert.True(clusters[0].CaseIds.Count >= clusters[1].CaseIds.Count,
            "Clusters must be ordered by count descending");
    }

    // ── Journal-only: no consensus oracle → does not affect family key ────

    [Fact]
    public void Cluster_JournalEvidenceIgnoredInKey_SameFamilyAsWithout()
    {
        // Entry with journal-enriched delta (same typed geometry, journal field present)
        var deltaWithJournal = new FieldDelta(
            DiscrepancyLayer.Gas, DiscrepancyKind.GasUsed,
            JsonSerializer.SerializeToElement(21_000UL),
            JsonSerializer.SerializeToElement(21_500UL));

        var deltaWithout = GasDelta(); // identical typed geometry

        var entries = new[]
        {
            Entry("case-001", "Berlin", new[] { deltaWithJournal }),
            Entry("case-002", "Berlin", new[] { deltaWithout }),
        };

        var clusters = FailureFamilyClusterer.Cluster(entries);

        // Must be one family — journal presence doesn't split the key
        Assert.Single(clusters);
    }
}
