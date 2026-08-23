using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Forks;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Tests.Execution;

public sealed class StorageEffectJournalTests
{
    [Fact]
    public async Task Sstore_RecordsExactWriteAndCorrelatesItsOpcode()
    {
        var target = Address.FromHex("0x7100000000000000000000000000000000000001");
        var state = new GlobalState();
        state.SetCode(target, Convert.FromHexString("602A60015500"));
        state.SetStorageAt(target, 1, 7);

        var result = await new StateTransition(new EvmMachine(
        [new OpcodePush1(), new OpcodeSstore(), new OpcodeStop()]))
            .ApplyTransactionAsync(
                new Transaction
                {
                    To = target,
                    GasLimit = 100_000,
                    Authorization = TransactionAuthorization.Internal,
                    EnableJournal = true
                }, state, new BlockContext { Rules = ForkRulesFactory.For("Osaka") }, commit: false);

        Assert.True(result.IsSuccess);
        var write = Assert.Single(result.Journal!.Events.OfType<StorageWriteEvent>());
        Assert.Equal(target, write.StorageAddress);
        Assert.Equal(BigInteger.One, write.Slot);
        Assert.Equal(new BigInteger(7), write.OriginalValue);
        Assert.Equal(new BigInteger(7), write.PreviousValue);
        Assert.Equal(new BigInteger(42), write.Value);
        Assert.Equal((byte)0x55, write.Opcode);
        Assert.NotNull(write.InstructionId);
        Assert.Contains(result.Journal.Events.OfType<OpcodeGasEvent>(),
            op => op.InstructionId == write.InstructionId && op.Name == "SSTORE");
        Assert.Equal(PersistenceDisposition.SimulationDiscarded,
            Assert.Single(JournalAnalysis.Build(result.Journal).StateEffects).PersistenceDisposition);
    }
}
