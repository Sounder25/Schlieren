using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Inspect;

namespace Schlieren.Tests.Execution.Inspect;

public sealed class ProtocolDiagnosisReportTests
{
    [Fact]
    public void GasMismatch_RemainingSurplus_ReportsActualWarmExpectedCold()
    {
        // EIP-3155 `gas` is remaining gas. +2600 remaining vs the reference means
        // Schlieren charged 2600 less (warm) than the expected cold access.
        var actual = Step(op: "BALANCE", remaining: 7_000, cost: 100);
        var expected = Step(op: "BALANCE", remaining: 4_400, cost: 100);

        var div = TraceDivergenceLocator.Compare([actual], [expected]);
        Assert.Equal("GasMismatch", div.Category);
        Assert.Equal(-2600, div.GasDelta);

        var report = ProtocolDiagnosisReportBuilder.FromTraceDivergence(div);
        Assert.Equal("cold", report.ExpectedState);
        Assert.Equal("warm", report.ActualState);
        Assert.Equal(-2600, report.GasDelta);
    }

    [Fact]
    public void GasMismatch_RemainingDeficit_ReportsActualColdExpectedWarm()
    {
        var actual = Step(op: "BALANCE", remaining: 4_400, cost: 100);
        var expected = Step(op: "BALANCE", remaining: 7_000, cost: 100);

        var div = TraceDivergenceLocator.Compare([actual], [expected]);
        Assert.Equal("GasMismatch", div.Category);
        Assert.Equal(2600, div.GasDelta);

        var report = ProtocolDiagnosisReportBuilder.FromTraceDivergence(div);
        Assert.Equal("warm", report.ExpectedState);
        Assert.Equal("cold", report.ActualState);
    }

    [Fact]
    public void GasCostMismatch_HigherCost_ReportsActualColdExpectedWarm()
    {
        var actual = Step(op: "BALANCE", remaining: 65_536, cost: 2_600);
        var expected = Step(op: "BALANCE", remaining: 65_536, cost: 100);

        var div = TraceDivergenceLocator.Compare([actual], [expected]);
        Assert.Equal("GasCostMismatch", div.Category);
        Assert.Equal(2500, div.GasDelta);

        var report = ProtocolDiagnosisReportBuilder.FromTraceDivergence(div);
        Assert.Equal("warm (100)", report.ExpectedState);
        Assert.Equal("cold (2600)", report.ActualState);
    }

    private static ExecutionTraceStep Step(string op, long remaining, long cost) => new()
    {
        Pc = 0,
        Op = op,
        Gas = $"0x{remaining:x}",
        GasCost = $"0x{cost:x}",
        Depth = 1
    };
}
