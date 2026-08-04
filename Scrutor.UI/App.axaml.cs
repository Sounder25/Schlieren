using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scrutor.UI.ViewModels;
using Scrutor.UI.Views;

namespace Scrutor.UI;

public partial class App : Application
{
    private IHost? _host;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Build the DI container
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // ViewModels
                services.AddSingleton<WorkbenchViewModel>();
                
                // Views
                services.AddSingleton<MainWindow>();
            })
            .Build();

        // Remove DataAnnotations validation plugin (Avalonia 11)
        BindingPlugins.DataValidators.RemoveAt(0);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _host.Services.GetRequiredService<MainWindow>();
            desktop.Exit += (s, e) => _host.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
