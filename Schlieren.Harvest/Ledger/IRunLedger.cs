using Schlieren.Harvest.Domain;

namespace Schlieren.Harvest.Ledger;

/// <summary>
/// Abstraction over the Harvest run ledger for test fakes and alternate backends.
///
/// All implementations must guarantee:
///   - Append-only: no finalized record may be overwritten or deleted.
///   - Atomic finalization: a run is either absent or complete with all declared case
///     artifacts; partial runs are never discoverable via ReadRunAsync or ListRunsAsync.
///   - Integrity: every read recomputes and verifies the content hash.
/// </summary>
public interface IRunLedger
{
    /// <summary>Root directory of the ledger on disk.</summary>
    string LedgerRoot { get; }

    /// <summary>
    /// Stages a run with its per-case outcomes and cluster records, then atomically
    /// finalizes it by moving from staging to the committed path with a completion marker.
    ///
    /// <paramref name="expectedCaseCount"/> is the manifest's declared case count.
    /// Finalization throws if the actual outcome count does not match.
    ///
    /// Throws <see cref="LedgerCollisionException"/> if the run is already finalized.
    /// Throws <see cref="InvalidOperationException"/> if case count doesn't match the manifest.
    /// </summary>
    Task FinalizeRunAsync(
        RunRecord record,
        IReadOnlyList<CaseOutcome> nonPassOutcomes,
        IReadOnlyList<ClusterRecord> clusters,
        int expectedCaseCount,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a finalized run's summary record (from run.json).</summary>
    Task<ContentEnvelope<RunRecord>> ReadRunAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>Reads a specific non-pass case artifact from a finalized run.</summary>
    Task<ContentEnvelope<CaseOutcome>> ReadCaseAsync(string runId, string caseId, CancellationToken cancellationToken = default);

    /// <summary>Returns true if a finalized (not staged) run exists.</summary>
    bool RunExists(string runId);

    /// <summary>
    /// Lists all finalized runs ordered by CompletedUtc ascending.
    /// Corrupt or incomplete entries are reported separately.
    /// </summary>
    Task<LedgerListResult> ListRunsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a campaign manifest at the canonical path.
    /// Throws <see cref="LedgerCollisionException"/> if already stored.
    /// </summary>
    Task StoreManifestAsync(
        string campaignId,
        string manifestHash,
        string manifestJson,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a stored campaign manifest by campaign ID and hash.</summary>
    Task<string> ReadManifestAsync(string campaignId, string manifestHash, CancellationToken cancellationToken = default);
}
