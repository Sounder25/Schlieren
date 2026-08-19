using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using Schlieren.EELS.Tests.Harness;
using Schlieren.UI.Services;
using Xunit;

namespace Schlieren.Tests;

public sealed class ConformanceLoadProgressTests
{
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly List<T> _sink;
        public SyncProgress(List<T> sink) => _sink = sink;
        public void Report(T value) => _sink.Add(value);
    }

    private static string SmallOsakaRoot()
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "fixtures", "state_tests", "for_osaka", "osaka", "eip7825_transaction_gas_limit_cap"));

    [Fact(DisplayName = "Fixture loader reports file progress while parsing")]
    public void LoadCases_ReportsFileProgress()
    {
        var root = SmallOsakaRoot();
        Assert.True(Directory.Exists(root), root);

        var reports = new List<EelsLoadProgress>();
        var options = new EelsHarnessOptions(root, "Osaka", int.MaxValue, IncludeSubdirectories: true);
        var cases = new EelsStateFixtureLoader().LoadCases(options, new SyncProgress<EelsLoadProgress>(reports));

        Assert.NotEmpty(cases);
        Assert.NotEmpty(reports);
        Assert.True(reports[^1].FilesDone == reports[^1].FilesTotal);
        Assert.True(reports[^1].CasesLoaded >= cases.Count);
    }

    [Fact(DisplayName = "Conformance run reports a loading update before the first case result")]
    public async Task RunAsync_FirstProgressIsLoadingPhase()
    {
        var root = SmallOsakaRoot();
        Assert.True(Directory.Exists(root), root);

        var reports = new List<ConformanceProgress>();
        var result = await ConformanceRunService.RunAsync(
            root, "Osaka", new SyncProgress<ConformanceProgress>(reports), CancellationToken.None);

        Assert.True(result.Total > 0);
        Assert.NotEmpty(reports);
        Assert.Contains("Loading", reports[0].CurrentCase, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "ExcludeFolder drops ported_static fixtures from the load")]
    public void LoadCases_ExcludePortedStatic_OmitsThatFolder()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "fixtures", "state_tests", "for_prague", "ported_static", "stCreate2", "create2collision_code"));
        Assert.True(Directory.Exists(root), root);

        var included = new EelsStateFixtureLoader().LoadCases(
            new EelsHarnessOptions(root, "Prague", int.MaxValue, true, ExcludeFolder: null));
        var excluded = new EelsStateFixtureLoader().LoadCases(
            new EelsHarnessOptions(root, "Prague", int.MaxValue, true, ExcludeFolder: "ported_static"));

        Assert.NotEmpty(included);
        Assert.Empty(excluded);
    }

    private static EelsStateCase MinimalCase(string caseId) => new(
        FixturePath: $"fake/{caseId}.json",
        CaseId: caseId,
        ForkName: "Osaka",
        BlockContext: new BlockContext(),
        Sender: Address.Zero,
        Transaction: new Transaction(),
        PreState: new Dictionary<Address, EelsFixtureAccount>(),
        ExpectedPostState: new Dictionary<Address, EelsFixtureAccount>(),
        ExpectedReceiptStatus: true);

    [Fact(DisplayName = "One case throwing does not lose the rest of the batch's results")]
    public async Task RunCasesAsync_OneCaseThrows_OthersStillComplete()
    {
        // Regression test for: a crash in a single fixture's executor call used to
        // propagate through Task.WhenAll and abort the entire fork sweep — no tally,
        // no results for any other case, even ones that would have passed cleanly.
        var passCase = MinimalCase("pass-case");
        var crashCase = MinimalCase("crash-case");
        var failCase = MinimalCase("fail-case");
        var cases = new[] { passCase, crashCase, failCase };

        Task<EelsCaseExecutionReport> Execute(EelsStateCase c, CancellationToken ct) => c.CaseId switch
        {
            "pass-case" => Task.FromResult(new EelsCaseExecutionReport(
                c.CaseId, true, 21000, 0, StateMatches: true, ReceiptStatusMatches: true, Mismatches: Array.Empty<string>())),
            "crash-case" => throw new NullReferenceException("simulated executor crash — object reference not set"),
            "fail-case" => Task.FromResult(new EelsCaseExecutionReport(
                c.CaseId, true, 21000, 0, StateMatches: false, ReceiptStatusMatches: true,
                Mismatches: new[] { "balance mismatch" })),
            _ => throw new InvalidOperationException("unexpected case")
        };

        var reports = new List<ConformanceProgress>();
        var result = await ConformanceRunService.RunCasesAsync(
            cases, Execute, new SyncProgress<ConformanceProgress>(reports), CancellationToken.None);

        // The whole batch must complete — this is exactly what used to throw and never return.
        Assert.Equal(3, result.Total);
        Assert.Equal(1, result.Passed);   // pass-case
        Assert.Equal(2, result.Failed);   // crash-case (tallied as a failure) + fail-case
        Assert.Equal(result.Total, result.Passed + result.Failed);

        // The crash must be visibly reported, not silently swallowed.
        Assert.Contains(reports, r => r.CurrentCase == "crash-case" &&
            r.FailureDetail != null &&
            r.FailureDetail.Contains("crashed", StringComparison.OrdinalIgnoreCase));

        // The genuinely-failing case's real mismatch must still surface distinctly from the crash.
        Assert.Contains(reports, r => r.CurrentCase == "fail-case" &&
            r.Mismatches != null && r.Mismatches.Contains("balance mismatch"));

        // The passing case must still be reported as passed, proving isolation both ways.
        Assert.Contains(reports, r => r.CurrentCase == "pass-case" && r.FailureDetail == null);
    }

    [Fact(DisplayName = "OperationCanceledException still aborts the whole run")]
    public async Task RunCasesAsync_RealCancellation_StillPropagates()
    {
        // The crash-isolation fix must not accidentally swallow genuine cancellation —
        // only unexpected executor exceptions get tallied as failures.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Task<EelsCaseExecutionReport> Execute(EelsStateCase c, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new EelsCaseExecutionReport(
                c.CaseId, true, 0, 0, StateMatches: true, ReceiptStatusMatches: true, Mismatches: Array.Empty<string>()));
        }

        var cases = new[] { MinimalCase("case-a") };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ConformanceRunService.RunCasesAsync(
                cases, Execute, new SyncProgress<ConformanceProgress>(new List<ConformanceProgress>()), cts.Token));
    }
}
