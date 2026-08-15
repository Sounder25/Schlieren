using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Primitives;
using ExecutionContext = Schlieren.Core.Execution.ExecutionContext;

namespace Schlieren.Core.Opcodes;

public sealed class OpcodeAnd : IOpcode
{
    public byte Code => 0x16;
    public string Name => "AND";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a) || !context.Stack.TryPop(out var b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
        
        if (!context.Stack.TryPush(a & b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));
        
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + 1));
    }
}

public sealed class OpcodeOr : IOpcode
{
    public byte Code => 0x17;
    public string Name => "OR";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a) || !context.Stack.TryPop(out var b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
        
        if (!context.Stack.TryPush(a | b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));
        
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + 1));
    }
}

public sealed class OpcodeXor : IOpcode
{
    public byte Code => 0x18;
    public string Name => "XOR";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a) || !context.Stack.TryPop(out var b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
        
        if (!context.Stack.TryPush(a ^ b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));
        
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + 1));
    }
}

public sealed class OpcodeNot : IOpcode
{
    public byte Code => 0x19;
    public string Name => "NOT";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
        
        var result = ~a & (BigInteger.Pow(2, 256) - 1);
        
        if (!context.Stack.TryPush(result))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));
        
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + 1));
    }
}

public sealed class OpcodeByte : IOpcode
{
    public byte Code => 0x1A;
    public string Name => "BYTE";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var i) || !context.Stack.TryPop(out var x))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
        
        if (i >= 32)
        {
            context.Stack.TryPush(0);
        }
        else
        {
            var bytes = x.ToByteArray(isUnsigned: true, isBigEndian: true);
            var padded = new byte[32];
            Array.Copy(bytes, 0, padded, 32 - bytes.Length, bytes.Length);
            
            context.Stack.TryPush(padded[(int)i]);
        }
        
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + 1));
    }
}

// [AI-EDIT 2026-01-10] EIP-145 bitwise shift opcodes: SHL, SHR, SAR (0x1B–0x1D)

/// <summary>
/// SHL (0x1B): Logical shift left. result = value &lt;&lt; shift (mod 2^256).
/// Gas: 3
/// </summary>
public sealed class OpcodeShl : IOpcode
{
    public byte Code => 0x1B;
    public string Name => "SHL";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Block.Rules.HasBitwiseShift)
            return new ValueTask<(ExecutionResult, int)>((
                ExecutionResult.Failure(EvmError.InvalidOpcode, context.GasLimit),
                context.ProgramCounter + 1));

        if (!context.Stack.TryPop(out var shift) || !context.Stack.TryPop(out var value))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        var result = shift >= 256 ? BigInteger.Zero : (value << (int)shift);

        if (!context.Stack.TryPush(result))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + 1));
    }
}

/// <summary>
/// SHR (0x1C): Logical shift right. result = value >> shift (zero-fill).
/// Gas: 3
/// </summary>
public sealed class OpcodeShr : IOpcode
{
    public byte Code => 0x1C;
    public string Name => "SHR";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Block.Rules.HasBitwiseShift)
            return new ValueTask<(ExecutionResult, int)>((
                ExecutionResult.Failure(EvmError.InvalidOpcode, context.GasLimit),
                context.ProgramCounter + 1));

        if (!context.Stack.TryPop(out var shift) || !context.Stack.TryPop(out var value))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        var result = shift >= 256 ? BigInteger.Zero : (value >> (int)shift);

        if (!context.Stack.TryPush(result))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + 1));
    }
}

/// <summary>
/// SAR (0x1D): Arithmetic shift right. Sign-extends on shift (treats value as signed).
/// Gas: 3
/// </summary>
public sealed class OpcodeSar : IOpcode
{
    public byte Code => 0x1D;
    public string Name => "SAR";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Block.Rules.HasBitwiseShift)
            return new ValueTask<(ExecutionResult, int)>((
                ExecutionResult.Failure(EvmError.InvalidOpcode, context.GasLimit),
                context.ProgramCounter + 1));

        if (!context.Stack.TryPop(out var shift) || !context.Stack.TryPop(out var value))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        BigInteger result;
        var signed = EvmArith.ToSigned(value);
        if (shift >= 256)
        {
            // All bits shift out; result is either 0 or -1 depending on sign bit
            result = signed < 0 ? EvmArith.TwoTo256 - 1 : BigInteger.Zero;
        }
        else
        {
            // Arithmetic right shift: preserves sign bit
            result = signed >> (int)shift;
        }

        if (!context.Stack.TryPush(result))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + 1));
    }
}

/// <summary>
/// CLZ (0x1E): Count leading zeros. Pushes the number of leading zero bits in a 256-bit word.
/// If x is zero, pushes 256. EIP-7939 (Osaka+).
/// Gas: 5
/// </summary>
public sealed class OpcodeClz : IOpcode
{
    public byte Code => 0x1E;
    public string Name => "CLZ";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        // CLZ is only valid on Osaka+ (EIP-7939). Treat as INVALID on earlier forks.
        if (context.Block?.Rules.HasEip7939Clz != true)
            return new ValueTask<(ExecutionResult, int)>((
                ExecutionResult.Failure(EvmError.InvalidOpcode, context.GasLimit),
                context.ProgramCounter + 1));

        if (!context.Stack.TryPop(out var value))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        BigInteger result;
        if (value.IsZero)
        {
            result = 256;
        }
        else
        {
            int bitLength = (int)value.GetBitLength();
            result = 256 - bitLength;
        }

        if (!context.Stack.TryPush(result))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(5), context.ProgramCounter + 1));
    }
}
