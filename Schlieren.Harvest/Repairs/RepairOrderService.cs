using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Ledger;
using Schlieren.Harvest.Serialization;

namespace Schlieren.Harvest.Repairs;

/// <summary>Status of a repair order through its lifecycle.</summary>
public enum RepairOrderStatus
{
    Open,
    Closed,
    NotFixed
}

/// <summary>
/// Immutable repair order record. Updates create new revisions (append-only).
/// </summary>
public sealed record RepairOrder(
    string               RepairOrderId,
    RepairOrderStatus    Status,
    string               RunId,
    string               FamilyId,
    string               FamilyKey,
    IReadOnlyList<string> AffectedCaseIds,
    DateTime             OpenedUtc,
    string?              RepairCommitSha,
    string?              PermanentTestReference,
    string?              ReinspectionRunId,
    string?              Disposition,
    DateTime?            ClosedUtc);

/// <summary>
/// Manages the repair-order lifecycle.
///
/// Contracts:
///   - Opening requires a finalized divergence cluster.
///   - Closing requires a commit SHA, permanent test reference,
///     identical-manifest reinspection run ID, and proof that the family
///     is eliminated (or an explicit non-fixed disposition).
///   - Records are append-only revisions; the open record is never edited.
/// </summary>
public sealed class RepairOrderService
{
    private readonly IRunLedger _ledger;

    public RepairOrderService(IRunLedger ledger)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    }

    /// <summary>
    /// Opens a repair order from a finalized cluster.
    ///
    /// Throws <see cref="InvalidOperationException"/> if:
    ///   - The run does not exist (not finalized).
    /// </summary>
    public RepairOrder Open(
        string runId,
        string familyId,
        string familyKey,
        IReadOnlyList<string> affectedCaseIds)
    {
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("RunId is required.", nameof(runId));
        if (string.IsNullOrWhiteSpace(familyId))
            throw new ArgumentException("FamilyId is required.", nameof(familyId));
        if (!_ledger.RunExists(runId))
            throw new InvalidOperationException(
                $"Cannot open repair order: run '{runId}' is not finalized.");

        var id = $"repair-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}";

        return new RepairOrder(
            RepairOrderId:          id,
            Status:                 RepairOrderStatus.Open,
            RunId:                  runId,
            FamilyId:               familyId,
            FamilyKey:              familyKey,
            AffectedCaseIds:        affectedCaseIds,
            OpenedUtc:              DateTime.UtcNow,
            RepairCommitSha:        null,
            PermanentTestReference: null,
            ReinspectionRunId:      null,
            Disposition:            null,
            ClosedUtc:              null);
    }

    /// <summary>
    /// Closes a repair order with evidence of resolution.
    ///
    /// Throws <see cref="InvalidOperationException"/> if:
    ///   - Required fields are missing.
    ///   - The reinspection run does not exist.
    ///   - The order is not in Open status.
    ///   - The reinspection run's manifest hash differs from the source run's.
    ///   - familyEliminated is claimed but the family still appears in the reinspection run.
    /// </summary>
    public async Task<RepairOrder> CloseAsync(
        RepairOrder order,
        string commitSha,
        string permanentTestReference,
        string reinspectionRunId,
        CancellationToken ct = default)
    {
        if (order is null) throw new ArgumentNullException(nameof(order));
        if (order.Status != RepairOrderStatus.Open)
            throw new InvalidOperationException(
                $"Cannot close repair order '{order.RepairOrderId}': status is {order.Status}, not Open.");
        if (string.IsNullOrWhiteSpace(commitSha))
            throw new ArgumentException("Commit SHA is required.", nameof(commitSha));
        if (string.IsNullOrWhiteSpace(permanentTestReference))
            throw new ArgumentException("Permanent test reference is required.", nameof(permanentTestReference));
        if (string.IsNullOrWhiteSpace(reinspectionRunId))
            throw new ArgumentException("Reinspection run ID is required.", nameof(reinspectionRunId));
        if (!_ledger.RunExists(reinspectionRunId))
            throw new InvalidOperationException(
                $"Cannot close repair: reinspection run '{reinspectionRunId}' is not finalized.");

        // Verify identical manifest hash between source run and reinspection run
        var sourceEnvelope = await _ledger.ReadRunAsync(order.RunId, ct);
        var reinspEnvelope = await _ledger.ReadRunAsync(reinspectionRunId, ct);

        if (!string.Equals(sourceEnvelope.Payload.ManifestHash,
                           reinspEnvelope.Payload.ManifestHash,
                           StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Cannot close repair: source run manifest hash '{sourceEnvelope.Payload.ManifestHash}' " +
                $"differs from reinspection run manifest hash '{reinspEnvelope.Payload.ManifestHash}'. " +
                "Repairs require identical-manifest reinspection.");

        // Determine actual family elimination from the reinspection run's outcomes
        var reinspOutcomes = reinspEnvelope.Payload.Outcomes;
        var familyStillPresent = reinspOutcomes
            .Where(o => o.Status == CaseStatus.Divergence && o.Deltas.Count > 0)
            .Any(o => order.AffectedCaseIds.Contains(o.CaseId));

        var familyEliminated = !familyStillPresent;

        return order with
        {
            Status                 = familyEliminated ? RepairOrderStatus.Closed : RepairOrderStatus.NotFixed,
            RepairCommitSha        = commitSha,
            PermanentTestReference = permanentTestReference,
            ReinspectionRunId      = reinspectionRunId,
            Disposition            = familyEliminated
                ? "Family eliminated — no affected cases diverge in reinspection"
                : "Family persists — affected cases still diverge in reinspection",
            ClosedUtc              = DateTime.UtcNow
        };
    }
}
