using Schlieren.Core.Execution.Journal;

namespace Schlieren.Tests.Execution;

public sealed class StateEffectEventModelTests
{
    [Fact]
    public void Journal_AssignsStableSequenceInstructionAndEffectIdentity()
    {
        var journal = new ExecutionJournal();
        var firstInstruction = journal.BeginInstruction();
        var secondInstruction = journal.BeginInstruction();

        Assert.True(secondInstruction > firstInstruction);

        journal.Record(new TestStateEffectEvent
        {
            Scope = StateEffectScope.Frame,
            FrameId = 1,
            InstructionId = firstInstruction,
            Pc = 0,
            Opcode = 0x54
        });
        journal.Record(new TestStateEffectEvent
        {
            Scope = StateEffectScope.Transaction
        });

        var effects = journal.Events.Cast<TestStateEffectEvent>().ToArray();
        Assert.Equal([0L, 1L], effects.Select(effect => effect.Sequence));
        Assert.Equal([1L, 2L], effects.Select(effect => effect.EffectId));
        Assert.Equal(firstInstruction, effects[0].InstructionId);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<ExecutionJournalEvent>)journal.Events).Add(effects[0]));
    }

    private sealed record TestStateEffectEvent : StateEffectEvent;
}
