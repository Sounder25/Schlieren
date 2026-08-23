namespace Schlieren.Core.Execution.Causal;

/// <summary>Typed facts used to derive a diagnosis grade.</summary>
public sealed record DiagnosisProofBasis(
    bool RuleApplicable,
    bool PhaseIsolated,
    bool ExactArithmetic = false,
    bool IndependentCorroboration = false,
    bool DirectExecutionEvidence = false)
{
    public DiagnosisGrade Grade =>
        RuleApplicable && PhaseIsolated && ExactArithmetic && IndependentCorroboration
            ? DiagnosisGrade.Proven
            : RuleApplicable && PhaseIsolated &&
              (ExactArithmetic || IndependentCorroboration || DirectExecutionEvidence)
                ? DiagnosisGrade.Strong
                : DiagnosisGrade.Possible;
}
