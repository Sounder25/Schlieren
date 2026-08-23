using System.Globalization;

namespace Schlieren.Core.Execution.Journal;

public enum JournalGasEffect
{
    None,
    Charge,
    Credit
}

public sealed record JournalGasNode(
    string Id,
    string Label,
    long? FrameId,
    GasSemantics Semantics,
    ulong Amount,
    JournalGasEffect Effect,
    ulong TotalGas,
    IReadOnlyList<long> EventSequences,
    IReadOnlyList<JournalGasNode> Children);

public sealed record JournalConservation(
    ulong DerivedGas,
    ulong SettledGas,
    string Delta,
    bool IsConserved);

public sealed record JournalGasTreeResult(
    JournalGasNode Root,
    JournalConservation Conservation);

public static class JournalGasTree
{
    public static JournalGasTreeResult Build(ExecutionJournal journal, ExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(journal);

        var frameBuilders = journal.Events
            .OfType<FrameEnteredEvent>()
            .Where(entry => entry.FrameId.HasValue)
            .ToDictionary(entry => entry.FrameId!.Value, entry => new FrameBuilder(entry));

        var rootItems = new List<NodeBuilder>();
        foreach (var entry in journal.Events)
        {
            if (entry is FrameEnteredEvent or FrameExitedEvent)
                continue;

            var node = FromEvent(entry);
            if (node is null)
                continue;

            if (entry.FrameId is long frameId && frameBuilders.TryGetValue(frameId, out var frame))
                frame.Items.Add(node);
            else
                rootItems.Add(node);
        }

        foreach (var frame in frameBuilders.Values)
        {
            if (frame.Entry.ParentFrameId is long parentId &&
                frameBuilders.TryGetValue(parentId, out var parent))
            {
                parent.Children.Add(frame);
            }
        }

        var rootFrames = frameBuilders.Values
            .Where(frame => frame.Entry.ParentFrameId is null)
            .OrderBy(frame => frame.Entry.Sequence)
            .Select(BuildFrame)
            .ToList();
        var rootChildren = rootItems
            .OrderBy(node => node.Sequence)
            .Select(node => node.Node)
            .Concat(rootFrames)
            .ToList();

        decimal derived = 0;
        foreach (var entry in journal.Events)
        {
            var effect = GetEffect(entry);
            var amount = GetAmount(entry);
            if (effect == JournalGasEffect.Charge)
                derived += amount;
            else if (effect == JournalGasEffect.Credit)
                derived -= amount;
        }

        if (derived < 0 || derived > ulong.MaxValue)
            throw new InvalidOperationException($"Journal gas total is outside UInt64 range: {derived}.");

        var derivedGas = (ulong)derived;
        var settledGas = journal.Events.OfType<TransactionSettledEvent>().LastOrDefault()?.ChargedGas
            ?? result.GasUsed;
        var delta = derived - settledGas;
        var conservation = new JournalConservation(
            derivedGas,
            settledGas,
            delta.ToString("+0;-0;0", CultureInfo.InvariantCulture),
            delta == 0);
        var root = new JournalGasNode(
            "transaction",
            "Transaction",
            null,
            GasSemantics.Observation,
            0,
            JournalGasEffect.None,
            derivedGas,
            journal.Events.Select(entry => entry.Sequence).ToArray(),
            rootChildren);
        return new JournalGasTreeResult(root, conservation);
    }

    private static JournalGasNode BuildFrame(FrameBuilder frame)
    {
        var children = frame.Items
            .Select(item => (item.Sequence, item.Node))
            .Concat(frame.Children.Select(child =>
                (child.Entry.Sequence, BuildFrame(child))))
            .OrderBy(item => item.Sequence)
            .Select(item => item.Item2)
            .ToList();
        var total = CalculateTotal(children);
        return new JournalGasNode(
            $"frame-{frame.Entry.FrameId}",
            $"{frame.Entry.CallType} {frame.Entry.ContractAddress}",
            frame.Entry.FrameId,
            GasSemantics.Allocation,
            frame.Entry.GasLimit,
            JournalGasEffect.None,
            total,
            new[] { frame.Entry.Sequence },
            children);
    }

    private static ulong CalculateTotal(IEnumerable<JournalGasNode> nodes)
    {
        decimal total = 0;
        foreach (var node in nodes)
        {
            if (node.Effect == JournalGasEffect.Charge)
                total += node.Amount;
            else if (node.Effect == JournalGasEffect.Credit)
                total -= node.Amount;
            if (node.Children.Count > 0)
                total += node.TotalGas;
        }
        return total <= 0 ? 0 : checked((ulong)total);
    }

    private static NodeBuilder? FromEvent(ExecutionJournalEvent entry)
    {
        var effect = GetEffect(entry);
        var amount = GetAmount(entry);
        var (label, semantics) = entry switch
        {
            TransactionStartedEvent => ("Transaction started", GasSemantics.Observation),
            IntrinsicGasChargedEvent e => ("Intrinsic gas", e.Semantics),
            OpcodeGasEvent e => ($"{e.Name} @ {e.Pc}", e.Semantics),
            GasComponentEvent e => (e.Component, e.Semantics),
            ExceptionalGasBurnedEvent e => ($"Exceptional burn: {e.Opcode}", e.Semantics),
            RefundCounterChangedEvent e => ($"Refund counter {e.Delta:+#;-#;0}", e.Semantics),
            EffectiveGasRefundedEvent e => ("Effective gas refund", e.Semantics),
            TransactionSettledEvent => ("Transaction settled", GasSemantics.Observation),
            _ => (entry.GetType().Name, GasSemantics.Observation)
        };
        var node = new JournalGasNode(
            $"event-{entry.Sequence}",
            label,
            entry.FrameId,
            semantics,
            amount,
            effect,
            effect == JournalGasEffect.Charge ? amount : 0,
            new[] { entry.Sequence },
            Array.Empty<JournalGasNode>());
        return new NodeBuilder(entry.Sequence, node);
    }

    private static JournalGasEffect GetEffect(ExecutionJournalEvent entry) => entry switch
    {
        IntrinsicGasChargedEvent => JournalGasEffect.Charge,
        OpcodeGasEvent { Semantics: GasSemantics.ExclusiveCharge } => JournalGasEffect.Charge,
        GasComponentEvent { Semantics: GasSemantics.ExclusiveCharge or GasSemantics.ExceptionalBurn } =>
            JournalGasEffect.Charge,
        ExceptionalGasBurnedEvent => JournalGasEffect.Charge,
        EffectiveGasRefundedEvent => JournalGasEffect.Credit,
        _ => JournalGasEffect.None
    };

    private static ulong GetAmount(ExecutionJournalEvent entry) => entry switch
    {
        IntrinsicGasChargedEvent e => e.Amount,
        OpcodeGasEvent e => e.Amount,
        GasComponentEvent e => e.Amount,
        ExceptionalGasBurnedEvent e => e.Amount,
        EffectiveGasRefundedEvent e => e.Amount,
        _ => 0
    };

    private sealed record NodeBuilder(long Sequence, JournalGasNode Node);

    private sealed class FrameBuilder(FrameEnteredEvent entry)
    {
        public FrameEnteredEvent Entry { get; } = entry;
        public List<NodeBuilder> Items { get; } = new();
        public List<FrameBuilder> Children { get; } = new();
    }
}
