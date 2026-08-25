using System.Text.Json;

namespace Schlieren.Harvest.Domain;

// ── Case-level status ─────────────────────────────────────────────────────────

/// <summary>Terminal status for one executed campaign case.</summary>
public enum CaseStatus
{
    Pass,
    Divergence,
    FixtureInvalid,
    HarnessError,
    Aborted,
    Quarantined
}

// ── Discrepancy classification ────────────────────────────────────────────────

/// <summary>The output layer where a discrepancy was observed.</summary>
public enum DiscrepancyLayer
{
    Validity,
    Receipt,
    Gas,
    ReturnData,
    Logs,
    Account,
    Storage,
    Journal
}

/// <summary>The specific field or measurement that diverged.</summary>
public enum DiscrepancyKind
{
    Status,
    GasUsed,
    RefundCounter,
    ReturnData,
    LogCount,
    LogAddress,
    LogTopics,
    LogData,
    AccountExistence,
    Nonce,
    Balance,
    Code,
    StorageValue,
    JournalConservation
}

// ── Run classification ────────────────────────────────────────────────────────

/// <summary>The purpose of a campaign run.</summary>
public enum RunKind
{
    Calibration,
    Inspection,
    Reinspection
}

/// <summary>The lifecycle state of a campaign run.</summary>
public enum RunState
{
    Staging,
    ApparatusFailed,
    InspectionFailed,
    Completed,
    Certified
}

// ── Typed comparison evidence ─────────────────────────────────────────────────

/// <summary>The independently-grounded expected value for one output field.</summary>
public sealed record ExpectedValue<T>(T Value, string Authority);

/// <summary>The actual value produced by Schlieren for one output field.</summary>
public sealed record ActualValue<T>(T Value);

/// <summary>
/// One typed discrepancy between Schlieren's output and the independent authority.
/// Expected and Actual are serialized as raw JSON elements to preserve typed fidelity
/// without forcing a generic parameter on the persisted record.
/// </summary>
public sealed record FieldDelta(
    DiscrepancyLayer Layer,
    DiscrepancyKind  Kind,
    JsonElement      Expected,
    JsonElement      Actual);

// ── Case outcome ──────────────────────────────────────────────────────────────

/// <summary>Complete terminal result for one executed case.</summary>
public sealed record CaseOutcome(
    string           CaseId,
    CaseStatus       Status,
    IReadOnlyList<FieldDelta> Deltas,
    string           RunId,
    DateTime         CreatedUtc,
    string?          Detail = null);

// ── Environment and tool identity ─────────────────────────────────────────────

/// <summary>Identifies the runtime environment in which a run was performed.</summary>
public sealed record EnvironmentIdentity(
    string OsDescription,
    string RuntimeVersion,
    string MachineName,
    int    ProcessorCount);

/// <summary>Identifies a pinned tool (EELS executable, Schlieren commit, etc.).</summary>
public sealed record ToolIdentity(
    string Name,
    string Version,
    string? CommitSha,
    string? Sha256);

// ── Append-only ledger envelope ───────────────────────────────────────────────

/// <summary>
/// Wraps every persisted ledger artifact. <c>ContentHash</c> is computed by
/// <see cref="Schlieren.Harvest.Serialization.ContentHasher"/> and must be
/// excluded from the bytes that are hashed.
/// </summary>
public sealed record ContentEnvelope<T>(
    string   SchemaVersion,
    DateTime CreatedUtc,
    string   ContentHash,
    T        Payload);
