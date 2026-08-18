using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Primitives;
using ExecutionContext = Schlieren.Core.Execution.ExecutionContext;

namespace Schlieren.Core.Opcodes;

public sealed class OpcodeStop : IOpcode
{
    public byte Code => 0x00;
    public string Name => "STOP";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(0), context.Code.Length));
    }
}

public sealed class OpcodeJump : IOpcode
{
    public byte Code => 0x56;
    public string Name => "JUMP";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var dest))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));
       
        if (dest > int.MaxValue || dest < 0)
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.BadJumpDestination), context.ProgramCounter + 1));

        var destInt = dest > int.MaxValue ? int.MaxValue : (int)dest;
        
        if (!context.IsValidJumpDestination(destInt))
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.BadJumpDestination), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(8), destInt));
    }
}

public sealed class OpcodeJumpi : IOpcode
{
    public byte Code => 0x57;
    public string Name => "JUMPI";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var dest) || !context.Stack.TryPop(out var condition))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        if (condition.IsZero)
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(10), context.ProgramCounter + 1));

        if (dest > int.MaxValue || dest < 0)
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.BadJumpDestination), context.ProgramCounter + 1));

        var destInt = dest > int.MaxValue ? int.MaxValue : (int)dest;

        if (!context.IsValidJumpDestination(destInt))
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.BadJumpDestination), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(10), destInt));
    }
}

public sealed class OpcodePc : IOpcode
{
    public byte Code => 0x58;
    public string Name => "PC";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPush(context.ProgramCounter))
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));
             
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(2), context.ProgramCounter + 1));
    }
}

public sealed class OpcodeJumpDest : IOpcode
{
    public byte Code => 0x5B;
    public string Name => "JUMPDEST";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(1), context.ProgramCounter + 1));
    }
}

public sealed class OpcodeReturn : IOpcode
{
    public byte Code => 0xF3;
    public string Name => "RETURN";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var offset) || !context.Stack.TryPop(out var length))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        if (!OperandValidation.TryResolveMemoryRange(offset, length, out var offsetInt, out var lengthInt, out var endExclusive))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.OutOfGas), context.ProgramCounter + 1));

        var expansionGas = context.Memory.CalculateGasCost((int)endExclusive);
        var data = context.Memory.Load(offsetInt, lengthInt);

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(expansionGas, data), context.Code.Length));
    }
}

public sealed class OpcodeRevert : IOpcode
{
    public byte Code => 0xFD;
    public string Name => "REVERT";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Block.Rules.HasRevert)
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.InvalidOpcode, context.GasLimit), context.ProgramCounter + 1));

        if (!context.Stack.TryPop(out var offset) || !context.Stack.TryPop(out var length))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        if (!OperandValidation.TryResolveMemoryRange(offset, length, out var offsetInt, out var lengthInt, out var endExclusive))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.OutOfGas), context.ProgramCounter + 1));

        var expansionGas = context.Memory.CalculateGasCost((int)endExclusive);
        var data = context.Memory.Load(offsetInt, lengthInt);

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.Revert, expansionGas, data), context.Code.Length));
    }
}
