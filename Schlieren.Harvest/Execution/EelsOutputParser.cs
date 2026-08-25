using System.Text.Json;

namespace Schlieren.Harvest.Execution;

/// <summary>
/// One parsed case result from the EELS stdout summary array.
/// Enriched with gas and return data from stderr structLog.
/// </summary>
public sealed record EelsCaseResult(
    string  Name,
    string  Fork,
    bool    Pass,
    string  StateRoot,
    string? Error,
    // From stderr structLog summary line {"output":"...","gasUsed":"0x..."}
    ulong   GasUsed     = 0,
    string  ReturnData  = "0x");

/// <summary>Result of parsing EELS stdout + stderr output.</summary>
public sealed record EelsParseResult(
    bool   IsSuccess,
    IReadOnlyList<EelsCaseResult> Cases,
    string? ParseError)
{
    public static EelsParseResult Failure(string error) =>
        new(false, Array.Empty<EelsCaseResult>(), error);
}

/// <summary>
/// Parses the output produced by:
///   ethereum-spec-evm statetest --json &lt;fixture&gt;
///
/// Output layout:
///   stdout — final JSON array: [{"name", "fork", "pass", "stateRoot", "error"?}]
///   stderr — NDJSON structLog lines (one JSON object per line):
///            - opcode trace: {"pc":N, "op":N, ..., "opName":"..."}  (has "pc" key)
///            - summary line: {"output":"0x...", "gasUsed":"0x..."}  (no "pc" key)
///            - stateRoot:    {"stateRoot": "0x..."}
///
/// The summary line is the sole source for gasUsed and returnData.
/// The stdout array is the sole source for pass/fail, stateRoot, fork, and name.
///
/// Contracts (no guessed defaults):
///   - Nonzero exit → HarnessError.
///   - Empty/null stdout → HarnessError.
///   - Malformed stdout JSON → HarnessError.
///   - Missing required "pass" or "name" field → HarnessError.
///   - If stderr is absent/malformed, gasUsed=0 and returnData="0x" (not guessed success).
///   - No hard-coded gas values anywhere.
/// </summary>
public static class EelsOutputParser
{
    /// <summary>
    /// Parse EELS output. <paramref name="stderr"/> is used to extract gasUsed and
    /// returnData from the structLog summary line; it is optional — absence does not
    /// fail the parse but leaves those fields at their zero-value defaults.
    /// </summary>
    public static EelsParseResult Parse(string? stdout, int exitCode, string? stderr = null)
    {
        // Nonzero exit always means the process failed
        if (exitCode != 0)
            return EelsParseResult.Failure($"EELS process exited with code {exitCode}");

        if (string.IsNullOrWhiteSpace(stdout))
            return EelsParseResult.Failure("EELS produced no output (empty stdout)");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(stdout.Trim());
        }
        catch (JsonException ex)
        {
            return EelsParseResult.Failure($"EELS stdout is not valid JSON: {ex.Message}");
        }

        // Parse gasUsed and returnData from stderr structLog summary line
        var (stderrGas, stderrReturn) = ParseStderrSummary(stderr);

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return EelsParseResult.Failure("EELS stdout root is not a JSON array");

            var cases = new List<EelsCaseResult>();

            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                // Required: "name"
                if (!elem.TryGetProperty("name", out var nameProp) ||
                    nameProp.ValueKind != JsonValueKind.String ||
                    string.IsNullOrEmpty(nameProp.GetString()))
                    return EelsParseResult.Failure("EELS result entry missing required 'name' field");

                // Required: "pass" (must be boolean — not guessed from absence)
                if (!elem.TryGetProperty("pass", out var passProp) ||
                    passProp.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return EelsParseResult.Failure(
                        $"EELS result entry '{nameProp.GetString()}' missing required boolean 'pass' field");

                var fork = elem.TryGetProperty("fork", out var forkProp) &&
                           forkProp.ValueKind == JsonValueKind.String
                    ? forkProp.GetString() ?? "" : "";

                var stateRoot = elem.TryGetProperty("stateRoot", out var srProp) &&
                                srProp.ValueKind == JsonValueKind.String
                    ? srProp.GetString() ?? "" : "";

                string? error = null;
                if (elem.TryGetProperty("error", out var errProp) &&
                    errProp.ValueKind == JsonValueKind.String)
                    error = errProp.GetString();

                cases.Add(new EelsCaseResult(
                    Name:       nameProp.GetString()!,
                    Fork:       fork,
                    Pass:       passProp.GetBoolean(),
                    StateRoot:  stateRoot,
                    Error:      error,
                    GasUsed:    stderrGas,
                    ReturnData: stderrReturn));
            }

            return new EelsParseResult(true, cases, null);
        }
    }

    // ── stderr summary parsing ────────────────────────────────────────────

    /// <summary>
    /// Extract gasUsed and output (returnData) from the stderr structLog.
    /// The summary line has "output" and "gasUsed" but NOT "pc".
    /// Returns (0, "0x") when not found — callers must not treat 0 as "correct".
    /// </summary>
    private static (ulong GasUsed, string ReturnData) ParseStderrSummary(string? stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return (0, "0x");

        foreach (var rawLine in stderr.Split('\n', '\r'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || !line.StartsWith('{')) continue;

            JsonDocument lineDoc;
            try { lineDoc = JsonDocument.Parse(line); }
            catch { continue; }

            using (lineDoc)
            {
                var root = lineDoc.RootElement;
                // Summary line has "gasUsed" and "output" but NOT "pc"
                if (root.TryGetProperty("gasUsed", out var guProp) &&
                    root.TryGetProperty("output",  out var outProp) &&
                    !root.TryGetProperty("pc", out _))
                {
                    var gasStr = guProp.GetString() ?? "0x0";
                    var ret    = outProp.GetString() ?? "";
                    var gas    = ParseHexUlong(gasStr);
                    var retData = string.IsNullOrEmpty(ret) ? "0x"
                                 : ret.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? ret
                                 : "0x" + ret;
                    return (gas, retData);
                }
            }
        }

        return (0, "0x");
    }

    private static ulong ParseHexUlong(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex is "0x" or "0x0") return 0;
        var s = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        return ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
    }
}
