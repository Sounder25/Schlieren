using System.Collections.ObjectModel;

namespace Scrutor.UI.ViewModels;

public class ProjectExplorerViewModel
{
    public ObservableCollection<SourceFileViewModel> Files { get; } = new();
    public SourceFileViewModel? ActiveFile { get; set; }
    public string SearchQuery { get; set; } = "";
}
