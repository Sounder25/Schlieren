using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Opcodes;
using Scrutor.Core.Primitives;
using Xunit;
using EvmExecutionContext = Scrutor.Core.Execution.ExecutionContext;

namespace Scrutor.Tests.Opcodes;

public class ArithmeticOpcodeTests
{
    [Fact]
    public async Task Add_TwoNumbers_ReturnsSum()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeAdd();
        
        ctx.Stack.Push(10);
        ctx.Stack.Push(20);
        
        var (result, _) = await opcode.ExecuteAsync(ctx);
        
        Assert.True(result.IsSuccess);
        Assert.Equal(3UL, result.GasUsed);
        Assert.Equal(new BigInteger(30), ctx.Stack.Pop());
    }

    [Fact]
    public async Task Add_WithOverflow_Wraps()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeAdd();
        
        var maxUint256 = BigInteger.Pow(2, 256) - 1;
        ctx.Stack.Push(maxUint256);
        ctx.Stack.Push(1);
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(BigInteger.Zero, ctx.Stack.Pop());
    }

    [Fact]
    public async Task Mul_TwoNumbers_ReturnsProduct()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeMul();
        
        ctx.Stack.Push(5);
        ctx.Stack.Push(6);
        
        var (result, _) = await opcode.ExecuteAsync(ctx);
        
        Assert.True(result.IsSuccess);
        Assert.Equal(5UL, result.GasUsed);
        Assert.Equal(new BigInteger(30), ctx.Stack.Pop());
    }

    [Fact]
    public async Task Sub_TwoNumbers_ReturnsDifference()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeSub();
        
        ctx.Stack.Push(30);
        ctx.Stack.Push(100);
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(new BigInteger(70), ctx.Stack.Pop());
    }

    [Fact]
    public async Task Sub_WithUnderflow_Wraps()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeSub();
        
        ctx.Stack.Push(10);
        ctx.Stack.Push(5);
        
        await opcode.ExecuteAsync(ctx);
        
        var result = ctx.Stack.Pop();
        var expected = BigInteger.Pow(2, 256) - 5;
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task Div_TwoNumbers_ReturnsQuotient()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeDiv();
        
        ctx.Stack.Push(3);
        ctx.Stack.Push(100);
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(new BigInteger(33), ctx.Stack.Pop());
    }

    [Fact]
    public async Task Mod_TwoNumbers_ReturnsRemainder()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeMod();
        
        ctx.Stack.Push(7);
        ctx.Stack.Push(100);
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(new BigInteger(2), ctx.Stack.Pop());
    }
}

public class EvmStackTests
{
    [Fact]
    public void Push_SingleValue_IncrementsCount()
    {
        var stack = new EvmStack();
        stack.Push(42);
        
        Assert.Equal(1, stack.Count);
        Assert.Equal(new BigInteger(42), stack.Pop());
    }

    [Fact]
    public void Push_MaxDepth_ThrowsOverflow()
    {
        var stack = new EvmStack();
        for (int i = 0; i < 1024; i++)
        {
            stack.Push(i);
        }
        
        Assert.Throws<EvmStackOverflowException>(() => stack.Push(1025));
    }

    [Fact]
    public void Pop_EmptyStack_ThrowsUnderflow()
    {
        var stack = new EvmStack();
        Assert.Throws<EvmStackUnderflowException>(() => stack.Pop());
    }
}

public class EvmMemoryTests
{
    [Fact]
    public void Store_AndLoad_ReturnsData()
    {
        var memory = new EvmMemory();
        var data = new byte[] { 1, 2, 3, 4 };
        
        memory.Store(0, data);
        var loaded = memory.Load(0, 4);
        
        Assert.Equal(data, loaded);
    }

    [Fact]
    public void Store_AutoExpands_Memory()
    {
        var memory = new EvmMemory();
        memory.Store(100, new byte[] { 0xFF });
        
        Assert.True(memory.Size >= 101);
        Assert.Equal(0, memory.Size % 32);
    }

    [Fact]
    public void CalculateGasCost_ForExpansion_ReturnsCorrectCost()
    {
        var memory = new EvmMemory();
        var cost = memory.CalculateGasCost(32);
        
        Assert.True(cost > 0);
    }
}