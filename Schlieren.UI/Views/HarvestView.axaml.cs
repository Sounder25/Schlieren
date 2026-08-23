using Avalonia.Controls;
using Avalonia.Input;
using Schlieren.UI.ViewModels;

namespace Schlieren.UI.Views;

public partial class HarvestView : UserControl
{
    public HarvestView()
    {
        InitializeComponent();
    }

    private void OnHarvestRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not HarvestViewModel vm) return;
        if (sender is Control c && c.DataContext is HarvestEntry entry)
            vm.SelectEntryCommand.Execute(entry);
    }
}
