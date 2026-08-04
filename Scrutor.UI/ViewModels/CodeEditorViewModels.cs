using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Scrutor.UI.ViewModels;

public partial class CodeLineViewModel : ObservableObject
{
    public int LineNumber { get; }
    public string Text { get; }
    
    [ObservableProperty] private bool _isActiveLine;
    [ObservableProperty] private bool _isVulnerableLine;

    public CodeLineViewModel(int lineNumber, string text, bool isVulnerable = false)
    {
        LineNumber = lineNumber;
        Text = text;
        IsVulnerableLine = isVulnerable;
    }
}

public partial class ProjectFileViewModel : ObservableObject
{
    public string FileName { get; }
    public string FilePath { get; }
    public ObservableCollection<CodeLineViewModel> Lines { get; } = new();

    [ObservableProperty] private bool _isSelected;

    public ProjectFileViewModel(string fileName, string filePath, IEnumerable<string> lines, HashSet<int>? vulnerableLines = null)
    {
        FileName = fileName;
        FilePath = filePath;
        int lineNum = 1;
        foreach (var line in lines)
        {
            bool isVuln = vulnerableLines != null && vulnerableLines.Contains(lineNum);
            Lines.Add(new CodeLineViewModel(lineNum++, line, isVuln));
        }
    }
}
