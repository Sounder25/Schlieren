using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Forks;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Tests.Execution;

public sealed class AccountEffectJournalTests
{
    [Fact]
    public async Task TransactionValue_RecordsOneLogicalFrameTransfer()
    {
        var sender = Address.FromHex("0x8100000000000000000000000000000000000001");
        var target = Address.FromHex("0x8200000000000000000000000000000000000002");
        var state = new GlobalState();
        state.SetBalance(sender, 1_000_000);
        state.SetCode(target, [0x00]);

        var result = await new StateTransition(new EvmMachine([new OpcodeStop()]))
            .ApplyTransactionAsync(new Transaction
            {
                From = sender,
                To = target,
                Value = 5,
                GasLimit = 100_000,
                GasPrice = 1,
                Authorization = TransactionAuthorization.Impersonated,
                EnableJournal = true
            }, state, new BlockContext { BaseFeePerGas = 1, Rules = ForkRulesFactory.For("Osaka") }, commit: false);

        Assert.True(result.IsSuccess);
        var transfer = Assert.Single(result.Journal!.Events.OfType<BalanceTransferEvent>(),
            effect => effect.Reason == BalanceTransferReason.TransactionValue);
        Assert.Equal(sender, transfer.From);
        Assert.Equal(target, transfer.To);
        Assert.Equal(new BigInteger(5), transfer.Amount);
        Assert.Equal(StateEffectScope.Frame, transfer.Scope);
    }
}
