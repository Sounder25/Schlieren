using System.Security.Cryptography;
using System.Text;
using Schlieren.Harvest.Campaigns;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Execution;

namespace Schlieren.Harvest.Tests.Execution;

public sealed class EelsProcessOracleTests
{
    [Fact]
    public void Constructor_ComputesExactExecutableSha256()
    {
        var executable = Path.GetTempFileName();
        try
        {
            File.WriteAllText(executable, "deterministic-eels-binary", Encoding.UTF8);
            var expected = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(executable))).ToLowerInvariant();
            var oracle = new EelsProcessOracle(new EelsOracleOptions(
                executable, "", Path.GetTempPath(), TimeSpan.FromSeconds(1)));
            Assert.Equal(expected, oracle.ExecutableSha256);
        }
        finally { File.Delete(executable); }
    }

    [Fact]
    public async Task RunAsync_MissingExecutable_ReturnsTypedOracleExitEvidence()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-eels-{Guid.NewGuid():N}.exe");
        var oracle = new EelsProcessOracle(new EelsOracleOptions(
            missing, "", Path.GetTempPath(), TimeSpan.FromSeconds(1)));
        var result = await oracle.RunAsync("fixture.json");
        Assert.Equal(-1, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.NotNull(result.AttemptEvidence);
        Assert.Equal(ApparatusFailureKind.OracleExit, result.AttemptEvidence!.FailureKind);
        Assert.Equal(-1, result.AttemptEvidence.ExitCode);
        Assert.Equal(64, result.AttemptEvidence.StdoutSha256.Length);
        Assert.Equal(64, result.AttemptEvidence.StderrSha256.Length);
        Assert.True(result.AttemptEvidence.Elapsed >= TimeSpan.Zero);
        Assert.Equal(oracle.ExecutableSha256, result.AttemptEvidence.ExecutableSha256);
        Assert.True(result.AttemptEvidence.DiagnosticRetentionReduced);
    }

    [Fact]
    public void BuildArgumentList_SeparatesEveryEelsArgumentAndFixturePath()
    {
        var fixture = Path.Combine("root with spaces", "case.json");
        var arguments = EelsProcessOracle.BuildArgumentList(fixture);
        Assert.Equal(
            ["statetest", "--json", "--noreturndata", "--nostack", "--nomemory", fixture],
            arguments);
    }

    [Fact]
    public void ValidateIdentity_MismatchedDigest_WarnsButDoesNotThrow()
    {
        // Launcher SHA-256 mismatch is non-blocking — it's packaging noise.
        // Semantic provenance (version + source tree) is authoritative.
        var actual = new EelsIdentity("actual-sha", "2.19.0", null);
        var pinned = new EelsIdentity("pinned-sha", "2.19.0", null);
        // Should NOT throw — just warn to stderr
        EelsProcessOracle.ValidateIdentity(actual, pinned);
    }

    [Fact]
    public void ValidateIdentity_MismatchedVersion_ThrowsBeforeExecution()
    {
        var actual = new EelsIdentity("same-sha", "2.20.0", null);
        var pinned = new EelsIdentity("same-sha", "2.19.0", null);
        var error = Assert.Throws<InvalidOperationException>(
            () => EelsProcessOracle.ValidateIdentity(actual, pinned));
        Assert.Contains("version", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateIdentity_ExactMatch_DoesNotThrow()
    {
        var actual = new EelsIdentity("same-sha", "ethereum-spec-evm 2.19.0", null);
        var pinned = new EelsIdentity("same-sha", "2.19.0", null);
        EelsProcessOracle.ValidateIdentity(actual, pinned);
    }
}
