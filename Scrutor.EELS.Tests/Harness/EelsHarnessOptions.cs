namespace Scrutor.EELS.Tests.Harness;

public sealed record EelsHarnessOptions(
    string FixturesRoot,
    string ForkName,
    int MaxCases,
    bool IncludeSubdirectories,
    string? ExcludeFolder = null)
{
    public static EelsHarnessOptions FromEnvironment()
    {
        var fixturesRoot = Environment.GetEnvironmentVariable("EELS_FIXTURES_ROOT");
        if (string.IsNullOrWhiteSpace(fixturesRoot))
        {
            fixturesRoot = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "fixtures",
                "state_tests"));
        }

        var fork = Environment.GetEnvironmentVariable("EELS_REQUIRED_FORK");
        if (string.IsNullOrWhiteSpace(fork))
        {
            fork = "Cancun";
        }

        var maxCasesRaw = Environment.GetEnvironmentVariable("EELS_MAX_CASES");
        if (!int.TryParse(maxCasesRaw, out var maxCases) || maxCases <= 0)
        {
            maxCases = 25;
        }

        var includeSubdirsRaw = Environment.GetEnvironmentVariable("EELS_INCLUDE_SUBDIRS");
        var includeSubdirs = string.Equals(includeSubdirsRaw, "1", StringComparison.Ordinal) ||
                             string.Equals(includeSubdirsRaw, "true", StringComparison.OrdinalIgnoreCase);

        var excludeFolder = Environment.GetEnvironmentVariable("EELS_FIXTURES_EXCLUDE");

        return new EelsHarnessOptions(fixturesRoot, fork, maxCases, includeSubdirs, excludeFolder);
    }
}
