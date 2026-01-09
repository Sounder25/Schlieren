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
        // Arithmetic
        services.AddSingleton<IOpcode, OpcodeAdd>();
        services.AddSingleton<IOpcode, OpcodeMul>();
        services.AddSingleton<IOpcode, OpcodeSub>();
        services.AddSingleton<IOpcode, OpcodeDiv>();
        services.AddSingleton<IOpcode, OpcodeMod>();

        // Bitwise
        services.AddSingleton<IOpcode, OpcodeAnd>();
        services.AddSingleton<IOpcode, OpcodeOr>();
        services.AddSingleton<IOpcode, OpcodeXor>();
        services.AddSingleton<IOpcode, OpcodeNot>();
        services.AddSingleton<IOpcode, OpcodeByte>();

        // Hashing
        services.AddSingleton<IOpcode, OpcodeKeccak256>();

        // Comparison
        services.AddSingleton<IOpcode, OpcodeLt>();
        services.AddSingleton<IOpcode, OpcodeGt>();
        services.AddSingleton<IOpcode, OpcodeSlt>();
        services.AddSingleton<IOpcode, OpcodeSgt>();
        services.AddSingleton<IOpcode, OpcodeEq>();
        services.AddSingleton<IOpcode, OpcodeIsZero>();

        // Control Flow
        services.AddSingleton<IOpcode, OpcodeStop>();
        services.AddSingleton<IOpcode, OpcodeJump>();
        services.AddSingleton<IOpcode, OpcodeJumpi>();
        services.AddSingleton<IOpcode, OpcodePc>();
        services.AddSingleton<IOpcode, OpcodeJumpDest>();
        services.AddSingleton<IOpcode, OpcodeReturn>();
        services.AddSingleton<IOpcode, OpcodeRevert>();

        // Environment / Execution
        services.AddSingleton<IOpcode, OpcodeChainId>();
        services.AddSingleton<IOpcode, OpcodeSelfBalance>();
        services.AddSingleton<IOpcode, OpcodeCaller>();
        services.AddSingleton<IOpcode, OpcodeCallValue>();
        services.AddSingleton<IOpcode, OpcodeCallDataLoad>();
        services.AddSingleton<IOpcode, OpcodeCallDataSize>();
        services.AddSingleton<IOpcode, OpcodeCallDataCopy>();
        services.AddSingleton<IOpcode, OpcodeCodeSize>();
        services.AddSingleton<IOpcode, OpcodeCodeCopy>();
        services.AddSingleton<IOpcode, OpcodeReturnDataSize>();
        services.AddSingleton<IOpcode, OpcodeReturnDataCopy>();
        services.AddSingleton<IOpcode, OpcodeOrigin>();
        services.AddSingleton<IOpcode, OpcodeGasPrice>();

        // System / Calls
        services.AddSingleton<IOpcode, OpcodeCreate>();
        services.AddSingleton<IOpcode, OpcodeCall>();

        // Stack
        services.AddSingleton<IOpcode, OpcodePush1>();
        services.AddSingleton<IOpcode, OpcodePush2>();
        services.AddSingleton<IOpcode, OpcodePush4>();
        services.AddSingleton<IOpcode, OpcodePush8>();
        services.AddSingleton<IOpcode, OpcodePush20>();
        services.AddSingleton<IOpcode, OpcodePush32>();
        services.AddSingleton<IOpcode, OpcodeDup1>();
        services.AddSingleton<IOpcode, OpcodeDup2>();
        services.AddSingleton<IOpcode, OpcodeDup3>();
        services.AddSingleton<IOpcode, OpcodeDup4>();
        services.AddSingleton<IOpcode, OpcodeDup16>();
        services.AddSingleton<IOpcode, OpcodeSwap1>();
        services.AddSingleton<IOpcode, OpcodeSwap2>();
        services.AddSingleton<IOpcode, OpcodeSwap3>();
        services.AddSingleton<IOpcode, OpcodeSwap16>();

        // Memory
        services.AddSingleton<IOpcode, OpcodeMload>();
        services.AddSingleton<IOpcode, OpcodeMstore>();
        services.AddSingleton<IOpcode, OpcodeMstore8>();
        services.AddSingleton<IOpcode, OpcodeMsize>();

        // Storage
        services.AddSingleton<IOpcode, OpcodeSload>();
        services.AddSingleton<IOpcode, OpcodeSstore>();

        // External State
        services.AddSingleton<IOpcode, OpcodeExtCodeSize>();
        services.AddSingleton<IOpcode, OpcodeExtCodeCopy>();
        services.AddSingleton<IOpcode, OpcodeExtCodeHash>();

        // Logging
        services.AddSingleton<IOpcode, OpcodeLog0>();
        services.AddSingleton<IOpcode, OpcodeLog1>();
        services.AddSingleton<IOpcode, OpcodeLog2>();
        services.AddSingleton<IOpcode, OpcodeLog3>();
        services.AddSingleton<IOpcode, OpcodeLog4>();
    }
}