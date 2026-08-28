using Schlieren.Harvest.Campaigns;
using Schlieren.Harvest.Domain;

namespace Schlieren.Harvest.Ledger;

// ── Run record ────────────────────────────────────────────────────────────────

/// <summary>
/// Terminal status summary for all cases in a completed run.
/// Stored inside <see cref="RunRecord"/> so the ledger holds full outcome data.
/// </summary>
public sealed record RunCaseSummary(
    int PassCount,
    int DivergenceCount,
    int FixtureInvalidCount,
    int HarnessErrorCount,
    int AbortedCount,
    int QuarantinedCount)
{
    public int Total =>
        PassCount + DivergenceCount + FixtureInvalidCount +
        HarnessErrorCount + AbortedCount + QuarantinedCount;
}

/// <summary>
/// Complete record for one run — frozen into the ledger as an append-only entry.
///
/// A RunRecord is wrapped in <see cref="Schlieren.Harvest.Domain.ContentEnvelope{T}"/>
/// before writing so every entry carries a schema version, timestamp, and content hash.
///
/// RunId must be globally unique.  By convention callers use:
///   <c>{campaignId}_{createdUtcYYYYMMDDHHmmss}_{random8hex}</c>
/// but the ledger enforces uniqueness without parsing the format.
///
/// State lifecycle: Staging → (ApparatusFailed | InspectionFailed | Completed) → Certified.
/// Aborted and Quarantined are case-level statuses, not run-level states.
/// Case outcomes retain typed process-attempt evidence for apparatus failures.
/// </summary>
public sealed record RunRecord(
    string               RunId,
    string               CampaignId,
    string               CampaignVersion,
    string               ManifestHash,
    RunKind              Kind,
    RunState             State,
    DateTime             StartedUtc,
    DateTime             CompletedUtc,
    EnvironmentIdentity  Environment,
    ToolIdentity         SchlierenTool,
    EelsIdentity?        EelsOracle,
    RunCaseSummary       Summary,
    IReadOnlyList<CaseOutcome> Outcomes);

// ── Lightweight list entry ────────────────────────────────────────────────────

/// <summary>
/// Minimal listing entry returned by <see cref="RunLedger.ListRuns"/> without
/// deserializing the full case outcome payload.
/// </summary>
public sealed record RunSummaryEntry(
    string   RunId,
    string   CampaignId,
    RunKind  Kind,
    RunState State,
    DateTime CompletedUtc,
    int      TotalCases,
    int      PassCount,
    string   ContentHash);

// ── Cluster record ────────────────────────────────────────────────────────────

/// <summary>
/// Persisted cluster artifact for one failure family within a run.
/// Written to runs/{run-id}/clusters/{family-id}.json.
/// </summary>
public sealed record ClusterRecord(
    string                   FamilyId,
    string                   FamilyKey,
    string                   Fork,
    string                   PrimaryLayer,
    string                   PrimaryKind,
    int                      Count,
    IReadOnlyList<string>    CaseIds);

// ── Completion marker ─────────────────────────────────────────────────────────

/// <summary>
/// Written as the LAST artifact in a run directory to signal atomic finalization.
/// If this file is absent, the run is considered incomplete/staged.
///
/// Fields:
///   RunId             — same as the directory name
///   RunContentHash    — SHA-256 of the run.json content (for cross-verification)
///   ExpectedCaseCount — total cases declared in the manifest
///   ActualCaseCount   — cases actually written to the run directory
///   FinalizedUtc      — wall-clock timestamp of finalization
/// </summary>
public sealed record CompletionMarker(
    string   RunId,
    string   RunContentHash,
    int      ExpectedCaseCount,
    int      ActualCaseCount,
    DateTime FinalizedUtc);

// ── Ledger exceptions ─────────────────────────────────────────────────────────

/// <summary>
/// Thrown when a ledger entry fails content-hash verification on read.
/// Indicates the file was modified after it was written (bit-rot or tampering).
/// </summary>
public sealed class LedgerCorruptionException(string runId, string path, string expected, string actual)
    : Exception($"Ledger entry for run '{runId}' at '{path}' failed hash verification. " +
                $"Expected contentHash '{expected}', computed '{actual}'.")
{
    public string RunId    { get; } = runId;
    public string Path     { get; } = path;
    public string Expected { get; } = expected;
    public string Actual   { get; } = actual;
}

/// <summary>
/// Thrown when attempting to append a run whose RunId already exists in the ledger.
/// The ledger is strictly append-only; overwriting an existing entry is forbidden.
/// </summary>
public sealed class LedgerCollisionException(string runId, string path)
    : Exception($"Ledger already contains an entry for run '{runId}' at '{path}'. " +
                "The ledger is append-only; duplicate RunIds are not permitted.")
{
    public string RunId { get; } = runId;
    public string Path  { get; } = path;
}
