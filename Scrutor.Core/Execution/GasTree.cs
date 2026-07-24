using System.Text;

namespace Scrutor.Core.Execution;

/// <summary>
/// A node in the gas causality tree.
/// Each node represents either the root transaction, a call frame, or a
/// grouped opcode bucket (e.g. "Cold SLOAD × 3").
/// </summary>
public sealed class GasTreeNode
{
    /// <summary>Human-readable label, e.g. "Cold SLOAD × 3", "Contract 0xabcd… execution".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Gas attributed to this node (excludes children).</summary>
    public ulong Gas { get; set; }

    /// <summary>Total gas including all children (computed after build).</summary>
    public ulong TotalGas => Gas + (ulong)Children.Sum(c => (long)c.TotalGas);

    public List<GasTreeNode> Children { get; } = new();

    public void AddChild(GasTreeNode child) => Children.Add(child);

    public GasTreeNode AddBucket(string label, ulong gas)
    {
        var n = new GasTreeNode { Label = label, Gas = gas };
        Children.Add(n);
        return n;
    }
}

/// <summary>
/// Builds a <see cref="GasTreeNode"/> from a completed StateTransition result.
/// </summary>
public sealed class GasTreeBuilder
{
    /// <summary>
    /// Build the tree from a completed execution result and the metadata captured
    /// during the run (intrinsic, calldata, frame journal).
    /// </summary>
    public GasTreeNode Build(
        string txLabel,
        ulong gasLimit,
        ulong intrinsicGas,
        ulong calldataGas,
        GasFrameNode rootFrame)
    {
        var root = new GasTreeNode { Label = txLabel };

        // Intrinsic base (21000 minus calldata portion)
        var baseIntrinsic = intrinsicGas > calldataGas ? intrinsicGas - calldataGas : intrinsicGas;
        if (baseIntrinsic > 0)
            root.AddBucket("Intrinsic transaction cost", baseIntrinsic);

        if (calldataGas > 0)
            root.AddBucket("Calldata", calldataGas);

        // Walk the call-frame tree
        var frameNode = BuildFrame(rootFrame);
        if (frameNode != null)
            root.Children.Add(frameNode);

        // Unused gas
        var used = root.TotalGas;
        if (gasLimit > used)
            root.AddBucket("Unused gas returned", gasLimit - used);

        // Stamp root total
        root.Gas = 0; // root gas is zero — all gas is in children
        return root;
    }

    private static GasTreeNode? BuildFrame(GasFrameNode frame)
    {
        if (frame.OpcodeGas == 0 && frame.Children.Count == 0)
            return null;

        var label = frame.Label;
        var node = new GasTreeNode { Label = label, Gas = 0 };

        // Bucket opcodes within this frame
        foreach (var bucket in BuildOpcodeBuckets(frame.OpcodeSteps))
            node.Children.Add(bucket);

        // Recurse into child frames
        foreach (var child in frame.Children)
        {
            var childNode = BuildFrame(child);
            if (childNode != null)
                node.Children.Add(childNode);
        }

        return node;
    }

    /// <summary>
    /// Groups a flat list of (opcode, gas) steps into meaningful buckets:
    /// - SLOAD cold/warm
    /// - SSTORE (by case)
    /// - TLOAD / TSTORE
    /// - Memory expansion
    /// - EXP
    /// - Everything else lumped per-opcode with count
    /// </summary>
    private static List<GasTreeNode> BuildOpcodeBuckets(List<(string op, ulong gas)> steps)
    {
        // Aggregate counters
        var coldSload = 0UL;   int coldSloadN = 0;
        var warmSload = 0UL;   int warmSloadN = 0;
        var coldSstore = 0UL;  int coldSstoreN = 0;
        var warmSstore = 0UL;  int warmSstoreN = 0;
        var tload = 0UL;       int tloadN = 0;
        var tstore = 0UL;      int tstoreN = 0;
        var memExpand = 0UL;   int memExpandN = 0;
        var others = new Dictionary<string, (ulong gas, int count)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (op, gas) in steps)
        {
            switch (op.ToUpperInvariant())
            {
                case "SLOAD":
                    if (gas >= 2100) { coldSload += gas; coldSloadN++; }
                    else             { warmSload += gas; warmSloadN++; }
                    break;
                case "SSTORE":
                    if (gas >= 2100) { coldSstore += gas; coldSstoreN++; }
                    else             { warmSstore += gas; warmSstoreN++; }
                    break;
                case "TLOAD":
                    tload += gas; tloadN++;
                    break;
                case "TSTORE":
                    tstore += gas; tstoreN++;
                    break;
                case "MSTORE": case "MSTORE8": case "MLOAD": case "CALLDATACOPY":
                case "CODECOPY": case "RETURNDATACOPY": case "MCOPY":
                    // Memory expansion cost is the delta above static opcode cost.
                    // We bucket any gas > the static base as expansion.
                    // Static base: MSTORE/MLOAD/MSTORE8 = 3, copy opcodes = 3+word
                    var staticBase = op.ToUpperInvariant() switch
                    {
                        "MSTORE" or "MSTORE8" or "MLOAD" => 3UL,
                        _ => 3UL + (gas > 3 ? 0UL : 0UL) // approximate
                    };
                    var expansion = gas > staticBase ? gas - staticBase : 0UL;
                    if (expansion > 0) { memExpand += expansion; memExpandN++; }
                    var baseGas = gas - expansion;
                    if (baseGas > 0)
                    {
                        if (!others.TryGetValue(op, out var ov)) ov = (0, 0);
                        others[op] = (ov.gas + baseGas, ov.count + 1);
                    }
                    break;
                default:
                    if (!others.TryGetValue(op, out var v)) v = (0, 0);
                    others[op] = (v.gas + gas, v.count + 1);
                    break;
            }
        }

        var buckets = new List<GasTreeNode>();

        void Emit(string label, ulong gas) { if (gas > 0) buckets.Add(new GasTreeNode { Label = label, Gas = gas }); }

        if (coldSloadN > 0)
            Emit(coldSloadN == 1 ? "Cold SLOAD" : $"Cold SLOAD × {coldSloadN}", coldSload);
        if (warmSloadN > 0)
            Emit(warmSloadN == 1 ? "Warm SLOAD" : $"Warm SLOAD × {warmSloadN}", warmSload);
        if (coldSstoreN > 0)
            Emit(coldSstoreN == 1 ? "SSTORE (cold slot)" : $"SSTORE cold × {coldSstoreN}", coldSstore);
        if (warmSstoreN > 0)
            Emit(warmSstoreN == 1 ? "SSTORE (warm slot)" : $"SSTORE warm × {warmSstoreN}", warmSstore);
        if (tloadN > 0)
            Emit(tloadN == 1 ? "TLOAD" : $"TLOAD × {tloadN}", tload);
        if (tstoreN > 0)
            Emit(tstoreN == 1 ? "TSTORE" : $"TSTORE × {tstoreN}", tstore);
        if (memExpandN > 0)
            Emit("Memory expansion", memExpand);

        foreach (var (op, (gas, count)) in others.OrderByDescending(x => x.Value.gas))
        {
            if (gas == 0) continue;
            var lbl = count == 1 ? op : $"{op} × {count}";
            buckets.Add(new GasTreeNode { Label = lbl, Gas = gas });
        }

        return buckets;
    }
}

// ---------------------------------------------------------------------------
// Frame journal — built during execution, independent of the flat trace
// ---------------------------------------------------------------------------

/// <summary>
/// Represents one call frame's worth of gas data, with nested children.
/// Built concurrently with execution via the SubCall callback chain.
/// </summary>
public sealed class GasFrameNode
{
    public string Label { get; set; } = "root";

    /// <summary>Opcode gas steps directly in this frame (not in sub-calls).</summary>
    public List<(string op, ulong gas)> OpcodeSteps { get; } = new();

    /// <summary>Total gas from OpcodeSteps (excludes children).</summary>
    public ulong OpcodeGas => (ulong)OpcodeSteps.Sum(s => (long)s.gas);

    public List<GasFrameNode> Children { get; } = new();
}

/// <summary>
/// Renders a <see cref="GasTreeNode"/> tree as a box-drawing ASCII string.
/// </summary>
public static class GasTreeRenderer
{
    public static string Render(GasTreeNode root)
    {
        var sb = new StringBuilder();
        // Root line: label + total gas
        sb.AppendLine($"{root.Label}: {root.TotalGas:N0} gas");
        RenderChildren(root.Children, sb, prefix: "");
        return sb.ToString().TrimEnd();
    }

    private static void RenderChildren(List<GasTreeNode> children, StringBuilder sb, string prefix)
    {
        for (int i = 0; i < children.Count; i++)
        {
            bool isLast = i == children.Count - 1;
            var node = children[i];
            var connector = isLast ? "└── " : "├── ";
            var childPrefix = prefix + (isLast ? "    " : "│   ");

            if (node.Children.Count == 0)
            {
                sb.AppendLine($"{prefix}{connector}{node.Label}: {node.TotalGas:N0}");
            }
            else
            {
                sb.AppendLine($"{prefix}{connector}{node.Label}: {node.TotalGas:N0}");
                RenderChildren(node.Children, sb, childPrefix);
            }
        }
    }
}
