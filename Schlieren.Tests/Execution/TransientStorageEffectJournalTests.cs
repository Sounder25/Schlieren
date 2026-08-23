using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Forks;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Tests.Execution;

public sealed class TransientStorageEffectJournalTests
{
    [Fact]
    public async Task TstoreThenTload_RecordsPreviousWriteAndObservedRead()
    {
        var target = Address.FromHex("0x7200000000000000000000000000000000000002");
        var state = new GlobalState();
        state.SetCode(target, Convert.FromHexString("602A60015D60015C00"));

        var result = await new StateTransition(new EvmMachine(
        [new OpcodePush1(), new OpcodeTstore(), new OpcodeTload(), new OpcodeStop()]))
            .ApplyTransactionAsync(
                new Transaction
                {
                    To = target,
                    GasLimit = 100_000,
                    Authorization = TransactionAuthorization.Internal,
                    EnableJournal = true
                }, state, new BlockContext { Rules = ForkRulesFactory.For("Osaka") }, commit: false);

        Assert.True(result.IsSuccess);
        var write = Assert.Single(result.Journal!.Events.OfType<TransientStorageWriteEvent>());
        var read = Assert.Single(result.Journal.Events.OfType<TransientStorageReadEvent>());
        Assert.Equal(BigInteger.Zero, write.PreviousValue);
        Assert.Equal(new BigInteger(42), write.Value);
        Assert.Equal(write.Value, read.Value);
        Assert.Equal(write.StorageAddress, read.StorageAddress);
        Assert.NotEqual(write.InstructionId, read.InstructionId);
    }
}
