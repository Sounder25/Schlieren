namespace Scrutor.UI.ViewModels;

public class SourceFileViewModel
{
    public string FileName { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string Content { get; init; } = "";
    public List<SourceLineViewModel> Lines { get; init; } = new();
    public bool IsActive { get; set; }
}

public class SourceLineViewModel
{
    public int LineNumber { get; init; }
    public string Content { get; init; } = "";
    public bool IsActiveLine { get; set; }
    public bool HasVulnerability { get; set; }
    public string? VulnerabilityType { get; set; }
}
