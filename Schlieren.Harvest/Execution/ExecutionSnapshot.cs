using Schlieren.Core.Models;

namespace Schlieren.Harvest.Execution;

/// <summary>
/// Authority tag for an expected field in an ExecutionSnapshot.
/// Every expected field names its independent authority so the comparator
/// knows where the ground truth came from.
/// </summary>
public enum FieldAuthority
{
    FixturePostState,
    EelsExecutable,
    FixtureMetadata
}

/// <summary>One typed log entry in normalized form.</summary>
public sealed record SnapshotLog(
    string          Address,
    IReadOnlyList<string> Topics,
    string          Data);

/// <summary>One account in the normalized post-state snapshot.</summary>
public sealed record SnapshotAccount(
    string  Address,
    ulong   Nonce,
    string  Balance,
    string  Code,
    IReadOnlyDictionary<string, string> Storage);

/// <summary>
/// Normalized execution snapshot produced by either Schlieren or EELS.
///
/// Every field that carries expected-vs-actual evidence includes an
/// <see cref="FieldAuthority"/> tag naming the independent ground-truth source.
/// Journal evidence is carried separately; it helps locate causes but cannot
/// substitute for an absent EELS expected value.
/// </summary>
public sealed record ExecutionSnapshot(
    // ── Receipt fields ───────────────────────────────────────────────────
    bool   IsSuccess,
    ulong  GasUsed,
    long?  GasRefundCounter,
    string ReturnData,

    // ── Logs ─────────────────────────────────────────────────────────────
    IReadOnlyList<SnapshotLog> Logs,

    // ── Post-state ───────────────────────────────────────────────────────
    IReadOnlyList<SnapshotAccount> PostState,

    // ── Optional journal evidence (journal-on path only) ──────────────────
    object? JournalEvidence = null)
{
    public static ExecutionSnapshot Empty => new(
        IsSuccess:          false,
        GasUsed:            0,
        GasRefundCounter:   0,
        ReturnData:         "0x",
        Logs:               Array.Empty<SnapshotLog>(),
        PostState:          Array.Empty<SnapshotAccount>());
}
