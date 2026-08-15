namespace Schlieren.Core.Execution.Causal;

/// <summary>Evidence semantics — not heuristic strength names.</summary>
public enum DiagnosisGrade
{
    /// <summary>Exact rule + inputs + formula reproduce the mismatch.</summary>
    Proven = 3,
    /// <summary>Fork/phase/state isolate the rule; operands were not captured.</summary>
    Strong = 2,
    /// <summary>Pattern matches; another cause could produce it.</summary>
    Possible = 1
}
