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
        // Catch any unhandled exceptions so they show in status bar instead of crashing
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            System.IO.File.AppendAllText(
                @"C:\projects\Schlieren\crash.log",
                $"[{DateTime.Now}] {e.ExceptionObject}\n\n");
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _workbench = new WorkbenchViewModel();
            var window = new MainWindow(_workbench);
            desktop.MainWindow = window;

            desktop.Exit += (_, _) =>
            {
                _workbench?.Dispose();
                _workbench = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
