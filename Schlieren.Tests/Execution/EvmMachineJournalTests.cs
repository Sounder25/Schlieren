using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using EvmExecutionContext = Schlieren.Core.Execution.ExecutionContext;

namespace Schlieren.Tests.Execution;

public sealed class EvmMachineJournalTests
{
    [Fact]
    public async Task Push1_RecordsExclusiveGasCharge()
    {
        var (context, journal) = CreateJournalContext([0x60, 0x2a], gasLimit: 100);

        var result = await new EvmMachine([new OpcodePush1()]).ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        var gas = Assert.Single(journal.Events.OfType<OpcodeGasEvent>());
        Assert.Equal("PUSH1", gas.Name);
        Assert.Equal(3UL, gas.Amount);
        Assert.Equal(GasSemantics.ExclusiveCharge, gas.Semantics);
    }

    [Fact]
    public async Task InvalidOpcode_RecordsExceptionalBurnForRemainingFrameGas()
    {
        var (context, journal) = CreateJournalContext([0xfe], gasLimit: 65_535);

        var result = await new EvmMachine([]).ExecuteAsync(context);

        Assert.Equal(EvmError.InvalidOpcode, result.Error);
        var burn = Assert.Single(journal.Events.OfType<ExceptionalGasBurnedEvent>());
        Assert.Equal(65_535UL, burn.Amount);
        Assert.Equal(EvmError.InvalidOpcode, burn.Error);
        Assert.Equal(GasSemantics.ExceptionalBurn, burn.Semantics);
    }

    [Fact]
    public async Task OutOfGas_RecordsObservationAndExceptionalBurn()
    {
        var (context, journal) = CreateJournalContext([0x51], gasLimit: 2);
        context.Stack.Push(0);

        var result = await new EvmMachine([new OpcodeMload()]).ExecuteAsync(context);

        Assert.Equal(EvmError.OutOfGas, result.Error);
        var gas = Assert.Single(journal.Events.OfType<OpcodeGasEvent>());
        Assert.Equal(0UL, gas.Amount);
        Assert.Equal(GasSemantics.Observation, gas.Semantics);
        var burn = Assert.Single(journal.Events.OfType<ExceptionalGasBurnedEvent>());
        Assert.Equal(2UL, burn.Amount);
        Assert.Equal(EvmError.OutOfGas, burn.Error);
    }

    [Fact]
    public async Task Call_RecordsInclusiveFrameDelta()
    {
        var (context, journal) = CreateJournalContext([0xf1], gasLimit: 100_000);
        var callee = Address.FromHex("0x0000000000000000000000000000000000001000");
        context.GlobalState = new GlobalState();
        context.SubCall = (_, _, _, _) => Task.FromResult(ExecutionResult.Success(0));
        context.Access.WarmAddress(callee);
        context.Stack.Push(0);      // return length
        context.Stack.Push(0);      // return offset
        context.Stack.Push(0);      // argument length
        context.Stack.Push(0);      // argument offset
        context.Stack.Push(0);      // value
        context.Stack.Push(0x1000); // callee
        context.Stack.Push(10_000); // requested gas

        var result = await new EvmMachine([new OpcodeCall()]).ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        var gas = Assert.Single(journal.Events.OfType<OpcodeGasEvent>());
        Assert.Equal("CALL", gas.Name);
        Assert.Equal(GasSemantics.InclusiveFrameDelta, gas.Semantics);
    }

    private static (EvmExecutionContext Context, ExecutionJournal Journal) CreateJournalContext(
        byte[] code,
        ulong gasLimit)
    {
        var journal = new ExecutionJournal();
        long frameId = journal.OpenFrame(parentFrameId: null);
        var context = new EvmExecutionContext
        {
            Code = code,
            GasLimit = gasLimit,
            Journal = journal,
            JournalFrameId = frameId,
            JournalParentFrameId = null
        };
        return (context, journal);
    }
}
