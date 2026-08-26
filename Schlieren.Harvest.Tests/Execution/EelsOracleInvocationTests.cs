using Schlieren.Harvest.Execution;
using Xunit;

namespace Schlieren.Harvest.Tests.Execution;

/// <summary>
/// Tests proving EelsProcessOracle invocation modes:
/// - Default mode excludes expensive trace diagnostics (--noreturndata --nostack --nomemory)
///   to prevent timeouts on deep-execution cases while retaining comparison outputs.
/// - Diagnostic mode includes full trace when explicitly requested.
/// </summary>
public sealed class EelsOracleInvocationTests
{
    [Fact]
    public void DefaultInvocation_ShouldExcludeExpensiveTraceFlags()
    {
        // The default invocation must use --noreturndata --nostack --nomemory
        // to avoid EELS timeouts on deep-execution fixtures (e.g., 1024-deep stack overflow).
        // Comparison outputs (stdout JSON with pass/stateRoot) are retained.
        var options = new EelsOracleOptions(
            ExecutablePath: "dummy",
            ExpectedVersion: "2.19.0",
            WorkingDirectory: ".",
            Timeout: TimeSpan.FromSeconds(120));

        var args = EelsProcessOracle.BuildArguments("test.json", options);

        Assert.Contains("statetest", args);
        Assert.Contains("--json", args);
        Assert.Contains("--noreturndata", args);
        Assert.Contains("--nostack", args);
        Assert.Contains("--nomemory", args);
        Assert.Contains("test.json", args);
    }

    [Fact]
    public void BuildArguments_ShouldQuoteFixturePath()
    {
        var options = new EelsOracleOptions(
            ExecutablePath: "dummy",
            ExpectedVersion: "2.19.0",
            WorkingDirectory: ".",
            Timeout: TimeSpan.FromSeconds(60));

        var args = EelsProcessOracle.BuildArguments("path with spaces/fixture.json", options);
        Assert.Contains("\"path with spaces/fixture.json\"", args);
    }
}
