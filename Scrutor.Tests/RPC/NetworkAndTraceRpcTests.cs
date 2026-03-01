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
using Scrutor.RPC.Handlers;
using Scrutor.RPC.Models;
using Scrutor.RPC.Server;
using Xunit;

namespace Scrutor.Tests.RPC;

public class NetworkAndTraceRpcTests
{
    [Fact]
    public async Task Router_NetworkAndClientMethods_ReturnExpectedValues()
    {
        var (chainState, _, _, handlers) = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var netVersionResponse = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"net_version\",\"params\":[]}");
        using var netVersionDoc = JsonDocument.Parse(netVersionResponse);
        Assert.Equal(chainState.ChainId.ToString(), netVersionDoc.RootElement.GetProperty("result").GetString());

        var netListeningResponse = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"net_listening\",\"params\":[]}");
        using var netListeningDoc = JsonDocument.Parse(netListeningResponse);
        Assert.True(netListeningDoc.RootElement.GetProperty("result").GetBoolean());

        var netPeerCountResponse = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"net_peerCount\",\"params\":[]}");
        using var netPeerCountDoc = JsonDocument.Parse(netPeerCountResponse);
        Assert.Equal("0x0", netPeerCountDoc.RootElement.GetProperty("result").GetString());

        var web3ClientVersionResponse = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"web3_clientVersion\",\"params\":[]}");
        using var web3ClientVersionDoc = JsonDocument.Parse(web3ClientVersionResponse);
        var clientVersion = web3ClientVersionDoc.RootElement.GetProperty("result").GetString();
        Assert.NotNull(clientVersion);
        Assert.StartsWith("Scrutor/", clientVersion);
    }

    [Fact]
    public async Task DebugTraceTransaction_ReturnsStructuredTraceForKnownTransaction()
    {
        var (_, globalState, txHashHex, handlers) = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest(
            $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"debug_traceTransaction\",\"params\":[\"{txHashHex}\",{{}}]}}");

        using var doc = JsonDocument.Parse(response);
        var result = doc.RootElement.GetProperty("result");

        Assert.StartsWith("0x", result.GetProperty("gas").GetString());
        Assert.True(result.TryGetProperty("failed", out _));
        Assert.True(result.TryGetProperty("returnValue", out _));

        var structLogs = result.GetProperty("structLogs");
        Assert.True(structLogs.GetArrayLength() > 0);
        Assert.Equal("PUSH1", structLogs[0].GetProperty("op").GetString());
    }

    [Fact]
    public async Task DebugTraceTransaction_ReturnsErrorForMissingTransaction()
    {
        var (_, _, _, handlers) = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"debug_traceTransaction\",\"params\":[\"0xdeadbeef\"]}");

        using var doc = JsonDocument.Parse(response);
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, error.GetProperty("code").GetInt32());
        Assert.Contains("Transaction not found", error.GetProperty("message").GetString());
    }

    private static (ChainState chainState, GlobalState globalState, string txHashHex, EthHandlers handlers) BuildFixture()
    {
        var globalState = new GlobalState();
        var mempool = new TxMempool();
        var blockStore = new BlockStore();
        var chainState = new ChainState(31337, blockStore);
        var stateTransition = new StateTransition(new EvmMachine(new List<IOpcode> { new OpcodeStop(), new OpcodePush1(), new OpcodeSstore() }));
        var miningService = new Mock<IMiningService>();
        var impersonation = new ImpersonationService();
        var accountManager = new AccountManager();
        var stateManager = new Mock<IStateManager>();

        var targetAddress = Address.FromHex("0x1000000000000000000000000000000000000001");
        // PUSH1 0x00 PUSH1 0x00 SSTORE STOP
        globalState.SetCode(targetAddress, new byte[] { 0x60, 0x00, 0x60, 0x00, 0x55, 0x00 });

        var txHash = new byte[32];
        txHash[31] = 0xAA;
        var txHashHex = EthereumTypes.ToEthHex(txHash);

        var tx = new Transaction
        {
            Hash = txHash,
            From = Address.FromHex("0x2000000000000000000000000000000000000002"),
            To = targetAddress,
            GasLimit = 21000,
            GasPrice = 1
        };
        globalState.SetBalance(tx.From, 1_000_000_000);

        var block = new Block
        {
            Number = 1,
            Hash = "0x" + new string('1', 64),
            Transactions = new List<Transaction> { tx }
        };
        chainState.UpdateHead(block);

        blockStore.AddReceipt(new TransactionReceipt
        {
            TransactionHash = txHashHex,
            BlockNumber = 1,
            BlockHash = block.Hash,
            GasUsed = 21000,
            CumulativeGasUsed = 21000,
            Status = 1
        });

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

        return (chainState, globalState, txHashHex, handlers);
    }
}
