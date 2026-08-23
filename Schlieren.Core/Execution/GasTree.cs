using System.Text;

namespace Schlieren.Core.Execution;

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

    /// <summary>Canonical journal total, or the legacy recursively computed total.</summary>
    public ulong TotalGas => RecordedTotalGas ?? Gas + (ulong)Children.Sum(c => (long)c.TotalGas);

    /// <summary>Exact total copied from the canonical journal compatibility projection.</summary>
    public ulong? RecordedTotalGas { get; set; }

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
