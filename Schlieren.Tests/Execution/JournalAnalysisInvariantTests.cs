using Schlieren.Core.Execution.Journal;

namespace Schlieren.Tests.Execution;

public sealed class JournalAnalysisInvariantTests
{
    [Fact]
    public void EffectWithUnknownFrame_IsRejectedWithStableCode()
    {
        var journal = new ExecutionJournal();
        journal.Record(new TestStateEffectEvent { Scope = StateEffectScope.Frame, FrameId = 99 });
        journal.Record(new TransactionPersistenceEvent
        {
            Outcome = TransactionPersistenceOutcome.SimulationDiscarded
        });

        var error = Assert.Throws<JournalAnalysisException>(() => JournalAnalysis.Build(journal));

        Assert.Equal("UnknownEffectFrame", error.Code);
    }

    [Fact]
    public void MissingFrameResolution_IsRejectedWithStableCode()
    {
        var journal = new ExecutionJournal();
        journal.Record(new Schlieren.Core.Execution.Journal.FrameEnteredEvent
        {
            FrameId = 1,
            Depth = 0,
            CallType = Schlieren.Core.Execution.CallType.Root,
            ContractAddress = Schlieren.Core.Primitives.Address.Zero,
            GasLimit = 1
        });
        journal.Record(new FrameStateCheckpointEvent { FrameId = 1 });
        journal.Record(new TransactionPersistenceEvent
        {
            Outcome = TransactionPersistenceOutcome.SimulationDiscarded
        });

        var error = Assert.Throws<JournalAnalysisException>(() => JournalAnalysis.Build(journal));

        Assert.Equal("MissingFrameResolution", error.Code);
    }

    private sealed record TestStateEffectEvent : StateEffectEvent;
}
