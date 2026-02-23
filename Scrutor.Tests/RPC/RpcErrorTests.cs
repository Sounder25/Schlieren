using Scrutor.RPC.Handlers;
using Scrutor.RPC.Models;
using Scrutor.RPC;
using Scrutor.Core.State;
using Scrutor.Core.Execution;
using Scrutor.Core.Primitives;
using Scrutor.Core.Configuration;
using Xunit;
using Moq;

namespace Scrutor.Tests.RPC;

public class RpcErrorTests
{
    [Fact]
    public void HandleSendRawTransaction_RejectsTypedTransaction_WithCorrectErrorCode()
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

        // EIP-2718 Typed Transaction (0x02 || RLP(...))
        var rawTx = "0x02f871018302a90f843b9aca00850dbe60a3d78252089470997970c51812dc3a010c7d01b50e0d17dc79c88080c080a06f2c349074b967d620c571754f9a767674257176767676767676767676767676a06f2c349074b967d620c571754f9a767674257176767676767676767676767676";

        // Act & Assert
        var ex = Assert.Throws<RpcException>(() => handlers.HandleSendRawTransaction(new object[] { rawTx }));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("Typed transactions (type 0x02) are not yet supported", ex.Message);
    }
}
