using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Primitives;
using ExecutionContext = Scrutor.Core.Execution.ExecutionContext;

namespace Scrutor.Core.Opcodes;

/// <summary>
/// ORIGIN (0x32): Get execution origination address
/// Gas: 2
/// </summary>
public sealed class OpcodeOrigin : IOpcode
{
    public byte Code => 0x32;
    public string Name => "ORIGIN";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        var origin = new BigInteger(context.Origin.Bytes, isUnsigned: true, isBigEndian: true);
        if (!context.Stack.TryPush(origin))
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(2), context.ProgramCounter + 1));
    }
}

/// <summary>
/// GASPRICE (0x3A): Get price of gas in current environment
/// Gas: 2
/// </summary>
public sealed class OpcodeGasPrice : IOpcode
{
    public byte Code => 0x3A;
    public string Name => "GASPRICE";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPush(context.GasPrice))
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(2), context.ProgramCounter + 1));
    }
}

/// <summary>
/// CALLER (0x33): Get caller address
/// Gas: 2
/// </summary>
public sealed class OpcodeCaller : IOpcode
{
    public byte Code => 0x33;
    public string Name => "CALLER";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        var caller = new BigInteger(context.Caller.Bytes, isUnsigned: true, isBigEndian: true);
        if (!context.Stack.TryPush(caller))
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(2), context.ProgramCounter + 1));
    }
}

/// <summary>
/// CALLVALUE (0x34): Get deposited value
/// Gas: 2
/// </summary>
public sealed class OpcodeCallValue : IOpcode
{
    public byte Code => 0x34;
    public string Name => "CALLVALUE";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPush(context.CallValue))
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(2), context.ProgramCounter + 1));
    }
}

/// <summary>
/// CALLDATALOAD (0x35): Get input data of current environment
/// Gas: 3
/// </summary>
public sealed class OpcodeCallDataLoad : IOpcode
{
    public byte Code => 0x35;
    public string Name => "CALLDATALOAD";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var offset))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        var offsetInt = (int)offset;
        var data = new byte[32];
        if (offsetInt < context.CallData.Length)
        {
            var count = Math.Min(32, context.CallData.Length - offsetInt);
            Array.Copy(context.CallData, offsetInt, data, 0, count);
        }

        var result = new BigInteger(data, isUnsigned: true, isBigEndian: true);
        if (!context.Stack.TryPush(result))
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3), context.ProgramCounter + 1));
    }
}

/// <summary>
/// CALLDATASIZE (0x36): Get size of input data
/// Gas: 2
/// </summary>
public sealed class OpcodeCallDataSize : IOpcode
{
    public byte Code => 0x36;
    public string Name => "CALLDATASIZE";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPush(context.CallData.Length))
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(2), context.ProgramCounter + 1));
    }
}

/// <summary>
/// CALLDATACOPY (0x37): Copy input data to memory
/// Gas: 3 + dynamic copy cost
/// </summary>
public sealed class OpcodeCallDataCopy : IOpcode
{
    public byte Code => 0x37;
    public string Name => "CALLDATACOPY";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var destOffset) || !context.Stack.TryPop(out var offset) || !context.Stack.TryPop(out var length))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        var destInt = (int)destOffset;
        var offsetInt = (int)offset;
        var lengthInt = (int)length;

        var expansionGas = context.Memory.CalculateGasCost(destInt + lengthInt);
        var copyGas = (ulong)(lengthInt + 31) / 32 * 3;

        var data = new byte[lengthInt];
        if (offsetInt < context.CallData.Length)
        {
            var count = Math.Min(lengthInt, context.CallData.Length - offsetInt);
            Array.Copy(context.CallData, offsetInt, data, 0, count);
        }

        context.Memory.Store(destInt, data);

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3 + expansionGas + copyGas), context.ProgramCounter + 1));
    }
}

/// <summary>
/// CODESIZE (0x38): Get size of code running in current environment
/// Gas: 2
/// </summary>
public sealed class OpcodeCodeSize : IOpcode
{
    public byte Code => 0x38;
    public string Name => "CODESIZE";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPush(context.Code.Length))
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(2), context.ProgramCounter + 1));
    }
}

/// <summary>
/// CODECOPY (0x39): Copy code running in current environment to memory
/// Gas: 3 + dynamic copy cost
/// </summary>
public sealed class OpcodeCodeCopy : IOpcode
{
    public byte Code => 0x39;
    public string Name => "CODECOPY";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var destOffset) || !context.Stack.TryPop(out var offset) || !context.Stack.TryPop(out var length))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        var destInt = (int)destOffset;
        var offsetInt = (int)offset;
        var lengthInt = (int)length;

        var expansionGas = context.Memory.CalculateGasCost(destInt + lengthInt);
        var copyGas = (ulong)(lengthInt + 31) / 32 * 3;

        var data = new byte[lengthInt];
        if (offsetInt < context.Code.Length)
        {
            var count = Math.Min(lengthInt, context.Code.Length - offsetInt);
            Array.Copy(context.Code, offsetInt, data, 0, count);
        }

        context.Memory.Store(destInt, data);

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3 + expansionGas + copyGas), context.ProgramCounter + 1));
    }
}

/// <summary>
/// RETURNDATASIZE (0x3D): Get size of output data from last call
/// Gas: 2
/// </summary>
public sealed class OpcodeReturnDataSize : IOpcode
{
    public byte Code => 0x3D;
    public string Name => "RETURNDATASIZE";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPush(context.LastReturnData.Length))
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(2), context.ProgramCounter + 1));
    }
}

/// <summary>
/// RETURNDATACOPY (0x3E): Copy output data from last call to memory
/// Gas: 3 + dynamic copy cost
/// </summary>
public sealed class OpcodeReturnDataCopy : IOpcode
{
    public byte Code => 0x3E;
    public string Name => "RETURNDATACOPY";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var destOffset) || !context.Stack.TryPop(out var offset) || !context.Stack.TryPop(out var length))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        var destInt = (int)destOffset;
        var offsetInt = (int)offset;
        var lengthInt = (int)length;

        if (offsetInt + lengthInt > context.LastReturnData.Length)
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.InvalidMemoryAccess), context.ProgramCounter + 1));

        var expansionGas = context.Memory.CalculateGasCost(destInt + lengthInt);
        var copyGas = (ulong)(lengthInt + 31) / 32 * 3;

        var responseData = new byte[lengthInt];
        Array.Copy(context.LastReturnData, offsetInt, responseData, 0, lengthInt);

        context.Memory.Store(destInt, responseData);

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(3 + expansionGas + copyGas), context.ProgramCounter + 1));
    }
}