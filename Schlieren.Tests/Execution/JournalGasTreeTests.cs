using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Primitives;

namespace Schlieren.Tests.Execution;

public sealed class JournalGasTreeTests
{
    [Fact]
    public void InclusiveCallDelta_IsEvidenceNotAnAdditiveCharge()
    {
        var journal = new ExecutionJournal();
        var frameId = journal.OpenFrame(null);
        journal.Record(new FrameEnteredEvent
        {
            FrameId = frameId,
            Depth = 0,
            CallType = CallType.Root,
            ContractAddress = Address.Zero,
            GasLimit = 100
        });
        journal.Record(new GasComponentEvent
        {
            FrameId = frameId,
            Scope = GasComponentScope.Opcode,
            Component = GasComponents.CallLocal,
            Amount = 10,
            Semantics = GasSemantics.ExclusiveCharge
        });
        journal.Record(new OpcodeGasEvent
        {
            FrameId = frameId,
            Pc = 0,
            Opcode = 0xf1,
            Name = "CALL",
            GasBefore = 100,
            GasAfter = 0,
            Amount = 100,
            Semantics = GasSemantics.InclusiveFrameDelta
        });

        var tree = JournalGasTree.Build(journal, ExecutionResult.Success(10));

        Assert.Equal(10UL, tree.Conservation.DerivedGas);
        Assert.True(tree.Conservation.IsConserved);
        Assert.Contains(Flatten(tree.Root), node =>
            node.Label.StartsWith("CALL") && node.Effect == JournalGasEffect.None);
    }

    [Fact]
    public void EffectiveRefund_SubtractsExactlyOnce()
    {
        var journal = new ExecutionJournal();
        journal.Record(new IntrinsicGasChargedEvent { Amount = 100 });
        journal.Record(new EffectiveGasRefundedEvent
        {
            GrossGasUsed = 100,
            RefundCap = 20,
            Amount = 20
        });
        journal.Record(new TransactionSettledEvent
        {
            ChargedGas = 80,
            UnusedGasReturned = 20
        });

        var tree = JournalGasTree.Build(journal, ExecutionResult.Success(80));

        Assert.Equal(80UL, tree.Conservation.DerivedGas);
        Assert.Equal("0", tree.Conservation.Delta);
        Assert.Single(Flatten(tree.Root), node => node.Effect == JournalGasEffect.Credit);
    }

    [Fact]
    public void ExceptionalBurn_IsAnExplicitCharge()
    {
        var journal = new ExecutionJournal();
        journal.Record(new ExceptionalGasBurnedEvent
        {
            Pc = 0,
            Opcode = "INVALID",
            Amount = 50,
            Error = EvmError.InvalidOpcode
        });

        var tree = JournalGasTree.Build(journal, ExecutionResult.Failure(EvmError.InvalidOpcode, 50));

        Assert.Equal(50UL, tree.Conservation.DerivedGas);
        Assert.True(tree.Conservation.IsConserved);
        Assert.Contains(Flatten(tree.Root), node =>
            node.Semantics == GasSemantics.ExceptionalBurn &&
            node.Effect == JournalGasEffect.Charge);
    }

    private static IEnumerable<JournalGasNode> Flatten(JournalGasNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Flatten(child))
            yield return descendant;
    }
}
