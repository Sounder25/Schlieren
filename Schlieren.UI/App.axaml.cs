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
    private HarvestViewModel?   _harvest;

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
                @"crash.log",
                $"[{DateTime.Now}] {e.ExceptionObject}\n\n");
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // ── Composition root ──────────────────────────────────────────────
            // Load HarvestServiceOptions from the four named environment keys.
            // No credential or corpus path has a compiled default.
            var harvestOptions = HarvestServiceOptions.FromEnvironment(
                key => Environment.GetEnvironmentVariable(key));

            var harvestService = new HarvestService(harvestOptions);

            _workbench = new WorkbenchViewModel();
            _harvest   = new HarvestViewModel(harvestService, harvestOptions);

            var window = new MainWindow(_workbench, _harvest);
            desktop.MainWindow = window;

            desktop.Exit += (_, _) =>
            {
                _workbench?.Dispose();
                _workbench = null;
                _harvest?.Dispose();
                _harvest = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
