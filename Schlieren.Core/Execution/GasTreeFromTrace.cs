using Schlieren.Core.Forks;
using Schlieren.Core.State;

namespace Schlieren.Core.Execution;

/// <summary>
/// Builds a gas tree from the same <see cref="ExecutionResult"/> that RESULTS uses.
/// Does not re-execute the transaction (the diagnostic frame path is not used).
/// </summary>
public static class GasTreeFromTrace
{
    public static GasTreeNode FromCanonical(
        Transaction tx,
        IForkRules rules,
        ExecutionResult result)
    {
        ulong calldataGas = 0;
        foreach (var b in tx.Data)
            calldataGas += b == 0 ? rules.CalldataZeroByteCost : rules.CalldataNonZeroByteCost;

        var intrinsic = tx.Authorization == TransactionAuthorization.Internal
            ? 0UL
            : IntrinsicGas.Compute(tx, rules);

        var rootLabel = tx.To.HasValue
            ? $"Contract {tx.To.Value} execution"
            : "CREATE execution";

        var frames = BuildFrames(result.TraceSteps, rootLabel);
        var tree = new GasTreeBuilder().Build(
            $"Transaction (canonical StateTransition)",
            tx.GasLimit,
            intrinsic,
            calldataGas,
            frames);

        // Settlement numbers come from the same ExecutionResult as RESULTS.
        // Drop the builder's unused estimate (opcode sum) and pin to GasUsed.
        tree.Children.RemoveAll(c =>
            c.Label.StartsWith("Unused", StringComparison.OrdinalIgnoreCase));
        if (result.GasRefundCounter != 0)
            tree.AddBucket("Refund counter", (ulong)Math.Max(0, result.GasRefundCounter));
        var unused = tx.GasLimit > result.GasUsed ? tx.GasLimit - result.GasUsed : 0UL;
        if (unused > 0)
            tree.AddBucket("Unused gas returned", unused);
        tree.Label = $"Transaction (canonical)  [{result.GasUsed:N0} gas used]";
        return tree;
    }

    public static GasFrameNode BuildFrames(IReadOnlyList<ExecutionTraceStep>? steps, string rootLabel)
    {
        var root = new GasFrameNode { Label = rootLabel };
        if (steps is null || steps.Count == 0)
            return root;

        var stack = new Stack<GasFrameNode>();
        stack.Push(root);
        var prevDepth = 1;

        foreach (var step in steps)
        {
            var depth = step.Depth < 1 ? 1 : step.Depth;
            var gasCost = ParseHexUlong(step.GasCost);
            var op = step.Op ?? "";

            while (depth < prevDepth && stack.Count > 1)
            {
                stack.Pop();
                prevDepth--;
            }

            var current = stack.Peek();
            if (IsCallLike(op))
            {
                var callee = string.IsNullOrWhiteSpace(step.ContractAddress)
                    ? "?"
                    : step.ContractAddress!;
                var child = new GasFrameNode { Label = $"{op} → {callee}" };
                current.Children.Add(child);
                if (gasCost > 0)
                    current.OpcodeSteps.Add((op, gasCost));
                stack.Push(child);
                prevDepth = depth + 1;
            }
            else
            {
                if (gasCost > 0)
                    current.OpcodeSteps.Add((op, gasCost));
                prevDepth = depth;
            }
        }

        return root;
    }

    private static bool IsCallLike(string op) =>
        op is "CALL" or "DELEGATECALL" or "STATICCALL" or "CALLCODE" or "CREATE" or "CREATE2";

    private static ulong ParseHexUlong(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        var s = raw.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            s = s[2..];
        return ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out var hx)
            ? hx
            : ulong.TryParse(raw, out var dec) ? dec : 0UL;
    }
}
