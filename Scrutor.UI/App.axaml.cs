using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Scrutor.UI.ViewModels;
using Scrutor.UI.Views;

namespace Scrutor.UI;

public partial class App : Application
{
    private WorkbenchViewModel? _workbench;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _workbench = new WorkbenchViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = _workbench
            };

            desktop.Exit += (_, _) =>
            {
                _workbench?.Dispose();
                _workbench = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
