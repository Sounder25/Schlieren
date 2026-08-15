using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Forks;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.UI.Services;

/// <summary>Options for a live bytecode run from the workbench.</summary>
public sealed class BytecodeRunOptions
{
    public ulong GasLimit { get; init; } = 10_000_000;
    public ulong ChainId { get; init; } = 1;
    public ulong BaseFeeGwei { get; init; } = 1;
    public ulong GasPriceGwei { get; init; } = 1;
    public ulong BlockGasLimit { get; init; } = 30_000_000;
    public string CoinbaseHex { get; init; } = "0x0000000000000000000000000000000000000000";
    public string CallerHex { get; init; } = "0x0000000000000000000000000000000000000001";
    public string ContractHex { get; init; } = "0x00000000000000000000000000000000000000aa";
    /// <summary>msg.value in wei (decimal string).</summary>
    public string ValueWei { get; init; } = "0";
    /// <summary>Calldata hex (optional 0x).</summary>
    public string CallDataHex { get; init; } = "";
    /// <summary>Report label only until Core exposes hard-fork gating.</summary>
    public string ForkLabel { get; init; } = "Osaka";
    /// <summary>Starting wei balance funded to the caller (default 1e24).</summary>
    public string CallerFundWei { get; init; } = "1000000000000000000000000";
}

/// <summary>Live run outcome with post-state balances for the inspector.</summary>
public sealed class WorkbenchRunResult
{
    public required ExecutionResult Result { get; init; }
    public string CallerAddress { get; init; } = "";
    public string ContractAddress { get; init; } = "";
    public string CallerBalanceWei { get; init; } = "0";
    public string ContractBalanceWei { get; init; } = "0";
    public int CodeSize { get; init; }
    public int CallDataSize { get; init; }
}

/// <summary>
/// Runs hex bytecode through the live Schlieren.Core EVM and returns a real trace.
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

    public static bool TryParseHexBytes(string? hex, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(hex))
            return true; // empty = empty bytes

        var cleaned = CleanHex(hex);
        if (cleaned.Length % 2 != 0)
            return false;
        try
        {
            bytes = cleaned.Length == 0 ? Array.Empty<byte>() : Convert.FromHexString(cleaned);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string CleanHex(string hex) =>
        hex.Replace("0x", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "")
            .Replace("\n", "")
            .Replace("\r", "")
            .Replace("\t", "")
            .Trim();

    public static async Task<WorkbenchRunResult?> RunAsync(
        string hexBytecode,
        BytecodeRunOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new BytecodeRunOptions();

        if (!TryParseHexBytes(hexBytecode, out var code) || code.Length == 0)
            return null;

        if (!TryParseHexBytes(options.CallDataHex, out var callData))
            return null;

        Address callerAddr;
        Address contractAddr;
        Address coinbase;
        try
        {
            callerAddr = Address.FromHex(string.IsNullOrWhiteSpace(options.CallerHex)
                ? "0x0000000000000000000000000000000000000001"
                : options.CallerHex);
            contractAddr = Address.FromHex(string.IsNullOrWhiteSpace(options.ContractHex)
                ? "0x00000000000000000000000000000000000000aa"
                : options.ContractHex);
            coinbase = Address.FromHex(string.IsNullOrWhiteSpace(options.CoinbaseHex)
                ? "0x0000000000000000000000000000000000000000"
                : options.CoinbaseHex);
        }
        catch
        {
            return null;
        }

        if (!BigInteger.TryParse(options.ValueWei, out var callValue))
            callValue = BigInteger.Zero;
        if (!BigInteger.TryParse(options.CallerFundWei, out var fund) || fund < 0)
            fund = BigInteger.Pow(10, 24);

        var state = new GlobalState();
        state.SetCode(contractAddr, code);
        state.SetBalance(callerAddr, fund);
        // Fund contract with 0; value transfer is handled by CALLVALUE context only for pure code runs
        // (full StateTransition path would move value; workbench injects CallValue on context)

        var baseFeeWei = options.BaseFeeGwei * 1_000_000_000UL;
        var gasPriceWei = options.GasPriceGwei * 1_000_000_000UL;
        var block = new BlockContext
        {
            ChainId = options.ChainId,
            Number = 1,
            Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            GasLimit = options.BlockGasLimit,
            Coinbase = coinbase,
            BaseFeePerGas = baseFeeWei,
            Rules = ForkRulesFactory.For(options.ForkLabel)
        };

        var gasLimit = Math.Min(options.GasLimit, options.BlockGasLimit);
        if (gasLimit == 0)
            gasLimit = 10_000_000;

        var context = new Schlieren.Core.Execution.ExecutionContext
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
            CallValue = callValue,
            CallData = callData,
            GasPrice = gasPriceWei
        };

        context.Access.WarmAddress(callerAddr);
        context.Access.WarmAddress(contractAddr);
        context.SetCallContext(CallType.Root, callerAddr, contractAddr);

        ExecutionResult result;
        try
        {
            result = await Machine.ExecuteAsync(context, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = ExecutionResult.Failure(EvmError.InternalError, context.GasUsed) with
            {
                TraceSteps = context.TraceSteps,
                ReturnData = System.Text.Encoding.UTF8.GetBytes(ex.Message),
                GasRefundCounter = context.GasRefundCounter,
                Logs = context.Logs.ToList()
            };
        }

        var callerBal = await state.GetBalanceAsync(callerAddr, ct);
        var contractBal = await state.GetBalanceAsync(contractAddr, ct);

        return new WorkbenchRunResult
        {
            Result = result,
            CallerAddress = callerAddr.ToString(),
            ContractAddress = contractAddr.ToString(),
            CallerBalanceWei = callerBal.ToString(),
            ContractBalanceWei = contractBal.ToString(),
            CodeSize = code.Length,
            CallDataSize = callData.Length
        };
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

    public static string ToHex(byte[]? data)
    {
        if (data is null || data.Length == 0) return "0x";
        return "0x" + Convert.ToHexString(data).ToLowerInvariant();
    }
}
