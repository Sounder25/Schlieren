using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Schlieren.Core.Execution;

namespace Schlieren.UI.ViewModels;

public partial class CallTopologyViewModel : ObservableObject
{
    public ObservableCollection<CallNodeViewModel> Nodes { get; } = new();
    public ObservableCollection<CallEdgeViewModel> Edges { get; } = new();
    public ObservableCollection<CallGraphRowViewModel> Rows { get; } = new();
    public string Title { get; set; } = "Call Topology";

    [ObservableProperty] private string _emptyHint = "Run bytecode first, then open CALL GRAPH.";
    [ObservableProperty] private bool _hasRows;

    public void LoadFromTrace(List<ExecutionTraceStep> steps)
    {
        Nodes.Clear();
        Edges.Clear();
        Rows.Clear();

        if (steps is null || steps.Count == 0)
        {
            EmptyHint = "No trace yet. Press RUN / F5 on the workbench, then open CALL GRAPH.";
            HasRows = false;
            return;
        }

        var rootAddr = steps
            .Select(s => s.ContractAddress)
            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) ?? "Root";

        var root = CallGraphRowViewModel.Node(
            "THIS CONTRACT (Root)",
            rootAddr,
            "#19D7E5",
            frameKey: "root",
            kind: "Contract",
            success: true);
        AddNode("Root", rootAddr, isRoot: true);
        Rows.Add(root);

        var calls = 0;
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (!IsCallLike(step.Op))
                continue;

            var meta = ExtractCallMeta(step, steps, i);
            var label = meta.IsPrecompile ? meta.PrecompileName : "Contract";
            var color = meta.IsPrecompile && meta.PrecompileName.Contains("P256", StringComparison.OrdinalIgnoreCase)
                ? "#00D4AA"
                : step.Op.Contains("CREATE", StringComparison.OrdinalIgnoreCase) ? "#FFAA00"
                : "#4A00E0";

            AddNode(label, meta.Target, isRoot: false);
            Edges.Add(new CallEdgeViewModel
            {
                From = rootAddr,
                To = meta.Target,
                Label = step.Op,
                IsReentrancy = step.Op.Contains("CALL", StringComparison.OrdinalIgnoreCase),
                StepIndex = i
            });

            var successWord = meta.Success is null ? "unknown" : meta.Success.Value ? "success" : "failure";
            Rows.Add(CallGraphRowViewModel.Edge(
                $"{step.Op} · step {i + 1}",
                $"gas forwarded: {meta.GasForwarded?.ToString("N0") ?? "—"}\ninput: {FormatBytes(meta.InputBytes)}\noutput: {FormatBytes(meta.OutputBytes)}\n{successWord}",
                stepIndex: i,
                frameKey: "edge:" + i));

            var childKind = meta.IsPrecompile ? "Precompile" : "Contract";
            var childSuccess = meta.Success is null ? "UNKNOWN" : meta.Success.Value ? "SUCCESS" : "FAILURE";
            var retLine = string.IsNullOrEmpty(meta.ReturnHex) ? "" : $"\nreturn {meta.ReturnHex}";
            var childDetail =
                $"{childKind}\n{meta.Target}\n{childSuccess}\ngas used: {meta.GasUsed?.ToString("N0") ?? "—"}{retLine}";
            Rows.Add(CallGraphRowViewModel.Node(
                label,
                childDetail,
                color,
                frameKey: "child:" + i,
                kind: childKind,
                address: meta.Target,
                success: meta.Success,
                gasUsed: meta.GasUsed,
                returnHint: meta.ReturnHex,
                stepIndex: i));
            calls++;
        }

        if (calls == 0)
        {
            EmptyHint = $"{steps.Count} steps, no CALL/CREATE. This bytecode never called another contract.";
            HasRows = Rows.Count > 0;
            return;
        }

        EmptyHint = $"{calls} call(s) from this contract · {steps.Count} steps";
        HasRows = Rows.Count > 0;
    }

    private void AddNode(string name, string address, bool isRoot)
    {
        if (Nodes.Any(n => n.Address.Equals(address, StringComparison.OrdinalIgnoreCase)
                           && n.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return;
        Nodes.Add(new CallNodeViewModel
        {
            Name = name,
            Address = address,
            X = 40,
            Y = 40 + Nodes.Count * 90,
            IsAttacker = isRoot,
            Color = isRoot ? "#19D7E5" : "#4A00E0"
        });
    }

    private static bool IsCallLike(string op) =>
        op is "CALL" or "STATICCALL" or "DELEGATECALL" or "CALLCODE" or "CREATE" or "CREATE2";

    /// <summary>
    /// Workbench traces snapshot the stack top-first (System.Stack.ToArray),
    /// before the opcode runs. CALL last-pushed is gas:
    /// [0]=gas [1]=addr [2]=value [3]=argsOff [4]=argsLen [5]=retOff [6]=retLen
    /// </summary>
    public static string ExtractCallTarget(ExecutionTraceStep step)
        => ExtractCallMeta(step, Array.Empty<ExecutionTraceStep>(), 0).Target;

    public static CallMeta ExtractCallMeta(
        ExecutionTraceStep step,
        IReadOnlyList<ExecutionTraceStep> all,
        int index)
    {
        if (step.Op is "CREATE" or "CREATE2")
        {
            return new CallMeta("(new contract)", false, "CREATE", null, null, null, null, null, null);
        }

        var stack = step.Stack ?? new List<string>();
        var target = stack.Count > 1 ? NormalizeAddress(stack[1]) : "(unknown)";
        if (!LooksLikeAddress(target))
        {
            var scanned = ScanPrecompile(stack);
            if (scanned != null) target = scanned;
        }

        var name = PrecompileLabel(target);
        var isPre = name != "Contract";
        ulong? gasFwd = stack.Count > 0 ? TryParseUlong(stack[0]) : null;
        ulong? inBytes = step.Op is "CALL" or "CALLCODE"
            ? (stack.Count > 4 ? TryParseUlong(stack[4]) : null)
            : (stack.Count > 3 ? TryParseUlong(stack[3]) : null);

        bool? success = null;
        ulong? outBytes = null;
        string? returnHex = null;
        if (step.OutputData is { Length: > 0 } output)
        {
            outBytes = (ulong)output.Length;
            returnHex = "0x" + Convert.ToHexString(output).ToLowerInvariant();
        }
        if (index + 1 < all.Count && all[index + 1].Stack is { Count: > 0 } s1)
        {
            var top = TryParseUlong(s1[0]);
            if (top is 0 or 1) success = top == 1;
        }
        if (outBytes is null && index + 2 < all.Count && all[index + 2].Stack is { Count: > 1 } s2)
            outBytes = TryParseUlong(s2[1]) ?? TryParseUlong(s2[0]);

        ulong? gasUsed = null;
        if (isPre && name.Contains("P256", StringComparison.OrdinalIgnoreCase))
            gasUsed = 6_900;
        else if (isPre && name.Contains("ECRECOVER", StringComparison.OrdinalIgnoreCase))
            gasUsed = 3_000;

        return new CallMeta(target, isPre, name, gasFwd, inBytes, outBytes, success, gasUsed, returnHex);
    }

    public readonly record struct CallMeta(
        string Target,
        bool IsPrecompile,
        string PrecompileName,
        ulong? GasForwarded,
        ulong? InputBytes,
        ulong? OutputBytes,
        bool? Success,
        ulong? GasUsed,
        string? ReturnHex);

    public static string PrecompileLabel(string address)
    {
        var hex = address.Replace("0x", "", StringComparison.OrdinalIgnoreCase).TrimStart('0');
        return hex.ToLowerInvariant() switch
        {
            "1" => "ECRECOVER (0x01)",
            "2" => "SHA256 (0x02)",
            "3" => "RIPEMD160 (0x03)",
            "4" => "IDENTITY (0x04)",
            "5" => "MODEXP (0x05)",
            "6" => "ECADD (0x06)",
            "7" => "ECMUL (0x07)",
            "8" => "ECPAIRING (0x08)",
            "9" => "BLAKE2F (0x09)",
            "a" => "KZG (0x0a)",
            "100" => "P256VERIFY (0x0100)",
            _ => "Contract"
        };
    }

    public static string NormalizeAddress(string raw)
    {
        var hex = raw.Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];
        hex = hex.PadLeft(40, '0');
        if (hex.Length > 40)
            hex = hex[^40..];
        return "0x" + hex.ToLowerInvariant();
    }

    private static string? ScanPrecompile(IReadOnlyList<string> stack)
    {
        foreach (var word in stack)
        {
            var addr = NormalizeAddress(word);
            if (PrecompileLabel(addr) != "Contract")
                return addr;
        }
        return null;
    }

    private static bool LooksLikeAddress(string addr)
        => addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && addr.Length == 42;

    private static ulong? TryParseUlong(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var h = hex.Trim();
        if (h.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) h = h[2..];
        if (h.Length == 0) return 0;
        return ulong.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : null;
    }

    private static string FormatBytes(ulong? n) => n is null ? "—" : $"{n.Value:N0} B";

    private static string? ShortAddress(string? addr)
    {
        if (string.IsNullOrWhiteSpace(addr)) return null;
        var a = addr.Trim();
        if (a.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && a.Length > 12)
            return a[..6] + "…" + a[^4..];
        return a.Length > 16 ? a[..16] + "…" : a;
    }
}

public class CallNodeViewModel
{
    public string Name { get; init; } = "";
    public string Address { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public bool IsAttacker { get; init; }
    public bool IsVictim { get; init; }
    public bool HasVulnerability { get; init; }
    public string Color { get; init; } = "#888";
}

public class CallEdgeViewModel
{
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public string Label { get; init; } = "";
    public bool IsReentrancy { get; init; }
    public int StepIndex { get; init; }
}

public sealed class CallGraphRowViewModel
{
    public bool IsEdge { get; init; }
    public bool IsNode => !IsEdge;
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Color { get; init; } = "#888";
    public string FrameKey { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Address { get; init; } = "";
    public bool? Success { get; init; }
    public ulong? GasUsed { get; init; }
    public string? ReturnHint { get; init; }
    public int StepIndex { get; init; } = -1;

    public static CallGraphRowViewModel Node(
        string title,
        string detail,
        string color,
        string frameKey = "",
        string kind = "",
        string address = "",
        bool? success = null,
        ulong? gasUsed = null,
        string? returnHint = null,
        int stepIndex = -1) => new()
    {
        IsEdge = false,
        Title = title,
        Detail = detail,
        Color = color,
        FrameKey = frameKey,
        Kind = kind,
        Address = address,
        Success = success,
        GasUsed = gasUsed,
        ReturnHint = returnHint,
        StepIndex = stepIndex
    };

    public static CallGraphRowViewModel Edge(
        string title,
        string detail = "",
        int stepIndex = -1,
        string frameKey = "") => new()
    {
        IsEdge = true,
        Title = title,
        Detail = detail,
        Color = "#888888",
        StepIndex = stepIndex,
        FrameKey = frameKey
    };
}
