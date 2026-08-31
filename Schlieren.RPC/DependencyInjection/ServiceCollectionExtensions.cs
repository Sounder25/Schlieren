using Microsoft.Extensions.DependencyInjection;
using Schlieren.RPC.Handlers;
using Schlieren.RPC.Server;
using Microsoft.Extensions.DependencyInjection.Extensions; // Required for TryAddSingleton

namespace Schlieren.RPC.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSchlierenRpc(this IServiceCollection services)
    {
        // Register RPC handlers
        services.TryAddSingleton<EthHandlers>();
        services.TryAddSingleton<GuardHandlers>();
        
        // Register the router
        services.TryAddSingleton<RpcRouter>();
        
        // Register the RPC server as a hosted service
        services.AddHostedService<RpcServer>();

        return services;
    }
}
