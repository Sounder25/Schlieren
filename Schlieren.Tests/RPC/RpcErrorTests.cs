using Schlieren.RPC.Handlers;
using Schlieren.RPC.Models;
using Schlieren.RPC;
using Schlieren.Core.State;
using Schlieren.Core.Execution;
using Schlieren.Core.Primitives;
using Schlieren.Core.Configuration;
using Xunit;
using Moq;

namespace Schlieren.Tests.RPC;

public class RpcErrorTests
{
    [Fact]
    public async Task HandleSendRawTransaction_RejectsUnknownTypedTransaction_WithCorrectErrorCode()
    {
        // Arrange
        var globalState = new GlobalState();
        var mempool = new TxMempool();
        var blockStore = new BlockStore();
        var chainState = new ChainState(1, blockStore);
        var stateTransition = new StateTransition(new EvmMachine(new List<IOpcode>()));
        var miningService = new Mock<IMiningService>();
        var impersonation = new Mock<IImpersonationService>();
        var accountManager = new Mock<IAccountManager>();
        var stateManager = new Mock<IStateManager>();
        
        var handlers = new EthHandlers(globalState, mempool, chainState, stateTransition, miningService.Object, impersonation.Object, accountManager.Object, new NodeConfiguration { Accounts = 0 }, stateManager.Object);

        // EIP-2718 Typed envelope with unsupported transaction type 0x04
        var rawTx = "0x04c0";

        // Act & Assert
        var ex = await Assert.ThrowsAsync<RpcException>(() => handlers.HandleSendRawTransaction(new object[] { rawTx }));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("Unsupported typed transaction type 0x04", ex.Message);
    }
}
