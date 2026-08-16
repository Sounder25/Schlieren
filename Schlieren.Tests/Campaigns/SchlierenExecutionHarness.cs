using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schlieren.Core.Execution;
using Schlieren.Core.State;

namespace Schlieren.Tests.Campaigns;

/// <summary>
/// Schlieren EVM execution harness adapter.
/// Bridges campaign framework to actual EvmExecutor.
/// </summary>
public sealed class SchlierenExecutionHarness : IEvmExecutionHarness
{
    private readonly IEvmExecutor _executor;

    public SchlierenExecutionHarness(IEvmExecutor executor)
    {
        _executor = executor;
    }

    public async Task<CampaignExecutionResult> ExecuteAsync(
        CampaignExecutionRequest request,
        CancellationToken ct = default)
    {
        // 1. Resolve fork rules
        var fork = ResolveFork(request.Fork);

        // 2. Build execution context
        var context = BuildExecutionContext(request, fork);

        // 3. Seed accounts/code/storage
        var state = BuildWorldState(request.Prestate);

        // 4. Execute
        var result = await _executor.ExecuteAsync(
            context,
            state,
            ct);

        // 5. Convert to fingerprint
        var fingerprint = BuildFingerprint(result);

        // 6. Return normalized result
        return new CampaignExecutionResult
        {
            Success = result.Success,
            GasUsed = result.GasUsed,
            ReturnData = ToHex(result.ReturnData),
            Fingerprint = fingerprint,
            RawTrace = result.Trace
        };
    }

    private ForkRules ResolveFork(string forkName)
    {
        return forkName switch
        {
            "Berlin" => ForkRules.Berlin,
            "London" => ForkRules.London,
            "Shanghai" => ForkRules.Shanghai,
            "Cancun" => ForkRules.Cancun,
            "Prague" => ForkRules.Prague,
            _ => throw new ArgumentException($"Unknown fork: {forkName}")
        };
    }

    private ExecutionContext BuildExecutionContext(
        CampaignExecutionRequest request,
        ForkRules fork)
    {
        return new ExecutionContext
        {
            Caller = ParseAddress(request.Caller),
            Target = ParseAddress(request.Target),
            Calldata = ParseHex(request.Calldata),
            Value = request.Value,
            GasLimit = request.GasLimit,
            Fork = fork,
            
            // Block context (use defaults for campaigns)
            BlockNumber = 1,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Coinbase = new byte[20],
            Difficulty = 0,
            GasPrice = 1
        };
    }

    private IWorldState BuildWorldState(IReadOnlyList<CampaignAccount> accounts)
    {
        var state = new MemoryWorldState();

        foreach (var account in accounts)
        {
            var address = ParseAddress(account.Address);
            
            // Set code
            if (!string.IsNullOrEmpty(account.Code) && account.Code != "0x")
            {
                state.SetCode(address, ParseHex(account.Code));
            }

            // Set balance
            if (account.Balance != "0x0" && account.Balance != "0x")
            {
                state.SetBalance(address, ParseUInt256(account.Balance));
            }

            // Set nonce
            if (account.Nonce > 0)
            {
                state.SetNonce(address, account.Nonce);
            }

            // Set storage
            foreach (var (slot, value) in account.Storage)
            {
                state.SetStorage(
                    address,
                    ParseUInt256(slot),
                    ParseUInt256(value));
            }
        }

        return state;
    }

    private ExecutionFingerprint BuildFingerprint(ExecutionResult result)
    {
        // Extract frame tree from trace
        var frames = BuildFrameTree(result.Trace);

        // Extract access set
        var accesses = BuildAccessFingerprint(result.Trace);

        // Extract state diff
        var stateDiff = BuildStateDiff(result.StateDiff);

        // Extract logs
        var logs = BuildLogs(result.Logs);

        return new ExecutionFingerprint
        {
            Success = result.Success,
            GasUsed = result.GasUsed,
            ReturnData = ToHex(result.ReturnData),
            Refund = result.Refund,
            FrameTree = frames,
            Accesses = accesses,
            StateDiff = stateDiff,
            Logs = logs
        };
    }

    private List<FrameFingerprint> BuildFrameTree(ExecutionTrace trace)
    {
        var frames = new List<FrameFingerprint>();
        var frameStack = new Stack<(int startIdx, int depth, string callType)>();

        // Root frame
        frameStack.Push((0, 1, "Root"));

        for (int i = 0; i < trace.Steps.Count; i++)
        {
            var step = trace.Steps[i];

            // Detect frame changes by depth
            if (step.Depth > frameStack.Peek().depth)
            {
                // New child frame
                var callType = DetectCallType(trace.Steps[i - 1].Op);
                frameStack.Push((i, step.Depth, callType));
            }
            else if (step.Depth < frameStack.Peek().depth)
            {
                // Frame returned - build fingerprint
                var (startIdx, depth, callType) = frameStack.Pop();
                var frame = BuildFrameFingerprint(trace, startIdx, i - 1, depth, callType);
                frames.Add(frame);
            }
        }

        // Close remaining frames
        while (frameStack.Count > 0)
        {
            var (startIdx, depth, callType) = frameStack.Pop();
            var frame = BuildFrameFingerprint(trace, startIdx, trace.Steps.Count - 1, depth, callType);
            frames.Add(frame);
        }

        return frames;
    }

    private FrameFingerprint BuildFrameFingerprint(
        ExecutionTrace trace,
        int startIdx,
        int endIdx,
        int depth,
        string callType)
    {
        var firstStep = trace.Steps[startIdx];
        var lastStep = trace.Steps[endIdx];

        // Sum gas for this frame only (not nested)
        var gasConsumed = 0UL;
        for (int i = startIdx; i <= endIdx; i++)
        {
            if (trace.Steps[i].Depth == depth)
            {
                gasConsumed += ParseGasCost(trace.Steps[i].GasCost);
            }
        }

        return new FrameFingerprint
        {
            Depth = depth,
            CallType = callType,
            CodeAddress = firstStep.Contract ?? "0x",
            ContextAddress = firstStep.Contract ?? "0x",  // TODO: Extract from DELEGATECALL context
            Caller = "0x01",  // TODO: Extract from execution context
            Value = "0",      // TODO: Extract from CALL value
            GasProvided = 0,  // TODO: Calculate from parent
            GasConsumed = gasConsumed,
            Success = !lastStep.Op.Contains("REVERT"),
            ReturnData = "0x" // TODO: Extract from RETURN/REVERT
        };
    }

    private string DetectCallType(string op)
    {
        return op switch
        {
            "CALL" => "Call",
            "DELEGATECALL" => "DelegateCall",
            "STATICCALL" => "StaticCall",
            "CALLCODE" => "CallCode",
            "CREATE" => "Create",
            "CREATE2" => "Create2",
            _ => "Unknown"
        };
    }

    private AccessFingerprint BuildAccessFingerprint(ExecutionTrace trace)
    {
        var coldAccounts = new HashSet<string>();
        var warmAccounts = new HashSet<string>();
        var coldSlots = new HashSet<string>();
        var warmSlots = new HashSet<string>();

        // TODO: Extract from trace access tracking
        // For now return empty - will populate when access tracking is available

        return new AccessFingerprint
        {
            ColdAccounts = coldAccounts.ToList(),
            WarmAccounts = warmAccounts.ToList(),
            ColdSlots = coldSlots.ToList(),
            WarmSlots = warmSlots.ToList()
        };
    }

    private Dictionary<string, string> BuildStateDiff(IReadOnlyDictionary<string, string>? stateDiff)
    {
        if (stateDiff == null) return new Dictionary<string, string>();
        return new Dictionary<string, string>(stateDiff);
    }

    private List<LogFingerprint> BuildLogs(IReadOnlyList<LogEntry>? logs)
    {
        if (logs == null) return new List<LogFingerprint>();

        return logs.Select(log => new LogFingerprint
        {
            Address = ToHex(log.Address),
            Topics = log.Topics.Select(ToHex).ToList(),
            Data = ToHex(log.Data)
        }).ToList();
    }

    // Parsing utilities
    private static byte[] ParseAddress(string hex)
    {
        var cleaned = hex.StartsWith("0x") ? hex[2..] : hex;
        return Convert.FromHexString(cleaned.PadLeft(40, '0'));
    }

    private static byte[] ParseHex(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex == "0x") return Array.Empty<byte>();
        var cleaned = hex.StartsWith("0x") ? hex[2..] : hex;
        if (cleaned.Length % 2 != 0) cleaned = "0" + cleaned;
        return Convert.FromHexString(cleaned);
    }

    private static UInt256 ParseUInt256(string hex)
    {
        var bytes = ParseHex(hex);
        return UInt256.FromBytes(bytes);
    }

    private static ulong ParseGasCost(string gasCostStr)
    {
        if (ulong.TryParse(gasCostStr, out var gas))
            return gas;
        if (gasCostStr.StartsWith("0x"))
            return Convert.ToUInt64(gasCostStr, 16);
        return 0;
    }

    private static string ToHex(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return "0x";
        return "0x" + Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

// Placeholder types - adjust to match your actual Schlieren.Core types
public interface IEvmExecutor
{
    Task<ExecutionResult> ExecuteAsync(
        ExecutionContext context,
        IWorldState state,
        CancellationToken ct = default);
}

public sealed record ExecutionResult
{
    public required bool Success { get; init; }
    public required ulong GasUsed { get; init; }
    public required byte[] ReturnData { get; init; }
    public required ulong Refund { get; init; }
    public required ExecutionTrace Trace { get; init; }
    public IReadOnlyDictionary<string, string>? StateDiff { get; init; }
    public IReadOnlyList<LogEntry>? Logs { get; init; }
}

public sealed record ExecutionContext
{
    public required byte[] Caller { get; init; }
    public required byte[] Target { get; init; }
    public required byte[] Calldata { get; init; }
    public required ulong Value { get; init; }
    public required ulong GasLimit { get; init; }
    public required ForkRules Fork { get; init; }
    public required ulong BlockNumber { get; init; }
    public required long Timestamp { get; init; }
    public required byte[] Coinbase { get; init; }
    public required ulong Difficulty { get; init; }
    public required ulong GasPrice { get; init; }
}

public sealed record ForkRules
{
    public static ForkRules Berlin => new() { Name = "Berlin" };
    public static ForkRules London => new() { Name = "London" };
    public static ForkRules Shanghai => new() { Name = "Shanghai" };
    public static ForkRules Cancun => new() { Name = "Cancun" };
    public static ForkRules Prague => new() { Name = "Prague" };
    
    public required string Name { get; init; }
}

public interface IWorldState
{
    void SetCode(byte[] address, byte[] code);
    void SetBalance(byte[] address, UInt256 balance);
    void SetNonce(byte[] address, ulong nonce);
    void SetStorage(byte[] address, UInt256 slot, UInt256 value);
}

public sealed class MemoryWorldState : IWorldState
{
    // TODO: Implement actual state storage
    public void SetCode(byte[] address, byte[] code) { }
    public void SetBalance(byte[] address, UInt256 balance) { }
    public void SetNonce(byte[] address, ulong nonce) { }
    public void SetStorage(byte[] address, UInt256 slot, UInt256 value) { }
}

public sealed record ExecutionTrace
{
    public required List<TraceStep> Steps { get; init; }
}

public sealed record TraceStep
{
    public required int Depth { get; init; }
    public required string Op { get; init; }
    public required string GasCost { get; init; }
    public string? Contract { get; init; }
}

public sealed record LogEntry
{
    public required byte[] Address { get; init; }
    public required List<byte[]> Topics { get; init; }
    public required byte[] Data { get; init; }
}

public struct UInt256
{
    public static UInt256 FromBytes(byte[] bytes) => default;
}
