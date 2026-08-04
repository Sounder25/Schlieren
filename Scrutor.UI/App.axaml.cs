using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Scrutor.UI.ViewModels;
using Scrutor.UI.Views;

namespace Scrutor.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new WorkbenchViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };
        }
        
        base.OnFrameworkInitializationCompleted();
    }
}
