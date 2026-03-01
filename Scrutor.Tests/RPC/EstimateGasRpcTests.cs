using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Scrutor.Core.Configuration;
using Scrutor.Core.Execution;
using Scrutor.Core.Opcodes;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using Scrutor.RPC;
using Scrutor.RPC.Handlers;
using Scrutor.RPC.Models;
using Scrutor.RPC.Server;
using Xunit;

namespace Scrutor.Tests.RPC;

public class EstimateGasRpcTests
{
    [Fact]
    public async Task EstimateGas_ForSimpleStopContract_ReturnsBaselineGas()
    {
        var (_, _, handlers) = BuildFixture();
        var to = "0x1000000000000000000000000000000000000001";

        var result = await handlers.HandleEstimateGas(new object[]
        {
            JsonSerializer.Deserialize<JsonElement>($"{{\"from\":\"0x2000000000000000000000000000000000000002\",\"to\":\"{to}\",\"data\":\"0x\"}}")
        });

        Assert.Equal("0x5208", result); // 21000
    }

    [Fact]
    public async Task EstimateGas_FailsWhenProvidedGasCapCannotExecute()
    {
        var (_, _, handlers) = BuildFixture();
        var to = "0x3000000000000000000000000000000000000003";

        var ex = await Assert.ThrowsAsync<RpcException>(() => handlers.HandleEstimateGas(new object[]
        {
            JsonSerializer.Deserialize<JsonElement>($"{{\"from\":\"0x2000000000000000000000000000000000000002\",\"to\":\"{to}\",\"gas\":\"0x3a98\"}}") // 15000
        }));

        Assert.Equal(JsonRpcErrorCodes.ExecutionError, ex.ErrorCode);
        Assert.Contains("Unable to estimate gas", ex.Message);
    }

    [Fact]
    public async Task RpcRouter_EthEstimateGas_RoutesAndReturnsHex()
    {
        var (_, _, handlers) = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);
        var to = "0x1000000000000000000000000000000000000001";

        var response = await router.ProcessRequest(
            $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"eth_estimateGas\",\"params\":[{{\"from\":\"0x2000000000000000000000000000000000000002\",\"to\":\"{to}\",\"data\":\"0x\"}}]}}");

        using var doc = JsonDocument.Parse(response);
        Assert.Equal("0x5208", doc.RootElement.GetProperty("result").GetString());
    }

    [Fact]
    public async Task EstimateGas_WithCalldata_UsesIntrinsicDataGasFloor()
    {
        var (_, _, handlers) = BuildFixture();
        var to = "0x1000000000000000000000000000000000000001";

        var result = await handlers.HandleEstimateGas(new object[]
        {
            JsonSerializer.Deserialize<JsonElement>($"{{\"from\":\"0x2000000000000000000000000000000000000002\",\"to\":\"{to}\",\"data\":\"0x0100\"}}")
        });

        // 21000 + 16 (non-zero) + 4 (zero) = 21020
        Assert.Equal("0x521c", result);
    }

    [Fact]
    public async Task EstimateGas_ForContractCreation_IncludesCreateIntrinsicGas()
    {
        var (_, _, handlers) = BuildFixture();

        var result = await handlers.HandleEstimateGas(new object[]
        {
            JsonSerializer.Deserialize<JsonElement>("{\"from\":\"0x2000000000000000000000000000000000000002\",\"data\":\"0x\"}")
        });

        // 21000 + 32000 = 53000
        Assert.Equal("0xcf08", result);
    }

    [Fact]
    public async Task EstimateGas_AcceptsLatestBlockTagParameter()
    {
        var (_, _, handlers) = BuildFixture();
        var to = "0x1000000000000000000000000000000000000001";

        var result = await handlers.HandleEstimateGas(new object[]
        {
            JsonSerializer.Deserialize<JsonElement>($"{{\"from\":\"0x2000000000000000000000000000000000000002\",\"to\":\"{to}\",\"data\":\"0x\"}}"),
            "latest"
        });

        Assert.Equal("0x5208", result);
    }

    [Fact]
    public async Task EstimateGas_RejectsFutureBlockTagParameter()
    {
        var (_, _, handlers) = BuildFixture();
        var to = "0x1000000000000000000000000000000000000001";

        var ex = await Assert.ThrowsAsync<RpcException>(() => handlers.HandleEstimateGas(new object[]
        {
            JsonSerializer.Deserialize<JsonElement>($"{{\"from\":\"0x2000000000000000000000000000000000000002\",\"to\":\"{to}\",\"data\":\"0x\"}}"),
            "0x10"
        }));

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("greater than current head", ex.Message);
    }

    private static (GlobalState globalState, ChainState chainState, EthHandlers handlers) BuildFixture()
    {
        var globalState = new GlobalState();
        var mempool = new TxMempool();
        var blockStore = new BlockStore();
        var chainState = new ChainState(31337, blockStore);

        // Contract A: STOP
        var stopAddress = Address.FromHex("0x1000000000000000000000000000000000000001");
        globalState.SetCode(stopAddress, new byte[] { 0x00 });

        // Contract B: PUSH1 0x00 PUSH1 0x00 SSTORE STOP
        var sstoreAddress = Address.FromHex("0x3000000000000000000000000000000000000003");
        globalState.SetCode(sstoreAddress, new byte[] { 0x60, 0x00, 0x60, 0x00, 0x55, 0x00 });

        var fromAddress = Address.FromHex("0x2000000000000000000000000000000000000002");
        globalState.SetBalance(fromAddress, 1_000_000_000);

        var opcodes = new List<IOpcode> { new OpcodeStop(), new OpcodePush1(), new OpcodeSstore() };
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

        return (globalState, chainState, handlers);
    }
}
