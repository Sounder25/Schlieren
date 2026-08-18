using System.Text.Json;
using Moq;
using Schlieren.Core.Configuration;
using Schlieren.Core.Execution;
using Schlieren.Core.Models;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using Schlieren.RPC;
using Schlieren.RPC.Handlers;
using Schlieren.RPC.Models;
using Xunit;

namespace Schlieren.Tests.RPC;

public class EthGetBlockByNumberRpcTests
{
    [Fact]
    public void HandleGetBlockByNumber_ReturnsHashes_WhenFullTransactionsFalse()
    {
        var handlers = BuildFixture();

        var result = handlers.HandleGetBlockByNumber(new object[] { "0x1", false });
        Assert.NotNull(result);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal("0x1", doc.RootElement.GetProperty("number").GetString());

        var txs = doc.RootElement.GetProperty("transactions");
        Assert.Equal(JsonValueKind.Array, txs.ValueKind);
        Assert.Equal("0x" + new string('a', 64), txs[0].GetString());
    }

    [Fact]
    public void HandleGetBlockByNumber_ReturnsFullTransactions_WhenFullTransactionsTrue()
    {
        var handlers = BuildFixture();

        var result = handlers.HandleGetBlockByNumber(new object[] { "latest", true });
        Assert.NotNull(result);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        var tx0 = doc.RootElement.GetProperty("transactions")[0];
        Assert.Equal("0x" + new string('a', 64), tx0.GetProperty("hash").GetString());
        Assert.Equal("0x0", tx0.GetProperty("transactionIndex").GetString());
        Assert.Equal("0x1", tx0.GetProperty("blockNumber").GetString());
    }

    [Fact]
    public void HandleGetBlockByNumber_ReturnsNull_WhenBlockMissing()
    {
        var handlers = BuildFixture();
        var result = handlers.HandleGetBlockByNumber(new object[] { "0x999", false });
        Assert.Null(result);
    }

    [Fact]
    public void HandleGetBlockByNumber_RejectsInvalidTag()
    {
        var handlers = BuildFixture();
        var ex = Assert.Throws<RpcException>(() => handlers.HandleGetBlockByNumber(new object[] { "safe", false }));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("Invalid block number", ex.Message);
    }

    private static EthHandlers BuildFixture()
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

        var tx = new Transaction
        {
            Hash = Convert.FromHexString(new string('a', 64)),
            From = Address.FromHex("0x2000000000000000000000000000000000000002"),
            To = Address.FromHex("0x1000000000000000000000000000000000000001"),
            Nonce = 3,
            GasLimit = 21000,
            GasPrice = 1,
            Value = 0
        };

        var block = new Block
        {
            Number = 1,
            Hash = "0x" + new string('1', 64),
            ParentHash = "0x" + new string('0', 64),
            Timestamp = 12345,
            GasLimit = 30_000_000,
            GasUsed = 21_000,
            BaseFeePerGas = 1,
            Transactions = new List<Transaction> { tx }
        };

        chainState.UpdateHead(block);

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
