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
}
