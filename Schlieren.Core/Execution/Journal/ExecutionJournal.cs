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

public enum GasComponentScope
{
    Transaction,
    Frame,
    Opcode
}

public static class GasComponents
{
    public const string CallLocal = "call.local";
    public const string CallForwarded = "call.forwarded";
    public const string CallUnusedReturn = "call.unused-return";
    public const string PrecompileExecution = "precompile.execution";
    public const string CreateCodeDeposit = "create.code-deposit";
    public const string CreateExceptionalBurn = "create.exceptional-burn";
    public const string TransactionCalldataFloor = "transaction.calldata-floor";
    public const string TransactionCollisionBurn = "transaction.collision-burn";
}

public abstract record ExecutionJournalEvent
{
    public long Sequence { get; internal init; }
    public long? InstructionId { get; init; }
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
    public int Depth { get; init; }
    public CallType? CallType { get; init; }
    public string? ContractAddress { get; init; }
    public string? CallerAddress { get; init; }
    public string? CodeAddress { get; init; }
    public IReadOnlyList<string> Stack { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Memory { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Storage { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    public IReadOnlyList<byte> Output { get; init; } = Array.Empty<byte>();
}

public sealed record GasComponentEvent : ExecutionJournalEvent
{
    public required GasComponentScope Scope { get; init; }
    public required string Component { get; init; }
    public required ulong Amount { get; init; }
    public required GasSemantics Semantics { get; init; }
    public int? Pc { get; init; }
    public byte? Opcode { get; init; }
    public string? OpcodeName { get; init; }
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
    private readonly HashSet<long> _resolvedFrames = new();
    private long _nextFrameId = 1;
    private long _nextInstructionId = 1;
    private long _nextEffectId = 1;
    private long _nextSequence;

    public ExecutionJournal()
    {
        _eventView = _events.AsReadOnly();
    }

    public IReadOnlyList<ExecutionJournalEvent> Events => _eventView;

    internal long BeginInstruction()
    {
        long instructionId = _nextInstructionId;
        _nextInstructionId = checked(_nextInstructionId + 1);
        return instructionId;
    }

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
        if (entry is FrameStateResolvedEvent resolution)
        {
            var resolvedFrameId = resolution.FrameId ??
                throw new InvalidOperationException("Frame resolution requires a frame ID.");
            if (!_resolvedFrames.Add(resolvedFrameId))
                throw new InvalidOperationException($"Frame {resolvedFrameId} was already resolved.");
        }
        if (entry is StateEffectEvent effect)
        {
            entry = effect with { EffectId = _nextEffectId };
            _nextEffectId = checked(_nextEffectId + 1);
        }
        if (entry is CodeChangedEvent code)
        {
            entry = code with
            {
                PreviousCodeHash = code.PreviousCodeHash.ToArray(),
                NewCodeHash = code.NewCodeHash.ToArray()
            };
        }
        else if (entry is LogEmittedEvent log)
        {
            entry = log with
            {
                Topics = log.Topics.ToArray(),
                Data = log.Data.ToArray()
            };
        }
        if (entry is OpcodeGasEvent opcode)
        {
            entry = opcode with
            {
                Stack = opcode.Stack.ToArray(),
                Memory = opcode.Memory.ToArray(),
                Storage = new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(opcode.Storage, StringComparer.OrdinalIgnoreCase)),
                Output = opcode.Output.ToArray()
            };
        }
        _events.Add(entry with { Sequence = _nextSequence });
        _nextSequence = checked(_nextSequence + 1);
    }

    internal void ResolveFrame(long? frameId, long? parentFrameId, FrameStateResolution resolution)
    {
        if (!frameId.HasValue)
            return;
        Record(new FrameStateResolvedEvent
        {
            FrameId = frameId,
            ParentFrameId = parentFrameId,
            Resolution = resolution
        });
    }
}
