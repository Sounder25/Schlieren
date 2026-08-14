namespace Schlieren.EELS.Tests.Harness;

public sealed class EelsSelfDestructRevertTests
{
    [Fact]
    public async Task RevertedChild_DoesNotKeepAddressesOrSlotsWarm()
    {
        var testCase = LoadCase("outer_selfdestruct_after_inner_call-same_tx");
        var report = await new EelsStateFixtureExecutor().ExecuteAsync(testCase);

        Assert.True(
            report.StateMatches,
            string.Join(Environment.NewLine, report.Mismatches));
    }

    [Fact]
    public async Task InsufficientBalanceCall_ChargesNetValueCallCost()
    {
        var testCase = LoadCase(
            "outer_selfdestruct_before_inner_call-not_same_tx-init_balance_2");
        var report = await new EelsStateFixtureExecutor().ExecuteAsync(testCase);

        Assert.True(
            report.StateMatches,
            string.Join(Environment.NewLine, report.Mismatches));
    }

    private static EelsStateCase LoadCase(string casePattern)
    {
        var fixtureRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "fixtures", "state_tests", "cancun", "eip6780_selfdestruct",
            "selfdestruct_revert"));

        var options = new EelsHarnessOptions(
            fixtureRoot,
            "Cancun",
            int.MaxValue,
            IncludeSubdirectories: false);

        return new EelsStateFixtureLoader()
            .LoadCases(options)
            .Single(testCase => testCase.CaseId.Contains(
                casePattern,
                StringComparison.Ordinal));
    }
}
