using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Schlieren.Core.Configuration;
using Schlieren.Core.Execution;
using Schlieren.Core.State;
using Schlieren.RPC.Handlers;
using Schlieren.RPC.Server;
using Xunit;

namespace Schlieren.Tests.RPC;

public class AutomineRpcTests
{
    [Fact]
    public void GetAutomine_DefaultsToTrue()
    {
        var (_, handlers) = BuildHandlersAndState();
        Assert.True(handlers.HandleAnvilGetAutomine());
    }

    [Fact]
    public void SetAutomine_TogglesChainState()
    {
        var (chainState, handlers) = BuildHandlersAndState();

        var ok = handlers.HandleAnvilSetAutomine(new object[] { false });
        Assert.True(ok);
        Assert.False(chainState.Automine);

        ok = handlers.HandleAnvilSetAutomine(new object[] { true });
        Assert.True(ok);
        Assert.True(chainState.Automine);
    }

    [Fact]
    public async Task RpcRouter_AnvilSetAndGetAutomine_RoundTrips()
    {
        var (chainState, handlers) = BuildHandlersAndState();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var setResponse = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"anvil_setAutomine\",\"params\":[false]}");
        var setDoc = JsonDocument.Parse(setResponse);
        Assert.True(setDoc.RootElement.GetProperty("result").GetBoolean());
        Assert.False(chainState.Automine);

        var getResponse = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"anvil_getAutomine\",\"params\":[]}");
        var getDoc = JsonDocument.Parse(getResponse);
        Assert.False(getDoc.RootElement.GetProperty("result").GetBoolean());

        var evmAliasResponse = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"evm_setAutomine\",\"params\":[true]}");
        var evmAliasDoc = JsonDocument.Parse(evmAliasResponse);
        Assert.True(evmAliasDoc.RootElement.GetProperty("result").GetBoolean());
        Assert.True(chainState.Automine);
    }

    private static (ChainState chainState, EthHandlers handlers) BuildHandlersAndState()
    {
        var globalState = new GlobalState();
        var mempool = new TxMempool();
        var blockStore = new BlockStore();
        var chainState = new ChainState(1, blockStore);
        var stateTransition = new StateTransition(new EvmMachine(new List<IOpcode>()));
        var miningService = new Mock<IMiningService>();
        var impersonation = new ImpersonationService();
        var accountManager = new AccountManager();
        var stateManager = new Mock<IStateManager>();

        var handlers = new EthHandlers(
            globalState,
            mempool,
            chainState,
            stateTransition,
            miningService.Object,
            impersonation,
            accountManager,
            new NodeConfiguration { Accounts = 0, Automine = true },
            stateManager.Object);

        return (chainState, handlers);
    }
}
