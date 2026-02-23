using System.Numerics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Scrutor.Core.Execution;
using Scrutor.Core.Models;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using Scrutor.Core.Opcodes;
using Scrutor.Core.Configuration;
using Scrutor.RPC.Handlers;
using Xunit;

namespace Scrutor.Tests;

public class CompatibilityTests
{
    [Fact]
    public async Task ReceiptCompatibility_MatchesStandardBehavior()
    {
        // Setup
        var mempool = new TxMempool();
        var globalState = new GlobalState();
        var blockStore = new BlockStore();
        var chainState = new ChainState(1, blockStore);
        
        var opcodes = new List<IOpcode> { new OpcodeLog0(), new OpcodeLog1(), new OpcodePush1(), new OpcodeStop() };
        var evm = new EvmMachine(opcodes);
        var stateTransition = new StateTransition(evm);
        
        var logger = new Mock<ILogger<MiningService>>();
        var accountManager = new AccountManager();
        var impersonation = new ImpersonationService();
        var miningService = new MiningService(mempool, globalState, chainState, stateTransition, logger.Object);
        var stateManager = new Mock<IStateManager>();

        var handlers = new EthHandlers(globalState, mempool, chainState, stateTransition, miningService, impersonation, accountManager, new NodeConfiguration { Accounts = 0 }, stateManager.Object);

        var sender = Address.FromHex("0x1234567890123456789012345678901234567890");
        globalState.SetBalance(sender, 10000000);

        // Contract that logs twice
        var code = new byte[] { 0x60, 0x00, 0x60, 0x00, 0xA0, 0x60, 0xAA, 0x60, 0x00, 0x60, 0x00, 0xA1, 0x00 };
        var contract = Address.FromHex("0x0000000000000000000000000000000000000001");
        globalState.SetCode(contract, code);

        // TX 1: Nonce 0, GasPrice 20
        var tx1 = new Transaction { From = sender, To = contract, GasPrice = 20, Hash = new byte[32] { 1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0 }, Nonce = 0, GasLimit = 100000, Authorization = TransactionAuthorization.Impersonated };
        // TX 2: Nonce 1, GasPrice 10
        var tx2 = new Transaction { From = sender, To = contract, GasPrice = 10, Hash = new byte[32] { 2,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0 }, Nonce = 1, GasLimit = 100000, Authorization = TransactionAuthorization.Impersonated };

        mempool.Add(tx1);
        mempool.Add(tx2);

        // Produce block
        await miningService.MineAsync(CancellationToken.None);
        
        // Verify Receipts
        var receipt1 = blockStore.GetReceiptByHash("0x" + Convert.ToHexString(tx1.Hash).ToLowerInvariant());
        var receipt2 = blockStore.GetReceiptByHash("0x" + Convert.ToHexString(tx2.Hash).ToLowerInvariant());

        Assert.NotNull(receipt1);
        Assert.NotNull(receipt2);

        // Indices
        Assert.Equal(0UL, receipt1.TransactionIndex);
        Assert.Equal(1UL, receipt2.TransactionIndex);

        // Log Indices (Block-global)
        Assert.Equal(2, receipt1.Logs.Count);
        Assert.Equal(0UL, receipt1.Logs[0].LogIndex);
        Assert.Equal(1UL, receipt1.Logs[1].LogIndex);

        Assert.Equal(2, receipt2.Logs.Count);
        Assert.Equal(2UL, receipt2.Logs[0].LogIndex);
        Assert.Equal(3UL, receipt2.Logs[1].LogIndex);

        // Cumulative Gas
        Assert.Equal(receipt1.GasUsed, receipt1.CumulativeGasUsed);
        Assert.Equal(receipt1.GasUsed + receipt2.GasUsed, receipt2.CumulativeGasUsed);
        
        Assert.Equal(receipt2.CumulativeGasUsed, chainState.CurrentBlock.GasUsed);
    }

    [Fact]
    public void GetLogsCompatibility_MatchesStandardBehavior()
    {
        // Setup
        var mempool = new TxMempool();
        var globalState = new GlobalState();
        var blockStore = new BlockStore();
        var chainState = new ChainState(1, blockStore);
        var stateTransition = new StateTransition(new EvmMachine(new List<IOpcode>()));
        var miningServiceMock = new Mock<IMiningService>();
        var impersonationService = new ImpersonationService();
        var accountManager = new AccountManager();
        var stateManager = new Mock<IStateManager>();
        var ethHandlers = new EthHandlers(globalState, mempool, chainState, stateTransition, miningServiceMock.Object, impersonationService, accountManager, new NodeConfiguration { Accounts = 0 }, stateManager.Object);

        var log1 = new TransactionLog 
        {
            BlockNumber = 1, Address = "0x11", LogIndex = 0, 
            Topics = new List<string> { "0x00000000000000000000000000000000000000000000000000000000000000aa", "0x00000000000000000000000000000000000000000000000000000000000000bb" },
            TransactionHash = "0x01"
        };
        var log2 = new TransactionLog 
        {
            BlockNumber = 1, Address = "0x22", LogIndex = 1, 
            Topics = new List<string> { "0x00000000000000000000000000000000000000000000000000000000000000cc", "0x00000000000000000000000000000000000000000000000000000000000000dd" },
            TransactionHash = "0x02"
        };
        
        var receipt1 = new TransactionReceipt { TransactionHash = "0x01", BlockNumber = 1, Logs = new List<TransactionLog> { log1 } };
        var receipt2 = new TransactionReceipt { TransactionHash = "0x02", BlockNumber = 1, Logs = new List<TransactionLog> { log2 } };
        
        blockStore.AddReceipt(receipt1);
        blockStore.AddReceipt(receipt2);
        chainState.UpdateHead(new Block { Number = 1, Hash = "0xABC" });

        // 1. Filter by address (OR list)
        var logs = ethHandlers.HandleGetLogs(new object[] { 
            JsonSerializer.Deserialize<JsonElement>("{\"address\": [\"0x11\", \"0x22\"]}") 
        });
        Assert.Equal(2, logs.Count);

        // 2. Filter by topic0 (Exact)
        logs = ethHandlers.HandleGetLogs(new object[] { 
            JsonSerializer.Deserialize<JsonElement>("{\"topics\": [\"0x00000000000000000000000000000000000000000000000000000000000000aa\"]}") 
        });
        Assert.Single(logs);
        Assert.Equal("0x00000000000000000000000000000000000000000000000000000000000000aa", logs[0].Topics[0], ignoreCase: true);

        // 3. Filter by topic0 (OR list)
        logs = ethHandlers.HandleGetLogs(new object[] { 
            JsonSerializer.Deserialize<JsonElement>("{\"topics\": [[\"0x00000000000000000000000000000000000000000000000000000000000000aa\", \"0x00000000000000000000000000000000000000000000000000000000000000cc\"]]}") 
        });
        Assert.Equal(2, logs.Count);

        // 4. Filter by topic1 with wildcard topic0
        logs = ethHandlers.HandleGetLogs(new object[] { 
            JsonSerializer.Deserialize<JsonElement>("{\"topics\": [null, \"0x00000000000000000000000000000000000000000000000000000000000000bb\"]}") 
        });
        Assert.Single(logs);
        Assert.Equal("0x00000000000000000000000000000000000000000000000000000000000000aa", logs[0].Topics[0], ignoreCase: true);

        // 5. Filter by non-existent topic
        logs = ethHandlers.HandleGetLogs(new object[] { 
            JsonSerializer.Deserialize<JsonElement>("{\"topics\": [\"0x00000000000000000000000000000000000000000000000000000000000000ff\"]}") 
        });
        Assert.Empty(logs);
    }
}
