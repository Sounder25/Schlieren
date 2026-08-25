using System.Text.Json;

namespace Schlieren.Harvest.Execution;

/// <summary>One parsed case result from the EELS stdout array.</summary>
public sealed record EelsCaseResult(
    string  Name,
    string  Fork,
    bool    Pass,
    string  StateRoot,
    string? Error);

/// <summary>Result of parsing EELS stdout output.</summary>
public sealed record EelsParseResult(
    bool   IsSuccess,
    IReadOnlyList<EelsCaseResult> Cases,
    string? ParseError)
{
    public static EelsParseResult Failure(string error) =>
        new(false, Array.Empty<EelsCaseResult>(), error);
}

/// <summary>
/// Parses the stdout JSON array produced by:
///   ethereum-spec-evm statetest --json &lt;fixture&gt;
///
/// Output shape: a JSON array where each entry is:
///   {"name": str, "fork": str, "pass": bool, "stateRoot": str, "error"?: str}
///
/// Contracts (per Task 6 Step 3 — no guessed defaults):
///   - Nonzero exit code → HarnessError immediately (process failed).
///   - Null / empty stdout → HarnessError.
///   - Malformed JSON → HarnessError (JsonException mapped, never swallowed).
///   - Missing required "pass" or "name" field → HarnessError.
///   - No catch{}/default-success anywhere.
///   - No hard-coded gas values.
/// </summary>
public static class EelsOutputParser
{
    public static EelsParseResult Parse(string? stdout, int exitCode)
    {
        // Nonzero exit code always means the process failed
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

                // Required: "pass" (must be a boolean — not guessed from absence)
                if (!elem.TryGetProperty("pass", out var passProp) ||
                    passProp.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return EelsParseResult.Failure(
                        $"EELS result entry '{nameProp.GetString()}' missing required boolean 'pass' field");

                var fork = elem.TryGetProperty("fork", out var forkProp) &&
                           forkProp.ValueKind == JsonValueKind.String
                    ? forkProp.GetString() ?? ""
                    : "";

                var stateRoot = elem.TryGetProperty("stateRoot", out var srProp) &&
                                srProp.ValueKind == JsonValueKind.String
                    ? srProp.GetString() ?? ""
                    : "";

                string? error = null;
                if (elem.TryGetProperty("error", out var errProp) &&
                    errProp.ValueKind == JsonValueKind.String)
                    error = errProp.GetString();

                cases.Add(new EelsCaseResult(
                    Name:      nameProp.GetString()!,
                    Fork:      fork,
                    Pass:      passProp.GetBoolean(),
                    StateRoot: stateRoot,
                    Error:     error));
            }

            return new EelsParseResult(true, cases, null);
        }
    }
}
