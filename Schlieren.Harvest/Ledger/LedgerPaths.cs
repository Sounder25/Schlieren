namespace Schlieren.Harvest.Ledger;

/// <summary>
/// Canonical path conventions for the hierarchical Harvest ledger.
///
/// Layout (from spec):
///   harvest/ledger/
///     campaigns/{campaign-id}/{manifest-hash}/manifest.json
///     runs/{run-id}/run.json
///     runs/{run-id}/cases/{case-id}.json
///     runs/{run-id}/clusters/{family-id}.json
///     runs/{run-id}/complete.json
///     staging/{run-id}/                          ← transient, removed after finalization
///     repairs/{repair-order-id}.json
///     comparisons/{before-run}--{after-run}.json
///     certificates/{certificate-id}.json
///     calibrations/{calibration-id}.json
///     reports/{run-id}.md
/// </summary>
public static class LedgerPaths
{
    // ── Top-level segments ─────────────────────────────────────────────────

    public const string Campaigns     = "campaigns";
    public const string Runs          = "runs";
    public const string Staging       = "staging";
    public const string Repairs       = "repairs";
    public const string Comparisons   = "comparisons";
    public const string Certificates  = "certificates";
    public const string Calibrations  = "calibrations";
    public const string Reports       = "reports";

    // ── Per-run segments ──────────────────────────────────────────────────

    public const string RunFile         = "run.json";
    public const string CasesDir        = "cases";
    public const string ClustersDir     = "clusters";
    public const string CompletionFile  = "complete.json";

    // ── Path builders ─────────────────────────────────────────────────────

    /// <summary>campaigns/{campaignId}/{manifestHash}/manifest.json</summary>
    public static string ManifestPath(string ledgerRoot, string campaignId, string manifestHash) =>
        Path.Combine(ledgerRoot, Campaigns, campaignId, manifestHash, "manifest.json");

    /// <summary>runs/{runId}/run.json</summary>
    public static string RunPath(string ledgerRoot, string runId) =>
        Path.Combine(ledgerRoot, Runs, runId, RunFile);

    /// <summary>runs/{runId}/</summary>
    public static string RunDir(string ledgerRoot, string runId) =>
        Path.Combine(ledgerRoot, Runs, runId);

    /// <summary>runs/{runId}/cases/{caseId}.json</summary>
    public static string CasePath(string ledgerRoot, string runId, string caseId) =>
        Path.Combine(ledgerRoot, Runs, runId, CasesDir, SanitizeFileName(caseId) + ".json");

    /// <summary>runs/{runId}/clusters/{familyId}.json</summary>
    public static string ClusterPath(string ledgerRoot, string runId, string familyId) =>
        Path.Combine(ledgerRoot, Runs, runId, ClustersDir, SanitizeFileName(familyId) + ".json");

    /// <summary>runs/{runId}/complete.json</summary>
    public static string CompletionPath(string ledgerRoot, string runId) =>
        Path.Combine(ledgerRoot, Runs, runId, CompletionFile);

    /// <summary>staging/{runId}/</summary>
    public static string StagingDir(string ledgerRoot, string runId) =>
        Path.Combine(ledgerRoot, Staging, runId);

    /// <summary>repairs/{repairOrderId}.json</summary>
    public static string RepairPath(string ledgerRoot, string repairOrderId) =>
        Path.Combine(ledgerRoot, Repairs, repairOrderId + ".json");

    /// <summary>comparisons/{beforeRun}--{afterRun}.json</summary>
    public static string ComparisonPath(string ledgerRoot, string beforeRun, string afterRun) =>
        Path.Combine(ledgerRoot, Comparisons, $"{beforeRun}--{afterRun}.json");

    /// <summary>certificates/{certificateId}.json</summary>
    public static string CertificatePath(string ledgerRoot, string certificateId) =>
        Path.Combine(ledgerRoot, Certificates, certificateId + ".json");

    /// <summary>calibrations/{calibrationId}.json</summary>
    public static string CalibrationPath(string ledgerRoot, string calibrationId) =>
        Path.Combine(ledgerRoot, Calibrations, calibrationId + ".json");

    /// <summary>reports/{runId}.md</summary>
    public static string ReportPath(string ledgerRoot, string runId) =>
        Path.Combine(ledgerRoot, Reports, runId + ".md");

    // ── Validation ────────────────────────────────────────────────────────

    /// <summary>
    /// Validates that an identifier is safe for use as a directory/file name segment.
    /// Throws <see cref="ArgumentException"/> on path traversal or invalid chars.
    /// </summary>
    public static void ValidateSegment(string segment, string paramName)
    {
        if (string.IsNullOrWhiteSpace(segment))
            throw new ArgumentException($"{paramName} must be a non-empty string.", paramName);

        if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException(
                $"{paramName} '{segment}' contains characters that are invalid in file names.", paramName);

        if (segment.Contains("..") || segment.Contains('/') || segment.Contains('\\'))
            throw new ArgumentException(
                $"{paramName} '{segment}' contains path traversal characters.", paramName);
    }

    /// <summary>
    /// Replaces characters unsafe for filenames with underscores, and truncates to
    /// 200 characters to stay well within Windows MAX_PATH limits.
    /// Used for case IDs which may contain slashes (e.g., "tests/berlin/...").
    /// </summary>
    public static string SanitizeFileName(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = input.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == '/' || chars[i] == '\\')
                chars[i] = '_';
        }
        var sanitized = new string(chars);
        // Truncate to 200 chars to stay within Windows MAX_PATH
        if (sanitized.Length > 200)
            sanitized = sanitized[..200];
        return sanitized;
    }
}
