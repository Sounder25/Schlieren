using Scrutor.Core.Execution;
using Scrutor.Core.Opcodes;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using EvmExecutionContext = Scrutor.Core.Execution.ExecutionContext;

namespace Scrutor.Tests.Execution;

public sealed class ExceptionalChildGasTests
{
    [Fact]
    public async Task InvalidOpcode_ConsumesEntireFrameGas()
    {
        var context = new EvmExecutionContext
        {
            Code = [0xfe],
            GasLimit = 65_535
        };

        var result = await new EvmMachine([]).ExecuteAsync(context);

        Assert.False(result.IsSuccess);
        Assert.Equal(EvmError.InvalidOpcode, result.Error);
        Assert.Equal(context.GasLimit, result.GasUsed);
    }

    [Fact]
    public async Task Revert_ReturnsUnusedFrameGas()
    {
        var context = new EvmExecutionContext
        {
            Code = [0xfd],
            GasLimit = 65_535
        };
        var machine = new EvmMachine([new OpcodeRevert()]);
        context.Stack.Push(0);
        context.Stack.Push(0);

        var result = await machine.ExecuteAsync(context);

        Assert.False(result.IsSuccess);
        Assert.Equal(EvmError.Revert, result.Error);
        Assert.Equal(0UL, result.GasUsed);
    }

    [Fact]
    public async Task StaticCall_ChargesWarmAccessCost()
    {
        var context = CreateCallContext();
        PushStaticCallArguments(context);

        await new OpcodeStaticCall().ExecuteAsync(context);

        Assert.Equal(100UL, context.GasUsed);
    }

    [Fact]
    public async Task DelegateCall_ChargesWarmAccessCost()
    {
        var context = CreateCallContext();
        PushStaticCallArguments(context);

        await new OpcodeDelegateCall().ExecuteAsync(context);

        Assert.Equal(100UL, context.GasUsed);
    }

    private static EvmExecutionContext CreateCallContext()
    {
        var callee = Address.FromHex(
            "0x0000000000000000000000000000000000001000");
        var context = new EvmExecutionContext
        {
            ContractAddress = Address.FromHex(
                "0x0000000000000000000000000000000000002000"),
            Caller = Address.Zero,
            GlobalState = new GlobalState(),
            GasLimit = 100_000,
            SubCall = (_, _, _, _) =>
                Task.FromResult(ExecutionResult.Success(0))
        };
        context.Access.WarmAddress(callee);
        return context;
    }

    private static void PushStaticCallArguments(EvmExecutionContext context)
    {
        context.Stack.Push(0);      // return length
        context.Stack.Push(0);      // return offset
        context.Stack.Push(0);      // argument length
        context.Stack.Push(0);      // argument offset
        context.Stack.Push(0x1000); // callee
        context.Stack.Push(10_000); // requested gas
    }
}
