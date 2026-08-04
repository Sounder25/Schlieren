using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Primitives;
using ExecutionContext = Scrutor.Core.Execution.ExecutionContext;

namespace Scrutor.Core.Opcodes;

public sealed class OpcodeMload : IOpcode
{
    public byte Code => 0x51;
    public string Name => "MLOAD";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var offset))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        // EELS: charge_gas(evm, GasCosts.OPCODE_MLOAD_BASE + extend_memory.cost)
        // Gas charged BEFORE memory expansion (spec requirement — OOG blocks the write).
        var offsetInt = offset > int.MaxValue - 32 ? int.MaxValue - 32 : (int)offset;
        var expansionGas = context.Memory.CalculateGasCost(offsetInt + 32);
        context.ConsumeGas(3 + expansionGas);
        context.Memory.Expand(offsetInt + 32);

        var data = context.Memory.Load(offsetInt, 32);
        var value = new BigInteger(data, isUnsigned: true, isBigEndian: true);
        
        if (!context.Stack.TryPush(value))
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(0), context.ProgramCounter + 1));
    }
}

public sealed class OpcodeMstore : IOpcode
{
    public byte Code => 0x52;
    public string Name => "MSTORE";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var offset) || !context.Stack.TryPop(out var value))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        // EELS: charge_gas(evm, GasCosts.OPCODE_MSTORE_BASE + extend_memory.cost)
        // Gas charged BEFORE memory expansion (spec requirement — OOG blocks the write).
        var offsetInt = offset > int.MaxValue - 32 ? int.MaxValue - 32 : (int)offset;
        var expansionGas = context.Memory.CalculateGasCost(offsetInt + 32);
        context.ConsumeGas(3 + expansionGas);
        context.Memory.Expand(offsetInt + 32);

        var data = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (data.Length < 32)
        {
            var padded = new byte[32];
            Array.Copy(data, 0, padded, 32 - data.Length, data.Length);
            data = padded;
        }
        else if (data.Length > 32)
        {
            data = data[^32..];
        }

        context.Memory.Store(offsetInt, data);

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(0), context.ProgramCounter + 1));
    }
}

public sealed class OpcodeMstore8 : IOpcode
{
    public byte Code => 0x53;
    public string Name => "MSTORE8";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var offset) || !context.Stack.TryPop(out var value))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        // EELS: charge_gas(evm, GasCosts.OPCODE_MSTORE8_BASE + extend_memory.cost)
        var offsetInt = offset > int.MaxValue - 1 ? int.MaxValue - 1 : (int)offset;
        var expansionGas = context.Memory.CalculateGasCost(offsetInt + 1);
        context.ConsumeGas(3 + expansionGas);
        context.Memory.Expand(offsetInt + 1);

        var b = (byte)(value & 0xFF);
        context.Memory.Store(offsetInt, new[] { b });

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(0), context.ProgramCounter + 1));
    }
}

public sealed class OpcodeMsize : IOpcode
{
    public byte Code => 0x59;
    public string Name => "MSIZE";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPush(context.Memory.Size))
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));
             
        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(2), context.ProgramCounter + 1));
    }
}
