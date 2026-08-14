using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Schlieren.Core.Configuration;
using Schlieren.Core.DependencyInjection;
using Schlieren.Core.Forking;
using Schlieren.RPC.DependencyInjection;

namespace Schlieren.CLI.Commands;

/// <summary>
/// Lightweight embedded-node launcher for use by 'schlieren run' and 'schlieren test'.
/// Builds the same DI host as 'schlieren node' but runs it in the background and
/// exposes a <see cref="Dispose"/> handle that stops it cleanly.
/// </summary>
public sealed class NodeBootstrap : IDisposable
{
    private readonly IHost _host;

    private NodeBootstrap(IHost host) => _host = host;

    public static NodeBootstrap Start(NodeConfiguration config)
    {
        var host = BuildHost(config);
        host.StartAsync().GetAwaiter().GetResult();
        return new NodeBootstrap(host);
    }

    public void Dispose()
    {
        try { _host.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); }
        catch { /* best effort */ }
        _host.Dispose();
    }

    // ── DI wiring (mirrors Program.cs BuildNodeCommand) ──────────────────────

    private static IHost BuildHost(NodeConfiguration config)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                // Silent — run/test output should not be polluted with DI logs
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Warning);
            })
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton(config);
                services.AddSchlierenCore();

                if (!string.IsNullOrEmpty(config.ForkUrl))
                {
                    services.AddSingleton<IBlockCache, BlockCache>();
                    services.AddHttpClient<IForkProvider, ForkProvider>(client =>
                    {
                        client.BaseAddress = new Uri(config.ForkUrl);
                        client.Timeout = TimeSpan.FromMilliseconds(config.ForkRequestTimeout);
                    });
                }

                services.AddSchlierenRpc();
            })
            .Build();
    }
}
