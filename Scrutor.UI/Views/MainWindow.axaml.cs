using Avalonia.Controls;
using Avalonia.Input;
using Scrutor.UI.ViewModels;

namespace Scrutor.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
    
    public MainWindow(WorkbenchViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
    
    private void OnTabClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is ProjectFileViewModel file)
        {
            if (DataContext is WorkbenchViewModel vm)
            {
                vm.SelectFileCommand.Execute(file);
            }
        }
    }
    
    private void OnFileClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is ProjectFileViewModel file)
        {
            if (DataContext is WorkbenchViewModel vm)
            {
                vm.SelectFileCommand.Execute(file);
            }
        }
    }
    
    private void OnCallGraphClick(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is WorkbenchViewModel vm)
        {
            vm.ShowCallGraphCommand.Execute(null);
        }
    }
}
