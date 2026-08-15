using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Schlieren.UI.ViewModels;

namespace Schlieren.UI.Views;

public partial class ConformanceView : UserControl
{
    public ConformanceView()
    {
        InitializeComponent();
        DataContext = new ConformanceViewModel();
    }

    private ConformanceViewModel? Vm => DataContext as ConformanceViewModel;

    public void Reset() => Vm?.ResetResultsCommand.Execute(null);

    private void OnFailureClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ConformanceFailureRow row })
            Vm?.SelectFailureCommand.Execute(row);
    }

    private void OnClusterClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ConformanceClusterRow row })
            Vm?.SelectClusterCommand.Execute(row);
    }
}
