using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Scrutor.Core.Configuration;
using Scrutor.Core.Execution;
using Scrutor.Core.Opcodes;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using Scrutor.RPC.Handlers;
using Scrutor.RPC.Server;
using Xunit;

namespace Scrutor.Tests.RPC;

public class DebugWhyNotRpcTests
{
    [Fact]
    public async Task DebugWhyNot_ClassifiesInsufficientFunds()
    {
        var handlers = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"debug_whyNot\",\"params\":[{\"from\":\"0x2000000000000000000000000000000000000002\",\"to\":\"0x1000000000000000000000000000000000000001\",\"gas\":\"0x5208\",\"gasPrice\":\"0x64\",\"value\":\"0x1\"}]}");

        using var doc = JsonDocument.Parse(response);
        var reasons = doc.RootElement.GetProperty("result").GetProperty("reasons");
        Assert.Contains(reasons.EnumerateArray(), x => x.GetProperty("code").GetString() == "insufficient_funds");
    }

    [Fact]
    public async Task DebugWhyNot_ClassifiesNonceTooHigh()
    {
        var handlers = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"debug_whyNot\",\"params\":[{\"from\":\"0x2000000000000000000000000000000000000002\",\"to\":\"0x1000000000000000000000000000000000000001\",\"nonce\":\"0x5\"}]}");

        using var doc = JsonDocument.Parse(response);
        var reasons = doc.RootElement.GetProperty("result").GetProperty("reasons");
        Assert.Contains(reasons.EnumerateArray(), x => x.GetProperty("code").GetString() == "nonce_too_high");
    }

    [Fact]
    public async Task DebugWhyNot_ForExecutableCall_ReturnsNoBlockerDetected()
    {
        var handlers = BuildFixture(balance: 1_000_000_000);
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"debug_whyNot\",\"params\":[{\"from\":\"0x2000000000000000000000000000000000000002\",\"to\":\"0x1000000000000000000000000000000000000001\",\"data\":\"0x\"}]}");

        using var doc = JsonDocument.Parse(response);
        var result = doc.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("success").GetBoolean());
        var reasons = result.GetProperty("reasons");
        Assert.Contains(reasons.EnumerateArray(), x => x.GetProperty("code").GetString() == "no_blocker_detected");
    }

    private static EthHandlers BuildFixture(long balance = 1)
    {
        var globalState = new GlobalState();
        var mempool = new TxMempool();
        var blockStore = new BlockStore();
        var chainState = new ChainState(31337, blockStore);
        var opcodes = new List<IOpcode> { new OpcodeStop() };
        var stateTransition = new StateTransition(new EvmMachine(opcodes));
        var miningService = new Mock<IMiningService>();
        var impersonation = new ImpersonationService();
        var accountManager = new AccountManager();
        var stateManager = new Mock<IStateManager>();

        var to = Address.FromHex("0x1000000000000000000000000000000000000001");
        globalState.SetCode(to, new byte[] { 0x00 });

        var from = Address.FromHex("0x2000000000000000000000000000000000000002");
        globalState.SetBalance(from, balance);
        globalState.SetNonce(from, 0);

        return new EthHandlers(
            globalState,
            mempool,
            chainState,
            stateTransition,
            miningService.Object,
            impersonation,
            accountManager,
            new NodeConfiguration { Accounts = 0, ChainId = 31337 },
            stateManager.Object);
    }
}
