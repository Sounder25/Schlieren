using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Schlieren.Tests.Campaigns;

/// <summary>
/// Execution harness interface - campaign knows nothing about UI/Workbench.
/// Clean boundary between campaign framework and EVM engine.
/// </summary>
public interface IEvmExecutionHarness
{
    Task<CampaignExecutionResult> ExecuteAsync(
        CampaignExecutionRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Campaign execution request - everything needed to reproduce an execution.
/// </summary>
public sealed record CampaignExecutionRequest
{
    public required string Fork { get; init; }
    
    // Transaction parameters
    public required string Caller { get; init; }
    public required string Target { get; init; }
    public string Calldata { get; init; } = "0x";
    public ulong Value { get; init; }
    public ulong GasLimit { get; init; } = 10_000_000;
    
    // Pre-state accounts
    public required IReadOnlyList<CampaignAccount> Prestate { get; init; }
}

/// <summary>
/// Account state for campaign execution.
/// </summary>
public sealed record CampaignAccount
{
    public required string Address { get; init; }
    public string Code { get; init; } = "0x";
    public string Balance { get; init; } = "0x0";
    public ulong Nonce { get; init; }
    public IReadOnlyDictionary<string, string> Storage { get; init; }
        = new Dictionary<string, string>();
}

/// <summary>
/// Campaign execution result - normalized output from any EVM.
/// </summary>
public sealed record CampaignExecutionResult
{
    public required bool Success { get; init; }
    public required ulong GasUsed { get; init; }
    public required string ReturnData { get; init; }
    public required ExecutionFingerprint Fingerprint { get; init; }
    
    /// <summary>Raw execution result from engine (for debugging).</summary>
    public required Core.Execution.ExecutionResult RawTrace { get; init; }
}

/// <summary>
/// Deterministic addresses for reproducible tests.
/// </summary>
public static class DeterministicAddresses
{
    public const string Caller = "0x0000000000000000000000000000000000000001";
    public const string Parent = "0x00000000000000000000000000000000000000aa";
    public const string Child = "0x00000000000000000000000000000000000000bb";
    public const string Grandchild = "0x00000000000000000000000000000000000000cc";
}
