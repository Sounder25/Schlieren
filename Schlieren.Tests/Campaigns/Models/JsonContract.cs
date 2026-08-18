using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Schlieren.Tests.Campaigns.Models;

/// <summary>
/// Stable JSON contract between Schlieren and oracle harnesses (revm, geth, etc).
/// This schema is version-controlled — breaking changes require migration.
/// </summary>

/// <summary>
/// Execution request sent to oracle harness via stdin.
/// </summary>
public sealed record ExecutionCase
{
    [JsonPropertyName("fork")]
    public required string Fork { get; init; }
    
    [JsonPropertyName("caller")]
    public required string Caller { get; init; }
    
    [JsonPropertyName("target")]
    public required string Target { get; init; }
    
    [JsonPropertyName("calldata")]
    public string Calldata { get; init; } = "0x";
    
    [JsonPropertyName("value")]
    public string Value { get; init; } = "0x0";
    
    [JsonPropertyName("gas_limit")]
    public ulong GasLimit { get; init; } = 10_000_000;
    
    [JsonPropertyName("block_number")]
    public ulong BlockNumber { get; init; } = 1;
    
    [JsonPropertyName("block_timestamp")]
    public ulong BlockTimestamp { get; init; } = 1000;
    
    [JsonPropertyName("block_coinbase")]
    public string BlockCoinbase { get; init; } = "0x0000000000000000000000000000000000000000";
    
    [JsonPropertyName("block_difficulty")]
    public string BlockDifficulty { get; init; } = "0x0";
    
    [JsonPropertyName("block_gas_limit")]
    public ulong BlockGasLimit { get; init; } = 30_000_000;
    
    [JsonPropertyName("block_base_fee")]
    public string BlockBaseFee { get; init; } = "0xa";
    
    [JsonPropertyName("prestate")]
    public required IReadOnlyList<AccountState> Prestate { get; init; }
}

/// <summary>
/// Account state in pre/post execution.
/// </summary>
public sealed record AccountState
{
    [JsonPropertyName("address")]
    public required string Address { get; init; }
    
    [JsonPropertyName("code")]
    public string Code { get; init; } = "0x";
    
    [JsonPropertyName("balance")]
    public string Balance { get; init; } = "0x0";
    
    [JsonPropertyName("nonce")]
    public ulong Nonce { get; init; }
    
    [JsonPropertyName("storage")]
    public IReadOnlyDictionary<string, string> Storage { get; init; }
        = new Dictionary<string, string>();
}

/// <summary>
/// Execution result returned by oracle harness via stdout.
/// </summary>
public sealed record ExecutionResult
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }
    
    [JsonPropertyName("gas_used")]
    public required ulong GasUsed { get; init; }
    
    [JsonPropertyName("refund")]
    public ulong Refund { get; init; }
    
    [JsonPropertyName("return_data")]
    public required string ReturnData { get; init; }
    
    [JsonPropertyName("frames")]
    public required List<ExecutionFrame> Frames { get; init; }
    
    [JsonPropertyName("logs")]
    public required List<ExecutionLog> Logs { get; init; }
    
    [JsonPropertyName("state_diff")]
    public required Dictionary<string, AccountState> StateDiff { get; init; }
    
    [JsonPropertyName("cold_accounts")]
    public List<string> ColdAccounts { get; init; } = new();
    
    [JsonPropertyName("warm_accounts")]
    public List<string> WarmAccounts { get; init; } = new();
    
    [JsonPropertyName("cold_slots")]
    public List<string> ColdSlots { get; init; } = new();
    
    [JsonPropertyName("warm_slots")]
    public List<string> WarmSlots { get; init; } = new();
}

/// <summary>
/// Call frame in execution tree.
/// </summary>
public sealed record ExecutionFrame
{
    [JsonPropertyName("depth")]
    public required int Depth { get; init; }
    
    [JsonPropertyName("call_type")]
    public required string CallType { get; init; }
    
    [JsonPropertyName("code_address")]
    public required string CodeAddress { get; init; }
    
    [JsonPropertyName("context_address")]
    public required string ContextAddress { get; init; }
    
    [JsonPropertyName("caller")]
    public required string Caller { get; init; }
    
    [JsonPropertyName("value")]
    public required string Value { get; init; }
    
    [JsonPropertyName("gas_provided")]
    public required ulong GasProvided { get; init; }
    
    [JsonPropertyName("gas_consumed")]
    public required ulong GasConsumed { get; init; }
    
    [JsonPropertyName("success")]
    public required bool Success { get; init; }
    
    [JsonPropertyName("return_data")]
    public required string ReturnData { get; init; }
}

/// <summary>
/// Event log entry.
/// </summary>
public sealed record ExecutionLog
{
    [JsonPropertyName("address")]
    public required string Address { get; init; }
    
    [JsonPropertyName("topics")]
    public required List<string> Topics { get; init; }
    
    [JsonPropertyName("data")]
    public required string Data { get; init; }
}
