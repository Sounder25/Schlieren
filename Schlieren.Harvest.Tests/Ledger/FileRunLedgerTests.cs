using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Ledger;
using Schlieren.Harvest.Serialization;

namespace Schlieren.Harvest.Tests.Ledger;

/// <summary>
/// Proves the hierarchical FileRunLedger contracts:
///   - Staged finalization: writes to staging/, moves to runs/ atomically.
///   - Completion marker is written last; absent marker = not discoverable.
///   - Non-pass case artifacts are persisted per-case under runs/{id}/cases/.
///   - Cluster artifacts are persisted under runs/{id}/clusters/.
///   - Collision on duplicate RunId.
///   - ListRuns only shows finalized (completed) runs.
///   - Manifest storage and retrieval.
///   - Tampered run.json detected on read.
///   - Path traversal in RunId rejected.
///   - Unknown RunId throws FileNotFoundException.
///   - Empty ledger lists cleanly.
/// </summary>
public class FileRunLedgerTests : IDisposable
{
    private readonly string _root;

    public FileRunLedgerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "harvest_file_ledger_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static RunRecord MakeRecord(
        string? runId = null,
        int passCount = 3,
        int divergeCount = 1,
        DateTime? completedUtc = null)
    {
        var now = completedUtc ?? new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        return new RunRecord(
            RunId:           runId ?? "run-" + Guid.NewGuid().ToString("N")[..8],
            CampaignId:      "campaign-1",
            CampaignVersion: "1",
            ManifestHash:    "abc123def456",
            Kind:            RunKind.Inspection,
            State:           RunState.Completed,
            StartedUtc:      now.AddMinutes(-5),
            CompletedUtc:    now,
            Environment:     new EnvironmentIdentity("Windows 10", "8.0.6", "build-host", 8),
            SchlierenTool:   new ToolIdentity("schlieren", "1.0.0", "deadbeef", null),
            EelsOracle:      null,
            Summary:         new RunCaseSummary(passCount, divergeCount, 0, 0, 0, 0),
            Outcomes:        Array.Empty<CaseOutcome>());
    }

    private static CaseOutcome MakeDivergence(string caseId) =>
        new(caseId, CaseStatus.Divergence,
            new[] { new FieldDelta(DiscrepancyLayer.Gas, DiscrepancyKind.GasUsed,
                System.Text.Json.JsonSerializer.SerializeToElement(100),
                System.Text.Json.JsonSerializer.SerializeToElement(200)) },
            "run-test", DateTime.UtcNow, "gas mismatch");

    private static ClusterRecord MakeCluster(string familyId, params string[] caseIds) =>
        new(familyId, "Berlin/Gas/GasUsed", "Berlin", "Gas", "GasUsed",
            caseIds.Length, caseIds);

    // ── Test 1: Round-trip finalization ────────────────────────────────────

    [Fact]
    public async Task FinalizeRun_ThenReadRun_ReturnsIdenticalRecord()
    {
        var ledger = new FileRunLedger(_root);
        var record = MakeRecord("run-rt");

        await ledger.FinalizeRunAsync(record, Array.Empty<CaseOutcome>(), Array.Empty<ClusterRecord>(), record.Summary.Total);
        var envelope = await ledger.ReadRunAsync("run-rt");

        Assert.Equal("run-rt", envelope.Payload.RunId);
        Assert.Equal("campaign-1", envelope.Payload.CampaignId);
        Assert.Equal(RunState.Completed, envelope.Payload.State);
    }

    // ── Test 2: Completion marker exists ──────────────────────────────────

    [Fact]
    public async Task FinalizeRun_WritesCompletionMarker()
    {
        var ledger = new FileRunLedger(_root);
        await ledger.FinalizeRunAsync(MakeRecord("run-marker"),
            Array.Empty<CaseOutcome>(), Array.Empty<ClusterRecord>(), 4);

        var markerPath = LedgerPaths.CompletionPath(_root, "run-marker");
        Assert.True(File.Exists(markerPath));

        var json   = await File.ReadAllTextAsync(markerPath);
        var marker = HarvestJson.Deserialize<CompletionMarker>(json);
        Assert.NotNull(marker);
        Assert.Equal("run-marker", marker!.RunId);
    }

    // ── Test 3: Non-pass case artifacts ───────────────────────────────────

    [Fact]
    public async Task FinalizeRun_WritesPerCaseArtifactsForNonPass()
    {
        var ledger  = new FileRunLedger(_root);
        var record  = MakeRecord("run-cases");
        var outcome = MakeDivergence("tests/berlin/storage_test");

        await ledger.FinalizeRunAsync(record, new[] { outcome }, Array.Empty<ClusterRecord>(), record.Summary.Total);

        var caseEnvelope = await ledger.ReadCaseAsync("run-cases", "tests/berlin/storage_test");
        Assert.Equal("tests/berlin/storage_test", caseEnvelope.Payload.CaseId);
        Assert.Equal(CaseStatus.Divergence, caseEnvelope.Payload.Status);
    }

    // ── Test 4: Cluster artifacts ─────────────────────────────────────────

    [Fact]
    public async Task FinalizeRun_WritesClusterArtifacts()
    {
        var ledger  = new FileRunLedger(_root);
        var record  = MakeRecord("run-clusters");
        var cluster = MakeCluster("fam-gas-001", "case-a", "case-b");

        await ledger.FinalizeRunAsync(record, Array.Empty<CaseOutcome>(), new[] { cluster }, record.Summary.Total);

        var clusterPath = Path.Combine(
            LedgerPaths.RunDir(_root, "run-clusters"),
            LedgerPaths.ClustersDir,
            "fam-gas-001.json");
        Assert.True(File.Exists(clusterPath));
    }

    // ── Test 5: Collision prevention ──────────────────────────────────────

    [Fact]
    public async Task FinalizeRun_Twice_ThrowsCollision()
    {
        var ledger = new FileRunLedger(_root);
        var record = MakeRecord("run-dup");

        await ledger.FinalizeRunAsync(record, Array.Empty<CaseOutcome>(), Array.Empty<ClusterRecord>(), record.Summary.Total);
        await Assert.ThrowsAsync<LedgerCollisionException>(
            () => ledger.FinalizeRunAsync(record, Array.Empty<CaseOutcome>(), Array.Empty<ClusterRecord>(), record.Summary.Total));
    }

    // ── Test 6: Staging cleanup on interrupted run ────────────────────────

    [Fact]
    public async Task ListRuns_IgnoresIncompleteStagingDirectories()
    {
        var ledger = new FileRunLedger(_root);
        await ledger.FinalizeRunAsync(MakeRecord("run-good"),
            Array.Empty<CaseOutcome>(), Array.Empty<ClusterRecord>(), 4);

        // Simulate an incomplete staging leftover in runs/ (no completion marker)
        var fakeRunDir = LedgerPaths.RunDir(_root, "run-incomplete");
        Directory.CreateDirectory(fakeRunDir);
        await File.WriteAllTextAsync(
            Path.Combine(fakeRunDir, LedgerPaths.RunFile), "{}");
        // No complete.json written

        var result = await ledger.ListRunsAsync();
        Assert.Single(result.Entries);
        Assert.Equal("run-good", result.Entries[0].RunId);
    }

    // ── Test 7: RunExists ─────────────────────────────────────────────────

    [Fact]
    public async Task RunExists_FalseBeforeFinalize_TrueAfter()
    {
        var ledger = new FileRunLedger(_root);
        Assert.False(ledger.RunExists("run-exist"));

        await ledger.FinalizeRunAsync(MakeRecord("run-exist"),
            Array.Empty<CaseOutcome>(), Array.Empty<ClusterRecord>(), 4);

        Assert.True(ledger.RunExists("run-exist"));
    }

    // ── Test 8: Manifest storage ──────────────────────────────────────────

    [Fact]
    public async Task StoreManifest_ThenReadManifest_RoundTrips()
    {
        var ledger = new FileRunLedger(_root);
        var json   = "{\"schemaVersion\":\"1\",\"campaignId\":\"c1\"}";

        await ledger.StoreManifestAsync("campaign-1", "abc123", json);
        var read = await ledger.ReadManifestAsync("campaign-1", "abc123");

        Assert.Equal(json, read);
    }

    // ── Test 9: Manifest collision ────────────────────────────────────────

    [Fact]
    public async Task StoreManifest_Twice_ThrowsCollision()
    {
        var ledger = new FileRunLedger(_root);
        await ledger.StoreManifestAsync("c1", "hash1", "{}");

        await Assert.ThrowsAsync<LedgerCollisionException>(
            () => ledger.StoreManifestAsync("c1", "hash1", "{}"));
    }

    // ── Test 10: Tampered run.json detected ───────────────────────────────

    [Fact]
    public async Task ReadRun_TamperedRunJson_ThrowsCorruptionException()
    {
        var ledger = new FileRunLedger(_root);
        await ledger.FinalizeRunAsync(MakeRecord("run-tamper"),
            Array.Empty<CaseOutcome>(), Array.Empty<ClusterRecord>(), 4);

        var runPath = LedgerPaths.RunPath(_root, "run-tamper");
        var json    = await File.ReadAllTextAsync(runPath);
        var bad     = json.Replace("\"campaign-1\"", "\"evil\"");
        await File.WriteAllTextAsync(runPath, bad);

        await Assert.ThrowsAsync<LedgerCorruptionException>(
            () => ledger.ReadRunAsync("run-tamper"));
    }

    // ── Test 11: Path traversal rejected ──────────────────────────────────

    [Fact]
    public async Task FinalizeRun_PathTraversal_ThrowsArgumentException()
    {
        var ledger = new FileRunLedger(_root);
        var record = MakeRecord("../evil");
        await Assert.ThrowsAsync<ArgumentException>(
            () => ledger.FinalizeRunAsync(record, Array.Empty<CaseOutcome>(), Array.Empty<ClusterRecord>(), record.Summary.Total));
    }

    // ── Test 12: Unknown RunId ────────────────────────────────────────────

    [Fact]
    public async Task ReadRun_UnknownRunId_ThrowsFileNotFound()
    {
        var ledger = new FileRunLedger(_root);
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => ledger.ReadRunAsync("no-such-run"));
    }

    // ── Test 13: Empty ledger ─────────────────────────────────────────────

    [Fact]
    public async Task ListRuns_EmptyLedger_ReturnsEmpty()
    {
        var ledger = new FileRunLedger(_root);
        var result = await ledger.ListRunsAsync();
        Assert.Empty(result.Entries);
        Assert.Empty(result.CorruptEntries);
    }

    // ── Test 14: ListRuns ordered by CompletedUtc ─────────────────────────

    [Fact]
    public async Task ListRuns_OrderedByCompletedUtc()
    {
        var ledger = new FileRunLedger(_root);
        var t1 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        await ledger.FinalizeRunAsync(MakeRecord("run-b", completedUtc: t2),
            Array.Empty<CaseOutcome>(), Array.Empty<ClusterRecord>(), 4);
        await ledger.FinalizeRunAsync(MakeRecord("run-a", completedUtc: t1),
            Array.Empty<CaseOutcome>(), Array.Empty<ClusterRecord>(), 4);
        await ledger.FinalizeRunAsync(MakeRecord("run-c", completedUtc: t3),
            Array.Empty<CaseOutcome>(), Array.Empty<ClusterRecord>(), 4);

        var result = await ledger.ListRunsAsync();
        Assert.Equal(3, result.Entries.Count);
        Assert.Equal("run-a", result.Entries[0].RunId);
        Assert.Equal("run-b", result.Entries[1].RunId);
        Assert.Equal("run-c", result.Entries[2].RunId);
    }

    // ── Test 15: Staging dir does not survive successful finalization ──────

    [Fact]
    public async Task FinalizeRun_StagingDirRemovedAfterSuccess()
    {
        var ledger = new FileRunLedger(_root);
        await ledger.FinalizeRunAsync(MakeRecord("run-staging"),
            Array.Empty<CaseOutcome>(), Array.Empty<ClusterRecord>(), 4);

        var stagingDir = LedgerPaths.StagingDir(_root, "run-staging");
        Assert.False(Directory.Exists(stagingDir));
    }
}
