using Schlieren.Harvest.Campaigns;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Ledger;
using Schlieren.Harvest.Repairs;

namespace Schlieren.Harvest.Certification;

/// <summary>Typed refusal reason for certification.</summary>
public enum CertificationRefusalReason
{
    CalibrationNotPassed,
    ManifestHashMismatch,
    IncompleteCaseCount,
    DivergencesPresent,
    FixtureInvalidPresent,
    HarnessErrorPresent,
    AbortedPresent,
    QuarantinedPresent,
    OpenRepairOrders,
    DownstreamRegression,
    DirtyRepository,
    MissingSuiteGate,
    MissingEelsIdentity,
    UnverifiedContentHash
}

/// <summary>One specific refusal with context.</summary>
public sealed record CertificationRefusal(
    CertificationRefusalReason Reason,
    string                     Detail);

/// <summary>Issued when all gates pass.</summary>
public sealed record Certificate(
    string            CertificateId,
    string            RunId,
    string            ManifestHash,
    string            SchlierenCommit,
    string            EelsExecutableSha256,
    string            EelsVersion,
    string            EnvironmentDescription,
    string            RunContentHash,
    DateTime          IssuedUtc);

/// <summary>Result of a certification attempt.</summary>
public sealed record CertificationResult(
    bool                               Certified,
    Certificate?                       Certificate,
    IReadOnlyList<CertificationRefusal> Refusals);

/// <summary>
/// Validates all certification gates and issues a certificate or typed refusals.
///
/// Gates (per spec):
///   - Calibration passed
///   - Manifest hash matches run's manifest hash
///   - All declared cases completed (expected count = actual count)
///   - 50/50 exact Pass (no divergence, invalid, error, aborted, or quarantined)
///   - No open repair orders for this campaign
///   - No downstream regressions
///   - Clean repository state
///   - Three-run suite gate passed
///   - EELS identity present
///   - Content hash verifiable
/// </summary>
public sealed class CertificationService
{
    /// <summary>
    /// Attempts certification of a finalized run.
    /// Returns either a certificate (all gates green) or a list of typed refusals.
    /// </summary>
    public CertificationResult Certify(
        RunRecord run,
        string runContentHash,
        string expectedManifestHash,
        bool calibrationPassed,
        bool suiteGatePassed,
        bool repositoryClean,
        bool hasOpenRepairOrders,
        bool hasRegressions,
        int expectedCaseCount = 50)
    {
        var refusals = new List<CertificationRefusal>();

        if (!calibrationPassed)
            refusals.Add(new(CertificationRefusalReason.CalibrationNotPassed,
                "Calibration suite did not pass all 6 signals."));

        if (!string.Equals(run.ManifestHash, expectedManifestHash, StringComparison.OrdinalIgnoreCase))
            refusals.Add(new(CertificationRefusalReason.ManifestHashMismatch,
                $"Run manifest hash '{run.ManifestHash}' does not match expected '{expectedManifestHash}'."));

        if (run.Summary.Total != expectedCaseCount)
            refusals.Add(new(CertificationRefusalReason.IncompleteCaseCount,
                $"Expected {expectedCaseCount} cases, got {run.Summary.Total}."));

        if (run.Summary.DivergenceCount > 0)
            refusals.Add(new(CertificationRefusalReason.DivergencesPresent,
                $"{run.Summary.DivergenceCount} divergence(s) remain."));

        if (run.Summary.FixtureInvalidCount > 0)
            refusals.Add(new(CertificationRefusalReason.FixtureInvalidPresent,
                $"{run.Summary.FixtureInvalidCount} fixture(s) invalid."));

        if (run.Summary.HarnessErrorCount > 0)
            refusals.Add(new(CertificationRefusalReason.HarnessErrorPresent,
                $"{run.Summary.HarnessErrorCount} harness error(s)."));

        if (run.Summary.AbortedCount > 0)
            refusals.Add(new(CertificationRefusalReason.AbortedPresent,
                $"{run.Summary.AbortedCount} case(s) aborted."));

        if (run.Summary.QuarantinedCount > 0)
            refusals.Add(new(CertificationRefusalReason.QuarantinedPresent,
                $"{run.Summary.QuarantinedCount} case(s) quarantined."));

        if (hasOpenRepairOrders)
            refusals.Add(new(CertificationRefusalReason.OpenRepairOrders,
                "Open repair orders exist for this campaign."));

        if (hasRegressions)
            refusals.Add(new(CertificationRefusalReason.DownstreamRegression,
                "Downstream regressions detected."));

        if (!repositoryClean)
            refusals.Add(new(CertificationRefusalReason.DirtyRepository,
                "Repository has uncommitted changes."));

        if (!suiteGatePassed)
            refusals.Add(new(CertificationRefusalReason.MissingSuiteGate,
                "Three consecutive identical full-suite runs not recorded."));

        if (run.EelsOracle is null)
            refusals.Add(new(CertificationRefusalReason.MissingEelsIdentity,
                "Run lacks EELS oracle identity — required for certification."));

        if (string.IsNullOrWhiteSpace(runContentHash))
            refusals.Add(new(CertificationRefusalReason.UnverifiedContentHash,
                "Run content hash could not be verified."));

        if (refusals.Count > 0)
            return new CertificationResult(false, null, refusals);

        // All gates pass — issue certificate
        var cert = new Certificate(
            CertificateId:        $"cert-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
            RunId:                run.RunId,
            ManifestHash:         run.ManifestHash,
            SchlierenCommit:      run.SchlierenTool.CommitSha ?? "unknown",
            EelsExecutableSha256: run.EelsOracle!.ExecutableSha256,
            EelsVersion:          run.EelsOracle.ReportedVersion,
            EnvironmentDescription: $"{run.Environment.OsDescription} / {run.Environment.RuntimeVersion}",
            RunContentHash:       runContentHash,
            IssuedUtc:            DateTime.UtcNow);

        return new CertificationResult(true, cert, Array.Empty<CertificationRefusal>());
    }
}
