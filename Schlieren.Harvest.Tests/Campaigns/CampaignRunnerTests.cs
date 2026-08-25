using Schlieren.Harvest.Campaigns;
using Schlieren.Harvest.Comparison;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Fixtures;
using Schlieren.Harvest.Ledger;

namespace Schlieren.Harvest.Tests.Campaigns;

/// <summary>
/// Proves CampaignRunner contracts:
///   - Exactly the manifest's cases are executed in order.
///   - Each case reaches one terminal status.
///   - One worker crash does not suppress later cases.
///   - Cancellation produces apparatus-failed state.
///   - Run is finalized into the ledger with correct summary.
/// </summary>
public class CampaignRunnerTests : IDisposable
{
    private readonly string _ledgerRoot;
    private readonly FileRunLedger _ledger;

    public CampaignRunnerTests()
    {
        _ledgerRoot = Path.Combine(Path.GetTempPath(), "harvest_runner_" + Guid.NewGuid().ToString("N"));
        _ledger = new FileRunLedger(_ledgerRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_ledgerRoot))
            Directory.Delete(_ledgerRoot, recursive: true);
    }

    private static CampaignManifest MakeManifest(int caseCount = 4)
    {
        var cases = Enumerable.Range(0, caseCount)
            .Select(i => new FixtureCaseMetadata(
                $"case-{i}", $"tests/case-{i}.json", "sha256hash", "Berlin",
                new HashSet<StorageDimension> { StorageDimension.Sload },
                AdmissionReasonCode.Admitted, null))
            .ToList();

        return CampaignManifest.Freeze(
            cases, "test-campaign", DateTime.UtcNow, allowNullIdentity: true);
    }

    private static EnvironmentIdentity Env => new("Windows", "8.0", "host", 4);
    private static ToolIdentity Tool => new("schlieren", "1.0", "abc", null);

    // ── Test 1: All manifest cases are executed ───────────────────────────

    [Fact]
    public async Task RunAsync_ExecutesExactlyManifestCases()
    {
        var manifest = MakeManifest(4);
        var executed = new List<string>();
        var worker   = new FakeWorker(c => { executed.Add(c.CaseId); return Pass(); });
        var runner   = new CampaignRunner(worker, _ledger);

        var runId = await runner.RunAsync(manifest, "/tmp", RunKind.Inspection, Env, Tool);

        Assert.Equal(4, executed.Count);
        Assert.Equal(manifest.Cases.Select(c => c.CaseId), executed);
        Assert.True(_ledger.RunExists(runId));
    }

    // ── Test 2: All-pass produces Completed state ─────────────────────────

    [Fact]
    public async Task RunAsync_AllPass_StateIsCompleted()
    {
        var manifest = MakeManifest(3);
        var worker   = new FakeWorker(_ => Pass());
        var runner   = new CampaignRunner(worker, _ledger);

        var runId    = await runner.RunAsync(manifest, "/tmp", RunKind.Inspection, Env, Tool);
        var envelope = await _ledger.ReadRunAsync(runId);

        Assert.Equal(RunState.Completed, envelope.Payload.State);
        Assert.Equal(3, envelope.Payload.Summary.PassCount);
        Assert.Equal(0, envelope.Payload.Summary.DivergenceCount);
    }

    // ── Test 3: One crash does not suppress later cases ───────────────────

    [Fact]
    public async Task RunAsync_WorkerCrash_ContinuesRemaining()
    {
        var manifest = MakeManifest(3);
        var callCount = 0;
        var worker = new FakeWorker(_ =>
        {
            callCount++;
            if (callCount == 2) throw new Exception("boom");
            return Pass();
        });
        var runner = new CampaignRunner(worker, _ledger);

        var runId    = await runner.RunAsync(manifest, "/tmp", RunKind.Inspection, Env, Tool);
        var envelope = await _ledger.ReadRunAsync(runId);

        Assert.Equal(3, callCount); // all 3 executed
        Assert.Equal(2, envelope.Payload.Summary.PassCount);
        Assert.Equal(1, envelope.Payload.Summary.AbortedCount);
    }

    // ── Test 4: Cancellation → ApparatusFailed ────────────────────────────

    [Fact]
    public async Task RunAsync_Cancelled_StateIsApparatusFailed()
    {
        var manifest = MakeManifest(5);
        var cts      = new CancellationTokenSource();
        var callCount = 0;
        var worker = new FakeWorker(_ =>
        {
            callCount++;
            if (callCount == 2) cts.Cancel();
            return Pass();
        });
        var runner = new CampaignRunner(worker, _ledger);

        var runId    = await runner.RunAsync(manifest, "/tmp", RunKind.Inspection, Env, Tool, ct: cts.Token);
        var envelope = await _ledger.ReadRunAsync(runId);

        Assert.Equal(RunState.ApparatusFailed, envelope.Payload.State);
        Assert.True(envelope.Payload.Summary.Total < 5); // didn't run all
    }

    // ── Test 5: Divergence → InspectionFailed ─────────────────────────────

    [Fact]
    public async Task RunAsync_WithDivergence_StateIsInspectionFailed()
    {
        var manifest = MakeManifest(3);
        var callCount = 0;
        var worker = new FakeWorker(_ =>
        {
            callCount++;
            return callCount == 2 ? Diverge() : Pass();
        });
        var runner = new CampaignRunner(worker, _ledger);

        var runId    = await runner.RunAsync(manifest, "/tmp", RunKind.Inspection, Env, Tool);
        var envelope = await _ledger.ReadRunAsync(runId);

        Assert.Equal(RunState.InspectionFailed, envelope.Payload.State);
        Assert.Equal(2, envelope.Payload.Summary.PassCount);
        Assert.Equal(1, envelope.Payload.Summary.DivergenceCount);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static ComparisonResult Pass() =>
        new(CaseStatus.Pass, Array.Empty<FieldDelta>());

    private static ComparisonResult Diverge() =>
        new(CaseStatus.Divergence, new[]
        {
            new FieldDelta(DiscrepancyLayer.Gas, DiscrepancyKind.GasUsed,
                System.Text.Json.JsonSerializer.SerializeToElement(100),
                System.Text.Json.JsonSerializer.SerializeToElement(200))
        });

    private sealed class FakeWorker : ICaseWorker
    {
        private readonly Func<ManifestCase, ComparisonResult> _handler;

        public FakeWorker(Func<ManifestCase, ComparisonResult> handler) =>
            _handler = handler;

        public Task<ComparisonResult> ExecuteCaseAsync(
            ManifestCase manifestCase, string catalogRoot, string manifestHash,
            CancellationToken ct = default) =>
            Task.FromResult(_handler(manifestCase));
    }
}
