using System.Numerics;
using Schlieren.Core.Execution.Causal;
using Schlieren.Core.Primitives;

namespace Schlieren.Core.Execution.Journal;

public enum SecurityCategory
{
    Reentrancy,
    StorageCollision
}

public enum SecuritySeverity
{
    Info,
    Medium,
    Critical
}

public sealed record SecurityFinding(
    string Id,
    string RuleId,
    SecurityCategory Category,
    SecuritySeverity Severity,
    DiagnosisGrade FactGrade,
    long PrimaryFrameId,
    long? InstructionId,
    IReadOnlyList<long> SupportingEventSequences,
    IReadOnlyList<long> FrameAncestry,
    ExecutionDisposition ExecutionDisposition,
    PersistenceDisposition PersistenceDisposition,
    IReadOnlyList<Address> Addresses,
    IReadOnlyList<BigInteger> StorageSlots,
    string Summary,
    string Limitation);
