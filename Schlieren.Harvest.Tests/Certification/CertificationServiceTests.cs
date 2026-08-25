using Schlieren.Harvest.Campaigns;
using Schlieren.Harvest.Certification;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Ledger;

namespace Schlieren.Harvest.Tests.Certification;

/// <summary>
/// Proves CertificationService contracts:
///   - One refusal per gate when conditions are not met.
///   - All gates green → certificate issued.
///   - Certificate contains correct provenance fields.
/// </summary>
public class CertificationServiceTests
{
    private static RunRecord MakeRun(
        int passCount = 50,
        int divCount = 0,
        int fixtureInvalid = 0,
        int harnessError = 0,
        int aborted = 0,
        int quarantined = 0,
        EelsIdentity? eels = null)
    {
        return new RunRecord(
            "run-cert", "c1", "1", "manifest-hash",
            RunKind.Inspection, RunState.Completed,
            DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow,
            new EnvironmentIdentity("W", "8", "h", 4),
            new ToolIdentity("s", "1.0", "deadbeef", null),
            eels,
            new RunCaseSummary(passCount, divCount, fixtureInvalid, harnessError, aborted, quarantined),
            Array.Empty<CaseOutcome>());
    }

    private static readonly EelsIdentity ValidEels = new("sha256abc", "v1.0", "eelscommit");

    // ── Test 1: All green → certificate issued ────────────────────────────

    [Fact]
    public void Certify_AllGatesGreen_IssuesCertificate()
    {
        var svc    = new CertificationService();
        var run    = MakeRun(eels: ValidEels);
        var result = svc.Certify(run, "hash123",
            calibrationPassed: true, suiteGatePassed: true,
            repositoryClean: true, hasOpenRepairOrders: false, hasRegressions: false);

        Assert.True(result.Certified);
        Assert.NotNull(result.Certificate);
        Assert.Equal("run-cert", result.Certificate!.RunId);
        Assert.Equal("manifest-hash", result.Certificate.ManifestHash);
        Assert.Empty(result.Refusals);
    }

    // ── Test 2: Calibration not passed ────────────────────────────────────

    [Fact]
    public void Certify_CalibrationFailed_Refuses()
    {
        var svc    = new CertificationService();
        var run    = MakeRun(eels: ValidEels);
        var result = svc.Certify(run, "hash",
            calibrationPassed: false, suiteGatePassed: true,
            repositoryClean: true, hasOpenRepairOrders: false, hasRegressions: false);

        Assert.False(result.Certified);
        Assert.Contains(result.Refusals, r => r.Reason == CertificationRefusalReason.CalibrationNotPassed);
    }

    // ── Test 3: Incomplete case count ─────────────────────────────────────

    [Fact]
    public void Certify_IncompleteCaseCount_Refuses()
    {
        var svc    = new CertificationService();
        var run    = MakeRun(passCount: 45, eels: ValidEels);
        var result = svc.Certify(run, "hash",
            calibrationPassed: true, suiteGatePassed: true,
            repositoryClean: true, hasOpenRepairOrders: false, hasRegressions: false);

        Assert.False(result.Certified);
        Assert.Contains(result.Refusals, r => r.Reason == CertificationRefusalReason.IncompleteCaseCount);
    }

    // ── Test 4: Divergences present ───────────────────────────────────────

    [Fact]
    public void Certify_DivergencesPresent_Refuses()
    {
        var svc    = new CertificationService();
        var run    = MakeRun(passCount: 48, divCount: 2, eels: ValidEels);
        var result = svc.Certify(run, "hash",
            calibrationPassed: true, suiteGatePassed: true,
            repositoryClean: true, hasOpenRepairOrders: false, hasRegressions: false);

        Assert.False(result.Certified);
        Assert.Contains(result.Refusals, r => r.Reason == CertificationRefusalReason.DivergencesPresent);
    }

    // ── Test 5: Open repair orders ────────────────────────────────────────

    [Fact]
    public void Certify_OpenRepairs_Refuses()
    {
        var svc    = new CertificationService();
        var run    = MakeRun(eels: ValidEels);
        var result = svc.Certify(run, "hash",
            calibrationPassed: true, suiteGatePassed: true,
            repositoryClean: true, hasOpenRepairOrders: true, hasRegressions: false);

        Assert.False(result.Certified);
        Assert.Contains(result.Refusals, r => r.Reason == CertificationRefusalReason.OpenRepairOrders);
    }

    // ── Test 6: Dirty repository ──────────────────────────────────────────

    [Fact]
    public void Certify_DirtyRepo_Refuses()
    {
        var svc    = new CertificationService();
        var run    = MakeRun(eels: ValidEels);
        var result = svc.Certify(run, "hash",
            calibrationPassed: true, suiteGatePassed: true,
            repositoryClean: false, hasOpenRepairOrders: false, hasRegressions: false);

        Assert.False(result.Certified);
        Assert.Contains(result.Refusals, r => r.Reason == CertificationRefusalReason.DirtyRepository);
    }

    // ── Test 7: Missing suite gate ────────────────────────────────────────

    [Fact]
    public void Certify_MissingSuiteGate_Refuses()
    {
        var svc    = new CertificationService();
        var run    = MakeRun(eels: ValidEels);
        var result = svc.Certify(run, "hash",
            calibrationPassed: true, suiteGatePassed: false,
            repositoryClean: true, hasOpenRepairOrders: false, hasRegressions: false);

        Assert.False(result.Certified);
        Assert.Contains(result.Refusals, r => r.Reason == CertificationRefusalReason.MissingSuiteGate);
    }

    // ── Test 8: Missing EELS identity ─────────────────────────────────────

    [Fact]
    public void Certify_MissingEelsIdentity_Refuses()
    {
        var svc    = new CertificationService();
        var run    = MakeRun(eels: null);
        var result = svc.Certify(run, "hash",
            calibrationPassed: true, suiteGatePassed: true,
            repositoryClean: true, hasOpenRepairOrders: false, hasRegressions: false);

        Assert.False(result.Certified);
        Assert.Contains(result.Refusals, r => r.Reason == CertificationRefusalReason.MissingEelsIdentity);
    }

    // ── Test 9: Unverified content hash ───────────────────────────────────

    [Fact]
    public void Certify_EmptyContentHash_Refuses()
    {
        var svc    = new CertificationService();
        var run    = MakeRun(eels: ValidEels);
        var result = svc.Certify(run, "",
            calibrationPassed: true, suiteGatePassed: true,
            repositoryClean: true, hasOpenRepairOrders: false, hasRegressions: false);

        Assert.False(result.Certified);
        Assert.Contains(result.Refusals, r => r.Reason == CertificationRefusalReason.UnverifiedContentHash);
    }

    // ── Test 10: Downstream regression ────────────────────────────────────

    [Fact]
    public void Certify_DownstreamRegression_Refuses()
    {
        var svc    = new CertificationService();
        var run    = MakeRun(eels: ValidEels);
        var result = svc.Certify(run, "hash",
            calibrationPassed: true, suiteGatePassed: true,
            repositoryClean: true, hasOpenRepairOrders: false, hasRegressions: true);

        Assert.False(result.Certified);
        Assert.Contains(result.Refusals, r => r.Reason == CertificationRefusalReason.DownstreamRegression);
    }
}
