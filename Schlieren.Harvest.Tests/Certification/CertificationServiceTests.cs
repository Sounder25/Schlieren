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

    private static readonly Schlieren.Harvest.Configuration.EelsSemanticIdentity ValidProvenance =
        new(
            PackageName:                "ethereum-execution",
            PackageVersion:             "2.19.0",
            SourceRepository:           "https://github.com/ethereum/execution-specs.git",
            SourceCommit:               "85aa48c742c38a2d5a876f84ebf8082a50273064",
            SourceTreeSha256:           "793296a2492e4c6f4d70679f9a73aa2d03ef19f68058465492555a37b9912c49",
            EvmToolsSha256:             "9e7ec26512f4feb9f30b76488e99ab5a3f9340b5f377249164ec1f53dc69c711",
            UvLockSha256:               "645f83fc5defbdb6a85ec5f9681abf83a33d0de18d0997b19b402487923bbfa6",
            PyprojectTomlSha256:        "f46eb6c72c82f54f9ea4374436976432e9544d8176e58d08ca1b2c907e4d3ae1",
            PythonImplementation:       "CPython",
            PythonVersion:              "3.13.11",
            RuntimePlatform:            "win32",
            InstallMode:                "editable",
            DistributionArtifactSha256: "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
            LauncherSha256:             "ee46923d2cfd47457f6324aeb5c21a5d42e363de2ed8597bbbcb69abcc56ee0f",
            IsCleanCheckout:            true,
            DependencyVersions:         new Dictionary<string, string>
            {
                ["cryptography"] = "45.0.7",
                ["py_ecc"] = "8.0.0",
                ["rlp"] = "4.0.1",
            });

    // ── Test 1: All green → certificate issued ────────────────────────────

    [Fact]
    public void Certify_AllGatesGreen_IssuesCertificate()
    {
        var svc    = new CertificationService();
        var run    = MakeRun(eels: ValidEels);
        var result = svc.Certify(run, "hash123", "manifest-hash",
            calibrationPassed: true, suiteGatePassed: true,
            repositoryClean: true, hasOpenRepairOrders: false, hasRegressions: false,
            eelsProvenance: ValidProvenance);

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
        var result = svc.Certify(run, "hash", "manifest-hash",
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
        var result = svc.Certify(run, "hash", "manifest-hash",
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
        var result = svc.Certify(run, "hash", "manifest-hash",
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
        var result = svc.Certify(run, "hash", "manifest-hash",
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
        var result = svc.Certify(run, "hash", "manifest-hash",
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
        var result = svc.Certify(run, "hash", "manifest-hash",
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
        var result = svc.Certify(run, "hash", "manifest-hash",
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
        var result = svc.Certify(run, "", "manifest-hash",
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
        var result = svc.Certify(run, "hash", "manifest-hash",
            calibrationPassed: true, suiteGatePassed: true,
            repositoryClean: true, hasOpenRepairOrders: false, hasRegressions: true);

        Assert.False(result.Certified);
        Assert.Contains(result.Refusals, r => r.Reason == CertificationRefusalReason.DownstreamRegression);
    }

    // ── Test 11: Manifest hash mismatch ───────────────────────────────────

    [Fact]
    public void Certify_ManifestHashMismatch_Refuses()
    {
        var svc    = new CertificationService();
        var run    = MakeRun(eels: ValidEels);
        var result = svc.Certify(run, "hash", "WRONG-MANIFEST-HASH",
            calibrationPassed: true, suiteGatePassed: true,
            repositoryClean: true, hasOpenRepairOrders: false, hasRegressions: false);

        Assert.False(result.Certified);
        Assert.Contains(result.Refusals, r => r.Reason == CertificationRefusalReason.ManifestHashMismatch);
    }

    // ── Test 12: Dirty EELS checkout ───────────────────────────────────────

    [Fact]
    public void Certify_DirtyEelsCheckout_Refuses()
    {
        var svc    = new CertificationService();
        var run    = MakeRun(eels: ValidEels);
        var dirtyProvenance = new Schlieren.Harvest.Configuration.EelsSemanticIdentity(
            PackageName:                "ethereum-execution",
            PackageVersion:             "2.19.0",
            SourceRepository:           "https://github.com/ethereum/execution-specs.git",
            SourceCommit:               "85aa48c742c38a2d5a876f84ebf8082a50273064",
            SourceTreeSha256:           "793296a2492e4c6f4d70679f9a73aa2d03ef19f68058465492555a37b9912c49",
            EvmToolsSha256:             "9e7ec26512f4feb9f30b76488e99ab5a3f9340b5f377249164ec1f53dc69c711",
            UvLockSha256:               "645f83fc5defbdb6a85ec5f9681abf83a33d0de18d0997b19b402487923bbfa6",
            PyprojectTomlSha256:        "f46eb6c72c82f54f9ea4374436976432e9544d8176e58d08ca1b2c907e4d3ae1",
            PythonImplementation:       "CPython",
            PythonVersion:              "3.13.11",
            RuntimePlatform:            "win32",
            InstallMode:                "editable",
            DistributionArtifactSha256: "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
            LauncherSha256:             "ee46923d2cfd47457f6324aeb5c21a5d42e363de2ed8597bbbcb69abcc56ee0f",
            IsCleanCheckout:            false,
            DependencyVersions:         new Dictionary<string, string>
            {
                ["cryptography"] = "45.0.7",
                ["py_ecc"] = "8.0.0",
                ["rlp"] = "4.0.1",
            });
        var result = svc.Certify(run, "hash", "manifest-hash",
            calibrationPassed: true, suiteGatePassed: true,
            repositoryClean: true, hasOpenRepairOrders: false, hasRegressions: false,
            eelsProvenance: dirtyProvenance);

        Assert.False(result.Certified);
        Assert.Contains(result.Refusals, r => r.Reason == CertificationRefusalReason.DirtyEelsCheckout);
    }

    // ── Test 13: Missing EELS provenance ───────────────────────────────────

    [Fact]
    public void Certify_MissingEelsProvenance_Refuses()
    {
        var svc    = new CertificationService();
        var run    = MakeRun(eels: ValidEels);
        var result = svc.Certify(run, "hash", "manifest-hash",
            calibrationPassed: true, suiteGatePassed: true,
            repositoryClean: true, hasOpenRepairOrders: false, hasRegressions: false,
            eelsProvenance: null);

        Assert.False(result.Certified);
        Assert.Contains(result.Refusals, r => r.Reason == CertificationRefusalReason.MissingEelsProvenance);
    }
}
