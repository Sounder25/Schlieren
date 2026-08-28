using Schlieren.Harvest.Campaigns;
using Schlieren.Harvest.Clustering;
using Schlieren.Harvest.Comparison;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Ledger;

namespace Schlieren.Harvest.Campaigns;

/// <summary>
/// Abstraction for executing a single case. Implemented by the real worker-subprocess
/// spawner in production; replaced with fakes in tests.
/// </summary>
public interface ICaseWorker
{
    /// <summary>
    /// Executes one manifest case and returns its comparison result.
    /// Never throws for case-level failures — wraps them as non-pass ComparisonResult.
    /// </summary>
    Task<ComparisonResult> ExecuteCaseAsync(
        ManifestCase manifestCase,
        string catalogRoot,
        string manifestHash,
        CancellationToken ct = default);
}

/// <summary>
/// Orchestrates execution of all cases in a frozen campaign manifest.
///
/// Contracts:
///   - Executes exactly the manifest's ordered cases, no more, no fewer.
///   - Each case reaches exactly one terminal status.
///   - One worker crash cannot suppress later case records.
///   - Cancellation finalizes an apparatus-failed record (never a certificate).
///   - Results are clustered and finalized atomically into the ledger.
/// </summary>
public sealed class CampaignRunner
{
    private readonly ICaseWorker _worker;
    private readonly IRunLedger _ledger;

    public CampaignRunner(ICaseWorker worker, IRunLedger ledger)
    {
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    }

    /// <summary>
    /// Executes a full campaign run against a frozen manifest.
    ///
    /// Returns the finalized RunId. The full run record is available
    /// via <c>_ledger.ReadRunAsync(runId)</c> after return.
    /// </summary>
    public async Task<string> RunAsync(
        CampaignManifest manifest,
        string catalogRoot,
        RunKind kind,
        EnvironmentIdentity environment,
        ToolIdentity schlierenTool,
        Campaigns.EelsIdentity? eelsOracle = null,
        CancellationToken ct = default)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));

        var runId     = GenerateRunId(manifest.CampaignId);
        var startedUtc = DateTime.UtcNow;
        var outcomes   = new List<CaseOutcome>();
        var cancelled  = false;

        foreach (var manifestCase in manifest.Cases)
        {
            if (ct.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            ComparisonResult result;
            try
            {
                result = await _worker.ExecuteCaseAsync(
                    manifestCase, catalogRoot, manifest.ManifestHash, ct);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                break;
            }
            catch (Exception ex)
            {
                // Worker crash must not suppress subsequent cases
                result = ConformanceComparator.Aborted($"Worker exception: {ex.Message}");
            }

            outcomes.Add(new CaseOutcome(
                CaseId:    manifestCase.CaseId,
                Status:    result.Status,
                Deltas:    result.Deltas,
                RunId:     runId,
                CreatedUtc: DateTime.UtcNow,
                Detail:    result.Detail,
                AttemptEvidence: result.AttemptEvidence));
        }

        // Compute summary
        var summary = ComputeSummary(outcomes);
        var state   = cancelled
            ? RunState.ApparatusFailed
            : (summary.DivergenceCount > 0 || summary.FixtureInvalidCount > 0 ||
               summary.HarnessErrorCount > 0 || summary.AbortedCount > 0)
                ? RunState.InspectionFailed
                : RunState.Completed;

        var record = new RunRecord(
            RunId:           runId,
            CampaignId:      manifest.CampaignId,
            CampaignVersion: manifest.CampaignVersion,
            ManifestHash:    manifest.ManifestHash,
            Kind:            kind,
            State:           state,
            StartedUtc:      startedUtc,
            CompletedUtc:    DateTime.UtcNow,
            Environment:     environment,
            SchlierenTool:   schlierenTool,
            EelsOracle:      eelsOracle,
            Summary:         summary,
            Outcomes:        outcomes);

        // Cluster failures
        var nonPassOutcomes = outcomes.Where(o => o.Status != CaseStatus.Pass).ToList();
        var clusters = BuildClusters(nonPassOutcomes);

        // Finalize atomically — use CancellationToken.None because the apparatus-failed
        // record must persist even when the run was cancelled.
        await _ledger.FinalizeRunAsync(record, nonPassOutcomes, clusters,
            manifest.Cases.Count, CancellationToken.None);

        return runId;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static RunCaseSummary ComputeSummary(IReadOnlyList<CaseOutcome> outcomes)
    {
        int pass = 0, div = 0, inv = 0, err = 0, abrt = 0, quar = 0;
        foreach (var o in outcomes)
        {
            switch (o.Status)
            {
                case CaseStatus.Pass:           pass++; break;
                case CaseStatus.Divergence:     div++;  break;
                case CaseStatus.FixtureInvalid: inv++;  break;
                case CaseStatus.HarnessError:   err++;  break;
                case CaseStatus.Aborted:        abrt++; break;
                case CaseStatus.Quarantined:    quar++; break;
            }
        }
        return new RunCaseSummary(pass, div, inv, err, abrt, quar);
    }

    private static IReadOnlyList<ClusterRecord> BuildClusters(IReadOnlyList<CaseOutcome> nonPass)
    {
        var divergences = nonPass
            .Where(o => o.Status == CaseStatus.Divergence && o.Deltas.Count > 0)
            .ToList();

        if (divergences.Count == 0)
            return Array.Empty<ClusterRecord>();

        var entries = divergences.Select(o => new HarvestFailureEntry(
            o.CaseId, "Unknown", o.Deltas)).ToList();

        // Group by fingerprint key
        var groups = entries
            .GroupBy(e => FailureFingerprint.FromDeltas(e.Fork, e.Deltas).Key)
            .Select((g, i) =>
            {
                var first = g.First();
                var fp    = FailureFingerprint.FromDeltas(first.Fork, first.Deltas);
                return new ClusterRecord(
                    FamilyId:     $"fam-{i:D3}",
                    FamilyKey:    fp.Key,
                    Fork:         fp.Fork,
                    PrimaryLayer: fp.PrimaryLayer.ToString(),
                    PrimaryKind:  fp.PrimaryKind.ToString(),
                    Count:        g.Count(),
                    CaseIds:      g.Select(e => e.CaseId).ToList());
            })
            .ToList();

        return groups;
    }

    private static string GenerateRunId(string campaignId)
    {
        var ts  = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var rnd = Guid.NewGuid().ToString("N")[..8];
        return $"{campaignId}_{ts}_{rnd}";
    }
}
