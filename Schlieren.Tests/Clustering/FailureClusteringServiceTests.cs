using Schlieren.Core.Execution.Causal;
using Xunit;

namespace Schlieren.Tests.Clustering;

/// <summary>
/// Regression tests for Core FailureClusteringService.
///
/// These protect existing callers while Task 8 extracts the typed primitive.
/// They verify that the service still groups by fingerprint correctly after
/// the Task 8 refactor.
/// </summary>
public class FailureClusteringServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static CausalDiagnosisEngine.Report MakeReport(string fingerprint, string ruleId)
    {
        var diag = new ScoredDiagnosis
        {
            RuleId         = ruleId,
            Title          = $"Test diagnosis {ruleId}",
            Phase          = ExecutionPhase.GasCharge,
            Basis          = new DiagnosisProofBasis(RuleApplicable: true, PhaseIsolated: true),
            Score          = 100,
            Why            = "test",
            Proof          = "test",
            Consequences   = "test",
            LikelyFix      = "test",
            CodeBoundary   = "test",
            Fingerprint    = fingerprint,
        };

        return new CausalDiagnosisEngine.Report
        {
            Root        = diag,
            Ranked      = new[] { diag },
            Fingerprint = fingerprint,
            FirstPhase  = ExecutionPhase.GasCharge,
        };
    }

    // ── Basic clustering ──────────────────────────────────────────────────

    [Fact]
    public void Cluster_SameFingerprint_MergesIntOneFamily()
    {
        var fp = CausalFingerprint.Build("Berlin", ExecutionPhase.GasCharge, "GAS.INITCODE_WORD");

        var entries = new[]
        {
            new FailureClusteringService.FailureEntry("case-001", "Berlin", MakeReport(fp, "GAS.INITCODE_WORD")),
            new FailureClusteringService.FailureEntry("case-002", "Berlin", MakeReport(fp, "GAS.INITCODE_WORD")),
        };

        var clusters = FailureClusteringService.Cluster(entries);

        Assert.Single(clusters);
        Assert.Equal(2, clusters[0].Count);
    }

    // ── Different fingerprints → separate families ────────────────────────

    [Fact]
    public void Cluster_DifferentFingerprints_SeparateFamilies()
    {
        var fp1 = CausalFingerprint.Build("Berlin", ExecutionPhase.GasCharge, "GAS.INITCODE_WORD");
        var fp2 = CausalFingerprint.Build("Berlin", ExecutionPhase.Refund,    "REFUND.CAP");

        var entries = new[]
        {
            new FailureClusteringService.FailureEntry("case-001", "Berlin", MakeReport(fp1, "GAS.INITCODE_WORD")),
            new FailureClusteringService.FailureEntry("case-002", "Berlin", MakeReport(fp2, "REFUND.CAP")),
        };

        var clusters = FailureClusteringService.Cluster(entries);

        Assert.Equal(2, clusters.Count);
    }

    // ── Different forks remain separate ──────────────────────────────────

    [Fact]
    public void Cluster_DifferentForks_SeparateFamilies()
    {
        var fpBerlin = CausalFingerprint.Build("Berlin",  ExecutionPhase.GasCharge, "GAS.TEST");
        var fpCancun = CausalFingerprint.Build("Cancun",  ExecutionPhase.GasCharge, "GAS.TEST");

        var entries = new[]
        {
            new FailureClusteringService.FailureEntry("case-001", "Berlin", MakeReport(fpBerlin, "GAS.TEST")),
            new FailureClusteringService.FailureEntry("case-002", "Cancun", MakeReport(fpCancun, "GAS.TEST")),
        };

        var clusters = FailureClusteringService.Cluster(entries);

        Assert.Equal(2, clusters.Count);
        Assert.Contains(clusters, c => c.Forks.Contains("Berlin"));
        Assert.Contains(clusters, c => c.Forks.Contains("Cancun"));
    }

    // ── Ordering: largest cluster first ──────────────────────────────────

    [Fact]
    public void Cluster_OrderedByCountDescending()
    {
        var fp1 = CausalFingerprint.Build("Berlin", ExecutionPhase.GasCharge, "GAS.A");
        var fp2 = CausalFingerprint.Build("Berlin", ExecutionPhase.Refund,    "REFUND.B");

        var entries = new[]
        {
            new FailureClusteringService.FailureEntry("c1", "Berlin", MakeReport(fp1, "GAS.A")),
            new FailureClusteringService.FailureEntry("c2", "Berlin", MakeReport(fp1, "GAS.A")),
            new FailureClusteringService.FailureEntry("c3", "Berlin", MakeReport(fp1, "GAS.A")),
            new FailureClusteringService.FailureEntry("c4", "Berlin", MakeReport(fp2, "REFUND.B")),
        };

        var clusters = FailureClusteringService.Cluster(entries);

        Assert.Equal(2, clusters.Count);
        Assert.Equal(3, clusters[0].Count);
        Assert.Equal(1, clusters[1].Count);
    }

    // ── Fingerprint uses typed phase and ruleId, not human text ──────────

    [Fact]
    public void CausalFingerprint_Format_TypedFieldsOnly()
    {
        var fp = CausalFingerprint.Build("Osaka", ExecutionPhase.StateMutation, "STATE.STORAGE_SLOT");

        // Must contain the fork name, the phase label, and the ruleId
        Assert.Contains("Osaka",        fp, StringComparison.Ordinal);
        Assert.Contains("STATE",        fp, StringComparison.Ordinal);
        Assert.Contains("STORAGE_SLOT", fp, StringComparison.Ordinal);
        // Must NOT contain free-form text or default "UNKNOWN"
        Assert.DoesNotContain("UNKNOWN", fp, StringComparison.Ordinal);
    }

    // ── Empty input ───────────────────────────────────────────────────────

    [Fact]
    public void Cluster_EmptyInput_ReturnsEmptyList()
    {
        var clusters = FailureClusteringService.Cluster(
            Array.Empty<FailureClusteringService.FailureEntry>());

        Assert.Empty(clusters);
    }

    // ── ClusterByTypedKey primitive: stable under input order ─────────────

    [Fact]
    public void Cluster_InputOrderIndependent_SameFamilies()
    {
        var fp = CausalFingerprint.Build("Berlin", ExecutionPhase.GasCharge, "GAS.X");

        var e1 = new FailureClusteringService.FailureEntry("case-001", "Berlin", MakeReport(fp, "GAS.X"));
        var e2 = new FailureClusteringService.FailureEntry("case-002", "Berlin", MakeReport(fp, "GAS.X"));

        var c1 = FailureClusteringService.Cluster(new[] { e1, e2 });
        var c2 = FailureClusteringService.Cluster(new[] { e2, e1 });

        Assert.Single(c1);
        Assert.Single(c2);
        Assert.Equal(c1[0].Fingerprint, c2[0].Fingerprint);
        Assert.Equal(c1[0].Count, c2[0].Count);
    }
}
