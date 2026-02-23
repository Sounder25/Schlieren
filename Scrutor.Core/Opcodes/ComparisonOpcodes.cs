using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Primitives;
using ExecutionContext = Scrutor.Core.Execution.ExecutionContext;

namespace Scrutor.Core.Opcodes;

public sealed class OpcodeLt : IOpcode
{
    public byte Code => 0x10;
    public string Name => "LT";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a) || !context.Stack.TryPop(out var b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
        
        context.Stack.TryPush(a < b ? 1 : 0);
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + 1));
    }
}

public sealed class OpcodeGt : IOpcode
{
    public byte Code => 0x11;
    public string Name => "GT";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a) || !context.Stack.TryPop(out var b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
        
        context.Stack.TryPush(a > b ? 1 : 0);
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + 1));
    }
}

public sealed class OpcodeSlt : IOpcode
{
    public byte Code => 0x12;
    public string Name => "SLT";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a) || !context.Stack.TryPop(out var b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
        
        var sa = ToSigned(a);
        var sb = ToSigned(b);
        
        context.Stack.TryPush(sa < sb ? 1 : 0);
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + 1));
    }

    private static BigInteger ToSigned(BigInteger val)
    {
        var limit = BigInteger.Pow(2, 255);
        if (val >= limit)
            return val - BigInteger.Pow(2, 256);
        return val;
    }
}

public sealed class OpcodeSgt : IOpcode
{
    public byte Code => 0x13;
    public string Name => "SGT";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a) || !context.Stack.TryPop(out var b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
        
        var sa = ToSigned(a);
        var sb = ToSigned(b);
        
        context.Stack.TryPush(sa > sb ? 1 : 0);
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + 1));
    }

    private static BigInteger ToSigned(BigInteger val)
    {
        var limit = BigInteger.Pow(2, 255);
        if (val >= limit)
            return val - BigInteger.Pow(2, 256);
        return val;
    }
}

public sealed class OpcodeEq : IOpcode
{
    public byte Code => 0x14;
    public string Name => "EQ";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a) || !context.Stack.TryPop(out var b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
        
        context.Stack.TryPush(a == b ? 1 : 0);
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + 1));
    }
}

public sealed class OpcodeIsZero : IOpcode
{
    public byte Code => 0x15;
    public string Name => "ISZERO";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
        
        context.Stack.TryPush(a == 0 ? 1 : 0);
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + 1));
    }
}
