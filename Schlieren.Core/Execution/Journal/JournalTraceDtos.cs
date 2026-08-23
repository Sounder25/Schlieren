using System.Text.Json.Serialization;

namespace Schlieren.Core.Execution.Journal;

public sealed record JournalTraceOptions(
    bool DisableStack = false,
    bool DisableMemory = false,
    bool DisableStorage = false);

public sealed record JournalExecutionDto(
    bool Success,
    string? Error,
    ulong GasUsed,
    long GasRefundCounter,
    string ReturnData);

public sealed record JournalEventDto(
    string Kind,
    long Sequence,
    long? FrameId,
    long? ParentFrameId,
    string Semantics,
    ulong? Amount,
    string? Component,
    int? Pc,
    string? Opcode,
    string? OpcodeName,
    object Data);

public sealed record JournalFrameDto(
    long Id,
    long? ParentId,
    int Depth,
    string CallType,
    string ContractAddress,
    string? CodeAddress,
    ulong GasLimit,
    bool? Success,
    string? Error,
    ulong? GasUsed,
    ulong? GasRemaining);

public sealed record JournalStepDto
{
    public required long Sequence { get; init; }
    public required long FrameId { get; init; }
    public long? ParentFrameId { get; init; }
    public required int Depth { get; init; }
    public required int Pc { get; init; }
    public required string Opcode { get; init; }
    public required string Op { get; init; }
    public required ulong GasBefore { get; init; }
    public required ulong GasAfter { get; init; }
    public required ulong GasCost { get; init; }
    public required string Semantics { get; init; }
    public string? CallType { get; init; }
    public string? ContractAddress { get; init; }
    public string? CallerAddress { get; init; }
    public string? CodeAddress { get; init; }
    public required string Output { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Stack { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Memory { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Storage { get; init; }
}

public sealed record JournalGasNodeDto(
    string Id,
    string Label,
    long? FrameId,
    string Semantics,
    ulong Amount,
    string Effect,
    ulong TotalGas,
    IReadOnlyList<long> EventSequences,
    IReadOnlyList<JournalGasNodeDto> Children);

public sealed record JournalTraceDto(
    bool Ok,
    string Fork,
    JournalExecutionDto Execution,
    IReadOnlyList<JournalEventDto> Events,
    IReadOnlyList<JournalFrameDto> Frames,
    IReadOnlyList<JournalStepDto> Steps,
    JournalGasNodeDto GasTree,
    JournalConservation Conservation);
