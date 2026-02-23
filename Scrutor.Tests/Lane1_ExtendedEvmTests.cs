using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Opcodes;
using Scrutor.Core.Primitives;
using Xunit;
using EvmExecutionContext = Scrutor.Core.Execution.ExecutionContext;

namespace Scrutor.Tests.Opcodes;

public class ComparisonOpcodeTests
{
    [Fact]
    public async Task Lt_ReturnsOneIfLess()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeLt();
        
        ctx.Stack.Push(100); 
        ctx.Stack.Push(50);  
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(BigInteger.One, ctx.Stack.Pop());
    }

    [Fact]
    public async Task Lt_ReturnsZeroIfGreaterOrEqual()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeLt();
        
        ctx.Stack.Push(50);  
        ctx.Stack.Push(100); 
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(BigInteger.Zero, ctx.Stack.Pop());
    }

    [Fact]
    public async Task Gt_ReturnsOneIfGreater()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeGt();
        
        ctx.Stack.Push(50);  
        ctx.Stack.Push(100); 
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(BigInteger.One, ctx.Stack.Pop());
    }

    [Fact]
    public async Task Slt_HandlesNegativeNumbers()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeSlt();
        
        var minusOne = BigInteger.Pow(2, 256) - 1;
        
        ctx.Stack.Push(1);       
        ctx.Stack.Push(minusOne); 
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(BigInteger.One, ctx.Stack.Pop());
    }

    [Fact]
    public async Task Eq_ReturnsOneIfEqual()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeEq();
        
        ctx.Stack.Push(12345);
        ctx.Stack.Push(12345);
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(BigInteger.One, ctx.Stack.Pop());
    }

    [Fact]
    public async Task IsZero_ReturnsOneIfZero()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeIsZero();
        
        ctx.Stack.Push(0);
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(BigInteger.One, ctx.Stack.Pop());
    }
}

public class BitwiseOpcodeTests
{
    [Fact]
    public async Task And_PerformsBitwiseAnd()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeAnd();
        
        ctx.Stack.Push(0xFF00);
        ctx.Stack.Push(0x00FF);
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(BigInteger.Zero, ctx.Stack.Pop());
    }

    [Fact]
    public async Task Or_PerformsBitwiseOr()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeOr();
        
        ctx.Stack.Push(0xF0);
        ctx.Stack.Push(0x0F);
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(new BigInteger(0xFF), ctx.Stack.Pop());
    }

    [Fact]
    public async Task Not_InvertsBits()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeNot();
        
        ctx.Stack.Push(0);
        
        await opcode.ExecuteAsync(ctx);
        
        var expected = BigInteger.Pow(2, 256) - 1;
        Assert.Equal(expected, ctx.Stack.Pop());
    }

    [Fact]
    public async Task Byte_ExtractsCorrectByte()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeByte();
        
        ctx.Stack.Push(0x123456);
        ctx.Stack.Push(31); 
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(new BigInteger(0x56), ctx.Stack.Pop());
    }
}

public class ControlFlowOpcodeTests
{
    [Fact]
    public async Task Jump_ValidLocation_UpdatesPc()
    {
        var ctx = new EvmExecutionContext
        {
            Code = new byte[] { 0x00, 0x5B } 
        };
        var opcode = new OpcodeJump();
        
        ctx.Stack.Push(1);
        
        var (_, nextPc) = await opcode.ExecuteAsync(ctx);
        ctx.ProgramCounter = nextPc;
        
        Assert.Equal(1, ctx.ProgramCounter);
    }

    [Fact]
    public async Task Jump_InvalidLocation_ReturnsFailure()
    {
        var ctx = new EvmExecutionContext
        {
            Code = new byte[] { 0x00, 0x00 } 
        };
        var opcode = new OpcodeJump();
        
        ctx.Stack.Push(1);
        
        var (result, _) = await opcode.ExecuteAsync(ctx);
        Assert.False(result.IsSuccess);
        Assert.Equal(EvmError.BadJumpDestination, result.Error);
    }

    [Fact]
    public async Task Jumpi_ConditionTrue_Jumps()
    {
        var ctx = new EvmExecutionContext
        {
            Code = new byte[] { 0x00, 0x5B }
        };
        var opcode = new OpcodeJumpi();
        
        ctx.Stack.Push(1); 
        ctx.Stack.Push(1); 
        
        var (_, nextPc) = await opcode.ExecuteAsync(ctx);
        ctx.ProgramCounter = nextPc;
        
        Assert.Equal(1, ctx.ProgramCounter);
    }

    [Fact]
    public async Task Jumpi_ConditionFalse_DoesNotJump()
    {
        var ctx = new EvmExecutionContext
        {
            Code = new byte[] { 0x00, 0x5B }
        };
        var opcode = new OpcodeJumpi();
        
        ctx.Stack.Push(0); 
        ctx.Stack.Push(1); 
        
        var (_, nextPc) = await opcode.ExecuteAsync(ctx);
        ctx.ProgramCounter = nextPc;
        
        Assert.Equal(1, ctx.ProgramCounter); 
    }

    [Fact]
    public async Task Pc_ReturnsCurrentPc()
    {
        var ctx = new EvmExecutionContext
        {
            ProgramCounter = 10
        };
        var opcode = new OpcodePc();
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(new BigInteger(10), ctx.Stack.Pop());
    }
}