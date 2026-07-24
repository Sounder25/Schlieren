using Microsoft.Extensions.DependencyInjection;
using Scrutor.Core.State;
using Scrutor.Core.Execution;
using Scrutor.Core.Opcodes;
using Scrutor.Core.Configuration;
using Scrutor.Core.Forking;
using Scrutor.Core.Models;

namespace Scrutor.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddScrutorCore(this IServiceCollection services)
    {
        services.AddSingleton<IAccountManager, AccountManager>();
        services.AddSingleton<IImpersonationService, ImpersonationService>();
        services.AddSingleton<ITxMempool, TxMempool>();
        services.AddSingleton<IBlockStore, BlockStore>();
        services.AddSingleton<IStateManager, StateManager>();
        services.AddSingleton<IChainState>(sp =>
        {
            var config = sp.GetRequiredService<NodeConfiguration>();
            var blockStore = sp.GetRequiredService<IBlockStore>();
            
            if (!string.IsNullOrEmpty(config.ForkUrl))
            {
                var forkProvider = sp.GetRequiredService<IForkProvider>();
                var forkBlockNumber = config.ForkBlockNumber;
                
                Scrutor.Core.Models.Block? forkBlock;
                if (forkBlockNumber.HasValue)
                {
                    forkBlock = forkProvider.GetBlockByNumberAsync(forkBlockNumber.Value).GetAwaiter().GetResult();
                }
                else
                {
                    var latest = forkProvider.GetLatestBlockNumberAsync().GetAwaiter().GetResult();
                    forkBlock = forkProvider.GetBlockByNumberAsync(latest).GetAwaiter().GetResult();
                    config.ForkBlockNumber = latest; // PIN IT
                }

                if (forkBlock != null)
                {
                    var chainState = new ChainState(config.ChainId, blockStore);
                    chainState.UpdateHead(forkBlock); // Set the fork block as the current head
                    return chainState;
                }
            }
            
            return new ChainState(config, blockStore);
        });

        // Register Global State with Forking Support
        services.AddSingleton<IGlobalState>(sp =>
        {
            var config = sp.GetRequiredService<NodeConfiguration>();
            var _ = sp.GetRequiredService<IChainState>(); // Force pinning
            var localState = new GlobalState();
            
            if (!string.IsNullOrEmpty(config.ForkUrl))
            {
                var forkProvider = sp.GetService<IForkProvider>();
                return new ForkingGlobalState(localState, forkProvider, config.ForkBlockNumber);
            }
            
            return localState;
        });

        // Register all opcodes
        RegisterOpcodes(services);

        services.AddSingleton<EvmMachine>();
        services.AddSingleton<IStateTransition, StateTransition>();
        
        services.AddSingleton<MiningService>();
        services.AddSingleton<IMiningService>(sp => sp.GetRequiredService<MiningService>());
        services.AddHostedService(sp => sp.GetRequiredService<MiningService>());
        
        services.AddHostedService<Scrutor.Core.Services.BootstrapService>();

        return services;
    }

    private static void RegisterOpcodes(IServiceCollection services)
    {
        // Auto-register every concrete IOpcode in this assembly so Solidity bytecode
        // (full PUSH/DUP/SWAP, SHL, BASEFEE, CREATE2, etc.) does not hit InvalidOpcode
        // from a partial DI list.
        var opcodeTypes = typeof(IOpcode).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IOpcode).IsAssignableFrom(t));

        foreach (var type in opcodeTypes)
            services.AddSingleton(typeof(IOpcode), type);
    }
}