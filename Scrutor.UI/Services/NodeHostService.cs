using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Scrutor.Core.DependencyInjection;
using Scrutor.Core.Configuration;
using Scrutor.Core.State;
using Scrutor.RPC.Handlers;
using Scrutor.RPC.Server;

namespace Scrutor.UI.Services;

public class NodeHostService
{
    private IHost? _host;
    private readonly Action<string> _logCallback;

    public NodeHostService(Action<string> logCallback)
    {
        _logCallback = logCallback;
    }

    public async Task StartAsync(NodeConfiguration config)
    {
        if (_host != null) await StopAsync();

        var builder = Host.CreateApplicationBuilder();
        ConfigureServices(builder, config);

        _host = builder.Build();
        await _host.StartAsync();
        
        _logCallback($"[System] Node started on port {config.Port} (ChainID: {config.ChainId})");
    }

    public T? GetService<T>() where T : class
    {
        return _host?.Services.GetService<T>();
    }

    public async Task StopAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
            _host = null;
            _logCallback("[System] Node stopped.");
        }
    }

    public ulong GetBlockHeight()
    {
        return GetService<IChainState>()?.CurrentBlock.Number ?? 0;
    }

    public int GetMempoolCount()
    {
        return GetService<ITxMempool>()?.Count ?? 0;
    }

    public NodeConfiguration? GetConfiguration()
    {
        return GetService<NodeConfiguration>();
    }

    private void ConfigureServices(HostApplicationBuilder builder, NodeConfiguration config)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new Scrutor.RPC.Logging.ObservableLoggerProvider());

        builder.Services.AddSingleton(config);
        builder.Services.AddScrutorCore();
        builder.Services.AddSingleton<EthHandlers>();
        builder.Services.AddSingleton<RpcRouter>();
        builder.Services.AddSingleton(sp => 
            new IOCPServer(config.Port, sp.GetRequiredService<RpcRouter>(), sp.GetRequiredService<ILogger<IOCPServer>>()));
            
        builder.Services.AddHostedService<ServerHostedService>();
    }

    private class ServerHostedService : IHostedService
    {
        private readonly IOCPServer _server;
        public ServerHostedService(IOCPServer server) => _server = server;
        
        public Task StartAsync(CancellationToken ct)
        {
            _ = _server.Start(); 
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken ct) => await _server.Stop();
    }
}
