using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Forks;
using Schlieren.Core.Gas;
using Schlieren.Core.Opcodes;
using EvmExecutionContext = Schlieren.Core.Execution.ExecutionContext;

namespace Schlieren.Tests.Gas;

public sealed class MemoryOpcodeGasRuleTests
{
    [Theory]
    [InlineData("OP.MLOAD", 0, 0, 32, 6)]
    [InlineData("OP.MSTORE", 32, 0, 32, 3)]
    [InlineData("OP.MSTORE8", 32, 32, 1, 6)]
    public void Calculate_CombinesBaseAndSharedExpansion(
        string id, int currentSize, int offset, int length, ulong expected)
    {
        var result = MemoryOpcodeGasRule.For(new GasRuleId(id)).Calculate(
            new MemoryGasContext(currentSize, offset, length),
            Fork.Osaka);

        Assert.Equal(expected, result.ChargedGas);
        Assert.Equal(3, (int)result.Components.Single(c => c.Id == "base").Amount);
        Assert.Equal(expected - 3, (ulong)result.Components.Single(c => c.Id == "memory-expansion").Amount);
    }

    [Fact]
    public async Task MloadExecution_ConsumesNamedFormulaBeforeReturning()
    {
        var context = new EvmExecutionContext { GasLimit = 100 };
        context.Stack.Push(BigInteger.Zero);

        var (result, _) = await new OpcodeMload().ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Equal(0UL, result.GasUsed);
        Assert.Equal(6UL, context.GasUsed);
        Assert.Equal(32, context.Memory.Size);
    }

    [Fact]
    public async Task Mstore_OversizedOffsetRunsOutOfGasBeforeHostWrite()
    {
        var context = new EvmExecutionContext { GasLimit = ulong.MaxValue };
        context.Stack.Push(BigInteger.One);
        context.Stack.Push(BigInteger.One << 255);

        await Assert.ThrowsAsync<EvmOutOfGasException>(async () =>
            await new OpcodeMstore().ExecuteAsync(context));
        Assert.Equal(0, context.Memory.Size);
    }
}
