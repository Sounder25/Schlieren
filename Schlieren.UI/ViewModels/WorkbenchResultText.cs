namespace Schlieren.UI.ViewModels;

public static class WorkbenchResultText
{
    public static (string Verdict, string Explain) Build(
        bool hasTrace,
        bool lastRunSuccess,
        string resultBanner,
        string errorText,
        string returnDataHex,
        IReadOnlyList<string> storageRows,
        bool? fixturePostMatches = null,
        string? fixtureNote = null)
    {
        if (!hasTrace && resultBanner.StartsWith("No run", StringComparison.OrdinalIgnoreCase))
        {
            return ("WAITING",
                "Nothing has run yet. Open or paste bytecode, set the fork (Osaka for 0x100), paste calldata if needed, then press RUN or F5.");
        }

        if (resultBanner.Contains("INTERNAL", StringComparison.OrdinalIgnoreCase)
            || errorText.Contains("InternalError", StringComparison.OrdinalIgnoreCase)
            || errorText.Contains("exception", StringComparison.OrdinalIgnoreCase))
        {
            return ("CRASH",
                "The engine threw. This is not a contract revert. Check the fork matches the opcode/precompile (P256VERIFY needs Osaka) and read the red error line.");
        }

        // Fixture expected post is the same check Conformance uses. EVM "success"
        // only means no revert — it is not a suite pass.
        if (fixturePostMatches == false)
        {
            var evm = lastRunSuccess ? "The EVM did not revert." : "The EVM reverted or OOGed.";
            var extra = string.IsNullOrWhiteSpace(fixtureNote) ? "" : " " + fixtureNote;
            return ("MISMATCH",
                evm + " Post-state does not match the fixture expected accounts (same check as Conformance)." + extra);
        }

        if (fixturePostMatches == true)
        {
            return ("MATCH",
                "Post-state matches the fixture expected accounts — same pass condition as Conformance. EVM revert/success is separate.");
        }

        if (!lastRunSuccess)
        {
            var why = string.IsNullOrWhiteSpace(errorText)
                ? "The transaction reverted or ran out of gas. No fixture expected-post was loaded, so this is EVM outcome only."
                : errorText;
            return ("FAIL", why);
        }

        var emptyReturn = string.IsNullOrWhiteSpace(returnDataHex)
                          || returnDataHex.Equals("0x", StringComparison.OrdinalIgnoreCase);
        var hasStorage = storageRows.Count > 0
                         && !storageRows.All(s => s.Contains("empty", StringComparison.OrdinalIgnoreCase));

        if (emptyReturn && hasStorage)
        {
            return ("PASS",
                "Transaction succeeded. This contract saved its result in STORAGE, not return data. Empty return data is normal. Copy STORAGE below.");
        }

        if (emptyReturn)
        {
            return ("PASS",
                "Transaction succeeded with no return data. If you expected output, scrub to the last step and check STORAGE.");
        }

        return ("PASS",
            "Transaction succeeded. Return data below is the contract output.");
    }

    public static string JoinOrEmpty(IEnumerable<string> rows, string empty)
    {
        var list = rows.ToList();
        return list.Count == 0 ? empty : string.Join(Environment.NewLine, list);
    }
}
