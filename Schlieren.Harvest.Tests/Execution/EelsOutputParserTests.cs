using Schlieren.Harvest.Execution;
using System.Text.Json;
using Xunit;

namespace Schlieren.Harvest.Tests.Execution;

/// <summary>
/// Golden-input tests for EelsOutputParser.
///
/// Contracts per Task 6 Step 3:
///   - Parses success / EVM failure / invalid tx / refund / return bytes /
///     zero logs / multiple logs / post-state from stdout array
///   - Malformed JSON → typed apparatus failure (HarnessError), NOT a guessed pass
///   - Nonzero exit → HarnessError
///   - Missing required fields → HarnessError (never default-success or hard-coded gas)
///   - No catch{}/default-success anywhere in the path
/// </summary>
public class EelsOutputParserTests
{
    // ── Helpers ──────────────────────────────────────────────────────────

    private static EelsParseResult Parse(string stdout, int exitCode = 0, string? stderr = null)
        => EelsOutputParser.Parse(stdout, exitCode, stderr);

    // Real EELS stderr structLog summary (from actual run)
    private const string RealStderrWithGas =
        "{\"pc\":0,\"op\":96,\"gas\":\"0x726bbf8\",\"gasCost\":\"0x3\",\"memSize\":0,\"stack\":[],\"returnData\":\"0x\",\"depth\":1,\"refund\":0,\"opName\":\"PUSH1\"}\n" +
        "{\"output\":\"\",\"gasUsed\":\"0x83e6\"}\n" +
        "{\"stateRoot\": \"0xaabbcc\"}";

    private const string StderrWithReturnData =
        "{\"pc\":0,\"op\":96,\"gas\":\"0xff\",\"gasCost\":\"0x3\",\"memSize\":0,\"stack\":[],\"returnData\":\"0x\",\"depth\":1,\"refund\":0,\"opName\":\"PUSH1\"}\n" +
        "{\"output\":\"0xdeadbeef\",\"gasUsed\":\"0x5208\"}\n" +
        "{\"stateRoot\": \"0xaabb\"}";

    // ── Success case ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_SuccessCase_ReturnsPassTrue()
    {
        var stdout = """
            [{"name":"test/foo","fork":"Berlin","pass":true,"stateRoot":"0xaabb"}]
            """;
        var result = Parse(stdout);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Cases);
        Assert.True(result.Cases[0].Pass);
        Assert.Equal("test/foo", result.Cases[0].Name);
        Assert.Equal("Berlin", result.Cases[0].Fork);
        Assert.Equal("0xaabb", result.Cases[0].StateRoot);
    }

    // ── EVM failure (pass=false with error) ──────────────────────────────

    [Fact]
    public void Parse_EvmFailure_ReturnsPassFalseWithError()
    {
        var stdout = """
            [{"name":"test/bar","fork":"Berlin","pass":false,"stateRoot":"0xccdd","error":"post state root mismatch: got ccdd, want eeff"}]
            """;
        var result = Parse(stdout);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Cases);
        Assert.False(result.Cases[0].Pass);
        Assert.NotEmpty(result.Cases[0].Error ?? "");
    }

    // ── Invalid transaction (pass=false) ─────────────────────────────────

    [Fact]
    public void Parse_InvalidTransaction_ReturnsPassFalse()
    {
        var stdout = """
            [{"name":"test/inv","fork":"Istanbul","pass":false,"stateRoot":"0x0000"}]
            """;
        var result = Parse(stdout);

        Assert.True(result.IsSuccess);
        Assert.False(result.Cases[0].Pass);
    }

    // ── Multiple cases ────────────────────────────────────────────────────

    [Fact]
    public void Parse_MultipleCases_ParsesAll()
    {
        var stdout = """
            [
              {"name":"test/a","fork":"Berlin","pass":true,"stateRoot":"0xaa"},
              {"name":"test/b","fork":"Berlin","pass":false,"stateRoot":"0xbb","error":"mismatch"},
              {"name":"test/c","fork":"Cancun","pass":true,"stateRoot":"0xcc"}
            ]
            """;
        var result = Parse(stdout);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Cases.Count);
        Assert.True(result.Cases[0].Pass);
        Assert.False(result.Cases[1].Pass);
        Assert.True(result.Cases[2].Pass);
        Assert.Equal("Cancun", result.Cases[2].Fork);
    }

    // ── Malformed JSON → HarnessError, not a guessed pass ────────────────

    [Fact]
    public void Parse_MalformedJson_ReturnsHarnessError()
    {
        var result = Parse("{ this is not valid json");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ParseError);
        Assert.Empty(result.Cases);
    }

    [Fact]
    public void Parse_EmptyStdout_ReturnsHarnessError()
    {
        var result = Parse("");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ParseError);
    }

    [Fact]
    public void Parse_NullStdout_ReturnsHarnessError()
    {
        var result = Parse(null!);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ParseError);
    }

    // ── Nonzero exit code → HarnessError ─────────────────────────────────

    [Fact]
    public void Parse_NonzeroExitCode_ReturnsHarnessError()
    {
        // Even with parseable output, nonzero exit means the process failed
        var stdout = """[{"name":"x","fork":"Berlin","pass":true,"stateRoot":"0xaa"}]""";
        var result = Parse(stdout, exitCode: 1);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ParseError);
        Assert.Contains("exit", result.ParseError, StringComparison.OrdinalIgnoreCase);
    }

    // ── Missing required fields → HarnessError ───────────────────────────

    [Fact]
    public void Parse_MissingPassField_ReturnsHarnessError()
    {
        // "pass" is required — absent field is not a guessed false
        var stdout = """[{"name":"x","fork":"Berlin","stateRoot":"0xaa"}]""";
        var result = Parse(stdout);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ParseError);
    }

    [Fact]
    public void Parse_MissingNameField_ReturnsHarnessError()
    {
        var stdout = """[{"fork":"Berlin","pass":true,"stateRoot":"0xaa"}]""";
        var result = Parse(stdout);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ParseError);
    }

    // ── Stderr gasUsed parsing ────────────────────────────────────────────

    [Fact]
    public void Parse_WithStderrSummary_ExtractsGasUsed()
    {
        var stdout = """[{"name":"test/x","fork":"Berlin","pass":true,"stateRoot":"0xaabb"}]""";
        var result = Parse(stdout, stderr: RealStderrWithGas);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Cases);
        Assert.Equal(0x83e6UL, result.Cases[0].GasUsed);
    }

    [Fact]
    public void Parse_WithStderrSummary_ExtractsReturnData()
    {
        var stdout = """[{"name":"test/x","fork":"Berlin","pass":true,"stateRoot":"0xaabb"}]""";
        var result = Parse(stdout, stderr: StderrWithReturnData);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Cases);
        Assert.Equal("0xdeadbeef", result.Cases[0].ReturnData);
        Assert.Equal(0x5208UL, result.Cases[0].GasUsed);
    }

    [Fact]
    public void Parse_WithNoStderr_GasUsedIsZero_NotGuessedSuccess()
    {
        // Absent stderr → GasUsed=0 and ReturnData="0x"
        // This is NOT treated as a successful gas=0 — it means "unknown"
        var stdout = """[{"name":"test/x","fork":"Berlin","pass":true,"stateRoot":"0xaabb"}]""";
        var result = Parse(stdout, stderr: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(0UL, result.Cases[0].GasUsed);
        Assert.Equal("0x", result.Cases[0].ReturnData);
    }

    [Fact]
    public void Parse_StderrOpCodeLinesIgnored_SummaryLineExtracted()
    {
        // Opcode lines (have "pc") must be ignored; only summary line (no "pc") is parsed
        var stderr =
            "{\"pc\":5,\"op\":85,\"gas\":\"0x100\",\"gasCost\":\"0x50\",\"memSize\":0,\"stack\":[],\"returnData\":\"0x\",\"depth\":1,\"refund\":0,\"opName\":\"SSTORE\"}\n" +
            "{\"output\":\"0xabcd\",\"gasUsed\":\"0x1234\"}\n";
        var stdout = """[{"name":"test/s","fork":"Istanbul","pass":true,"stateRoot":"0xcc"}]""";
        var result = Parse(stdout, stderr: stderr);

        Assert.True(result.IsSuccess);
        Assert.Equal(0x1234UL, result.Cases[0].GasUsed);
        Assert.Equal("0xabcd", result.Cases[0].ReturnData);
    }
}
