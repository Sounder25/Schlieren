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
        var chainState = new ChainState(1, new BlockStore());
        var stateTransition = new StateTransition(new EvmMachine(new List<IOpcode>()));
        var miningService = new Mock<IMiningService>();
        var impersonationService = new Mock<IImpersonationService>();
        var accountManager = new AccountManager();
        var handlers = new EthHandlers(globalState, mempool, chainState, stateTransition, miningService.Object, impersonationService.Object, accountManager, new NodeConfiguration { Accounts = 0 });

        // Act & Assert
        var ex = Assert.Throws<RpcException>(() => handlers.HandleSendRawTransaction(new object[] { "0x020102" }));
        
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("Typed transactions (type 0x02) are not yet supported", ex.Message);
    }
}