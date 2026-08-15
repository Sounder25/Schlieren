using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Primitives;
using ExecutionContext = Schlieren.Core.Execution.ExecutionContext;

namespace Schlieren.Core.Opcodes;

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

        // [CONSENSUS] Oversized operands result in OutOfGas, NOT InternalError.
        // Zero-length operations are always valid regardless of offset.
        if (!OperandValidation.TryResolveMemoryRange(offset, length, out var offsetInt, out var lengthInt, out var endExclusive))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.OutOfGas, context.GasLimit), context.ProgramCounter + 1));

        var expansionGas = context.Memory.CalculateGasCost((int)endExclusive);
        
        var words = ((ulong)lengthInt + 31) / 32;
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
/// CHAINID (0x46): Get current chain ID
/// Gas: 2
/// </summary>
public sealed class OpcodeChainId : IOpcode
{
    public byte Code => 0x46;
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
        if (!context.Block.Rules.HasSelfBalance)
            return (ExecutionResult.Failure(EvmError.InvalidOpcode, context.GasLimit), context.ProgramCounter + 1);

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
/// Gas: EIP-2929 warm = 100, cold = 2600
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
        var rules   = context.Block.Rules;
        bool isWarm = rules.HasEip2929WarmCold ? context.Access.TouchAddress(address) : true;
        var gasCost = rules.ExtAccountCost(isWarm);

        var code = await context.GlobalState.GetCodeAsync(address, ct);
        
        if (!context.Stack.TryPush(code.Length))
             return (ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1);

        return (ExecutionResult.Success(gasCost), context.ProgramCounter + 1);
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
/// Gas: EIP-2929 warm = 100, cold = 2600, + dynamic copy cost
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
        var rules   = context.Block.Rules;
        bool isWarm  = rules.HasEip2929WarmCold ? context.Access.TouchAddress(address) : true;
        var addressGas = rules.ExtAccountCost(isWarm);

        if (!OperandValidation.TryResolveMemoryRange(destOffset, length, out var destInt, out var lengthInt, out var endExclusive))
            return (ExecutionResult.Failure(EvmError.OutOfGas, context.GasLimit), context.ProgramCounter + 1);

        var code = await context.GlobalState.GetCodeAsync(address, ct);
        var offsetInt = offset > int.MaxValue ? int.MaxValue : (int)offset;
        var expansionGas = context.Memory.CalculateGasCost((int)endExclusive);
        var copyGas = ((ulong)lengthInt + 31) / 32 * 3;

        var data = new byte[lengthInt];
        if (lengthInt > 0 && offsetInt < code.Length)
        {
            var remaining = Math.Min(lengthInt, code.Length - offsetInt);
            Array.Copy(code, offsetInt, data, 0, remaining);
        }
        context.Memory.Store(destInt, data);

        return (ExecutionResult.Success(addressGas + expansionGas + copyGas), context.ProgramCounter + 1);
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

// [AI-EDIT 2026-01-10] Block information opcodes: BLOCKHASH, COINBASE, TIMESTAMP, NUMBER, DIFFICULTY, GASLIMIT, BASEFEE

/// <summary>
/// BLOCKHASH (0x40): Get hash of a recent block.
/// Gas: 20. Returns 0 if block number is not within the last 256 blocks.
/// </summary>
public sealed class OpcodeBlockHash : IOpcode
{
    public byte Code => 0x40;
    public string Name => "BLOCKHASH";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPop(out var blockNum))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackUnderflow), context.ProgramCounter + 1));

        // BLOCKHASH returns 0 for:
        //   - requested block >= current block (future / current)
        //   - requested block < current block - 256 (too old)
        // Otherwise look up from the block's hash table.
        var currentBlock = context.Block.Number;
        BigInteger hash = BigInteger.Zero;
        var requested = (ulong)blockNum;
        if (blockNum >= 0 && requested < currentBlock &&
            (currentBlock <= 256 || requested >= currentBlock - 256))
        {
            if (context.Block.BlockHashes.TryGetValue(requested, out var hashBytes))
                hash = new BigInteger(hashBytes, isUnsigned: true, isBigEndian: true);
            // else hash stays 0 (no entry provided — valid in tests that don't care)
        }

        if (!context.Stack.TryPush(hash))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(20), context.ProgramCounter + 1));
    }
}

/// <summary>
/// COINBASE (0x41): Get the block's beneficiary address.
/// Gas: 2
/// </summary>
public sealed class OpcodeCoinbase : IOpcode
{
    public byte Code => 0x41;
    public string Name => "COINBASE";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        var coinbase = new BigInteger(context.Block.Coinbase.Bytes, isUnsigned: true, isBigEndian: true);
        if (!context.Stack.TryPush(coinbase))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(2), context.ProgramCounter + 1));
    }
}

/// <summary>
/// TIMESTAMP (0x42): Get the block's timestamp.
/// Gas: 2
/// </summary>
public sealed class OpcodeTimestamp : IOpcode
{
    public byte Code => 0x42;
    public string Name => "TIMESTAMP";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPush(context.Block.Timestamp))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(2), context.ProgramCounter + 1));
    }
}

/// <summary>
/// NUMBER (0x43): Get the block's number.
/// Gas: 2
/// </summary>
public sealed class OpcodeNumber : IOpcode
{
    public byte Code => 0x43;
    public string Name => "NUMBER";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPush(context.Block.Number))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(2), context.ProgramCounter + 1));
    }
}

/// <summary>
/// DIFFICULTY / PREVRANDAO (0x44): Get the block's difficulty or prevrandao (post-merge).
/// Gas: 2
/// </summary>
public sealed class OpcodeDifficulty : IOpcode
{
    public byte Code => 0x44;
    public string Name => "DIFFICULTY";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPush(context.Block.Difficulty))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(2), context.ProgramCounter + 1));
    }
}

/// <summary>
/// GASLIMIT (0x45): Get the block's gas limit.
/// Gas: 2
/// </summary>
public sealed class OpcodeGasLimit : IOpcode
{
    public byte Code => 0x45;
    public string Name => "GASLIMIT";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Stack.TryPush(context.Block.GasLimit))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(2), context.ProgramCounter + 1));
    }
}

/// <summary>
/// BASEFEE (0x48): Get the EIP-1559 base fee for the current block.
/// Gas: 2
/// </summary>
public sealed class OpcodeBaseFee : IOpcode
{
    public byte Code => 0x48;
    public string Name => "BASEFEE";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Block.Rules.HasEip1559BaseFee)
            return new ValueTask<(ExecutionResult, int)>((
                ExecutionResult.Failure(EvmError.InvalidOpcode, context.GasLimit),
                context.ProgramCounter + 1));

        if (!context.Stack.TryPush(context.Block.BaseFeePerGas))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(2), context.ProgramCounter + 1));
    }
}

/// <summary>
/// BLOBBASEFEE (0x4A): Get the EIP-4844 blob base fee for the current block.
/// Gas: 2
/// </summary>
public sealed class OpcodeBlobBaseFee : IOpcode
{
    public byte Code => 0x4A;
    public string Name => "BLOBBASEFEE";

    public ValueTask<(ExecutionResult, int)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
    {
        if (!context.Block.Rules.HasEip4844BlobTx)
            return new ValueTask<(ExecutionResult, int)>((
                ExecutionResult.Failure(EvmError.InvalidOpcode, context.GasLimit),
                context.ProgramCounter + 1));

        if (!context.Stack.TryPush(context.Block.GetBlobBaseFee()))
            return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Failure(EvmError.StackOverflow), context.ProgramCounter + 1));

        return new ValueTask<(ExecutionResult, int)>((ExecutionResult.Success(2), context.ProgramCounter + 1));
    }
}

/// <summary>
/// EXTCODEHASH (0x3F): Get hash of an account's code
/// Gas: EIP-2929 warm = 100, cold = 2600
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
        var rules   = context.Block.Rules;
        bool isWarm = rules.HasEip2929WarmCold ? context.Access.TouchAddress(address) : true;
        var gasCost = rules.ExtCodeHashCost(isWarm);
        
        if (!await context.GlobalState.AccountExistsAsync(address, ct))
            context.Stack.TryPush(0);
        else
        {
            var code = await context.GlobalState.GetCodeAsync(address, ct);
            var hash = CryptoUtils.Keccak256(code);
            context.Stack.TryPush(new BigInteger(hash, isUnsigned: true, isBigEndian: true));
        }

        return (ExecutionResult.Success(gasCost), context.ProgramCounter + 1);
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
