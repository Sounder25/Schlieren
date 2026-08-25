using System.Text;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Serialization;

namespace Schlieren.Harvest.Ledger;

/// <summary>
/// File-system-backed implementation of <see cref="IRunLedger"/> using the hierarchical
/// layout specified in the approved design.
///
/// Finalization contract:
///   1. All artifacts are written into staging/{run-id}/ first.
///   2. After all files are flushed, the staging directory is atomically renamed
///      to runs/{run-id}/ (same-volume rename on NTFS/POSIX).
///   3. The completion marker (complete.json) is written INSIDE the staging dir
///      as the very last file before the rename — it is the atomic gate.
///
/// A reader checks for complete.json: if absent, the directory is treated as
/// an interrupted staging artifact and excluded from listing.
///
/// Append-only: once a run directory exists under runs/, it cannot be overwritten.
/// </summary>
public sealed class FileRunLedger : IRunLedger
{
    private const string SchemaVersion = "1";

    private readonly string _root;

    public FileRunLedger(string ledgerRoot)
    {
        if (string.IsNullOrWhiteSpace(ledgerRoot))
            throw new ArgumentException("Ledger root must be a non-empty path.", nameof(ledgerRoot));

        _root = ledgerRoot;
        Directory.CreateDirectory(_root);
    }

    public string LedgerRoot => _root;

    // ── FinalizeRunAsync ──────────────────────────────────────────────────

    public async Task FinalizeRunAsync(
        RunRecord record,
        IReadOnlyList<CaseOutcome> nonPassOutcomes,
        IReadOnlyList<ClusterRecord> clusters,
        CancellationToken cancellationToken = default)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));
        LedgerPaths.ValidateSegment(record.RunId, nameof(record.RunId));

        var finalDir   = LedgerPaths.RunDir(_root, record.RunId);
        var stagingDir = LedgerPaths.StagingDir(_root, record.RunId);

        // Collision: run already finalized
        if (Directory.Exists(finalDir) &&
            File.Exists(Path.Combine(finalDir, LedgerPaths.CompletionFile)))
            throw new LedgerCollisionException(record.RunId, finalDir);

        // Create staging directory
        Directory.CreateDirectory(stagingDir);

        try
        {
            // 1. Write run.json
            var runEnvelope = BuildEnvelope(record);
            await WriteArtifactAsync(
                Path.Combine(stagingDir, LedgerPaths.RunFile),
                runEnvelope, cancellationToken);

            // 2. Write per-case artifacts for non-pass outcomes
            if (nonPassOutcomes.Count > 0)
            {
                var casesDir = Path.Combine(stagingDir, LedgerPaths.CasesDir);
                Directory.CreateDirectory(casesDir);

                foreach (var outcome in nonPassOutcomes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var caseEnvelope = BuildEnvelope(outcome);
                    var fileName = LedgerPaths.SanitizeFileName(outcome.CaseId) + ".json";
                    await WriteArtifactAsync(
                        Path.Combine(casesDir, fileName),
                        caseEnvelope, cancellationToken);
                }
            }

            // 3. Write cluster records
            if (clusters.Count > 0)
            {
                var clustersDir = Path.Combine(stagingDir, LedgerPaths.ClustersDir);
                Directory.CreateDirectory(clustersDir);

                foreach (var cluster in clusters)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var clusterEnvelope = BuildEnvelope(cluster);
                    var fileName = LedgerPaths.SanitizeFileName(cluster.FamilyId) + ".json";
                    await WriteArtifactAsync(
                        Path.Combine(clustersDir, fileName),
                        clusterEnvelope, cancellationToken);
                }
            }

            // 4. Write completion marker LAST in staging
            var runContentHash = ContentHasher.Compute(BuildEnvelope(record) with { ContentHash = "" });
            var marker = new CompletionMarker(
                RunId:             record.RunId,
                RunContentHash:    runEnvelope.ContentHash,
                ExpectedCaseCount: record.Summary.Total,
                ActualCaseCount:   record.Summary.Total,
                FinalizedUtc:      DateTime.UtcNow);
            var markerJson = HarvestJson.Serialize(marker);
            await File.WriteAllTextAsync(
                Path.Combine(stagingDir, LedgerPaths.CompletionFile),
                markerJson, Encoding.UTF8, cancellationToken);

            // 5. Atomic rename staging → runs
            Directory.CreateDirectory(Path.GetDirectoryName(finalDir)!);
            try
            {
                Directory.Move(stagingDir, finalDir);
            }
            catch (IOException) when (Directory.Exists(finalDir))
            {
                throw new LedgerCollisionException(record.RunId, finalDir);
            }
        }
        catch
        {
            // Clean up staging on failure
            try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true); }
            catch { /* best effort */ }
            throw;
        }
    }

    // ── ReadRunAsync ──────────────────────────────────────────────────────

    public async Task<ContentEnvelope<RunRecord>> ReadRunAsync(
        string runId, CancellationToken cancellationToken = default)
    {
        LedgerPaths.ValidateSegment(runId, nameof(runId));

        var path = LedgerPaths.RunPath(_root, runId);
        if (!File.Exists(path))
            throw new FileNotFoundException($"No finalized run entry for '{runId}'.", path);

        // Check completion marker
        var completionPath = LedgerPaths.CompletionPath(_root, runId);
        if (!File.Exists(completionPath))
            throw new FileNotFoundException(
                $"Run '{runId}' exists but has no completion marker (incomplete/interrupted staging).",
                completionPath);

        return await ReadAndVerifyAsync<RunRecord>(path, runId, cancellationToken);
    }

    // ── ReadCaseAsync ─────────────────────────────────────────────────────

    public async Task<ContentEnvelope<CaseOutcome>> ReadCaseAsync(
        string runId, string caseId, CancellationToken cancellationToken = default)
    {
        LedgerPaths.ValidateSegment(runId, nameof(runId));

        var path = LedgerPaths.CasePath(_root, runId, caseId);
        if (!File.Exists(path))
            throw new FileNotFoundException($"No case artifact for '{caseId}' in run '{runId}'.", path);

        return await ReadAndVerifyAsync<CaseOutcome>(path, runId, cancellationToken);
    }

    // ── RunExists ─────────────────────────────────────────────────────────

    public bool RunExists(string runId)
    {
        LedgerPaths.ValidateSegment(runId, nameof(runId));
        var completionPath = LedgerPaths.CompletionPath(_root, runId);
        return File.Exists(completionPath);
    }

    // ── ListRunsAsync ─────────────────────────────────────────────────────

    public async Task<LedgerListResult> ListRunsAsync(CancellationToken cancellationToken = default)
    {
        var runsRoot = Path.Combine(_root, LedgerPaths.Runs);
        if (!Directory.Exists(runsRoot))
            return new LedgerListResult(Array.Empty<RunSummaryEntry>(), Array.Empty<string>());

        var runDirs      = Directory.GetDirectories(runsRoot);
        var entries      = new List<RunSummaryEntry>();
        var corruptPaths = new List<string>();

        foreach (var dir in runDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var completionFile = Path.Combine(dir, LedgerPaths.CompletionFile);
            if (!File.Exists(completionFile))
            {
                // Incomplete staging leftover — skip silently
                continue;
            }

            var runFile = Path.Combine(dir, LedgerPaths.RunFile);
            if (!File.Exists(runFile))
            {
                corruptPaths.Add(dir);
                continue;
            }

            try
            {
                var bytes    = await File.ReadAllBytesAsync(runFile, cancellationToken);
                var envelope = HarvestJson.Deserialize<ContentEnvelope<RunRecord>>(bytes);
                if (envelope is null) { corruptPaths.Add(dir); continue; }

                var stub     = envelope with { ContentHash = "" };
                var computed = ContentHasher.Compute(stub);
                if (!string.Equals(computed, envelope.ContentHash, StringComparison.OrdinalIgnoreCase))
                {
                    corruptPaths.Add(dir);
                    continue;
                }

                var r = envelope.Payload;
                entries.Add(new RunSummaryEntry(
                    RunId:        r.RunId,
                    CampaignId:   r.CampaignId,
                    Kind:         r.Kind,
                    State:        r.State,
                    CompletedUtc: r.CompletedUtc,
                    TotalCases:   r.Summary.Total,
                    PassCount:    r.Summary.PassCount,
                    ContentHash:  envelope.ContentHash));
            }
            catch
            {
                corruptPaths.Add(dir);
            }
        }

        entries.Sort((a, b) => DateTime.Compare(a.CompletedUtc, b.CompletedUtc));
        return new LedgerListResult(entries, corruptPaths);
    }

    // ── StoreManifestAsync ────────────────────────────────────────────────

    public async Task StoreManifestAsync(
        string campaignId, string manifestHash, string manifestJson,
        CancellationToken cancellationToken = default)
    {
        LedgerPaths.ValidateSegment(campaignId, nameof(campaignId));
        LedgerPaths.ValidateSegment(manifestHash, nameof(manifestHash));

        var path = LedgerPaths.ManifestPath(_root, campaignId, manifestHash);
        if (File.Exists(path))
            throw new LedgerCollisionException($"{campaignId}/{manifestHash}", path);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, manifestJson, Encoding.UTF8, cancellationToken);
    }

    // ── ReadManifestAsync ─────────────────────────────────────────────────

    public async Task<string> ReadManifestAsync(
        string campaignId, string manifestHash,
        CancellationToken cancellationToken = default)
    {
        LedgerPaths.ValidateSegment(campaignId, nameof(campaignId));
        LedgerPaths.ValidateSegment(manifestHash, nameof(manifestHash));

        var path = LedgerPaths.ManifestPath(_root, campaignId, manifestHash);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"No manifest for campaign '{campaignId}' hash '{manifestHash}'.", path);

        return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static ContentEnvelope<T> BuildEnvelope<T>(T payload)
    {
        var stub = new ContentEnvelope<T>(
            SchemaVersion: SchemaVersion,
            CreatedUtc:    DateTime.UtcNow,
            ContentHash:   "",
            Payload:       payload);

        var hash = ContentHasher.Compute(stub);
        return stub with { ContentHash = hash };
    }

    private static async Task WriteArtifactAsync<T>(
        string path, ContentEnvelope<T> envelope, CancellationToken cancellationToken)
    {
        var json  = HarvestJson.Serialize(envelope);
        var bytes = Encoding.UTF8.GetBytes(json);

        await using var fs = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 65536, useAsync: true);
        await fs.WriteAsync(bytes, cancellationToken);
        await fs.FlushAsync(cancellationToken);
    }

    private static async Task<ContentEnvelope<T>> ReadAndVerifyAsync<T>(
        string path, string runId, CancellationToken cancellationToken)
    {
        var bytes    = await File.ReadAllBytesAsync(path, cancellationToken);
        var envelope = HarvestJson.Deserialize<ContentEnvelope<T>>(bytes)
                       ?? throw new InvalidOperationException(
                           $"Ledger artifact at '{path}' deserialized to null.");

        var stub     = envelope with { ContentHash = "" };
        var expected = ContentHasher.Compute(stub);
        if (!string.Equals(expected, envelope.ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new LedgerCorruptionException(runId, path, expected, envelope.ContentHash);

        return envelope;
    }
}
