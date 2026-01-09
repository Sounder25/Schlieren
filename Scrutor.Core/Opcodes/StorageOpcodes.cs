using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Primitives;
using ExecutionContext = Scrutor.Core.Execution.ExecutionContext;

namespace Scrutor.Core.Opcodes;

public sealed class OpcodeSload : IOpcode
{
    public byte Code => 0x54;
    public string Name => "SLOAD";

    public async ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var key))
            return (ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1);

        var value = await context.Storage.LoadAsync(key);
        
        if (!context.Stack.TryPush(value))
             return (ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1);

        return (ExecutionResult.Success(2100), context.ProgramCounter + 1);
    }
}

public sealed class OpcodeSstore : IOpcode
{
    public byte Code => 0x55;
    public string Name => "SSTORE";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var key) || !context.Stack.TryPop(out var value))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        context.Storage.Store(key, value);

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(20000), context.ProgramCounter + 1));
    }
}