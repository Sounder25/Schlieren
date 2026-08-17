using Schlieren.EELS.Tests.Harness;

namespace Schlieren.EELS.Tests.Suites;

/// <summary>
/// Osaka all_opcodes from the moved v20 state_tests tree (not the repo-root copy).
/// </summary>
public sealed class AllOpcodesMovedFolderTests
{
    private static readonly string FixtureRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "fixtures", "state_tests", "for_osaka", "frontier", "opcodes", "all_opcodes"));

    [Fact]
    public async Task MovedFolder_Osaka_AllOpcodes_MatchesPost()
    {
        Assert.True(Directory.Exists(FixtureRoot), $"missing moved folder: {FixtureRoot}");

        var options = new EelsHarnessOptions(
            FixtureRoot,
            "Osaka",
            int.MaxValue,
            IncludeSubdirectories: true);

        var cases = new EelsStateFixtureLoader().LoadCases(options);
        Assert.True(cases.Count > 0, $"no Osaka cases in {FixtureRoot}");

        var executor = new EelsStateFixtureExecutor();
        var failed = new List<string>();
        var passed = 0;
        foreach (var testCase in cases)
        {
            var report = await executor.ExecuteAsync(testCase);
            if (report.StateMatches && report.ReceiptStatusMatches)
            {
                passed++;
                continue;
            }

            failed.Add(
                $"{testCase.CaseId}  mismatches={report.Mismatches.Count}\n    " +
                string.Join("\n    ", report.Mismatches.Take(6)));
        }

        Assert.True(
            failed.Count == 0,
            $"folder={FixtureRoot}\npassed={passed} failed={failed.Count} total={cases.Count}\n" +
            string.Join("\n", failed.Take(20)));
    }
}
