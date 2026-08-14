using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using Xunit;
using EvmExecutionContext = Schlieren.Core.Execution.ExecutionContext;

namespace Schlieren.Tests.Opcodes;

public class ComparisonOpcodeTests
{
    // [AI-EDIT 2026-01-10] Fixed: Yellow Paper LT = µ_s[0] < µ_s[1].
    // a = first pop (top), b = second pop. Result = a < b.
    [Fact]
    public async Task Lt_ReturnsOneIfLess()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeLt();
        
        ctx.Stack.Push(100);  // b = second pop
        ctx.Stack.Push(50);   // a = first pop (top) → 50 < 100 = true
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(BigInteger.One, ctx.Stack.Pop());
    }

    [Fact]
    public async Task Lt_ReturnsZeroIfGreaterOrEqual()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeLt();
        
        ctx.Stack.Push(50);   // b = second pop
        ctx.Stack.Push(100);  // a = first pop (top) → 100 < 50 = false
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(BigInteger.Zero, ctx.Stack.Pop());
    }

    [Fact]
    public async Task Gt_ReturnsOneIfGreater()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeGt();
        
        ctx.Stack.Push(50);   // b = second pop
        ctx.Stack.Push(100);  // a = first pop (top) → 100 > 50 = true
        
        await opcode.ExecuteAsync(ctx);
        
        Assert.Equal(BigInteger.One, ctx.Stack.Pop());
    }

    [Fact]
    // [AI-EDIT 2026-01-10] SLT = µ_s[0] < µ_s[1] (signed). a=top, b=second.
    public async Task Slt_HandlesNegativeNumbers()
    {
        var ctx = new EvmExecutionContext();
        var opcode = new OpcodeSlt();
        
        var minusOne = BigInteger.Pow(2, 256) - 1;
        
        ctx.Stack.Push(1);           // b = second pop (signed: 1)
        ctx.Stack.Push(minusOne);    // a = first pop (top, signed: -1) → -1 < 1 = true
        
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
    public async Task Jump_DestinationInsidePushData_ReturnsFailure()
    {
        var ctx = new EvmExecutionContext
        {
            // PUSH2 0x5B00, JUMPDEST, STOP
            // Byte at index 1 is 0x5B but is immediate data, not a valid jumpdest.
            Code = new byte[] { 0x61, 0x5B, 0x00, 0x5B, 0x00 }
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

public class EnvironmentAndTransientOpcodeTests
{
    [Fact]
    public async Task Address_PushesCurrentContractAddress()
    {
        var contract = Address.FromHex("0x1000000000000000000000000000000000000001");
        var ctx = new EvmExecutionContext
        {
            ContractAddress = contract
        };
        var opcode = new OpcodeAddress();

        var (result, _) = await opcode.ExecuteAsync(ctx);

        Assert.True(result.IsSuccess);
        var expected = new BigInteger(contract.Bytes, isUnsigned: true, isBigEndian: true);
        Assert.Equal(expected, ctx.Stack.Pop());
    }

    [Fact]
    public async Task Balance_LoadsAccountBalance()
    {
        var target = Address.FromHex("0x2000000000000000000000000000000000000002");
        var state = new GlobalState();
        state.SetBalance(target, 123456);

        var ctx = new EvmExecutionContext
        {
            GlobalState = state
        };
        var opcode = new OpcodeBalance();
        ctx.Stack.Push(new BigInteger(target.Bytes, isUnsigned: true, isBigEndian: true));

        var (result, _) = await opcode.ExecuteAsync(ctx);

        Assert.True(result.IsSuccess);
        Assert.Equal(new BigInteger(123456), ctx.Stack.Pop());
    }

    [Fact]
    public async Task Gas_PushesRemainingGasAfterItsOwnBaseCost()
    {
        var ctx = new EvmExecutionContext
        {
            GasLimit = 1000,
            GasUsed = 100
        };
        var opcode = new OpcodeGas();

        var (result, _) = await opcode.ExecuteAsync(ctx);

        Assert.True(result.IsSuccess);
        Assert.Equal(2UL, result.GasUsed);
        Assert.Equal(new BigInteger(898), ctx.Stack.Pop());
    }

    [Fact]
    public async Task Tload_And_Tstore_RoundTripValue()
    {
        var transient = new Dictionary<(Address, BigInteger), BigInteger>();
        var contract = Address.FromHex("0x3000000000000000000000000000000000000003");

        var ctx = new EvmExecutionContext
        {
            ContractAddress = contract,
            TransientLoad = (addr, key) => transient.TryGetValue((addr, key), out var value) ? value : BigInteger.Zero,
            TransientStore = (addr, key, value) => transient[(addr, key)] = value
        };

        var tstore = new OpcodeTstore();
        var tload = new OpcodeTload();

        ctx.Stack.Push(99);
        ctx.Stack.Push(1);
        var (storeResult, _) = await tstore.ExecuteAsync(ctx);
        Assert.True(storeResult.IsSuccess);

        ctx.Stack.Push(1);
        var (loadResult, _) = await tload.ExecuteAsync(ctx);
        Assert.True(loadResult.IsSuccess);
        Assert.Equal(new BigInteger(99), ctx.Stack.Pop());
    }

    [Fact]
    public async Task Tstore_InStaticContext_ReturnsViolation()
    {
        var ctx = new EvmExecutionContext
        {
            IsStatic = true
        };
        var opcode = new OpcodeTstore();
        ctx.Stack.Push(1);
        ctx.Stack.Push(1);

        var (result, _) = await opcode.ExecuteAsync(ctx);

        Assert.False(result.IsSuccess);
        Assert.Equal(EvmError.StaticModeViolation, result.Error);
    }

    [Fact]
    public async Task Call_IdentityPrecompile_CopiesReturnData()
    {
        var ctx = new EvmExecutionContext
        {
            ContractAddress = Address.FromHex("0x3000000000000000000000000000000000000003"),
            GlobalState = new GlobalState(),
            GasLimit = 100000
        };

        ctx.Memory.Store(0, new byte[] { 0xAA, 0xBB });

        // CALL(gas=1000,to=0x04,value=0,argsOffset=0,argsLength=2,retOffset=32,retLength=2)
        ctx.Stack.Push(2);
        ctx.Stack.Push(32);
        ctx.Stack.Push(2);
        ctx.Stack.Push(0);
        ctx.Stack.Push(0);
        ctx.Stack.Push(4);
        ctx.Stack.Push(1000);

        var opcode = new OpcodeCall();
        var (result, _) = await opcode.ExecuteAsync(ctx);

        Assert.True(result.IsSuccess);
        Assert.Equal(BigInteger.One, ctx.Stack.Pop());
        Assert.Equal(new byte[] { 0xAA, 0xBB }, ctx.LastReturnData);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, ctx.Memory.Load(32, 2));
    }

    [Fact]
    public async Task Call_ModExpPrecompile_ComputesExpectedResult()
    {
        var ctx = new EvmExecutionContext
        {
            ContractAddress = Address.FromHex("0x3000000000000000000000000000000000000003"),
            GlobalState = new GlobalState(),
            GasLimit = 100000
        };

        // lenB=1, lenE=1, lenM=1, base=2, exp=5, mod=13 => 2^5 mod 13 = 6
        var input = new byte[99];
        input[31] = 1;
        input[63] = 1;
        input[95] = 1;
        input[96] = 2;
        input[97] = 5;
        input[98] = 13;
        ctx.Memory.Store(0, input);

        ctx.Stack.Push(1);
        ctx.Stack.Push(200);
        ctx.Stack.Push(input.Length);
        ctx.Stack.Push(0);
        ctx.Stack.Push(0);
        ctx.Stack.Push(5);
        ctx.Stack.Push(5000);

        var opcode = new OpcodeCall();
        var (result, _) = await opcode.ExecuteAsync(ctx);

        Assert.True(result.IsSuccess);
        Assert.Equal(BigInteger.One, ctx.Stack.Pop());
        Assert.Equal(new byte[] { 0x06 }, ctx.Memory.Load(200, 1));
    }
}