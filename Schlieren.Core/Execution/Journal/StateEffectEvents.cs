using System.Numerics;
using Schlieren.Core.Primitives;

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

public enum NonceChangeReason
{
    TransactionSender,
    ContractCreation,
    Authorization
}

public abstract record StateEffectEvent : ExecutionJournalEvent
{
    public long EffectId { get; internal init; }
    public required StateEffectScope Scope { get; init; }
    public int? Pc { get; init; }
    public byte? Opcode { get; init; }
}

public sealed record StorageReadEvent : StateEffectEvent
{
    public required Address StorageAddress { get; init; }
    public required BigInteger Slot { get; init; }
    public required BigInteger Value { get; init; }
    public required bool IsWarm { get; init; }
}

public sealed record StorageWriteEvent : StateEffectEvent
{
    public required Address StorageAddress { get; init; }
    public required BigInteger Slot { get; init; }
    public required BigInteger OriginalValue { get; init; }
    public required BigInteger PreviousValue { get; init; }
    public required BigInteger Value { get; init; }
    public required bool IsWarm { get; init; }
}

public sealed record TransientStorageReadEvent : StateEffectEvent
{
    public required Address StorageAddress { get; init; }
    public required BigInteger Slot { get; init; }
    public required BigInteger Value { get; init; }
}

public sealed record TransientStorageWriteEvent : StateEffectEvent
{
    public required Address StorageAddress { get; init; }
    public required BigInteger Slot { get; init; }
    public required BigInteger PreviousValue { get; init; }
    public required BigInteger Value { get; init; }
}

public sealed record BalanceTransferEvent : StateEffectEvent
{
    public Address? From { get; init; }
    public Address? To { get; init; }
    public required BigInteger Amount { get; init; }
    public required BalanceTransferReason Reason { get; init; }
}

public sealed record NonceChangedEvent : StateEffectEvent
{
    public required Address Address { get; init; }
    public required ulong Previous { get; init; }
    public required ulong Current { get; init; }
    public required NonceChangeReason Reason { get; init; }
}

public sealed record CodeChangedEvent : StateEffectEvent
{
    public required Address Address { get; init; }
    public required CodeChangeAction Action { get; init; }
    public required IReadOnlyList<byte> PreviousCodeHash { get; init; }
    public required IReadOnlyList<byte> NewCodeHash { get; init; }
    public required int PreviousSize { get; init; }
    public required int NewSize { get; init; }
}

public sealed record LogEmittedEvent : StateEffectEvent
{
    public required Address Address { get; init; }
    public required IReadOnlyList<BigInteger> Topics { get; init; }
    public required IReadOnlyList<byte> Data { get; init; }
}

public sealed record SelfDestructEvent : StateEffectEvent
{
    public required Address Contract { get; init; }
    public required Address Beneficiary { get; init; }
    public required BigInteger TransferredBalance { get; init; }
    public required bool DeletionEligible { get; init; }
    public required bool DeletionScheduled { get; init; }
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
