using Schlieren.UI.ViewModels;
using Xunit;

namespace Schlieren.Tests;

public sealed class WorkbenchResultTextTests
{
    [Fact]
    public void Waiting_WhenNoRun()
    {
        var (v, e) = WorkbenchResultText.Build(false, false, "No run yet", "", "0x", Array.Empty<string>());
        Assert.Equal("WAITING", v);
        Assert.Contains("Nothing has run yet", e);
    }

    [Fact]
    public void Crash_OnInternalError()
    {
        var (v, e) = WorkbenchResultText.Build(true, false, "INTERNAL ERROR · 10,000,000 gas", "", "0x", Array.Empty<string>());
        Assert.Equal("CRASH", v);
        Assert.Contains("engine threw", e);
    }

    [Fact]
    public void Pass_EmptyReturnWithStorage()
    {
        var (v, e) = WorkbenchResultText.Build(
            true, true, "SUCCESS · 21,000 gas", "", "0x",
            new[] { "slot 0x0 = 0x1", "slot 0x1 = 0x20" });
        Assert.Equal("PASS", v);
        Assert.Contains("STORAGE", e);
    }

    [Fact]
    public void FixtureMismatch_EvenWhenEvmSucceeded()
    {
        var (v, e) = WorkbenchResultText.Build(
            true, true, "SUCCESS · 21,000 gas", "", "0x",
            Array.Empty<string>(), fixturePostMatches: false,
            fixtureNote: "2 field(s) differ.");
        Assert.Equal("MISMATCH", v);
        Assert.Contains("did not revert", e, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Conformance", e, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FixtureMatch_IsNotJustEvmSuccess()
    {
        var (v, e) = WorkbenchResultText.Build(
            true, true, "SUCCESS · 21,000 gas", "", "0x",
            Array.Empty<string>(), fixturePostMatches: true);
        Assert.Equal("MATCH", v);
        Assert.Contains("fixture", e, StringComparison.OrdinalIgnoreCase);
    }
}
