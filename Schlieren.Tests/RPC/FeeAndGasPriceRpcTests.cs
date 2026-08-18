using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Schlieren.Core.Configuration;
using Schlieren.Core.Execution;
using Schlieren.Core.Models;
using Schlieren.Core.State;
using Schlieren.RPC.Handlers;
using Schlieren.RPC.Models;
using Schlieren.RPC.Server;
using Xunit;

namespace Schlieren.Tests.RPC;

public class FeeAndGasPriceRpcTests
{
    [Fact]
    public async Task EthGasPrice_ReturnsConfiguredGasPrice()
    {
        var (_, _, _, handlers) = BuildFixture(gasPrice: 1234);
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"eth_gasPrice\",\"params\":[]}");
        using var doc = JsonDocument.Parse(response);
        Assert.Equal("0x4d2", doc.RootElement.GetProperty("result").GetString());
    }

    [Fact]
    public async Task EthFeeHistory_ReturnsExpectedShapeAndValues()
    {
        var (_, _, _, handlers) = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"eth_feeHistory\",\"params\":[\"0x2\",\"latest\",[25,75]]}");

        using var doc = JsonDocument.Parse(response);
        var result = doc.RootElement.GetProperty("result");

        Assert.Equal("0x1", result.GetProperty("oldestBlock").GetString());

        var baseFees = result.GetProperty("baseFeePerGas");
        Assert.Equal(3, baseFees.GetArrayLength()); // blockCount + 1
        Assert.Equal("0x64", baseFees[0].GetString()); // block 1 base fee
        Assert.Equal("0x6e", baseFees[1].GetString()); // block 2 base fee

        var ratios = result.GetProperty("gasUsedRatio");
        Assert.Equal(2, ratios.GetArrayLength());
        Assert.InRange(ratios[0].GetDouble(), 0.49, 0.51);
        Assert.InRange(ratios[1].GetDouble(), 0.79, 0.81);

        var reward = result.GetProperty("reward");
        Assert.Equal(2, reward.GetArrayLength());
        Assert.Equal(2, reward[0].GetArrayLength());
        Assert.Equal(2, reward[1].GetArrayLength());
    }

    [Fact]
    public async Task EthFeeHistory_RejectsUnsortedPercentiles()
    {
        var (_, _, _, handlers) = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"eth_feeHistory\",\"params\":[\"0x1\",\"latest\",[80,10]]}");

        using var doc = JsonDocument.Parse(response);
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, error.GetProperty("code").GetInt32());
        Assert.Contains("sorted", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task EthFeeHistory_RejectsFutureNewestBlock()
    {
        var (_, _, _, handlers) = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"eth_feeHistory\",\"params\":[\"0x1\",\"0x3\",[50]]}");

        using var doc = JsonDocument.Parse(response);
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, error.GetProperty("code").GetInt32());
        Assert.Contains("greater than current head", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task EthFeeHistory_WithoutPercentiles_OmitsRewardField()
    {
        var (_, _, _, handlers) = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"eth_feeHistory\",\"params\":[\"0x1\",\"latest\"]}");

        using var doc = JsonDocument.Parse(response);
        var result = doc.RootElement.GetProperty("result");
        Assert.False(result.TryGetProperty("reward", out _));
    }

    [Fact]
    public async Task EthFeeHistory_WhenWindowExceedsGenesis_ClampsOldestAndLengths()
    {
        var (_, _, _, handlers) = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"eth_feeHistory\",\"params\":[\"0x5\",\"latest\",[50]]}");

        using var doc = JsonDocument.Parse(response);
        var result = doc.RootElement.GetProperty("result");

        Assert.Equal("0x0", result.GetProperty("oldestBlock").GetString());
        // Effective count is blocks 0..2 => 3, plus one next base fee => 4
        Assert.Equal(4, result.GetProperty("baseFeePerGas").GetArrayLength());
        Assert.Equal(3, result.GetProperty("gasUsedRatio").GetArrayLength());
        Assert.Equal(3, result.GetProperty("reward").GetArrayLength());
    }

    private static (GlobalState globalState, ChainState chainState, BlockStore blockStore, EthHandlers handlers) BuildFixture(ulong gasPrice = 1_000_000_000)
    {
        var globalState = new GlobalState();
        var mempool = new TxMempool();
        var blockStore = new BlockStore();
        var chainState = new ChainState(31337, blockStore);
        var stateTransition = new StateTransition(new EvmMachine(new List<IOpcode>()));
        var miningService = new Mock<IMiningService>();
        var impersonation = new ImpersonationService();
        var accountManager = new AccountManager();
        var stateManager = new Mock<IStateManager>();

        var block1 = new Block
        {
            Number = 1,
            Hash = "0x" + new string('1', 64),
            GasLimit = 100_000,
            GasUsed = 50_000,
            BaseFeePerGas = 100
        };

        var block2 = new Block
        {
            Number = 2,
            Hash = "0x" + new string('2', 64),
            GasLimit = 100_000,
            GasUsed = 80_000,
            BaseFeePerGas = 110
        };

        chainState.UpdateHead(block1);
        chainState.UpdateHead(block2);

        blockStore.AddReceipt(new TransactionReceipt
        {
            TransactionHash = "0x" + new string('a', 64),
            BlockNumber = 1,
            BlockHash = block1.Hash,
            GasUsed = 21_000,
            EffectiveGasPrice = 130
        });

        blockStore.AddReceipt(new TransactionReceipt
        {
            TransactionHash = "0x" + new string('b', 64),
            BlockNumber = 2,
            BlockHash = block2.Hash,
            GasUsed = 30_000,
            EffectiveGasPrice = 140
        });

        blockStore.AddReceipt(new TransactionReceipt
        {
            TransactionHash = "0x" + new string('c', 64),
            BlockNumber = 2,
            BlockHash = block2.Hash,
            GasUsed = 10_000,
            EffectiveGasPrice = 120
        });

        var handlers = new EthHandlers(
            globalState,
            mempool,
            chainState,
            stateTransition,
            miningService.Object,
            impersonation,
            accountManager,
            new NodeConfiguration { Accounts = 0, ChainId = 31337, GasPrice = gasPrice },
            stateManager.Object);

        return (globalState, chainState, blockStore, handlers);
    }
}
