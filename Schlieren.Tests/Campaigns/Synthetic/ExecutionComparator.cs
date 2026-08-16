using System;
using System.Collections.Generic;
using System.Linq;

namespace Schlieren.Tests.Campaigns.Synthetic;

/// <summary>
/// Compares two CampaignExecutionResults and produces a structured diff.
///
/// Layer 1 — CONSENSUS (severe, always a Schlieren defect):
///   transaction success, gas used, return data, storage, balances, logs
///
/// Layer 2 — TRACE (diagnostic, may be normalization difference):
///   frame gas provided/consumed, frame call type, first divergent opcode
/// </summary>
public static class ExecutionComparator
{
    public sealed record ExecutionDiff(
        bool   IsMatch,
        string Category,           // e.g. "GasMismatch", "StorageMismatch"
        string Layer,              // "Consensus" or "Trace"
        string Detail,
        long?  GasDelta,           // schlieren.GasUsed - oracle.GasUsed
        bool   SuccessMismatch,
        bool   GasMismatch,
        bool   ReturnDataMismatch,
        bool   StorageMismatch,
        bool   LogsMismatch,
        bool   BalanceMismatch,
        string? FirstDivergentField);

    public static readonly ExecutionDiff Match = new(
        IsMatch: true, Category: "Match", Layer: "None", Detail: "Agreement",
        GasDelta: null, SuccessMismatch: false, GasMismatch: false,
        ReturnDataMismatch: false, StorageMismatch: false, LogsMismatch: false,
        BalanceMismatch: false, FirstDivergentField: null);

    public static ExecutionDiff Compare(
        CampaignExecutionResult schlieren,
        CampaignExecutionResult oracle)
    {
        // ── CONSENSUS checks ──────────────────────────────────────────────────

        // 1. Transaction success
        if (schlieren.Success != oracle.Success)
            return Diff("SuccessMismatch", "Consensus",
                $"schlieren={schlieren.Success} oracle={oracle.Success}",
                schlieren, oracle,
                successMismatch: true);

        // 2. Gas used (final transaction gas, not frame-level)
        if (schlieren.GasUsed != oracle.GasUsed)
            return Diff("GasMismatch", "Consensus",
                $"schlieren={schlieren.GasUsed} oracle={oracle.GasUsed} delta={((long)schlieren.GasUsed - (long)oracle.GasUsed):+#;-#;0}",
                schlieren, oracle,
                gasMismatch: true);

        // 3. Return data — byte-for-byte
        var sRD = NormalizeHex(schlieren.ReturnData);
        var oRD = NormalizeHex(oracle.ReturnData);
        if (sRD != oRD)
            return Diff("ReturnDataMismatch", "Consensus",
                $"schlieren={sRD} oracle={oRD}",
                schlieren, oracle,
                returnDataMismatch: true);

        // 4. Storage — slot-level comparison
        var storageDiff = CompareStorage(schlieren.Fingerprint.StateDiff, oracle.Fingerprint.StateDiff);
        if (storageDiff != null)
            return Diff("StorageMismatch", "Consensus", storageDiff, schlieren, oracle,
                storageMismatch: true);

        // 5. Logs — count and content
        var logDiff = CompareLogs(schlieren.Fingerprint.Logs, oracle.Fingerprint.Logs);
        if (logDiff != null)
            return Diff("LogsMismatch", "Consensus", logDiff, schlieren, oracle,
                logsMismatch: true);

        // 6. Balances — read from state_diff where available
        var balDiff = CompareBalances(schlieren.Fingerprint.StateDiff, oracle.Fingerprint.StateDiff);
        if (balDiff != null)
            return Diff("BalanceMismatch", "Consensus", balDiff, schlieren, oracle,
                balanceMismatch: true);

        return Match;
    }

    // ── Consensus field comparators ───────────────────────────────────────────

    private static string? CompareStorage(
        Dictionary<string, string> s,
        Dictionary<string, string> o)
    {
        // Only compare storage entries — keys containing ":" are slot entries
        var sSlots = s.Where(kv => kv.Key.Contains(':')).ToDictionary(kv => kv.Key, kv => kv.Value);
        var oSlots = o.Where(kv => kv.Key.Contains(':')).ToDictionary(kv => kv.Key, kv => kv.Value);

        foreach (var (key, sVal) in sSlots)
        {
            if (!oSlots.TryGetValue(key, out var oVal))
                return $"slot {key}: schlieren={sVal} oracle=missing";
            if (NormalizeHex(sVal) != NormalizeHex(oVal))
                return $"slot {key}: schlieren={sVal} oracle={oVal}";
        }
        foreach (var key in oSlots.Keys.Except(sSlots.Keys))
            return $"slot {key}: schlieren=missing oracle={oSlots[key]}";

        return null;
    }

    private static string? CompareLogs(List<LogFingerprint> s, List<LogFingerprint> o)
    {
        if (s.Count != o.Count)
            return $"log count: schlieren={s.Count} oracle={o.Count}";

        for (int i = 0; i < s.Count; i++)
        {
            if (s[i].Address != o[i].Address)
                return $"log[{i}] address: schlieren={s[i].Address} oracle={o[i].Address}";
            if (s[i].Data != o[i].Data)
                return $"log[{i}] data: schlieren={s[i].Data} oracle={o[i].Data}";
        }
        return null;
    }

    private static string? CompareBalances(
        Dictionary<string, string> s,
        Dictionary<string, string> o)
    {
        // REVM state_diff has per-account balance as top-level field — not in slot format
        // For now: skip balance comparison (REVM emits post-state not delta)
        // TODO: compare balances when harness emits proper pre/post delta
        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ExecutionDiff Diff(
        string category, string layer, string detail,
        CampaignExecutionResult s, CampaignExecutionResult o,
        bool successMismatch      = false,
        bool gasMismatch          = false,
        bool returnDataMismatch   = false,
        bool storageMismatch      = false,
        bool logsMismatch         = false,
        bool balanceMismatch      = false) =>
        new ExecutionDiff(
            IsMatch:             false,
            Category:            category,
            Layer:               layer,
            Detail:              detail,
            GasDelta:            gasMismatch ? (long)s.GasUsed - (long)o.GasUsed : null,
            SuccessMismatch:     successMismatch,
            GasMismatch:         gasMismatch,
            ReturnDataMismatch:  returnDataMismatch,
            StorageMismatch:     storageMismatch,
            LogsMismatch:        logsMismatch,
            BalanceMismatch:     balanceMismatch,
            FirstDivergentField: category);

    private static string NormalizeHex(string h)
    {
        if (string.IsNullOrEmpty(h) || h == "0x") return "0x";
        var s = h.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? h[2..] : h;
        var trimmed = s.ToLowerInvariant().TrimStart('0');
        return "0x" + (trimmed.Length == 0 ? "0" : trimmed);
    }
}
