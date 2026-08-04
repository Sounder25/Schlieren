namespace Scrutor.UI.ViewModels;

public class SecurityFindingViewModel
{
    public string SeverityEmoji { get; init; } = "";
    public string Description { get; init; } = "";
    public string Details { get; init; } = "";
    public int LineNumber { get; init; }
    public string FileName { get; init; } = "";
    public int StepIndex { get; init; }
    
    public string LocationText => $"{FileName}:{LineNumber}";
    
    // Command property for data binding - set by parent ViewModel
    public System.Windows.Input.ICommand? JumpToCodeCommand { get; set; }
}
