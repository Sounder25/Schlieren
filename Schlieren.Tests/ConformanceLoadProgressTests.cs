using Schlieren.EELS.Tests.Harness;
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
        => Path.Combine(FixtureRoot(), "for_osaka", "osaka", "eip7825_transaction_gas_limit_cap");

    private static string FixtureRoot() =>
        Environment.GetEnvironmentVariable("EELS_FIXTURES_ROOT") is { Length: > 0 } configured
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "state_tests"));

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

    [Fact(DisplayName = "ExcludeFolder drops ported_static fixtures from the load")]
    public void LoadCases_ExcludePortedStatic_OmitsThatFolder()
    {
        var root = Path.Combine(
            FixtureRoot(), "for_prague", "ported_static", "stCreate2", "create2collision_code");
        Assert.True(Directory.Exists(root), root);

        var included = new EelsStateFixtureLoader().LoadCases(
            new EelsHarnessOptions(root, "Prague", int.MaxValue, true, ExcludeFolder: null));
        var excluded = new EelsStateFixtureLoader().LoadCases(
            new EelsHarnessOptions(root, "Prague", int.MaxValue, true, ExcludeFolder: "ported_static"));

        Assert.NotEmpty(included);
        Assert.Empty(excluded);
    }
}
