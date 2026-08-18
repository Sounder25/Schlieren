using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Schlieren.UI.Services;
using Schlieren.UI.ViewModels;
using Schlieren.UI.Views;

namespace Schlieren.UI;

public partial class App : Application
{
    private WorkbenchViewModel? _workbench;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        SkinService.LoadAndApply();
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
