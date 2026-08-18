namespace Schlieren.UI.ViewModels;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public enum DiagnosticConfidence
{
    Low,
    Medium,
    High
}

public sealed record DiagnosticFinding(
    string Category,
    DiagnosticSeverity Severity,
    string Title,
    string Summary,
    string? Detail,
    string? LikelyCause,
    bool IsExpectedBehavior,
    DiagnosticConfidence Confidence,
    int? StepIndex = null);
