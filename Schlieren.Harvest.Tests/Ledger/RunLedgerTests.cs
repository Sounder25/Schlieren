using System.Text;
using System.Text.Json;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Ledger;
using Schlieren.Harvest.Serialization;

namespace Schlieren.Harvest.Tests.Ledger;

/// <summary>
/// Proves the atomic append-only run ledger contracts:
///   - Round-trip: Append → Read returns identical RunRecord.
///   - Content hash is embedded and verifiable.
///   - Collision: a second Append with the same RunId throws LedgerCollisionException.
///   - Corruption: a tampered file throws LedgerCorruptionException.
///   - Existence check (Exists) reflects committed state.
///   - ListRuns returns all committed entries ordered by CompletedUtc.
///   - ListRuns skips corrupt entries and reports them separately.
///   - Path traversal in RunId is rejected.
///   - Empty-directory listing returns empty result.
///   - RunId with leading/trailing whitespace is rejected.
///   - ReadAsync throws FileNotFoundException for unknown RunId.
///   - RunCaseSummary.Total is the sum of all status counts.
///   - LedgerDirectory is created automatically if absent.
///   - Multiple appends to the same ledger are all listed.
///   - Serialized envelope uses SchemaVersion "1".
/// </summary>
public class RunLedgerTests : IDisposable
{
    // Each test gets its own temp directory — no shared state.
    private readonly string _dir;

    public RunLedgerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "harvest_ledger_tests_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static RunRecord MakeRecord(
        string? runId   = null,
        string? campaignId = null,
        RunKind kind    = RunKind.Inspection,
        RunState state  = RunState.Completed,
        DateTime? completedUtc = null,
        int passCount   = 3,
        int divergeCount = 1)
    {
        var now = completedUtc ?? new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        return new RunRecord(
            RunId:           runId ?? "campaign1_" + Guid.NewGuid().ToString("N")[..8],
            CampaignId:      campaignId ?? "campaign-1",
            CampaignVersion: "1",
            ManifestHash:    "abc123",
            Kind:            kind,
            State:           state,
            StartedUtc:      now.AddMinutes(-5),
            CompletedUtc:    now,
            Environment:     new EnvironmentIdentity("Windows 10", "8.0.6", "build-host", 8),
            SchlierenTool:   new ToolIdentity("schlieren", "1.0.0", "deadbeef", null),
            EelsOracle:      null,
            Summary:         new RunCaseSummary(passCount, divergeCount, 0, 0, 0, 0),
            Outcomes:        Array.Empty<CaseOutcome>());
    }

    // ── Test 1: Round-trip ────────────────────────────────────────────────

    [Fact]
    public async Task Append_ThenRead_ReturnsIdenticalRecord()
    {
        var ledger = new RunLedger(_dir);
        var record = MakeRecord("run-roundtrip");

        await ledger.AppendAsync(record);
        var envelope = await ledger.ReadAsync("run-roundtrip");

        Assert.Equal("run-roundtrip",  envelope.Payload.RunId);
        Assert.Equal("campaign-1",     envelope.Payload.CampaignId);
        Assert.Equal(RunKind.Inspection, envelope.Payload.Kind);
        Assert.Equal(RunState.Completed, envelope.Payload.State);
        Assert.Equal(3, envelope.Payload.Summary.PassCount);
        Assert.Equal(1, envelope.Payload.Summary.DivergenceCount);
    }

    // ── Test 2: Content hash embedded ─────────────────────────────────────

    [Fact]
    public async Task Append_EmbedsSha256ContentHash_NotEmpty()
    {
        var ledger = new RunLedger(_dir);
        var record = MakeRecord("run-hash-check");

        await ledger.AppendAsync(record);
        var envelope = await ledger.ReadAsync("run-hash-check");

        Assert.NotEmpty(envelope.ContentHash);
        Assert.Equal(64, envelope.ContentHash.Length); // SHA-256 hex = 64 chars
        Assert.Matches("^[0-9a-f]{64}$", envelope.ContentHash);
    }

    // ── Test 3: Hash verification on read ─────────────────────────────────

    [Fact]
    public async Task Append_ThenTamper_ReadThrowsLedgerCorruptionException()
    {
        var ledger = new RunLedger(_dir);
        var record = MakeRecord("run-tampered");
        await ledger.AppendAsync(record);

        // Tamper: overwrite the committed file with modified JSON
        var path    = Path.Combine(_dir, "run-tampered.json");
        var json    = await File.ReadAllTextAsync(path);
        var tampered = json.Replace("\"campaignId\":\"campaign-1\"",
                                    "\"campaignId\":\"evil-campaign\"");
        await File.WriteAllTextAsync(path, tampered);

        await Assert.ThrowsAsync<LedgerCorruptionException>(
            () => ledger.ReadAsync("run-tampered"));
    }

    // ── Test 4: Collision prevention ──────────────────────────────────────

    [Fact]
    public async Task Append_TwiceWithSameRunId_ThrowsLedgerCollisionException()
    {
        var ledger  = new RunLedger(_dir);
        var record1 = MakeRecord("run-collision");
        var record2 = MakeRecord("run-collision");

        await ledger.AppendAsync(record1);
        await Assert.ThrowsAsync<LedgerCollisionException>(
            () => ledger.AppendAsync(record2));
    }

    // ── Test 5: Exists reflects committed state ───────────────────────────

    [Fact]
    public async Task Exists_ReturnsFalseBeforeAppend_TrueAfter()
    {
        var ledger = new RunLedger(_dir);
        Assert.False(ledger.Exists("run-exists-check"));

        await ledger.AppendAsync(MakeRecord("run-exists-check"));

        Assert.True(ledger.Exists("run-exists-check"));
    }

    // ── Test 6: ListRuns ordering ─────────────────────────────────────────

    [Fact]
    public async Task ListRuns_ReturnsEntriesOrderedByCompletedUtcAscending()
    {
        var ledger = new RunLedger(_dir);
        var t1 = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 8, 25, 11, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

        await ledger.AppendAsync(MakeRecord("run-b", completedUtc: t2));
        await ledger.AppendAsync(MakeRecord("run-a", completedUtc: t1));
        await ledger.AppendAsync(MakeRecord("run-c", completedUtc: t3));

        var result = await ledger.ListRunsAsync();

        Assert.Empty(result.CorruptEntries);
        Assert.Equal(3, result.Entries.Count);
        Assert.Equal("run-a", result.Entries[0].RunId);
        Assert.Equal("run-b", result.Entries[1].RunId);
        Assert.Equal("run-c", result.Entries[2].RunId);
    }

    // ── Test 7: ListRuns skips corrupt entries ────────────────────────────

    [Fact]
    public async Task ListRuns_SkipsCorruptEntries_ReportsThemSeparately()
    {
        var ledger = new RunLedger(_dir);
        await ledger.AppendAsync(MakeRecord("run-good"));

        // Write a corrupt (invalid JSON) file directly
        var corruptPath = Path.Combine(_dir, "run-corrupt.json");
        await File.WriteAllTextAsync(corruptPath, "{ this is not valid json ]]]");

        var result = await ledger.ListRunsAsync();

        Assert.Single(result.Entries);
        Assert.Equal("run-good", result.Entries[0].RunId);
        Assert.Single(result.CorruptEntries);
        Assert.Contains(corruptPath, result.CorruptEntries);
    }

    // ── Test 8: Path traversal rejected ──────────────────────────────────

    [Fact]
    public async Task Append_RunIdWithPathTraversal_ThrowsArgumentException()
    {
        var ledger = new RunLedger(_dir);
        var record = MakeRecord("../evil/run");
        await Assert.ThrowsAsync<ArgumentException>(() => ledger.AppendAsync(record));
    }

    // ── Test 9: Empty directory ───────────────────────────────────────────

    [Fact]
    public async Task ListRuns_EmptyDirectory_ReturnsEmptyResult()
    {
        var ledger = new RunLedger(_dir);
        var result = await ledger.ListRunsAsync();

        Assert.Empty(result.Entries);
        Assert.Empty(result.CorruptEntries);
    }

    // ── Test 10: Whitespace RunId rejected ────────────────────────────────

    [Fact]
    public async Task Append_WhitespaceRunId_ThrowsArgumentException()
    {
        var ledger = new RunLedger(_dir);
        var record = MakeRecord("   ");
        await Assert.ThrowsAsync<ArgumentException>(() => ledger.AppendAsync(record));
    }

    // ── Test 11: Read unknown RunId ───────────────────────────────────────

    [Fact]
    public async Task Read_UnknownRunId_ThrowsFileNotFoundException()
    {
        var ledger = new RunLedger(_dir);
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => ledger.ReadAsync("does-not-exist"));
    }

    // ── Test 12: RunCaseSummary.Total ─────────────────────────────────────

    [Fact]
    public void RunCaseSummary_Total_IsSumOfAllCounts()
    {
        var s = new RunCaseSummary(
            PassCount:           10,
            DivergenceCount:     2,
            FixtureInvalidCount: 1,
            HarnessErrorCount:   1,
            AbortedCount:        1,
            QuarantinedCount:    1);

        Assert.Equal(16, s.Total);
    }

    // ── Test 13: Auto-creates directory ──────────────────────────────────

    [Fact]
    public async Task Constructor_CreatesDirectoryIfAbsent()
    {
        var nested = Path.Combine(_dir, "deep", "ledger");
        Assert.False(Directory.Exists(nested));

        var ledger = new RunLedger(nested);
        Assert.True(Directory.Exists(nested));

        // Sanity: can still append after auto-create
        await ledger.AppendAsync(MakeRecord("run-autocreate"));
        Assert.True(ledger.Exists("run-autocreate"));
    }

    // ── Test 14: Multiple appends, all listed ─────────────────────────────

    [Fact]
    public async Task ListRuns_ReflectsAllAppendedEntries()
    {
        var ledger = new RunLedger(_dir);
        for (var i = 0; i < 5; i++)
        {
            var t = new DateTime(2026, 8, 25, 10 + i, 0, 0, DateTimeKind.Utc);
            await ledger.AppendAsync(MakeRecord($"run-multi-{i}", completedUtc: t));
        }

        var result = await ledger.ListRunsAsync();
        Assert.Equal(5, result.Entries.Count);
        Assert.Empty(result.CorruptEntries);
    }

    // ── Test 15: Envelope uses SchemaVersion "1" ──────────────────────────

    [Fact]
    public async Task Append_StoredEnvelope_HasSchemaVersionOne()
    {
        var ledger = new RunLedger(_dir);
        await ledger.AppendAsync(MakeRecord("run-schema-version"));

        var envelope = await ledger.ReadAsync("run-schema-version");
        Assert.Equal("1", envelope.SchemaVersion);
    }
}
