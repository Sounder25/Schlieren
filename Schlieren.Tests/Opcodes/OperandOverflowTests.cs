using System.Numerics;
using System.Reflection;
using Schlieren.Core.Execution;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using ExecutionContext = Schlieren.Core.Execution.ExecutionContext;

namespace Schlieren.Tests.Opcodes;

public sealed class OperandOverflowTests
{
    public enum MemoryOpcode
    {
        Return,
        Revert,
        CallDataCopy,
        CodeCopy,
        ReturnDataCopy,
        Mcopy,
        Keccak256
    }

    public static IEnumerable<object[]> MemoryRangeMatrix()
    {
        BigInteger[] offsets =
        [
            BigInteger.Zero,
            new(int.MaxValue),
            new BigInteger(int.MaxValue) + 1,
            (BigInteger.One << 256) - 1
        ];

        BigInteger[] lengths =
        [
            BigInteger.Zero,
            BigInteger.One,
            new(int.MaxValue),
            new BigInteger(int.MaxValue) + 1,
            (BigInteger.One << 256) - 1
        ];

        foreach (var offset in offsets)
        foreach (var length in lengths)
        {
            var expected = length.IsZero ||
                (offset <= int.MaxValue &&
                 length <= int.MaxValue &&
                 offset + length <= int.MaxValue);

            yield return [offset, length, expected];
        }
    }

    [Theory]
    [MemberData(nameof(MemoryRangeMatrix))]
    public void TryResolveMemoryRange_ImplementsHostRangeContract(
        BigInteger offset,
        BigInteger length,
        bool expected)
    {
        var method = typeof(EvmMachine).Assembly
            .GetType("Schlieren.Core.Execution.OperandValidation", throwOnError: true)!
            .GetMethod(
                "TryResolveMemoryRange",
                BindingFlags.Static | BindingFlags.NonPublic,
                binder: null,
                types:
                [
                    typeof(BigInteger),
                    typeof(BigInteger),
                    typeof(int).MakeByRefType(),
                    typeof(int).MakeByRefType(),
                    typeof(ulong).MakeByRefType()
                ],
                modifiers: null);

        Assert.NotNull(method);

        object[] arguments = [offset, length, 0, 0, 0UL];
        var actual = Assert.IsType<bool>(method!.Invoke(null, arguments));

        Assert.Equal(expected, actual);

        if (length.IsZero)
        {
            Assert.Equal(0, arguments[2]);
            Assert.Equal(0, arguments[3]);
            Assert.Equal(0UL, arguments[4]);
        }
        else if (expected)
        {
            Assert.Equal((int)offset, arguments[2]);
            Assert.Equal((int)length, arguments[3]);
            Assert.Equal((ulong)(offset + length), arguments[4]);
        }
    }

    [Theory]
    [InlineData(MemoryOpcode.Return)]
    [InlineData(MemoryOpcode.Revert)]
    [InlineData(MemoryOpcode.CallDataCopy)]
    [InlineData(MemoryOpcode.CodeCopy)]
    [InlineData(MemoryOpcode.ReturnDataCopy)]
    [InlineData(MemoryOpcode.Mcopy)]
    [InlineData(MemoryOpcode.Keccak256)]
    public async Task MemoryOpcodes_OversizedNonzeroRange_ReturnsOutOfGas(MemoryOpcode opcode)
    {
        var context = CreateContext();
        PushOperands(context, opcode, new BigInteger(int.MaxValue), BigInteger.One);

        var (result, _) = await CreateOpcode(opcode).ExecuteAsync(context);

        Assert.False(result.IsSuccess);
        Assert.Equal(EvmError.OutOfGas, result.Error);
    }

    [Theory]
    [InlineData(MemoryOpcode.Return)]
    [InlineData(MemoryOpcode.Revert)]
    [InlineData(MemoryOpcode.CallDataCopy)]
    [InlineData(MemoryOpcode.CodeCopy)]
    [InlineData(MemoryOpcode.Mcopy)]
    [InlineData(MemoryOpcode.Keccak256)]
    public async Task MemoryOpcodes_ZeroLengthIgnoresMemoryOffsets(MemoryOpcode opcode)
    {
        var context = CreateContext();
        var huge = (BigInteger.One << 256) - 1;
        PushOperands(context, opcode, huge, BigInteger.Zero);

        var (result, _) = await CreateOpcode(opcode).ExecuteAsync(context);

        if (opcode == MemoryOpcode.Revert)
            Assert.Equal(EvmError.Revert, result.Error);
        else
            Assert.True(result.IsSuccess);
        Assert.Equal(0, context.Memory.Size);
    }

    [Theory]
    [InlineData(MemoryOpcode.CallDataCopy)]
    [InlineData(MemoryOpcode.CodeCopy)]
    public async Task ZeroPaddingCopyOpcodes_AcceptHugeSourceOffset(MemoryOpcode opcode)
    {
        var context = CreateContext();
        var huge = (BigInteger.One << 256) - 1;
        context.Stack.Push(BigInteger.One);
        context.Stack.Push(huge);
        context.Stack.Push(BigInteger.Zero);

        var (result, _) = await CreateOpcode(opcode).ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Equal(new byte[] { 0 }, context.Memory.Load(0, 1));
    }

    [Fact]
    public async Task ReturnDataCopy_ZeroLengthPastSourceEndReturnsOutOfGas()
    {
        var context = CreateContext();
        context.LastReturnData = [0x01];
        context.Stack.Push(BigInteger.Zero);
        context.Stack.Push(new BigInteger(2));
        context.Stack.Push(BigInteger.Zero);

        var (result, _) = await new OpcodeReturnDataCopy().ExecuteAsync(context);

        Assert.False(result.IsSuccess);
        Assert.Equal(EvmError.OutOfGas, result.Error);
    }

    [Fact]
    public async Task EvmMachine_RethrowsUnexpectedOpcodeExceptions()
    {
        var machine = new EvmMachine([new ThrowingOpcode()]);
        var context = new ExecutionContext { Code = [ThrowingOpcode.OpcodeByte] };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => machine.ExecuteAsync(context));

        Assert.Equal("deliberate test failure", exception.Message);
    }

    [Fact]
    public void ModExp_EnforcesMinimumGasAndPadsTruncatedInput()
    {
        var input = CreateModExpInput(1, 1, 1, 2, 5);

        var insufficient = ExecuteModExp(input, 199);
        var sufficient = ExecuteModExp(input, 200);

        Assert.Equal(EvmError.OutOfGas, insufficient.Error);
        Assert.True(sufficient.IsSuccess);
        Assert.Equal(200UL, sufficient.GasUsed);
        Assert.Equal(new byte[] { 0 }, sufficient.ReturnData);
    }

    [Fact]
    public void ModExp_EnormousDeclaredLengthReturnsOutOfGasBeforeAllocation()
    {
        var input = new byte[96];
        input[64] = 1;

        var result = ExecuteModExp(input, ulong.MaxValue);

        Assert.False(result.IsSuccess);
        Assert.Equal(EvmError.OutOfGas, result.Error);
    }

    private static ExecutionContext CreateContext() => new()
    {
        Code = [0xAA],
        CallData = [0xBB],
        GasLimit = 100_000
    };

    private static byte[] CreateModExpInput(
        byte baseLength,
        byte exponentLength,
        byte modulusLength,
        params byte[] payload)
    {
        var input = new byte[96 + payload.Length];
        input[31] = baseLength;
        input[63] = exponentLength;
        input[95] = modulusLength;
        Array.Copy(payload, 0, input, 96, payload.Length);
        return input;
    }

    private static ExecutionResult ExecuteModExp(byte[] input, ulong gasLimit)
    {
        var type = typeof(OpcodeCall).Assembly
            .GetType("Schlieren.Core.Opcodes.PrecompileExecutor", throwOnError: true)!;
        var method = type.GetMethod(
            "Execute",
            BindingFlags.Static | BindingFlags.Public)!;
        var address = Address.FromHex("0x0000000000000000000000000000000000000005");

        return Assert.IsType<ExecutionResult>(
            method.Invoke(null, [address, input, gasLimit]));
    }

    private static IOpcode CreateOpcode(MemoryOpcode opcode) => opcode switch
    {
        MemoryOpcode.Return => new OpcodeReturn(),
        MemoryOpcode.Revert => new OpcodeRevert(),
        MemoryOpcode.CallDataCopy => new OpcodeCallDataCopy(),
        MemoryOpcode.CodeCopy => new OpcodeCodeCopy(),
        MemoryOpcode.ReturnDataCopy => new OpcodeReturnDataCopy(),
        MemoryOpcode.Mcopy => new OpcodeMcopy(),
        MemoryOpcode.Keccak256 => new OpcodeKeccak256(),
        _ => throw new ArgumentOutOfRangeException(nameof(opcode))
    };

    private static void PushOperands(
        ExecutionContext context,
        MemoryOpcode opcode,
        BigInteger offset,
        BigInteger length)
    {
        context.Stack.Push(length);
        if (opcode is MemoryOpcode.CallDataCopy or MemoryOpcode.CodeCopy or MemoryOpcode.ReturnDataCopy or MemoryOpcode.Mcopy)
            context.Stack.Push(BigInteger.Zero);
        context.Stack.Push(offset);
    }

    private sealed class ThrowingOpcode : IOpcode
    {
        public const byte OpcodeByte = 0xAA;
        public byte Code => OpcodeByte;
        public string Name => "THROW";

        public ValueTask<(ExecutionResult Result, int NextPc)> ExecuteAsync(
            ExecutionContext context,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("deliberate test failure");
    }
}
