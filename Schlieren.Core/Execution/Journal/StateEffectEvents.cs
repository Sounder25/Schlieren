namespace Schlieren.Core.Execution.Journal;

public enum FrameStateResolution
{
    Commit,
    Rollback
}

public enum TransactionPersistenceOutcome
{
    CommittedToState,
    SimulationDiscarded
}

public enum ExecutionDisposition
{
    Survived,
    Reverted
}

public enum PersistenceDisposition
{
    CommittedToState,
    SimulationDiscarded,
    NotApplicable
}

public enum StateEffectScope
{
    Transaction,
    Frame
}

public enum BalanceTransferReason
{
    TransactionValue,
    CallValue,
    SelfDestruct,
    GasRefund,
    MinerFee,
    ProtocolReward
}

public enum CodeChangeAction
{
    Created,
    Installed,
    Cleared,
    Deleted,
    DelegationDesignated
}

public abstract record StateEffectEvent : ExecutionJournalEvent
{
    public long EffectId { get; internal init; }
    public required StateEffectScope Scope { get; init; }
    public int? Pc { get; init; }
    public byte? Opcode { get; init; }
}

public sealed record FrameStateCheckpointEvent : ExecutionJournalEvent;

public sealed record FrameStateResolvedEvent : ExecutionJournalEvent
{
    public required FrameStateResolution Resolution { get; init; }
}

public sealed record TransactionPersistenceEvent : ExecutionJournalEvent
{
    public required TransactionPersistenceOutcome Outcome { get; init; }
}
