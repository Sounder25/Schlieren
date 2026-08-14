namespace Schlieren.UI.ViewModels;

public class SecurityFindingViewModel
{
    public string SeverityEmoji { get; init; } = "";
    public string Description { get; init; } = "";
    public string Details { get; init; } = "";
    public int LineNumber { get; init; }
    public string FileName { get; init; } = "";
    public int StepIndex { get; init; }

    public string LocationText =>
        string.IsNullOrEmpty(FileName)
            ? (LineNumber > 0 ? $"step→line {LineNumber}" : $"step {StepIndex}")
            : $"{FileName}:{LineNumber}";
}
