using System.Numerics;
using System.Text;
using Scrutor.Core.Execution;
using Scrutor.Core.Models;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using Scrutor.Core.Opcodes;
using Scrutor.Core.Configuration;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;

namespace Scrutor.Tests.State;

public class ReceiptTests
{
    [Fact]
    public async Task MiningService_GeneratesReceipts()
    {
        // Arrange
        var mempool = new TxMempool();
        var globalState = new GlobalState();
        var blockStore = new BlockStore();
        var chainState = new ChainState(1, blockStore);
        
        // Setup EVM with Log support
        var opcodes = new List<IOpcode> { new OpcodeLog0(), new OpcodePush1(), new OpcodeStop() };
        var evm = new EvmMachine(opcodes);
        var stateTransition = new StateTransition(evm);
        
        var logger = new Mock<ILogger<MiningService>>();
        var service = new MiningService(mempool, globalState, chainState, stateTransition, logger.Object);

        var code = new byte[] { 0x60, 0x00, 0x60, 0x00, 0xA0, 0x00 };
        var sender = Address.FromHex("0x1234567890123456789012345678901234567890");
        globalState.SetBalance(sender, 1000000);
        
        // [AI-EDIT 2026-01-10] Use a non-precompile address (0x01 is ecRecover precompile).
        var tx = new Transaction
        {
            From = sender,
            To = Address.FromHex("0x0000000000000000000000000000000000001001"),
            Data = Array.Empty<byte>(),
            GasLimit = 100000,
            GasPrice = 1,
            Nonce = 0,
            Hash = new byte[32] { 0x01, 0x02, 0x03, 0x04, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0 },
            Authorization = TransactionAuthorization.Impersonated
        };
        globalState.SetCode(tx.To.Value, code);
        
        mempool.Add(tx);

        // Act
        var cts = new CancellationTokenSource();
        var task = service.StartAsync(cts.Token);
        
        for(int i=0; i<20; i++)
        {
            if (chainState.CurrentBlock.Number > 0) break;
            await Task.Delay(50);
        }
        
        cts.Cancel();

        // Assert
        Assert.True(chainState.CurrentBlock.Number > 0, "Mining service failed to produce a block in time.");
        var receipt = blockStore.GetReceiptByHash("0x" + Convert.ToHexString(tx.Hash).ToLowerInvariant());
        
        Assert.NotNull(receipt);
        Assert.Equal(1UL, receipt.Status);
        Assert.Single(receipt.Logs);
        Assert.Equal(tx.To.Value.ToString(), receipt.Logs[0].Address);
    }
}
