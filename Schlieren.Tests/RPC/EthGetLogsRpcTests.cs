using System.Text.Json;
using Moq;
using Schlieren.Core.Configuration;
using Schlieren.Core.Execution;
using Schlieren.Core.Models;
using Schlieren.Core.Opcodes;
using Schlieren.Core.State;
using Schlieren.RPC;
using Schlieren.RPC.Handlers;
using Schlieren.RPC.Models;
using Xunit;

namespace Schlieren.Tests.RPC;

public class EthGetLogsRpcTests
{
    [Fact]
    public void GetLogs_BlockHashWithRange_ThrowsInvalidParams()
    {
        var handlers = BuildFixture();
        var filter = JsonSerializer.Deserialize<JsonElement>(
            "{\"blockHash\":\"0x1111111111111111111111111111111111111111111111111111111111111111\",\"fromBlock\":\"0x1\"}");

        var ex = Assert.Throws<RpcException>(() => handlers.HandleGetLogs(new object[] { filter }));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("Cannot specify blockHash", ex.Message);
    }

    [Fact]
    public void GetLogs_BlockHashOnly_SearchesSingleBlock()
    {
        var handlers = BuildFixture();
        var filter = JsonSerializer.Deserialize<JsonElement>(
            "{\"blockHash\":\"0x1111111111111111111111111111111111111111111111111111111111111111\"}");

        var logs = handlers.HandleGetLogs(new object[] { filter });
        Assert.Single(logs);
        Assert.Equal(1UL, logs[0].BlockNumber);
    }

    [Fact]
    public void GetLogs_InvalidAddressFilter_ThrowsInvalidParams()
    {
        var handlers = BuildFixture();
        var filter = JsonSerializer.Deserialize<JsonElement>("{\"address\":\"0x123\"}");

        var ex = Assert.Throws<RpcException>(() => handlers.HandleGetLogs(new object[] { filter }));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("Invalid address filter", ex.Message);
    }

    [Fact]
    public void GetLogs_EmptyAddressArray_MatchesNothing()
    {
        var handlers = BuildFixture();
        var filter = JsonSerializer.Deserialize<JsonElement>("{\"address\":[]}");
        var logs = handlers.HandleGetLogs(new object[] { filter });
        Assert.Empty(logs);
    }

    private static EthHandlers BuildFixture()
    {
        var globalState = new GlobalState();
        var mempool = new TxMempool();
        var blockStore = new BlockStore();
        var chainState = new ChainState(31337, blockStore);
        var stateTransition = new StateTransition(new EvmMachine(new List<IOpcode> { new OpcodeStop() }));
        var miningService = new Mock<IMiningService>();
        var impersonation = new ImpersonationService();
        var accountManager = new AccountManager();
        var stateManager = new Mock<IStateManager>();

        var block1 = new Block
        {
            Number = 1,
            Hash = "0x1111111111111111111111111111111111111111111111111111111111111111",
            ParentHash = "0x" + new string('0', 64),
            GasLimit = 30_000_000,
            Timestamp = 100
        };
        var block2 = new Block
        {
            Number = 2,
            Hash = "0x2222222222222222222222222222222222222222222222222222222222222222",
            ParentHash = block1.Hash,
            GasLimit = 30_000_000,
            Timestamp = 101
        };
        chainState.UpdateHead(block1);
        chainState.UpdateHead(block2);

        blockStore.AddReceipt(new TransactionReceipt
        {
            TransactionHash = "0xaa",
            BlockNumber = 1,
            BlockHash = block1.Hash,
            Logs = new List<TransactionLog>
            {
                new()
                {
                    Address = "0x1111111111111111111111111111111111111111",
                    Topics = new List<string>{ "0x00000000000000000000000000000000000000000000000000000000000000aa" },
                    BlockNumber = 1,
                    BlockHash = block1.Hash,
                    LogIndex = 0
                }
            }
        });
        blockStore.AddReceipt(new TransactionReceipt
        {
            TransactionHash = "0xbb",
            BlockNumber = 2,
            BlockHash = block2.Hash,
            Logs = new List<TransactionLog>
            {
                new()
                {
                    Address = "0x2222222222222222222222222222222222222222",
                    Topics = new List<string>{ "0x00000000000000000000000000000000000000000000000000000000000000bb" },
                    BlockNumber = 2,
                    BlockHash = block2.Hash,
                    LogIndex = 1
                }
            }
        });

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
