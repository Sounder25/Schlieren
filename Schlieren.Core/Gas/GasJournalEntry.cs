using Schlieren.Core.Execution;
using Schlieren.Core.Primitives;

namespace Schlieren.Core.Gas;

/// <summary>One chronological gas calculation or movement in an execution frame.</summary>
public sealed record GasJournalEntry(
    long Sequence,
    string TransactionId,
    long FrameId,
    long? ParentFrameId,
    CallType? CallType,
    int Depth,
    Address? ContractAddress,
    Address? CodeAddress,
    int? ProgramCounter,
    string? Opcode,
    ulong GasBefore,
    ulong GasAfter,
    GasMovementKind MovementKind,
    long? RelatedSequence,
    GasCalculation Calculation,
    bool Succeeded,
    string? Error);