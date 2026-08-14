using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Primitives;
using ExecutionContext = Scrutor.Core.Execution.ExecutionContext;

namespace Scrutor.Core.Opcodes;

public sealed class OpcodeAdd : IOpcode
{
    public byte Code => 0x01;
    public string Name => "ADD";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a) || !context.Stack.TryPop(out var b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
        
        if (!context.Stack.TryPush(a + b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));
        
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + 1));
    }
}

public sealed class OpcodeMul : IOpcode
{
    public byte Code => 0x02;
    public string Name => "MUL";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a) || !context.Stack.TryPop(out var b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
        
        if (!context.Stack.TryPush(a * b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));
        
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(5), context.ProgramCounter + 1));
    }
}

public sealed class OpcodeSub : IOpcode
{
    public byte Code => 0x03;
    public string Name => "SUB";

    // [AI-EDIT 2026-01-10] Yellow Paper: SUB = µ_s[0] − µ_s[1], where µ_s[0] is top of stack (first pop).
    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a) || !context.Stack.TryPop(out var b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
        
        if (!context.Stack.TryPush(a - b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));
        
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + 1));
    }
}

public sealed class OpcodeDiv : IOpcode
{
    public byte Code => 0x04;
    public string Name => "DIV";

    // [AI-EDIT 2026-01-10] Yellow Paper: DIV = µ_s[0] / µ_s[1], zero when µ_s[1]=0.
    // a = first pop (top), b = second pop. Result = a / b.
    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a) || !context.Stack.TryPop(out var b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
        
        var result = b == BigInteger.Zero ? BigInteger.Zero : a / b;
        
        if (!context.Stack.TryPush(result))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));
        
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(5), context.ProgramCounter + 1));
    }
}

public sealed class OpcodeMod : IOpcode
{
    public byte Code => 0x06;
    public string Name => "MOD";

    // [AI-EDIT 2026-01-10] Yellow Paper: MOD = µ_s[0] mod µ_s[1], zero when µ_s[1]=0.
    // a = first pop (top), b = second pop. Result = a % b.
    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a) || !context.Stack.TryPop(out var b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
        
        var result = b == BigInteger.Zero ? BigInteger.Zero : a % b;
        
        if (!context.Stack.TryPush(result))
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));
        
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(5), context.ProgramCounter + 1));
    }
}

// [AI-EDIT 2026-01-10] Missing arithmetic opcodes: SDIV, SMOD, ADDMOD, MULMOD, EXP, SIGNEXTEND

/// <summary>Shared 256-bit signed arithmetic helpers.</summary>
internal static class EvmArith
{
    // 2^256 — modulus for uint256 arithmetic
    internal static readonly BigInteger TwoTo256 = BigInteger.Pow(2, 256);
    // 2^255 — sign bit threshold
    internal static readonly BigInteger TwoTo255 = BigInteger.Pow(2, 255);

    /// <summary>
    /// Interprets an unsigned uint256 value as a signed 256-bit two's complement integer.
    /// </summary>
    internal static BigInteger ToSigned(BigInteger u256)
    {
        return u256 >= TwoTo255 ? u256 - TwoTo256 : u256;
    }
}

/// <summary>
/// SDIV (0x06): Signed integer division (truncated toward zero).
/// Gas: 5
/// </summary>
public sealed class OpcodeSdiv : IOpcode
{
    public byte Code => 0x05;
    public string Name => "SDIV";

    // [AI-EDIT 2026-01-10] Yellow Paper: SDIV = µ_s[0] / µ_s[1] (signed), zero when µ_s[1]=0.
    // a = first pop (top), b = second pop. Result = a / b.
    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a) || !context.Stack.TryPop(out var b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        BigInteger result;
        if (b == BigInteger.Zero)
        {
            result = BigInteger.Zero;
        }
        else
        {
            var sa = EvmArith.ToSigned(a);
            var sb = EvmArith.ToSigned(b);
            if (sa == -EvmArith.TwoTo255 && sb == BigInteger.MinusOne)
                result = EvmArith.TwoTo255;  // -2^255 / -1 overflows → wraps via TryPush mod
            else
                result = BigInteger.Divide(sa, sb);
        }

        if (!context.Stack.TryPush(result))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(5), context.ProgramCounter + 1));
    }
}

/// <summary>
/// SMOD (0x07): Signed modulo operation (result has same sign as dividend).
/// Gas: 5
/// </summary>
public sealed class OpcodeSmod : IOpcode
{
    public byte Code => 0x07;
    public string Name => "SMOD";

    // [AI-EDIT 2026-01-10] Yellow Paper: SMOD = µ_s[0] mod µ_s[1] (signed), zero when µ_s[1]=0.
    // a = first pop (top), b = second pop. Sign follows a (the dividend).
    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a) || !context.Stack.TryPop(out var b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        BigInteger result;
        if (b == BigInteger.Zero)
        {
            result = BigInteger.Zero;
        }
        else
        {
            var sa = EvmArith.ToSigned(a);
            var sb = EvmArith.ToSigned(b);
            // sgn(µ_s[0]) × (|µ_s[0]| mod |µ_s[1]|)
            var absResult = BigInteger.Abs(sa) % BigInteger.Abs(sb);
            result = sa.Sign < 0 ? -absResult : absResult;
        }

        if (!context.Stack.TryPush(result))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(5), context.ProgramCounter + 1));
    }
}

/// <summary>
/// ADDMOD (0x08): (a + b) % N with arbitrary-precision intermediate.
/// Gas: 8
/// </summary>
public sealed class OpcodeAddMod : IOpcode
{
    public byte Code => 0x08;
    public string Name => "ADDMOD";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a) || !context.Stack.TryPop(out var b) || !context.Stack.TryPop(out var n))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        var result = n == BigInteger.Zero ? BigInteger.Zero : (a + b) % n;

        if (!context.Stack.TryPush(result))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(8), context.ProgramCounter + 1));
    }
}

/// <summary>
/// MULMOD (0x09): (a * b) % N with arbitrary-precision intermediate.
/// Gas: 8
/// </summary>
public sealed class OpcodeMulMod : IOpcode
{
    public byte Code => 0x09;
    public string Name => "MULMOD";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a) || !context.Stack.TryPop(out var b) || !context.Stack.TryPop(out var n))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        var result = n == BigInteger.Zero ? BigInteger.Zero : (a * b) % n;

        if (!context.Stack.TryPush(result))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(8), context.ProgramCounter + 1));
    }
}

/// <summary>
/// EXP (0x0A): Exponential operation: a ** b (mod 2^256).
/// Gas: 10 + 50 * ceil(log256(b)) — i.e. 50 per byte of exponent.
/// </summary>
public sealed class OpcodeExp : IOpcode
{
    public byte Code => 0x0A;
    public string Name => "EXP";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var baseVal) || !context.Stack.TryPop(out var exp))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        // Gas: 10 base + 50 per byte of exponent (0 exponent = 0 extra bytes)
        var expByteCount = exp == BigInteger.Zero ? 0 : (exp.GetBitLength() + 7) / 8;
        var gasCost = 10UL + 50UL * (ulong)expByteCount;

        var result = BigInteger.ModPow(baseVal, exp, EvmArith.TwoTo256);

        if (!context.Stack.TryPush(result))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(gasCost), context.ProgramCounter + 1));
    }
}

/// <summary>
/// SIGNEXTEND (0x0B): Extends sign of value x from bit b*8+7.
/// b: byte index from right (0 = least significant byte). Gas: 5.
/// </summary>
public sealed class OpcodeSignExtend : IOpcode
{
    public byte Code => 0x0B;
    public string Name => "SIGNEXTEND";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var b) || !context.Stack.TryPop(out var x))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        BigInteger result;
        if (b >= 31)
        {
            // b >= 31 means sign is already the full 256-bit sign — no change
            result = x;
        }
        else
        {
            var byteIndex = (int)b;
            var signBit = 7 + byteIndex * 8;       // bit position of sign bit
            var bitMask = BigInteger.Pow(2, signBit);
            // Check if the sign bit is set
            if ((x & bitMask) != BigInteger.Zero)
            {
                // Sign bit is 1 → fill all bits above signBit with 1s
                var fillMask = EvmArith.TwoTo256 - BigInteger.Pow(2, signBit + 1);
                result = x | fillMask;
            }
            else
            {
                // Sign bit is 0 → zero all bits above signBit
                result = x & (BigInteger.Pow(2, signBit + 1) - 1);
            }
        }

        if (!context.Stack.TryPush(result))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(5), context.ProgramCounter + 1));
    }
}
