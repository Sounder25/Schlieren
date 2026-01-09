using Microsoft.Extensions.Hosting;
using Scrutor.Core.Configuration; // For NodeConfiguration
using Microsoft.Extensions.Logging; // For ILogger

namespace Scrutor.RPC.Server;

/// <summary>
/// Implements IRpcServer and wraps the IOCPServer for integration with IHostedService.
/// Manages the lifecycle of the underlying IOCPServer.
/// </summary>
public sealed class RpcServer : IRpcServer
{
    private readonly IOCPServer _iocpServer;
    private readonly ILogger<RpcServer> _logger;
    private readonly NodeConfiguration _config;

    public RpcServer(NodeConfiguration config, RpcRouter router, ILogger<RpcServer> logger)
    {
        _config = config;
        _logger = logger;
        // The IOCPServer now takes the port from the NodeConfiguration
        _iocpServer = new IOCPServer(config.Port, router, _logger);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[RpcServer] Starting RPC server on port {Port}", _config.Port);
        return _iocpServer.Start();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[RpcServer] Stopping RPC server...");
        return _iocpServer.Stop();
    }

    public void Dispose()
    {
        _logger.LogInformation("[RpcServer] Disposing RPC server resources.");
        _iocpServer.Dispose();
    }
}
