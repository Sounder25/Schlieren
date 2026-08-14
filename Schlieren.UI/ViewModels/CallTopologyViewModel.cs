using System.Collections.ObjectModel;
using Schlieren.Core.Execution;

namespace Schlieren.UI.ViewModels;

public class CallTopologyViewModel
{
    public ObservableCollection<CallNodeViewModel> Nodes { get; } = new();
    public ObservableCollection<CallEdgeViewModel> Edges { get; } = new();
    public string Title { get; set; } = "Call Topology";
    public string EmptyHint { get; private set; } = "Run bytecode to populate call frames.";

    public void LoadFromTrace(List<ExecutionTraceStep> steps)
    {
        Nodes.Clear();
        Edges.Clear();

        if (steps is null || steps.Count == 0)
        {
            EmptyHint = "No trace steps — nothing to graph.";
            return;
        }

        // Frame key: contract address (or call-type fallback) → max depth seen
        var frames = new Dictionary<string, (int depth, string label, string address)>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<(string from, string to, string op, int step)>();

        string? lastKey = null;

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var key = FrameKey(step);
            var label = FrameLabel(step);
            var address = ShortAddress(step.ContractAddress) ?? ShortAddress(step.CodeAddress) ?? key;
            var depth = Math.Max(1, step.Depth);

            if (!frames.ContainsKey(key))
                frames[key] = (depth, label, address);
            else
            {
                var existing = frames[key];
                if (depth < existing.depth)
                    frames[key] = (depth, existing.label, existing.address);
            }

            if (lastKey != null && !string.Equals(lastKey, key, StringComparison.OrdinalIgnoreCase))
            {
                edges.Add((lastKey, key, step.Op, i));
            }

            lastKey = key;
        }

        var nodeIndex = 0;
        foreach (var (key, info) in frames.OrderBy(k => k.Value.depth).ThenBy(k => k.Key))
        {
            var isRoot = info.depth <= 1 || key.Equals("ROOT", StringComparison.OrdinalIgnoreCase);
            Nodes.Add(new CallNodeViewModel
            {
                Name = info.label,
                Address = info.address,
                X = 40 + (info.depth - 1) * 180,
                Y = 60 + nodeIndex * 100,
                IsAttacker = isRoot,
                IsVictim = false,
                HasVulnerability = false,
                Color = isRoot ? "#19D7E5" : info.depth >= 3 ? "#FFAA00" : "#4A00E0"
            });
            nodeIndex++;
        }

        foreach (var edge in edges.DistinctBy(e => (e.from, e.to, e.op)))
        {
            Edges.Add(new CallEdgeViewModel
            {
                From = edge.from,
                To = edge.to,
                Label = edge.op,
                IsReentrancy = edge.op.Contains("CALL", StringComparison.OrdinalIgnoreCase),
                StepIndex = edge.step
            });
        }

        EmptyHint = Nodes.Count == 0
            ? "Trace had no frame identity fields."
            : $"{Nodes.Count} frame(s), {Edges.Count} transition(s)";
    }

    private static string FrameKey(ExecutionTraceStep step)
    {
        if (!string.IsNullOrWhiteSpace(step.ContractAddress))
            return step.ContractAddress.Trim();
        if (!string.IsNullOrWhiteSpace(step.CodeAddress))
            return "code:" + step.CodeAddress.Trim();
        if (step.CallType is { } ct)
            return ct.ToString();
        return $"depth:{step.Depth}";
    }

    private static string FrameLabel(ExecutionTraceStep step)
    {
        if (step.CallType is CallType.Root || step.Depth <= 1)
            return "Root";
        if (step.CallType is { } ct)
            return ct.ToString();
        return $"Depth {step.Depth}";
    }

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
