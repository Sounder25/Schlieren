using System.Collections.ObjectModel;
using Schlieren.Core.Primitives;

namespace Schlieren.Core.Execution.Journal;

public enum GasSemantics
{
    ExclusiveCharge,
    InclusiveFrameDelta,
    Allocation,
    Return,
    RefundCounter,
    Credit,
    ExceptionalBurn,
    Observation
}

public abstract record ExecutionJournalEvent
{
    public long Sequence { get; internal init; }
    public long? FrameId { get; init; }
    public long? ParentFrameId { get; init; }
}

public sealed record TransactionStartedEvent : ExecutionJournalEvent
{
    public required ulong GasLimit { get; init; }
    public required bool IsInternal { get; init; }
}

public sealed record IntrinsicGasChargedEvent : ExecutionJournalEvent
{
    public required ulong Amount { get; init; }
    public GasSemantics Semantics => GasSemantics.ExclusiveCharge;
}

public sealed record FrameEnteredEvent : ExecutionJournalEvent
{
    public required int Depth { get; init; }
    public required CallType CallType { get; init; }
    public required Address ContractAddress { get; init; }
    public Address? CodeAddress { get; init; }
    public required ulong GasLimit { get; init; }
    public GasSemantics Semantics => GasSemantics.Allocation;
}

public sealed record OpcodeGasEvent : ExecutionJournalEvent
{
    public required int Pc { get; init; }
    public required byte Opcode { get; init; }
    public required string Name { get; init; }
    public required ulong GasBefore { get; init; }
    public required ulong GasAfter { get; init; }
    public required ulong Amount { get; init; }
    public required GasSemantics Semantics { get; init; }
}

public sealed record ExceptionalGasBurnedEvent : ExecutionJournalEvent
{
    public required int Pc { get; init; }
    public required string Opcode { get; init; }
    public required ulong Amount { get; init; }
    public required EvmError Error { get; init; }
    public GasSemantics Semantics => GasSemantics.ExceptionalBurn;
}

public sealed record RefundCounterChangedEvent : ExecutionJournalEvent
{
    public required long Previous { get; init; }
    public required long Current { get; init; }
    public long Delta => Current - Previous;
    public GasSemantics Semantics => GasSemantics.RefundCounter;
}

public sealed record FrameExitedEvent : ExecutionJournalEvent
{
    public required int Depth { get; init; }
    public required bool Success { get; init; }
    public required EvmError Error { get; init; }
    public required ulong GasUsed { get; init; }
    public required ulong GasRemaining { get; init; }
    public GasSemantics Semantics => GasSemantics.Return;
}

public sealed record EffectiveGasRefundedEvent : ExecutionJournalEvent
{
    public required ulong GrossGasUsed { get; init; }
    public required ulong RefundCap { get; init; }
    public required ulong Amount { get; init; }
    public GasSemantics Semantics => GasSemantics.Credit;
}

public sealed record TransactionSettledEvent : ExecutionJournalEvent
{
    public required ulong ChargedGas { get; init; }
    public required ulong UnusedGasReturned { get; init; }
}

public sealed class ExecutionJournal
{
    private readonly List<ExecutionJournalEvent> _events = new();
    private readonly ReadOnlyCollection<ExecutionJournalEvent> _eventView;
    private long _nextFrameId = 1;
    private long _nextSequence;

    public ExecutionJournal()
    {
        _eventView = _events.AsReadOnly();
    }

    public IReadOnlyList<ExecutionJournalEvent> Events => _eventView;

    internal long OpenFrame(long? parentFrameId)
    {
        _ = parentFrameId;
        long frameId = _nextFrameId;
        _nextFrameId = checked(_nextFrameId + 1);
        return frameId;
    }

    internal void Record(ExecutionJournalEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _events.Add(entry with { Sequence = _nextSequence });
        _nextSequence = checked(_nextSequence + 1);
    }
}
