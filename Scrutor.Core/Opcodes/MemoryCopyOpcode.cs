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

        // [CONSENSUS] Oversized operands result in OutOfGas, NOT InternalError.
        // Zero-length operations are always valid regardless of offsets.
        if (!OperandValidation.TryResolveMemoryRange(dst, length, out var dstInt, out var lengthInt, out var dstEnd))
            return new ValueTask<(ExecutionResult, int)>(
                (ExecutionResult.Failure(EvmError.OutOfGas, context.GasLimit), context.ProgramCounter + 1));

        if (!OperandValidation.TryResolveMemoryRange(src, length, out var srcInt, out _, out var srcEnd))
            return new ValueTask<(ExecutionResult, int)>(
                (ExecutionResult.Failure(EvmError.OutOfGas, context.GasLimit), context.ProgramCounter + 1));

        if (lengthInt == 0)
        {
            // Zero-length copy: only base gas cost (3), no memory expansion
            return new ValueTask<(ExecutionResult, int)>(
                (ExecutionResult.Success(3), context.ProgramCounter + 1));
        }

        // Gas: 3 (base) + 3 * words (copy cost) + memory expansion for both src and dst regions
        var words = ((ulong)lengthInt + 31) / 32;
        var copyCost = 3UL + 3UL * words;

        // Memory expansion cost: expand for whichever end is larger
        var maxEnd = Math.Max((int)dstEnd, (int)srcEnd);
        var expansionGas = context.Memory.CalculateGasCost(maxEnd);

        // Load source data, then store at destination (handles overlapping copies correctly
        // because Load returns a new array)
        var data = context.Memory.Load(srcInt, lengthInt);
        context.Memory.Store(dstInt, data);

        return new ValueTask<(ExecutionResult, int)>(
            (ExecutionResult.Success(copyCost + expansionGas), context.ProgramCounter + 1));
    }
}
