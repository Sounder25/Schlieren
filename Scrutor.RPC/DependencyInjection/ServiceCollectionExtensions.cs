using Microsoft.Extensions.DependencyInjection;
using Scrutor.RPC.Handlers;
using Scrutor.RPC.Server;
using Microsoft.Extensions.DependencyInjection.Extensions; // Required for TryAddSingleton

namespace Scrutor.RPC.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddScrutorRpc(this IServiceCollection services)
    {
        // Register RPC handlers
        services.TryAddSingleton<EthHandlers>();
        
        // Register the router
        services.TryAddSingleton<RpcRouter>();
        
        // Register the RPC server as a hosted service
        services.AddHostedService<RpcServer>();

        return services;
    }
}
