using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Primitives;
using ExecutionContext = Scrutor.Core.Execution.ExecutionContext;

namespace Scrutor.Core.Opcodes;

/// <summary>
/// KECCAK256 (0x20): Compute Keccak-256 hash of a memory range
/// Gas: 30 + 6 * (size/32) + memory expansion
/// </summary>
public sealed class OpcodeKeccak256 : IOpcode
{
    public byte Code => 0x20;
    public string Name => "KECCAK256";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var offset) || !context.Stack.TryPop(out var length))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        var offsetInt = (int)offset;
        var lengthInt = (int)length;

        var expansionGas = context.Memory.CalculateGasCost(offsetInt + lengthInt);
        
        var words = (ulong)(lengthInt + 31) / 32;
        var hashGas = 30 + 6 * words;

        var data = context.Memory.Load(offsetInt, lengthInt);
        var hash = CryptoUtils.Keccak256(data);
        var result = new BigInteger(hash, isUnsigned: true, isBigEndian: true);

        if (!context.Stack.TryPush(result))
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(hashGas + expansionGas), context.ProgramCounter + 1));
    }
}

/// <summary>
/// CHAINID (0x45): Get current chain ID
/// Gas: 2
/// </summary>
public sealed class OpcodeChainId : IOpcode
{
    public byte Code => 0x45;
    public string Name => "CHAINID";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPush(context.Block.ChainId))
             return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(2), context.ProgramCounter + 1));
    }
}

/// <summary>
/// SELFBALANCE (0x47): Get balance of the current contract
/// Gas: 5
/// </summary>
public sealed class OpcodeSelfBalance : IOpcode
{
    public byte Code => 0x47;
    public string Name => "SELFBALANCE";

    public async ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (context.GlobalState == null)
             return (ExecutionResult.Failure(EvmError.InternalError), context.ProgramCounter + 1);

        var balance = await context.GlobalState.GetBalanceAsync(context.ContractAddress, ct);
        
        if (!context.Stack.TryPush(balance))
             return (ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1);

        return (ExecutionResult.Success(5), context.ProgramCounter + 1);
    }
}

/// <summary>
/// EXTCODESIZE (0x3B): Get size of an account's code
/// Gas: 100..2600 (using 2600 for now)
/// </summary>
public sealed class OpcodeExtCodeSize : IOpcode
{
    public byte Code => 0x3B;
    public string Name => "EXTCODESIZE";

    public async ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var addr))
            return (ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1);

        var address = ToAddress(addr);
        var code = await context.GlobalState.GetCodeAsync(address, ct);
        
        if (!context.Stack.TryPush(code.Length))
             return (ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1);

        return (ExecutionResult.Success(2600), context.ProgramCounter + 1);
    }

    private static Address ToAddress(BigInteger val)
    {
        var bytes = val.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length == 20) return new Address(bytes);
        var padded = new byte[20];
        if (bytes.Length > 20) Array.Copy(bytes, bytes.Length - 20, padded, 0, 20);
        else Array.Copy(bytes, 0, padded, 20 - bytes.Length, bytes.Length);
        return new Address(padded);
    }
}

/// <summary>
/// EXTCODECOPY (0x3C): Copy an account's code to memory
/// Gas: 100..2600 + dynamic copy cost
/// </summary>
public sealed class OpcodeExtCodeCopy : IOpcode
{
    public byte Code => 0x3C;
    public string Name => "EXTCODECOPY";

    public async ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var addr) || !context.Stack.TryPop(out var destOffset) || 
            !context.Stack.TryPop(out var offset) || !context.Stack.TryPop(out var length))
            return (ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1);

        var address = ToAddress(addr);
        var code = await context.GlobalState.GetCodeAsync(address, ct);

        var destInt = (int)destOffset;
        var offsetInt = (int)offset;
        var lengthInt = (int)length;

        var expansionGas = context.Memory.CalculateGasCost(destInt + lengthInt);
        var copyGas = (ulong)(lengthInt + 31) / 32 * 3;

        var data = new byte[lengthInt];
        if (offsetInt < code.Length)
        {
            var remaining = Math.Min(lengthInt, code.Length - offsetInt);
            Array.Copy(code, offsetInt, data, 0, remaining);
        }

        context.Memory.Store(destInt, data);

        return (ExecutionResult.Success(2600 + expansionGas + copyGas), context.ProgramCounter + 1);
    }

    private static Address ToAddress(BigInteger val)
    {
        var bytes = val.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length == 20) return new Address(bytes);
        var padded = new byte[20];
        if (bytes.Length > 20) Array.Copy(bytes, bytes.Length - 20, padded, 0, 20);
        else Array.Copy(bytes, 0, padded, 20 - bytes.Length, bytes.Length);
        return new Address(padded);
    }
}

/// <summary>
/// EXTCODEHASH (0x3F): Get hash of an account's code
/// Gas: 100..2600
/// </summary>
public sealed class OpcodeExtCodeHash : IOpcode
{
    public byte Code => 0x3F;
    public string Name => "EXTCODEHASH";

    public async ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var addr))
            return (ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1);

        var address = ToAddress(addr);
        
        if (!await context.GlobalState.AccountExistsAsync(address, ct))
        {
            context.Stack.TryPush(0);
        }
        else
        {
            var code = await context.GlobalState.GetCodeAsync(address, ct);
            var hash = CryptoUtils.Keccak256(code);
            context.Stack.TryPush(new BigInteger(hash, isUnsigned: true, isBigEndian: true));
        }

        return (ExecutionResult.Success(2600), context.ProgramCounter + 1);
    }

    private static Address ToAddress(BigInteger val)
    {
        var bytes = val.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length == 20) return new Address(bytes);
        var padded = new byte[20];
        if (bytes.Length > 20) Array.Copy(bytes, bytes.Length - 20, padded, 0, 20);
        else Array.Copy(bytes, 0, padded, 20 - bytes.Length, bytes.Length);
        return new Address(padded);
    }
}