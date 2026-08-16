using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Schlieren.Tests.Campaigns;

/// <summary>
/// REVM oracle harness. Serializes CampaignExecutionRequest to the revm-harness
/// stdin/stdout JSON contract, runs the subprocess, and deserializes the result
/// back into a CampaignExecutionResult for differential comparison.
/// </summary>
public sealed class RevmExecutionHarness : IEvmExecutionHarness
{
    private readonly string _binaryPath;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling              = JsonNumberHandling.AllowReadingFromString,
    };

    public RevmExecutionHarness(string? binaryPath = null)
    {
        _binaryPath = binaryPath ?? DefaultBinaryPath();
    }

    public static string DefaultBinaryPath() =>
        Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..",  // net8.0 -> Debug -> bin -> Schlieren.Tests -> repo root
            "oracle", "revm-harness", "target", "release", "revm-harness.exe"));

    public async Task<CampaignExecutionResult> ExecuteAsync(
        CampaignExecutionRequest request,
        CancellationToken ct = default)
    {
        var input = BuildRevmInput(request);
        var json  = JsonSerializer.Serialize(input, _jsonOpts);

        var psi = new ProcessStartInfo(_binaryPath)
        {
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start revm-harness at {_binaryPath}");

        await proc.StandardInput.WriteAsync(json);
        proc.StandardInput.Close();

        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);

        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            throw new InvalidOperationException(
                $"revm-harness exited {proc.ExitCode}: {stderr.Trim()}");

        var resp = JsonSerializer.Deserialize<RevmResponse>(stdout, _jsonOpts)
            ?? throw new InvalidOperationException("revm-harness returned null");

        return BuildResult(resp, request);
    }

    // ── Input serialization ───────────────────────────────────────────────────

    private static object BuildRevmInput(CampaignExecutionRequest r) => new
    {
        fork          = r.Fork,
        caller        = r.Caller,
        target        = r.Target,
        calldata      = r.Calldata,
        value         = $"0x{r.Value:x}",
        gas_limit     = r.GasLimit,
        prestate      = r.Prestate.Select(a => new
        {
            address = a.Address,
            code    = string.IsNullOrEmpty(a.Code) ? "0x" : a.Code,
            balance = a.Balance,
            nonce   = a.Nonce,
            storage = a.Storage.ToDictionary(kv => kv.Key, kv => kv.Value),
        }).ToArray(),
    };

    // ── Output deserialization ────────────────────────────────────────────────

    private static CampaignExecutionResult BuildResult(RevmResponse resp, CampaignExecutionRequest req)
    {
        // Build StateDiff: extract slot-level changes from state_diff.storage per account
        var stateDiff = new Dictionary<string, string>();
        foreach (var (addrKey, acct) in resp.StateDiff ?? new())
        {
            foreach (var (slot, value) in acct.Storage ?? new())
            {
                // Normalise: lowercase hex
                var normSlot  = NormalizeHex(slot);
                var normValue = NormalizeHex(value);
                stateDiff[$"{NormalizeAddr(addrKey)}:{normSlot}"] = normValue;
            }
        }

        // Frame tree from REVM frames (may be empty if inspector not wired)
        var frames = (resp.Frames ?? new()).Select((f, i) => new FrameFingerprint
        {
            Depth          = (int)f.Depth,
            CallType       = f.CallType ?? "Root",
            CodeAddress    = NormalizeAddr(f.CodeAddress ?? "0x"),
            ContextAddress = NormalizeAddr(f.ContextAddress ?? "0x"),
            Caller         = NormalizeAddr(f.Caller ?? "0x"),
            Value          = f.Value ?? "0",
            GasProvided    = f.GasProvided,
            GasConsumed    = f.GasConsumed,
            Success        = f.Success,
            ReturnData     = f.ReturnData ?? "0x",
        }).ToList();

        var logs = (resp.Logs ?? new()).Select(l => new LogFingerprint
        {
            Address = NormalizeAddr(l.Address ?? "0x"),
            Topics  = l.Topics ?? new(),
            Data    = l.Data ?? "0x",
        }).ToList();

        var fingerprint = new ExecutionFingerprint
        {
            Success    = resp.Success,
            GasUsed    = resp.GasUsed,
            ReturnData = NormalizeHex(resp.ReturnData ?? "0x"),
            Refund     = resp.Refund,
            FrameTree  = frames,
            Accesses   = new AccessFingerprint
            {
                ColdAccounts = resp.ColdAccounts ?? new(),
                WarmAccounts = resp.WarmAccounts ?? new(),
                ColdSlots    = resp.ColdSlots    ?? new(),
                WarmSlots    = resp.WarmSlots    ?? new(),
            },
            StateDiff = stateDiff,
            Logs      = logs,
        };

        // PostExecutionState not applicable for subprocess oracle
        return new CampaignExecutionResult
        {
            Success              = resp.Success,
            GasUsed              = resp.GasUsed,
            ReturnData           = NormalizeHex(resp.ReturnData ?? "0x"),
            Fingerprint          = fingerprint,
            RawTrace             = Core.Execution.ExecutionResult.Success(resp.GasUsed, Array.Empty<byte>()),
            PostExecutionState   = new Core.State.GlobalState(),
        };
    }

    private static string NormalizeHex(string h)
    {
        if (string.IsNullOrEmpty(h) || h == "0x") return "0x";
        var s = h.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? h[2..] : h;
        return "0x" + s.ToLowerInvariant().TrimStart('0').PadLeft(1, '0') switch
        {
            "" => "0",
            var v => v
        };
    }

    private static string NormalizeAddr(string a)
    {
        var s = a.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? a[2..] : a;
        return "0x" + s.ToLowerInvariant().PadLeft(40, '0');
    }

    // ── Rust JSON contract types ──────────────────────────────────────────────

    private sealed class RevmResponse
    {
        [JsonPropertyName("success")]    public bool   Success   { get; init; }
        [JsonPropertyName("gas_used")]   public ulong  GasUsed   { get; init; }
        [JsonPropertyName("refund")]     public ulong  Refund    { get; init; }
        [JsonPropertyName("return_data")]public string? ReturnData{ get; init; }
        [JsonPropertyName("frames")]     public List<RevmFrame>? Frames { get; init; }
        [JsonPropertyName("logs")]       public List<RevmLog>?   Logs   { get; init; }
        [JsonPropertyName("state_diff")] public Dictionary<string, RevmAccountDiff>? StateDiff { get; init; }
        [JsonPropertyName("cold_accounts")] public List<string>? ColdAccounts { get; init; }
        [JsonPropertyName("warm_accounts")] public List<string>? WarmAccounts { get; init; }
        [JsonPropertyName("cold_slots")]    public List<string>? ColdSlots    { get; init; }
        [JsonPropertyName("warm_slots")]    public List<string>? WarmSlots    { get; init; }
    }

    private sealed class RevmFrame
    {
        [JsonPropertyName("depth")]           public uint   Depth          { get; init; }
        [JsonPropertyName("call_type")]       public string? CallType      { get; init; }
        [JsonPropertyName("code_address")]    public string? CodeAddress   { get; init; }
        [JsonPropertyName("context_address")] public string? ContextAddress{ get; init; }
        [JsonPropertyName("caller")]          public string? Caller        { get; init; }
        [JsonPropertyName("value")]           public string? Value         { get; init; }
        [JsonPropertyName("gas_provided")]    public ulong  GasProvided    { get; init; }
        [JsonPropertyName("gas_consumed")]    public ulong  GasConsumed    { get; init; }
        [JsonPropertyName("success")]         public bool   Success        { get; init; }
        [JsonPropertyName("return_data")]     public string? ReturnData    { get; init; }
    }

    private sealed class RevmLog
    {
        [JsonPropertyName("address")] public string?       Address { get; init; }
        [JsonPropertyName("topics")]  public List<string>? Topics  { get; init; }
        [JsonPropertyName("data")]    public string?       Data    { get; init; }
    }

    private sealed class RevmAccountDiff
    {
        [JsonPropertyName("address")] public string? Address { get; init; }
        [JsonPropertyName("balance")] public string? Balance { get; init; }
        [JsonPropertyName("nonce")]   public ulong   Nonce   { get; init; }
        [JsonPropertyName("storage")] public Dictionary<string, string>? Storage { get; init; }
    }
}
