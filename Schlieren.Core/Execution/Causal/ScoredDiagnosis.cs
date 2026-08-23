namespace Schlieren.Core.Execution.Causal;

public sealed class ScoredDiagnosis
{
    public required string RuleId { get; init; }
    public required string Title { get; init; }
    public required ExecutionPhase Phase { get; init; }
    public required DiagnosisProofBasis Basis { get; init; }
    public DiagnosisGrade Grade => Basis.Grade;
    public required int Score { get; init; }
    public required string Why { get; init; }
    public required string Proof { get; init; }
    public required string Consequences { get; init; }
    public required string LikelyFix { get; init; }
    public required string CodeBoundary { get; init; }
    public string ProtocolRule { get; init; } = "";
    public long? GasDelta { get; init; }
    public string Fingerprint { get; init; } = "";
}
