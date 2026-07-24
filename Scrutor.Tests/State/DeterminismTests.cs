using System.Numerics;
using Microsoft.Extensions.Logging;
using Moq;
using Scrutor.Core.Execution;
using Scrutor.Core.Models;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using Scrutor.Core.Opcodes;
using Xunit;

namespace Scrutor.Tests.State;

public class DeterminismTests
{
    [Fact]
    public async Task BlockProduction_IsDeterministic()
    {
        // Setup shared environment
        var sender = Address.FromHex("0x1234567890123456789012345678901234567890");
        var recipient = Address.FromHex("0xdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef");
        var hash1 = new byte[32]; hash1[0] = 1;
        var hash2 = new byte[32]; hash2[0] = 2;

        // Simple ETH transfers (To is required so intrinsic gas = 21000, matching GasLimit exactly)
        var tx1 = new Transaction { From = sender, To = recipient, GasPrice = 20, Hash = hash1, Nonce = 0, GasLimit = 21000, Authorization = TransactionAuthorization.Impersonated };
        var tx2 = new Transaction { From = sender, To = recipient, GasPrice = 10, Hash = hash2, Nonce = 1, GasLimit = 21000, Authorization = TransactionAuthorization.Impersonated };

        // Run 1
        var block1 = await ProduceTestBlock(tx1, tx2);
        
        // Run 2
        var block2 = await ProduceTestBlock(tx1, tx2);

        // Assert
        Assert.Equal(block1.Hash, block2.Hash);
        Assert.Equal(2, block1.Transactions.Count);
        Assert.Equal(block1.Transactions[0].Hash, block2.Transactions[0].Hash); // Should be tx1 (higher gas price)
        Assert.Equal(block1.Transactions[1].Hash, block2.Transactions[1].Hash);
    }

    private async Task<Block> ProduceTestBlock(params Transaction[] txs)
    {
        var mempool = new TxMempool();
        var globalState = new GlobalState();
        var blockStore = new BlockStore();
        var chainState = new ChainState(1, blockStore);
        
        // Setup basic EVM
        var opcodes = new List<IOpcode> { new OpcodeStop() };
        var evm = new EvmMachine(opcodes);
        var stateTransition = new StateTransition(evm);
        
        var logger = new Mock<ILogger<MiningService>>();
        var service = new MiningService(mempool, globalState, chainState, stateTransition, logger.Object);

        globalState.SetBalance(txs[0].From, 10000000);

        foreach (var tx in txs) mempool.Add(tx);

        // Produce block
        await service.MineAsync(CancellationToken.None);

        return chainState.CurrentBlock;
    }
}
