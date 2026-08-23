using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Forks;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Tests.Execution;

public sealed class JournalDispositionTests
{
    [Fact]
    public async Task DryRunExecution_EmitsCompleteLifecycleAndDiscardedPersistence()
    {
        var target = Address.FromHex("0x6100000000000000000000000000000000000001");
        var state = new GlobalState();
        state.SetCode(target, [0x00]);

        var result = await new StateTransition(new EvmMachine([new OpcodeStop()]))
            .ApplyTransactionAsync(
                new Transaction
                {
                    To = target,
                    GasLimit = 100_000,
                    Authorization = TransactionAuthorization.Internal,
                    EnableJournal = true
                },
                state,
                new BlockContext { Rules = ForkRulesFactory.For("Osaka") },
                commit: false);

        Assert.True(result.IsSuccess);
        var journal = Assert.IsType<ExecutionJournal>(result.Journal);
        var analysis = JournalAnalysis.Build(journal);
        var frame = Assert.Single(analysis.Frames.Values);
        Assert.Equal(FrameStateResolution.Commit, frame.Resolution);
        Assert.Single(journal.Events.OfType<TransactionPersistenceEvent>(),
            entry => entry.Outcome == TransactionPersistenceOutcome.SimulationDiscarded);
    }

    [Fact]
    public void ParentRollback_RevertsOtherwiseSuccessfulChildEffect()
    {
        var journal = new ExecutionJournal();
        Enter(journal, 1, null, 0);
        Enter(journal, 2, 1, 1);
        journal.Record(new TestStateEffectEvent { Scope = StateEffectScope.Frame, FrameId = 2 });
        Resolve(journal, 2, 1, FrameStateResolution.Commit);
        Resolve(journal, 1, null, FrameStateResolution.Rollback);
        Persist(journal, TransactionPersistenceOutcome.CommittedToState);

        var effect = Assert.Single(JournalAnalysis.Build(journal).StateEffects);

        Assert.Equal(ExecutionDisposition.Reverted, effect.ExecutionDisposition);
        Assert.Equal(1, effect.RevertedByFrameId);
        Assert.Equal(PersistenceDisposition.NotApplicable, effect.PersistenceDisposition);
    }

    [Fact]
    public void SurvivingDryRunEffect_IsSimulationDiscarded()
    {
        var journal = new ExecutionJournal();
        Enter(journal, 1, null, 0);
        journal.Record(new TestStateEffectEvent { Scope = StateEffectScope.Frame, FrameId = 1 });
        Resolve(journal, 1, null, FrameStateResolution.Commit);
        Persist(journal, TransactionPersistenceOutcome.SimulationDiscarded);

        var analysis = JournalAnalysis.Build(journal);
        var effect = Assert.Single(analysis.StateEffects);

        Assert.Equal(ExecutionDisposition.Survived, effect.ExecutionDisposition);
        Assert.Null(effect.RevertedByFrameId);
        Assert.Equal(PersistenceDisposition.SimulationDiscarded, effect.PersistenceDisposition);
        Assert.Empty(analysis.Frames[1].AncestorIds);
    }

    private static void Enter(ExecutionJournal journal, long id, long? parentId, int depth)
    {
        journal.Record(new FrameEnteredEvent
        {
            FrameId = id,
            ParentFrameId = parentId,
            Depth = depth,
            CallType = depth == 0 ? CallType.Root : CallType.Call,
            ContractAddress = Address.Zero,
            GasLimit = 100_000
        });
        journal.Record(new FrameStateCheckpointEvent { FrameId = id, ParentFrameId = parentId });
    }

    private static void Resolve(ExecutionJournal journal, long id, long? parentId, FrameStateResolution resolution) =>
        journal.Record(new FrameStateResolvedEvent
        {
            FrameId = id,
            ParentFrameId = parentId,
            Resolution = resolution
        });

    private static void Persist(ExecutionJournal journal, TransactionPersistenceOutcome outcome) =>
        journal.Record(new TransactionPersistenceEvent { Outcome = outcome });

    private sealed record TestStateEffectEvent : StateEffectEvent;
}
