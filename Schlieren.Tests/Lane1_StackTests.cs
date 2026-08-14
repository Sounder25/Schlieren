using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Xunit;
using EvmExecutionContext = Schlieren.Core.Execution.ExecutionContext;

namespace Schlieren.Tests.Opcodes;

public class StackOpcodeTests
{
    [Fact]
    public async Task Push1_PushesOneByte()
    {
        var ctx = new EvmExecutionContext
        {
            Code = new byte[] { 0x60, 0xFF }, 
            ProgramCounter = 0
        };
        var opcode = new OpcodePush1();
        
        var (_, nextPc) = await opcode.ExecuteAsync(ctx);
        ctx.ProgramCounter = nextPc;
        
        Assert.Equal(new BigInteger(0xFF), ctx.Stack.Pop());
        Assert.Equal(2, ctx.ProgramCounter); 
    }

    [Fact]
    public async Task Push2_PushesTwoBytes_BigEndian()
    {
        var ctx = new EvmExecutionContext
        {
            Code = new byte[] { 0x61, 0x01, 0x02 }, 
            ProgramCounter = 0
        };
        var opcode = new OpcodePush2();
        
        var (_, nextPc) = await opcode.ExecuteAsync(ctx);
        ctx.ProgramCounter = nextPc;
        
        Assert.Equal(new BigInteger(0x0102), ctx.Stack.Pop());
        Assert.Equal(3, ctx.ProgramCounter); 
    }
    
    [Fact]
    public async Task Dup1_DuplicatesTopItem()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeDup1();
        
        ctx.Stack.Push(42);
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(2, ctx.Stack.Count);
        Assert.Equal(new BigInteger(42), ctx.Stack.Pop());
        Assert.Equal(new BigInteger(42), ctx.Stack.Pop());
    }

    [Fact]
    public async Task Dup2_DuplicatesSecondItem()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeDup2();
        
        ctx.Stack.Push(10);
        ctx.Stack.Push(20);
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(3, ctx.Stack.Count);
        Assert.Equal(new BigInteger(10), ctx.Stack.Pop());
        Assert.Equal(new BigInteger(20), ctx.Stack.Pop());
        Assert.Equal(new BigInteger(10), ctx.Stack.Pop());
    }

    [Fact]
    public async Task Swap1_SwapsTopTwoItems()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeSwap1();
        
        ctx.Stack.Push(10);
        ctx.Stack.Push(20);
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(new BigInteger(10), ctx.Stack.Pop());
        Assert.Equal(new BigInteger(20), ctx.Stack.Pop());
    }

    [Fact]
    public async Task Swap2_SwapsTopWithThird()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeSwap2();
        
        ctx.Stack.Push(10);
        ctx.Stack.Push(20);
        ctx.Stack.Push(30);
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(new BigInteger(10), ctx.Stack.Pop());
        Assert.Equal(new BigInteger(20), ctx.Stack.Pop());
        Assert.Equal(new BigInteger(30), ctx.Stack.Pop());
    }
}