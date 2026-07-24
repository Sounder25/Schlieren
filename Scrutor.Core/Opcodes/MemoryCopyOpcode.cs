using System.Numerics;
using Scrutor.Core.Execution;
using ExecutionContext = Scrutor.Core.Execution.ExecutionContext;

namespace Scrutor.Core.Opcodes;

/// <summary>
/// MCOPY (0x5E): Copy memory area (EIP-5656, Cancun).
/// Stack: [dst, src, length] → []
/// Gas: 3 + 3 * ceil(length / 32) + memory expansion cost
/// </summary>
public sealed class OpcodeMcopy : IOpcode
{
    public byte Code => 0x5E;
    public string Name => "MCOPY";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var dst) ||
            !context.Stack.TryPop(out var src) ||
            !context.Stack.TryPop(out var length))
            return new ValueTask<(ExecutionResult, int)>(
                (ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        var lengthInt = (int)length;
        if (lengthInt == 0)
        {
            // Zero-length copy: only base gas cost (3), no memory expansion
            return new ValueTask<(ExecutionResult, int)>(
                (ExecutionResult.Success(3), context.ProgramCounter + 1));
        }

        var dstInt = (int)dst;
        var srcInt = (int)src;

        // Gas: 3 (base) + 3 * words (copy cost) + memory expansion for both src and dst regions
        var words = (ulong)(lengthInt + 31) / 32;
        var copyCost = 3UL + 3UL * words;

        // Memory expansion cost: expand for whichever end is larger
        var maxEnd = Math.Max(dstInt + lengthInt, srcInt + lengthInt);
        var expansionGas = context.Memory.CalculateGasCost(maxEnd);

        // Load source data, then store at destination (handles overlapping copies correctly
        // because Load returns a new array)
        var data = context.Memory.Load(srcInt, lengthInt);
        context.Memory.Store(dstInt, data);

        return new ValueTask<(ExecutionResult, int)>(
            (ExecutionResult.Success(copyCost + expansionGas), context.ProgramCounter + 1));
    }
}
