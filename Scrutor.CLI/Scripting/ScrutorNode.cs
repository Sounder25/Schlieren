using System.Net.Http.Json;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Scrutor.CLI.Scripting;

// ─────────────────────────────────────────────────────────────────────────────
//  Public API types
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Structured gas accounting record returned by every Send/Deploy call.
/// Satisfies the "emit a structured gas explanation record" requirement for
/// all evaluated transactions.
/// </summary>
public sealed class GasRecord
{
    public ulong GasUsed        { get; init; }
    public ulong GasLimit       { get; init; }
    public BigInteger GasPrice  { get; init; }
    public BigInteger BaseFee   { get; init; }
    public BigInteger Tip        { get; init; }
    public BigInteger TotalCost  { get; init; } // GasUsed * GasPrice (wei)
    public string TxHash        { get; init; } = string.Empty;

    public override string ToString() =>
        $"gas_used={GasUsed}  gas_price={GasPrice}  base_fee={BaseFee}  tip={Tip}  cost_wei={TotalCost}  tx={TxHash[..Math.Min(18, TxHash.Length)]}…";
}

/// <summary>
/// Result of a Send/Deploy operation — tx hash, receipt, and the gas record.
/// </summary>
public sealed class TxResult
{
    public string   TxHash          { get; init; } = string.Empty;
    public bool     Success         { get; init; }
    public string?  ContractAddress { get; init; }
    public GasRecord Gas            { get; init; } = new();
    public IReadOnlyList<LogEntry> Logs { get; init; } = Array.Empty<LogEntry>();
    public string? RevertReason     { get; init; }
}

/// <summary>EVM event log returned from a transaction receipt.</summary>
public sealed class LogEntry
{
    public string Address           { get; init; } = string.Empty;
    public IReadOnlyList<string> Topics { get; init; } = Array.Empty<string>();
    public string Data              { get; init; } = string.Empty;
    public ulong  LogIndex          { get; init; }
}

/// <summary>
/// A deployed contract proxy — Call / Send with ABI-encoded arguments.
/// </summary>
public sealed class Contract
{
    private readonly ScrutorNode _node;
    private readonly string      _address;
    private readonly JsonElement _abi;

    internal Contract(ScrutorNode node, string address, JsonElement abi)
    {
        _node    = node;
        _address = address;
        _abi     = abi;
    }

    public string Address => _address;

    /// <summary>Read-only call — returns decoded first output value.</summary>
    public Task<T> Call<T>(string function, params object[] args) =>
        _node.CallContract<T>(_address, _abi, function, args);

    /// <summary>Convenience: call with no args, return raw hex string.</summary>
    public Task<string> Call(string function) =>
        _node.CallContract<string>(_address, _abi, function, Array.Empty<object>());

    /// <summary>State-changing call — full gas record in TxResult.</summary>
    public Task<TxResult> Send(string function, string? from = null, BigInteger? value = null, params object[] args) =>
        _node.SendContract(_address, _abi, function, from, value, args);

    /// <summary>Convenience: send with only positional args, no from/value needed.</summary>
    public Task<TxResult> Send(string function, params object[] args) =>
        _node.SendContract(_address, _abi, function, null, null, args);

    public override string ToString() => $"Contract({_address})";
}

// ─────────────────────────────────────────────────────────────────────────────
//  ScrutorNode — the primary script host context object
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Injected into every .csx script as `node`.
/// Wraps JSON-RPC with Scrutor-specific conveniences: gas records, snapshots,
/// fork inspection, and trace hooks.
/// </summary>
public sealed class ScrutorNode : IAsyncDisposable
{
    private readonly HttpClient     _http;
    private readonly string         _rpcUrl;
    private readonly string         _artifactsDir;
    private readonly JsonSerializerOptions _jOpts = new() { PropertyNameCaseInsensitive = true };
    private          int            _reqId = 1;

    public ScrutorNode(string rpcUrl, string artifactsDir)
    {
        _rpcUrl       = rpcUrl;
        _artifactsDir = artifactsDir;
        _http         = new HttpClient { BaseAddress = new Uri(rpcUrl) };
    }

    // ── Accounts ─────────────────────────────────────────────────────────────

    public async Task<string[]> GetAccountsAsync()
    {
        var result = await RpcAsync<string[]>("eth_accounts");
        return result ?? Array.Empty<string>();
    }

    // ── Contract deploy ──────────────────────────────────────────────────────

    public async Task<Contract> Deploy(string contractName, string? from = null, params object[] constructorArgs)
    {
        var artifact = LoadArtifact(contractName);
        var bytecode = artifact.GetProperty("bytecode").GetString() ?? throw new Exception($"No bytecode in {contractName}.json");
        var abi      = artifact.GetProperty("abi");

        // Encode constructor args if any
        var data = bytecode;
        if (constructorArgs.Length > 0)
        {
            var ctorAbi = FindAbiEntry(abi, null, "constructor");
            data += EncodeAbiArgs(ctorAbi, constructorArgs);
        }

        var deployer = from ?? (await GetAccountsAsync()).FirstOrDefault() ?? throw new Exception("No accounts available");
        var result   = await SendRaw(deployer, null, data, value: null);

        if (!result.Success)
            throw new Exception($"Deploy failed for {contractName}: {result.RevertReason ?? $"tx={result.TxHash} status=0"}");

        var address = result.ContractAddress ?? throw new Exception($"No contract address in receipt for {contractName}");
        Console.WriteLine($"  ✓  {contractName} deployed at {address}  ({result.Gas})");
        return new Contract(this, address, abi);
    }

    // ── Contract call / send (used by Contract proxy) ───────────────────────

    internal async Task<T> CallContract<T>(string address, JsonElement abi, string function, object[] args)
    {
        var entry = FindAbiEntry(abi, function, "function");
        var data  = FunctionSelector(function, entry) + EncodeAbiArgs(entry, args);

        var result = await RpcAsync<string>("eth_call", new
        {
            to   = address,
            data = "0x" + data
        }, "latest");

        return DecodeAbiReturn<T>(entry, result ?? "0x");
    }

    internal async Task<TxResult> SendContract(
        string address, JsonElement abi, string function,
        string? from, BigInteger? value, object[] args)
    {
        var entry    = FindAbiEntry(abi, function, "function");
        var data     = FunctionSelector(function, entry) + EncodeAbiArgs(entry, args);
        var deployer = from ?? (await GetAccountsAsync()).FirstOrDefault() ?? throw new Exception("No accounts available");

        return await SendRaw(deployer, address, "0x" + data, value);
    }

    // ── eth_sendTransaction + poll receipt ───────────────────────────────────

    private async Task<TxResult> SendRaw(string from, string? to, string data, BigInteger? value)
    {
        var baseFee  = await GetBaseFeeAsync();
        var gasPrice = baseFee + BigInteger.Parse("1000000000"); // base + 1 gwei tip

        var txParams = new JsonObject
        {
            ["from"]     = from,
            ["data"]     = data,
            ["gasPrice"] = "0x" + gasPrice.ToString("X")
        };
        if (to != null)     txParams["to"]    = to;
        if (value.HasValue) txParams["value"] = "0x" + value.Value.ToString("X");

        // Estimate gas — fall back to 3M on estimation failure (e.g. pre-deploy state)
        ulong gasLimit;
        try
        {
            var est = await RpcAsync<string>("eth_estimateGas", txParams);
            gasLimit = (ulong)(HexToUlong(est ?? "0x2DC6C0") * 12 / 10); // +20% headroom
        }
        catch { gasLimit = 3_000_000; }
        txParams["gas"] = "0x" + gasLimit.ToString("X");

        var txHash = await RpcAsync<string>("eth_sendTransaction", txParams)
            ?? throw new Exception("eth_sendTransaction returned null hash");

        // Poll for receipt (automine is instant on Scrutor dev-node)
        JsonElement? receipt = null;
        for (int i = 0; i < 50; i++)
        {
            receipt = await RpcAsync<JsonElement?>("eth_getTransactionReceipt", txHash);
            if (receipt.HasValue && receipt.Value.ValueKind == JsonValueKind.Object) break;
            await Task.Delay(100);
        }

        if (!receipt.HasValue || receipt.Value.ValueKind != JsonValueKind.Object)
            throw new Exception($"No receipt for {txHash} after timeout");

        var r = receipt.Value;
        var gasUsed    = HexToUlong(r.GetProperty("gasUsed").GetString() ?? "0x0");
        var effectiveGasPrice = HexToBigInt(r.GetProperty("effectiveGasPrice").GetString() ?? "0x0");
        var status     = HexToUlong(r.GetProperty("status").GetString() ?? "0x0");
        var contractAddr = r.TryGetProperty("contractAddress", out var ca) && ca.ValueKind != JsonValueKind.Null
            ? ca.GetString() : null;

        var logs = new List<LogEntry>();
        if (r.TryGetProperty("logs", out var logsEl) && logsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var log in logsEl.EnumerateArray())
            {
                var topics = new List<string>();
                if (log.TryGetProperty("topics", out var topicsEl))
                    foreach (var t in topicsEl.EnumerateArray())
                        topics.Add(t.GetString() ?? "");

                logs.Add(new LogEntry
                {
                    Address  = log.TryGetProperty("address", out var adEl) ? (adEl.GetString() ?? "") : "",
                    Topics   = topics,
                    Data     = log.TryGetProperty("data", out var dataEl) ? (dataEl.GetString() ?? "0x") : "0x",
                    LogIndex = log.TryGetProperty("logIndex", out var liEl) ? HexToUlong(liEl.GetString() ?? "0x0") : 0
                });
            }
        }

        var tip = effectiveGasPrice - baseFee;
        if (tip < BigInteger.Zero) tip = BigInteger.Zero;

        var gas = new GasRecord
        {
            GasUsed   = gasUsed,
            GasLimit  = 30_000_000,
            GasPrice  = effectiveGasPrice,
            BaseFee   = baseFee,
            Tip       = tip,
            TotalCost = effectiveGasPrice * gasUsed,
            TxHash    = txHash
        };

        return new TxResult
        {
            TxHash          = txHash,
            Success         = status == 1,
            ContractAddress = contractAddr,
            Gas             = gas,
            Logs            = logs
        };
    }

    // ── Snapshot / Revert (repeatability) ────────────────────────────────────

    /// <summary>
    /// Take a state snapshot. Returns the snapshot ID.
    /// Use <see cref="RevertAsync"/> to restore deterministic state.
    /// </summary>
    public Task<string> SnapshotAsync() =>
        RpcAsync<string>("evm_snapshot").ContinueWith(t => t.Result ?? "0x0");

    /// <summary>Restore state to a previously taken snapshot.</summary>
    public Task<bool> RevertAsync(string snapshotId) =>
        RpcAsync<bool>("evm_revert", snapshotId).ContinueWith(t => t.Result);

    // ── Fork inspection ──────────────────────────────────────────────────────

    /// <summary>Returns the current chain ID.</summary>
    public async Task<ulong> ChainIdAsync()
    {
        var r = await RpcAsync<string>("eth_chainId");
        return HexToUlong(r ?? "0x0");
    }

    /// <summary>Returns the current block number.</summary>
    public async Task<ulong> BlockNumberAsync()
    {
        var r = await RpcAsync<string>("eth_blockNumber");
        return HexToUlong(r ?? "0x0");
    }

    /// <summary>Returns the ETH balance of an address in wei.</summary>
    public async Task<BigInteger> GetBalanceAsync(string address)
    {
        var r = await RpcAsync<string>("eth_getBalance", address, "latest");
        return HexToBigInt(r ?? "0x0");
    }

    /// <summary>
    /// Returns the effective gas price suggested by the node (max priority fee + base fee).
    /// </summary>
    public async Task<BigInteger> GetGasPriceAsync()
    {
        var r = await RpcAsync<string>("eth_gasPrice");
        return HexToBigInt(r ?? "0x0");
    }

    /// <summary>Returns the current base fee per gas.</summary>
    public async Task<BigInteger> GetBaseFeeAsync()
    {
        var block = await RpcAsync<JsonElement?>("eth_getBlockByNumber", "latest", false);
        if (block.HasValue && block.Value.ValueKind == JsonValueKind.Object
            && block.Value.TryGetProperty("baseFeePerGas", out var bf))
            return HexToBigInt(bf.GetString() ?? "0x0");
        return BigInteger.Zero;
    }

    /// <summary>Advance time by the given number of seconds (evm_increaseTime).</summary>
    public Task IncreaseTimeAsync(ulong seconds) =>
        RpcAsync<object>("evm_increaseTime", (long)seconds);

    /// <summary>Mine a block immediately regardless of automine setting (evm_mine).</summary>
    public Task MineAsync() => RpcAsync<object>("evm_mine");

    // ── Raw RPC ──────────────────────────────────────────────────────────────

    /// <summary>Send a raw JSON-RPC call and return the result element.</summary>
    public async Task<JsonElement> RawAsync(string method, params object?[] args)
    {
        var result = await RpcAsync<JsonElement>(method, args);
        return result;
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private async Task<T> RpcAsync<T>(string method, params object?[] args)
    {
        var id  = Interlocked.Increment(ref _reqId);
        var req = new { jsonrpc = "2.0", id, method, @params = args };
        var body = JsonSerializer.Serialize(req);

        using var resp = await _http.PostAsync("/",
            new StringContent(body, Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var err))
        {
            var msg = err.TryGetProperty("message", out var m) ? m.GetString() : "RPC error";
            throw new Exception($"RPC {method} failed: {msg}");
        }

        if (!root.TryGetProperty("result", out var resultEl))
            return default!;

        return resultEl.Deserialize<T>(_jOpts) ?? default!;
    }

    private JsonElement LoadArtifact(string contractName)
    {
        var path = Path.Combine(_artifactsDir, $"{contractName}.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Artifact not found: {path}. Run 'scrutor compile' first.");
        using var fs  = File.OpenRead(path);
        var doc = JsonDocument.Parse(fs);
        return doc.RootElement.Clone();
    }

    // ── Minimal ABI encoder (uint256, address, bool, bytes, string only) ─────

    private static JsonElement FindAbiEntry(JsonElement abi, string? name, string type)
    {
        // Strip trailing (...) from a function signature so callers can pass either
        // "increment" or "increment()" or "setNumber(uint256)" — all match "setNumber".
        var baseName = name == null ? null : name.Contains('(') ? name[..name.IndexOf('(')] : name;

        foreach (var entry in abi.EnumerateArray())
        {
            if (!entry.TryGetProperty("type", out var t)) continue;
            if (t.GetString() != type) continue;

            if (baseName == null) return entry; // constructor

            if (entry.TryGetProperty("name", out var n) && n.GetString() == baseName)
                return entry;
        }
        throw new Exception($"ABI entry not found: {type} '{name}'");
    }

    private static string FunctionSelector(string name, JsonElement entry)
    {
        // Strip any trailing (...) the caller may have included — we build the
        // canonical signature ourselves from the ABI inputs.
        var baseName = name.Contains('(') ? name[..name.IndexOf('(')] : name;

        // Build canonical signature: name(type1,type2,...)
        var inputs = entry.TryGetProperty("inputs", out var inp) ? inp : default;
        var types  = new List<string>();
        if (inputs.ValueKind == JsonValueKind.Array)
            foreach (var p in inputs.EnumerateArray())
                if (p.TryGetProperty("type", out var t))
                    types.Add(t.GetString() ?? "uint256");

        var sig = $"{baseName}({string.Join(",", types)})";
        // keccak256 of sig → first 4 bytes
        var hash = Keccak256Ascii(sig);
        return Convert.ToHexString(hash[..4]).ToLowerInvariant();
    }

    private static string EncodeAbiArgs(JsonElement entry, object[] args)
    {
        // Simplified ABI encoder for scalar dev/test scripts.
        // Supports: uint/int (→ 32-byte big-endian), address (→ 32-byte padded), bool.
        // Struct/array/bytes support omitted intentionally — scripts needing that should
        // call eth_sendRawTransaction with a pre-encoded payload.
        if (args.Length == 0) return string.Empty;

        var sb = new StringBuilder();
        foreach (var arg in args)
        {
            var hex = arg switch
            {
                BigInteger bi => bi.ToString("X").PadLeft(64, '0'),
                ulong ul      => ul.ToString("X").PadLeft(64, '0'),
                long l        => ((BigInteger)l).ToString("X").PadLeft(64, '0'),
                int i         => ((BigInteger)i).ToString("X").PadLeft(64, '0'),
                bool b        => (b ? "1" : "0").PadLeft(64, '0'),
                string s when s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                              => s[2..].PadLeft(64, '0'),
                _ => throw new Exception($"Unsupported ABI arg type: {arg.GetType().Name}")
            };
            sb.Append(hex[^64..]);
        }
        return sb.ToString();
    }

    private static T DecodeAbiReturn<T>(JsonElement entry, string hexResult)
    {
        var raw = hexResult.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? hexResult[2..] : hexResult;
        if (raw.Length == 0 || raw.All(c => c == '0'))
        {
            if (typeof(T) == typeof(ulong))   return (T)(object)0UL;
            if (typeof(T) == typeof(BigInteger)) return (T)(object)BigInteger.Zero;
            if (typeof(T) == typeof(string))  return (T)(object)"0x0";
            if (typeof(T) == typeof(bool))    return (T)(object)false;
        }

        var bi = BigInteger.Parse("0" + raw.TrimStart('0').PadLeft(1, '0'),
            System.Globalization.NumberStyles.HexNumber);

        return typeof(T) switch
        {
            var t when t == typeof(ulong)     => (T)(object)(ulong)bi,
            var t when t == typeof(BigInteger)=> (T)(object)bi,
            var t when t == typeof(bool)      => (T)(object)(bi != BigInteger.Zero),
            var t when t == typeof(string)    => (T)(object)("0x" + raw),
            _ => (T)(object)bi
        };
    }

    // ── Crypto helpers ───────────────────────────────────────────────────────

    private static byte[] Keccak256Ascii(string s)
    {
        var bytes = Encoding.ASCII.GetBytes(s);
        // Use Scrutor.Core's keccak if reachable — otherwise inline SHA3 via
        // System.Security.Cryptography (Keccak not in stdlib; use Sha3_256 as shim
        // for function selector computation in non-security contexts only).
        // Real Keccak available via Scrutor.Core.Primitives.CryptoUtils.Keccak256.
        return Scrutor.Core.Primitives.CryptoUtils.Keccak256(bytes);
    }

    private static ulong HexToUlong(string hex)
    {
        var s = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        return s.Length == 0 ? 0UL : Convert.ToUInt64(s, 16);
    }

    private static BigInteger HexToBigInt(string hex)
    {
        var s = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        if (s.Length == 0) return BigInteger.Zero;
        return BigInteger.Parse("0" + s, System.Globalization.NumberStyles.HexNumber);
    }

    public ValueTask DisposeAsync() { _http.Dispose(); return ValueTask.CompletedTask; }
}
