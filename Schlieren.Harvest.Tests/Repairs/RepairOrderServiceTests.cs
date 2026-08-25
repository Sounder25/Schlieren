using System.Text.Json;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Ledger;
using Schlieren.Harvest.Repairs;

namespace Schlieren.Harvest.Tests.Repairs;

/// <summary>
/// Proves RepairOrderService contracts:
///   - Opening requires a finalized run.
///   - Opening produces Open status with all fields.
///   - CloseAsync requires commit, test ref, reinspection run.
///   - CloseAsync verifies identical manifest hash between source and reinspection.
///   - CloseAsync determines family elimination from actual reinspection outcomes.
///   - Cannot close an already-closed order.
///   - Cannot open against non-existent run.
///   - CloseAsync with different manifest hash throws.
/// </summary>
public class RepairOrderServiceTests : IDisposable
{
    private readonly string _root;
    private readonly FileRunLedger _ledger;

    public RepairOrderServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "harvest_repair_" + Guid.NewGuid().ToString("N"));
        _ledger = new FileRunLedger(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private async Task<string> SeedRun(string runId, string manifestHash = "hash-abc",
        IReadOnlyList<CaseOutcome>? outcomes = null)
    {
        outcomes ??= Array.Empty<CaseOutcome>();
        var record = new RunRecord(runId, "c1", "1", manifestHash, RunKind.Inspection,
            RunState.InspectionFailed,
            DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow,
            new EnvironmentIdentity("W", "8", "h", 4),
            new ToolIdentity("s", "1", "a", null), null,
            new RunCaseSummary(2, 1, 0, 0, 0, 0), outcomes);
        await _ledger.FinalizeRunAsync(record, Array.Empty<CaseOutcome>(), Array.Empty<ClusterRecord>());
        return runId;
    }

    private static CaseOutcome MakeDiv(string caseId) =>
        new(caseId, CaseStatus.Divergence, new[]
        {
            new FieldDelta(DiscrepancyLayer.Gas, DiscrepancyKind.GasUsed,
                JsonSerializer.SerializeToElement(100), JsonSerializer.SerializeToElement(200))
        }, "r1", DateTime.UtcNow);

    private static CaseOutcome MakePass(string caseId) =>
        new(caseId, CaseStatus.Pass, Array.Empty<FieldDelta>(), "r1", DateTime.UtcNow);

    // ── Test 1: Open succeeds with finalized run ──────────────────────────

    [Fact]
    public async Task Open_FinalizedRun_ReturnsOpenOrder()
    {
        await SeedRun("run-1");
        var svc   = new RepairOrderService(_ledger);
        var order = svc.Open("run-1", "fam-001", "Berlin/Gas/GasUsed", new[] { "case-a" });

        Assert.Equal(RepairOrderStatus.Open, order.Status);
        Assert.Equal("run-1", order.RunId);
        Assert.Equal("fam-001", order.FamilyId);
        Assert.Contains("case-a", order.AffectedCaseIds);
    }

    // ── Test 2: Open fails for non-existent run ───────────────────────────

    [Fact]
    public void Open_NonExistentRun_Throws()
    {
        var svc = new RepairOrderService(_ledger);
        Assert.Throws<InvalidOperationException>(
            () => svc.Open("no-such-run", "fam-001", "key", new[] { "c1" }));
    }

    // ── Test 3: CloseAsync with family eliminated ─────────────────────────

    [Fact]
    public async Task CloseAsync_FamilyEliminated_StatusIsClosed()
    {
        // Source run: case-a diverges
        await SeedRun("run-src", "hash-abc", new[] { MakeDiv("case-a"), MakePass("case-b") });
        // Reinspection: case-a now passes (family eliminated)
        await SeedRun("run-reinsp", "hash-abc", new[] { MakePass("case-a"), MakePass("case-b") });

        var svc   = new RepairOrderService(_ledger);
        var order = svc.Open("run-src", "fam-001", "key", new[] { "case-a" });

        var closed = await svc.CloseAsync(order, "deadbeef", "MyTest.Passes", "run-reinsp");

        Assert.Equal(RepairOrderStatus.Closed, closed.Status);
        Assert.Equal("deadbeef", closed.RepairCommitSha);
        Assert.Equal("run-reinsp", closed.ReinspectionRunId);
        Assert.Contains("eliminated", closed.Disposition!);
    }

    // ── Test 4: CloseAsync with family persisting → NotFixed ──────────────

    [Fact]
    public async Task CloseAsync_FamilyPersists_StatusIsNotFixed()
    {
        // Source run: case-a diverges
        await SeedRun("run-src2", "hash-abc", new[] { MakeDiv("case-a") });
        // Reinspection: case-a still diverges
        await SeedRun("run-reinsp2", "hash-abc", new[] { MakeDiv("case-a") });

        var svc   = new RepairOrderService(_ledger);
        var order = svc.Open("run-src2", "fam-001", "key", new[] { "case-a" });

        var closed = await svc.CloseAsync(order, "abc123", "Test.Name", "run-reinsp2");

        Assert.Equal(RepairOrderStatus.NotFixed, closed.Status);
        Assert.Contains("persists", closed.Disposition!);
    }

    // ── Test 5: Cannot close already-closed order ─────────────────────────

    [Fact]
    public async Task CloseAsync_AlreadyClosed_Throws()
    {
        await SeedRun("run-s3", "hash-abc", new[] { MakeDiv("case-a") });
        await SeedRun("run-r3", "hash-abc", new[] { MakePass("case-a") });

        var svc    = new RepairOrderService(_ledger);
        var order  = svc.Open("run-s3", "fam-001", "key", new[] { "case-a" });
        var closed = await svc.CloseAsync(order, "abc", "T", "run-r3");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CloseAsync(closed, "def", "T2", "run-r3"));
    }

    // ── Test 6: CloseAsync requires commit SHA ────────────────────────────

    [Fact]
    public async Task CloseAsync_MissingCommit_Throws()
    {
        await SeedRun("run-s4", "hash-abc");
        await SeedRun("run-r4", "hash-abc");
        var svc   = new RepairOrderService(_ledger);
        var order = svc.Open("run-s4", "fam-001", "key", new[] { "c1" });

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CloseAsync(order, "", "T", "run-r4"));
    }

    // ── Test 7: CloseAsync requires reinspection run to exist ─────────────

    [Fact]
    public async Task CloseAsync_NonExistentReinspectionRun_Throws()
    {
        await SeedRun("run-s5", "hash-abc");
        var svc   = new RepairOrderService(_ledger);
        var order = svc.Open("run-s5", "fam-001", "key", new[] { "c1" });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CloseAsync(order, "abc", "T", "run-ghost"));
    }

    // ── Test 8: CloseAsync rejects different manifest hashes ──────────────

    [Fact]
    public async Task CloseAsync_DifferentManifestHash_Throws()
    {
        await SeedRun("run-s6", "hash-AAA");
        await SeedRun("run-r6", "hash-BBB"); // different manifest!

        var svc   = new RepairOrderService(_ledger);
        var order = svc.Open("run-s6", "fam-001", "key", new[] { "c1" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CloseAsync(order, "abc", "T", "run-r6"));
        Assert.Contains("manifest hash", ex.Message);
    }
}
