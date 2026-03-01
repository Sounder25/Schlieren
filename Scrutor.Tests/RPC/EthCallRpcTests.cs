using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Scrutor.Core.Configuration;
using Scrutor.Core.Execution;
using Scrutor.Core.Models;
using Scrutor.Core.Opcodes;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using Scrutor.RPC;
using Scrutor.RPC.Models;
using Scrutor.RPC.Handlers;
using Scrutor.RPC.Server;
using Xunit;

namespace Scrutor.Tests.RPC;

public class EthCallRpcTests
{
    [Fact]
    public async Task EthCall_WithLatestTag_ReturnsHexResult()
    {
        var (_, handlers) = BuildFixture();
        var to = "0x1000000000000000000000000000000000000001";

        var result = await handlers.HandleEthCall(new object[]
        {
            JsonSerializer.Deserialize<JsonElement>($"{{\"to\":\"{to}\",\"data\":\"0x\"}}"),
            "latest"
        });

        Assert.Equal("0x", result);
    }

    [Fact]
    public async Task EthCall_RejectsFutureBlockNumber()
    {
        var (_, handlers) = BuildFixture();
        var to = "0x1000000000000000000000000000000000000001";

        var ex = await Assert.ThrowsAsync<RpcException>(() => handlers.HandleEthCall(new object[]
        {
            JsonSerializer.Deserialize<JsonElement>($"{{\"to\":\"{to}\"}}"),
            "0x2"
        }));

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("greater than current head", ex.Message);
    }

    [Fact]
    public async Task EthCall_RejectsInvalidBlockTag()
    {
        var (_, handlers) = BuildFixture();
        var to = "0x1000000000000000000000000000000000000001";
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest(
            $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"eth_call\",\"params\":[{{\"to\":\"{to}\"}},\"safe\"]}}");
        using var doc = JsonDocument.Parse(response);
        var error = doc.RootElement.GetProperty("error");

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, error.GetProperty("code").GetInt32());
        Assert.Contains("Invalid block tag", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task EthCall_RejectsInvalidFromAddress()
    {
        var (_, handlers) = BuildFixture();

        var ex = await Assert.ThrowsAsync<RpcException>(() => handlers.HandleEthCall(new object[]
        {
            JsonSerializer.Deserialize<JsonElement>("{\"from\":\"0x123\",\"to\":\"0x1000000000000000000000000000000000000001\"}")
        }));

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("Invalid 'from' address", ex.Message);
    }

    private static (ChainState chainState, EthHandlers handlers) BuildFixture()
    {
        var globalState = new GlobalState();
        var mempool = new TxMempool();
        var blockStore = new BlockStore();
        var chainState = new ChainState(31337, blockStore);

        var target = Address.FromHex("0x1000000000000000000000000000000000000001");
        globalState.SetCode(target, new byte[] { 0x00 }); // STOP

        var block = new Block
        {
            Number = 1,
            Hash = "0x" + new string('1', 64),
            GasLimit = 30_000_000,
            BaseFeePerGas = 1
        };
        chainState.UpdateHead(block);

        var opcodes = new List<IOpcode> { new OpcodeStop() };
        var stateTransition = new StateTransition(new EvmMachine(opcodes));
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
            new NodeConfiguration { Accounts = 0, ChainId = 31337 },
            stateManager.Object);

        return (chainState, handlers);
    }
}
