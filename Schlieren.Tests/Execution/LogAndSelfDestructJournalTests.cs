using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Forks;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Tests.Execution;

public sealed class LogAndSelfDestructJournalTests
{
    [Fact]
    public async Task Log0_RecordsTypedLogAtItsInstruction()
    {
        var target = Address.FromHex("0x8300000000000000000000000000000000000003");
        var state = new GlobalState();
        state.SetCode(target, Convert.FromHexString("60006000A000"));

        var result = await Execute(state, target,
            [new OpcodePush1(), new OpcodeLog0(), new OpcodeStop()]);

        var effect = Assert.Single(result.Journal!.Events.OfType<LogEmittedEvent>());
        Assert.Equal(target, effect.Address);
        Assert.Empty(effect.Topics);
        Assert.Empty(effect.Data);
        Assert.Contains(result.Journal.Events.OfType<OpcodeGasEvent>(),
            opcode => opcode.InstructionId == effect.InstructionId && opcode.Name == "LOG0");
    }

    [Fact]
    public async Task SelfDestruct_RecordsTransferAndDeletionDecision()
    {
        var target = Address.FromHex("0x8400000000000000000000000000000000000004");
        var beneficiary = Address.FromHex("0x8500000000000000000000000000000000000005");
        var code = new List<byte> { 0x73 };
        code.AddRange(beneficiary.Bytes);
        code.Add(0xff);
        var state = new GlobalState();
        state.SetCode(target, code.ToArray());
        state.SetBalance(target, 9);

        var result = await Execute(state, target,
            [new OpcodePush20(), new OpcodeSelfDestruct()]);

        var effect = Assert.Single(result.Journal!.Events.OfType<SelfDestructEvent>());
        Assert.Equal(target, effect.Contract);
        Assert.Equal(beneficiary, effect.Beneficiary);
        Assert.Equal(new BigInteger(9), effect.TransferredBalance);
        Assert.False(effect.DeletionEligible);
        Assert.False(effect.DeletionScheduled);
        Assert.Single(result.Journal.Events.OfType<BalanceTransferEvent>(),
            transfer => transfer.Reason == BalanceTransferReason.SelfDestruct);
    }

    private static Task<ExecutionResult> Execute(GlobalState state, Address target, IEnumerable<IOpcode> opcodes) =>
        new StateTransition(new EvmMachine(opcodes)).ApplyTransactionAsync(
            new Transaction
            {
                To = target,
                GasLimit = 100_000,
                Authorization = TransactionAuthorization.Internal,
                EnableJournal = true
            }, state, new BlockContext { Rules = ForkRulesFactory.For("Osaka") }, commit: false);
}
