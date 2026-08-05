using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Opcodes;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;

namespace Scrutor.UI.Services;

/// <summary>Options for a live bytecode run from the workbench.</summary>
public sealed class BytecodeRunOptions
{
    public ulong GasLimit { get; init; } = 10_000_000;
    public ulong ChainId { get; init; } = 1;
    public ulong BaseFeeGwei { get; init; } = 1;
    public ulong BlockGasLimit { get; init; } = 30_000_000;
    public string CoinbaseHex { get; init; } = "0x0000000000000000000000000000000000000000";
    /// <summary>Report / UI label only — Core currently uses a unified modern opcode set.</summary>
    public string ForkLabel { get; init; } = "Cancun";
}

/// <summary>
/// Runs hex bytecode through the live Scrutor.Core EVM and returns a real trace.
/// </summary>
public static class BytecodeExecutionService
{
    private static readonly EvmMachine Machine = BuildMachine();

    private static EvmMachine BuildMachine()
    {
        var opcodeInstances = typeof(IOpcode).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IOpcode).IsAssignableFrom(t))
            .Select(t => (IOpcode)Activator.CreateInstance(t)!)
            .ToList();

        return new EvmMachine(opcodeInstances);
    }

    public static async Task<ExecutionResult?> RunAsync(
        string hexBytecode,
        BytecodeRunOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new BytecodeRunOptions();

        var cleaned = hexBytecode
            .Replace("0x", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "")
            .Replace("\n", "")
            .Replace("\r", "")
            .Replace("\t", "")
            .Trim();

        if (cleaned.Length == 0 || cleaned.Length % 2 != 0)
            return null;

        byte[] code;
        try
        {
            code = Convert.FromHexString(cleaned);
        }
        catch
        {
            return null;
        }

        var state = new GlobalState();
        var callerAddr = Address.Zero;
        var contractAddr = Address.FromHex("0x00000000000000000000000000000000000000aa");

        state.SetCode(contractAddr, code);
        state.SetBalance(callerAddr, BigInteger.Pow(10, 24));

        Address coinbase;
        try
        {
            coinbase = Address.FromHex(options.CoinbaseHex);
        }
        catch
        {
            coinbase = Address.Zero;
        }

        // Gwei → wei for BaseFeePerGas
        var baseFeeWei = options.BaseFeeGwei * 1_000_000_000UL;
        var block = new BlockContext
        {
            ChainId = options.ChainId,
            Number = 1,
            Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            GasLimit = options.BlockGasLimit,
            Coinbase = coinbase,
            BaseFeePerGas = baseFeeWei,
            BlobHashEnabled = true
        };

        var gasLimit = Math.Min(options.GasLimit, options.BlockGasLimit);
        if (gasLimit == 0)
            gasLimit = 10_000_000;

        var context = new Scrutor.Core.Execution.ExecutionContext
        {
            Code = code,
            GasLimit = gasLimit,
            Caller = callerAddr,
            Origin = callerAddr,
            ContractAddress = contractAddr,
            StorageAddress = contractAddr,
            State = state,
            Block = block,
            CaptureTrace = true,
            CallValue = BigInteger.Zero,
            CallData = Array.Empty<byte>(),
            GasPrice = baseFeeWei
        };

        context.Access.WarmAddress(callerAddr);
        context.Access.WarmAddress(contractAddr);
        context.SetCallContext(CallType.Root, callerAddr, contractAddr);

        try
        {
            return await Machine.ExecuteAsync(context, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ExecutionResult.Failure(EvmError.InternalError, context.GasUsed) with
            {
                TraceSteps = context.TraceSteps,
                ReturnData = System.Text.Encoding.UTF8.GetBytes(ex.Message),
                GasRefundCounter = context.GasRefundCounter
            };
        }
    }

    public static string DescribeOpcode(string opName) => opName switch
    {
        "STOP" => "Halt execution",
        "ADD" => "a + b",
        "MUL" => "a × b",
        "SUB" => "a − b",
        "DIV" => "a ÷ b (uint)",
        "MOD" => "a % b",
        "EXP" => "a ^ b",
        "LT" => "a < b",
        "GT" => "a > b",
        "EQ" => "a == b",
        "ISZERO" => "a == 0",
        "AND" => "a & b",
        "OR" => "a | b",
        "XOR" => "a ^ b",
        "NOT" => "~a",
        "SHL" => "shift left",
        "SHR" => "shift right",
        "MLOAD" => "memory read 32B",
        "MSTORE" => "memory write 32B",
        "MSTORE8" => "memory write 1B",
        "SLOAD" => "storage read",
        "SSTORE" => "storage write",
        "JUMP" => "unconditional jump",
        "JUMPI" => "conditional jump",
        "JUMPDEST" => "jump target",
        "CALL" => "external call",
        "DELEGATECALL" => "delegatecall",
        "STATICCALL" => "read-only call",
        "RETURN" => "return data",
        "REVERT" => "revert + data",
        "SHA3" or "KECCAK256" => "keccak256",
        "CALLDATALOAD" => "read calldata",
        "CALLDATASIZE" => "calldata length",
        "CODESIZE" => "bytecode length",
        "GAS" => "remaining gas",
        "CALLER" => "msg.sender",
        "ADDRESS" => "this address",
        "CALLVALUE" => "msg.value",
        _ when opName.StartsWith("PUSH") => $"push {opName[4..]}B literal",
        _ when opName.StartsWith("DUP") => $"dup stack[{opName[3..]}]",
        _ when opName.StartsWith("SWAP") => $"swap stack[{opName[4..]}]",
        _ when opName.StartsWith("LOG") => $"emit log{opName[3..]} topics",
        _ => ""
    };
}
