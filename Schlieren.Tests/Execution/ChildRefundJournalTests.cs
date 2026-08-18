using Schlieren.Core.Execution;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using EvmExecutionContext = Schlieren.Core.Execution.ExecutionContext;

namespace Schlieren.Tests.Execution;

public sealed class ChildRefundJournalTests
{
    [Fact]
    public async Task Call_MergesSuccessfulChildRefundCounter()
    {
        var context = CreateCallContext(
            ExecutionResult.Success(100) with { GasRefundCounter = 4_800 });

        var (result, _) = await new OpcodeCall().ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Equal(4_800, context.GasRefundCounter);
    }

    [Fact]
    public async Task Call_DiscardsFailedChildRefundCounter()
    {
        var context = CreateCallContext(
            ExecutionResult.Failure(EvmError.Revert, 100) with
            {
                GasRefundCounter = 4_800
            });

        var (result, _) = await new OpcodeCall().ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, context.GasRefundCounter);
    }

    private static EvmExecutionContext CreateCallContext(
        ExecutionResult childResult)
    {
        var context = new EvmExecutionContext
        {
            ContractAddress = Address.FromHex(
                "0x1000000000000000000000000000000000000001"),
            Caller = Address.FromHex(
                "0x2000000000000000000000000000000000000002"),
            GlobalState = new GlobalState(),
            GasLimit = 100_000,
            SubCall = (_, _, _, _) => Task.FromResult(childResult)
        };

        context.Stack.Push(0);      // return length
        context.Stack.Push(0);      // return offset
        context.Stack.Push(0);      // argument length
        context.Stack.Push(0);      // argument offset
        context.Stack.Push(0);      // value
        context.Stack.Push(0x1000); // callee
        context.Stack.Push(10_000); // requested gas
        return context;
    }
}
