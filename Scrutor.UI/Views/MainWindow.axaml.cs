using Avalonia.Controls;
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
}
