using Microsoft.Extensions.Hosting;

namespace Schlieren.RPC.Server;

/// <summary>
/// Defines the contract for the high-concurrency RPC server.
/// This service is managed by the .NET Host lifetime.
/// </summary>
public interface IRpcServer : IHostedService, IDisposable
{
}
