using System.Numerics;
using System.Text.Json.Serialization;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;

namespace Scrutor.Core.Models;

/// <summary>
/// Canonical Block Header Model (Shared Interface)
/// Satisfies: Execution Context (Lane 1) and RPC Serialization (Lane 2/3)
/// </summary>
public class BlockHeader
{
    [JsonPropertyName("number")]
    public ulong Number { get; set; }

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("parentHash")]
    public string ParentHash { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public ulong Timestamp { get; set; }

    [JsonPropertyName("stateRoot")]
    public string StateRoot { get; set; } = string.Empty;

    [JsonPropertyName("transactionsRoot")]
    public string TransactionsRoot { get; set; } = string.Empty;

    [JsonPropertyName("receiptsRoot")]
    public string ReceiptsRoot { get; set; } = string.Empty;

    [JsonPropertyName("miner")]
    public string Miner { get; set; } = string.Empty;

    [JsonPropertyName("difficulty")]
    public BigInteger Difficulty { get; set; }

    [JsonPropertyName("gasLimit")]
    public ulong GasLimit { get; set; }

    [JsonPropertyName("gasUsed")]
    public ulong GasUsed { get; set; }
    
    [JsonPropertyName("baseFeePerGas")]
    public ulong BaseFeePerGas { get; set; }
}

/// <summary>
/// Canonical Block Model
/// </summary>
public class Block : BlockHeader
{
    [JsonPropertyName("transactions")]
    public List<Transaction> Transactions { get; set; } = new();
}
