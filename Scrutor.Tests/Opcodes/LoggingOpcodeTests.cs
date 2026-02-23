using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Opcodes;
using Scrutor.Core.Primitives;
using Xunit;
using EvmExecutionContext = Scrutor.Core.Execution.ExecutionContext;

namespace Scrutor.Tests.Opcodes;

public class LoggingOpcodeTests
{
    [Fact]
    public async Task Log0_ConsumesCorrectGas()
    {
        var ctx = new EvmExecutionContext
        {
            ContractAddress = Address.FromHex("0x1234567890123456789012345678901234567890")
        };
        var opcode = new OpcodeLog0();
        
        ctx.Stack.Push(32); // length
        ctx.Stack.Push(0);  // offset
        ctx.Memory.Store(0, new byte[32]);
        
        var (result, _) = await opcode.ExecuteAsync(ctx);
        
        // 375 + 375*0 + 8*32 + 0 = 375 + 256 = 631
        Assert.True(result.IsSuccess);
        Assert.Equal(631UL, result.GasUsed);
        Assert.Single(ctx.Logs);
        Assert.Equal(ctx.ContractAddress.ToString(), ctx.Logs[0].Address);
    }

    [Fact]
    public async Task Log4_WithTopics_ConsumesCorrectGas()
    {
        var ctx = new EvmExecutionContext
        {
            ContractAddress = Address.FromHex("0x1234567890123456789012345678901234567890")
        };
        var opcode = new OpcodeLog4();
        
        ctx.Stack.Push(0x1111); // topic 4
        ctx.Stack.Push(0x2222); // topic 3
        ctx.Stack.Push(0x3333); // topic 2
        ctx.Stack.Push(0x4444); // topic 1
        ctx.Stack.Push(0);      // length
        ctx.Stack.Push(0);      // offset
        
        var (result, _) = await opcode.ExecuteAsync(ctx);
        
        // 375 + 375*4 + 8*0 + 0 = 375 + 1500 = 1875
        Assert.True(result.IsSuccess);
        Assert.Equal(1875UL, result.GasUsed);
        Assert.Single(ctx.Logs);
        Assert.Equal(4, ctx.Logs[0].Topics.Count);
    }

    [Fact]
    public async Task Log_WithMemoryExpansion_ConsumesCorrectGas()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeLog0();
        
        ctx.Stack.Push(32);  // length
        ctx.Stack.Push(100); // offset (triggers expansion to 128+)
        
        var (result, _) = await opcode.ExecuteAsync(ctx);
        
        // Expansion gas for 132 bytes:
        // current words = 0
        // new words = (132+31)/32 = 5
        // cost = 3*5 + (5*5)/512 = 15
        
        // Log gas = 375 + 256 = 631
        // Total = 631 + 15 = 646
        Assert.Equal(646UL, result.GasUsed);
    }
}
