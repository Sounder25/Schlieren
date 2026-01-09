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

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a) || !context.Stack.TryPop(out var b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
        
        var result = b == 0 ? BigInteger.Zero : a / b;
        
        if (!context.Stack.TryPush(result))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));
        
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(5), context.ProgramCounter + 1));
    }
}

public sealed class OpcodeMod : IOpcode
{
    public byte Code => 0x05;
    public string Name => "MOD";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var a) || !context.Stack.TryPop(out var b))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
        
        var result = b == 0 ? BigInteger.Zero : a % b;
        
        if (!context.Stack.TryPush(result))
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));
        
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(5), context.ProgramCounter + 1));
    }
}
