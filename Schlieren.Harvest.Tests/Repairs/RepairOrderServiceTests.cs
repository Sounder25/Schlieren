using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Ledger;
using Schlieren.Harvest.Repairs;

namespace Schlieren.Harvest.Tests.Repairs;

/// <summary>
/// Proves RepairOrderService contracts:
///   - Opening requires a finalized run.
///   - Opening produces Open status with all fields.
///   - Closing requires commit, test ref, reinspection run.
///   - Closing with family eliminated → Closed status.
///   - Closing with family persisting → NotFixed status.
///   - Cannot close an already-closed order.
///   - Cannot open against non-existent run.
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

    private async Task<string> SeedRun(string runId)
    {
        var record = new RunRecord(runId, "c1", "1", "hash", RunKind.Inspection,
            RunState.InspectionFailed,
            DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow,
            new EnvironmentIdentity("W", "8", "h", 4),
            new ToolIdentity("s", "1", "a", null), null,
            new RunCaseSummary(2, 1, 0, 0, 0, 0), Array.Empty<CaseOutcome>());
        await _ledger.FinalizeRunAsync(record, Array.Empty<CaseOutcome>(), Array.Empty<ClusterRecord>());
        return runId;
    }

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

    // ── Test 3: Close with elimination → Closed ───────────────────────────

    [Fact]
    public async Task Close_FamilyEliminated_StatusIsClosed()
    {
        await SeedRun("run-1");
        await SeedRun("run-2");
        var svc   = new RepairOrderService(_ledger);
        var order = svc.Open("run-1", "fam-001", "key", new[] { "c1" });

        var closed = svc.Close(order, "deadbeef", "MyTest.Passes", "run-2", familyEliminated: true);

        Assert.Equal(RepairOrderStatus.Closed, closed.Status);
        Assert.Equal("deadbeef", closed.RepairCommitSha);
        Assert.Equal("run-2", closed.ReinspectionRunId);
        Assert.NotNull(closed.ClosedUtc);
    }

    // ── Test 4: Close with family persisting → NotFixed ───────────────────

    [Fact]
    public async Task Close_FamilyPersists_StatusIsNotFixed()
    {
        await SeedRun("run-1");
        await SeedRun("run-2");
        var svc   = new RepairOrderService(_ledger);
        var order = svc.Open("run-1", "fam-001", "key", new[] { "c1" });

        var closed = svc.Close(order, "abc123", "Test.Name", "run-2", familyEliminated: false);

        Assert.Equal(RepairOrderStatus.NotFixed, closed.Status);
    }

    // ── Test 5: Cannot close already-closed order ─────────────────────────

    [Fact]
    public async Task Close_AlreadyClosed_Throws()
    {
        await SeedRun("run-1");
        await SeedRun("run-2");
        var svc   = new RepairOrderService(_ledger);
        var order = svc.Open("run-1", "fam-001", "key", new[] { "c1" });
        var closed = svc.Close(order, "abc", "T", "run-2", familyEliminated: true);

        Assert.Throws<InvalidOperationException>(
            () => svc.Close(closed, "def", "T2", "run-2", familyEliminated: true));
    }

    // ── Test 6: Close requires commit SHA ─────────────────────────────────

    [Fact]
    public async Task Close_MissingCommit_Throws()
    {
        await SeedRun("run-1");
        await SeedRun("run-2");
        var svc   = new RepairOrderService(_ledger);
        var order = svc.Open("run-1", "fam-001", "key", new[] { "c1" });

        Assert.Throws<ArgumentException>(
            () => svc.Close(order, "", "T", "run-2", familyEliminated: true));
    }

    // ── Test 7: Close requires reinspection run to exist ──────────────────

    [Fact]
    public async Task Close_NonExistentReinspectionRun_Throws()
    {
        await SeedRun("run-1");
        var svc   = new RepairOrderService(_ledger);
        var order = svc.Open("run-1", "fam-001", "key", new[] { "c1" });

        Assert.Throws<InvalidOperationException>(
            () => svc.Close(order, "abc", "T", "run-ghost", familyEliminated: true));
    }
}
