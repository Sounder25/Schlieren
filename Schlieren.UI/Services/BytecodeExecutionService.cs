using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Forks;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.UI.Services;

/// <summary>One extra account seeded into workbench pre-state (besides caller + primary code).</summary>
public sealed class WorkbenchAccountSeed
{
    public string AddressHex { get; init; } = "";
    public string BalanceWei { get; init; } = "0";
    public string CodeHex { get; init; } = "";
    public ulong Nonce { get; init; }
    public IReadOnlyDictionary<string, string>? StorageHex { get; init; }
}

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
    /// <summary>Locks fork rules for the whole tx (same as EELS).</summary>
    public string ForkLabel { get; init; } = "Osaka";
    /// <summary>Starting wei balance funded to the caller (default 1e24).</summary>
    public string CallerFundWei { get; init; } = "1000000000000000000000000";
    /// <summary>Additional accounts (callees, tokens, impls) loaded before the tx.</summary>
    public IReadOnlyList<WorkbenchAccountSeed>? ExtraAccounts { get; init; }
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
    public string Fork { get; init; } = "";
    public IReadOnlyList<string> StateDiff { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Workbench front-end over the same <see cref="StateTransition"/> pipeline EELS uses.
/// Nested CALL/CREATE go through SubCall; fork is locked on the block for the whole tx.
/// </summary>
public static class BytecodeExecutionService
{
    private static readonly StateTransition Pipeline = new(BuildMachine());

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
        state.SetNonce(callerAddr, 0);
        state.SetBalance(callerAddr, fund);
        if (!TryApplySeeds(state, options.ExtraAccounts))
            return null;

        var tracked = new List<Address> { callerAddr, contractAddr };
        if (options.ExtraAccounts is { Count: > 0 })
        {
            foreach (var seed in options.ExtraAccounts)
            {
                if (TryAddress(seed.AddressHex, out var extra))
                    tracked.Add(extra);
            }
        }

        var pre = await SnapshotAsync(state, tracked, ct);

        var baseFeeWei = options.BaseFeeGwei * 1_000_000_000UL;
        var gasPriceWei = options.GasPriceGwei * 1_000_000_000UL;
        var rules = ForkRulesFactory.For(options.ForkLabel);
        var block = new BlockContext
        {
            ChainId = options.ChainId,
            Number = 1,
            Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            GasLimit = options.BlockGasLimit,
            Coinbase = coinbase,
            BaseFeePerGas = baseFeeWei,
            Rules = rules
        };

        var gasLimit = Math.Min(options.GasLimit, options.BlockGasLimit);
        if (gasLimit == 0)
            gasLimit = 10_000_000;

        var tx = new Transaction
        {
            From = callerAddr,
            To = contractAddr,
            Value = callValue,
            Data = callData,
            GasLimit = gasLimit,
            GasPrice = gasPriceWei,
            Nonce = 0,
            Authorization = TransactionAuthorization.Impersonated,
            EnableTracing = true
        };

        ExecutionResult result;
        try
        {
            result = await Pipeline.ApplyTransactionAsync(tx, state, block, commit: true, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = ExecutionResult.Failure(EvmError.InternalError, gasLimit) with
            {
                ReturnData = System.Text.Encoding.UTF8.GetBytes(ex.Message)
            };
        }

        var callerBal = await state.GetBalanceAsync(callerAddr, ct);
        var contractBal = await state.GetBalanceAsync(contractAddr, ct);
        var post = await SnapshotAsync(state, tracked, ct);

        return new WorkbenchRunResult
        {
            Result = result,
            CallerAddress = callerAddr.ToString(),
            ContractAddress = contractAddr.ToString(),
            CallerBalanceWei = callerBal.ToString(),
            ContractBalanceWei = contractBal.ToString(),
            CodeSize = code.Length,
            CallDataSize = callData.Length,
            Fork = options.ForkLabel,
            StateDiff = DiffSnapshots(pre, post)
        };
    }

    private static bool TryApplySeeds(GlobalState state, IReadOnlyList<WorkbenchAccountSeed>? seeds)
    {
        if (seeds is null) return true;
        foreach (var seed in seeds)
        {
            if (!TryAddress(seed.AddressHex, out var addr))
                return false;
            if (!TryParseHexBytes(seed.CodeHex, out var extraCode))
                return false;
            if (!BigInteger.TryParse(string.IsNullOrWhiteSpace(seed.BalanceWei) ? "0" : seed.BalanceWei, out var bal))
                bal = BigInteger.Zero;
            if (extraCode.Length > 0)
                state.SetCode(addr, extraCode);
            state.SetBalance(addr, bal);
            state.SetNonce(addr, seed.Nonce);
            if (seed.StorageHex is null) continue;
            foreach (var (k, v) in seed.StorageHex)
            {
                if (!TryParseWord(k, out var slot) || !TryParseWord(v, out var word))
                    return false;
                state.SetStorageAt(addr, slot, word);
            }
        }
        return true;
    }

    private static bool TryAddress(string? hex, out Address addr)
    {
        addr = default;
        try
        {
            addr = Address.FromHex(hex ?? "");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseWord(string hex, out BigInteger value)
    {
        value = BigInteger.Zero;
        if (!TryParseHexBytes(hex, out var bytes))
            return false;
        value = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        return true;
    }

    private sealed record AccountSnap(string Address, string Balance, ulong Nonce, int CodeLen);

    private static async Task<List<AccountSnap>> SnapshotAsync(
        GlobalState state, IEnumerable<Address> addrs, CancellationToken ct)
    {
        var snaps = new List<AccountSnap>();
        foreach (var a in addrs.Distinct())
        {
            var bal = await state.GetBalanceAsync(a, ct);
            var nonce = await state.GetNonceAsync(a, ct);
            var code = await state.GetCodeAsync(a, ct);
            snaps.Add(new AccountSnap(a.ToString(), bal.ToString(), nonce, code.Length));
        }
        return snaps;
    }

    private static List<string> DiffSnapshots(List<AccountSnap> pre, List<AccountSnap> post)
    {
        var lines = new List<string>();
        foreach (var after in post)
        {
            var before = pre.FirstOrDefault(p => p.Address == after.Address);
            if (before is null)
            {
                lines.Add($"{after.Address} created  bal={after.Balance} nonce={after.Nonce} code={after.CodeLen}B");
                continue;
            }
            if (before.Balance != after.Balance)
                lines.Add($"{after.Address} balance {before.Balance} → {after.Balance}");
            if (before.Nonce != after.Nonce)
                lines.Add($"{after.Address} nonce {before.Nonce} → {after.Nonce}");
            if (before.CodeLen != after.CodeLen)
                lines.Add($"{after.Address} code {before.CodeLen}B → {after.CodeLen}B");
        }
        if (lines.Count == 0)
            lines.Add("(no account-level balance/nonce/code diffs)");
        return lines;
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
