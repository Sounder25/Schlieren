using System.Numerics;
using System.Text.Json.Serialization;
using Scrutor.Core.Primitives;

namespace Scrutor.Core.Models;

/// <summary>
/// Ethereum Transaction Receipt Model
/// </summary>
public sealed class TransactionReceipt
{
    [JsonPropertyName("transactionHash")]
    public string TransactionHash { get; set; } = string.Empty;

    [JsonPropertyName("transactionIndex")]
    public ulong TransactionIndex { get; set; }

    [JsonPropertyName("blockHash")]
    public string BlockHash { get; set; } = string.Empty;

    [JsonPropertyName("blockNumber")]
    public ulong BlockNumber { get; set; }

    [JsonPropertyName("from")]
    public string From { get; set; } = string.Empty;

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("cumulativeGasUsed")]
    public ulong CumulativeGasUsed { get; set; }

    [JsonPropertyName("gasUsed")]
    public ulong GasUsed { get; set; }

    [JsonPropertyName("contractAddress")]
    public string? ContractAddress { get; set; }

    [JsonPropertyName("logs")]
    public List<TransactionLog> Logs { get; set; } = new();

    [JsonPropertyName("logsBloom")]
    public string LogsBloom { get; set; } = "0x" + new string('0', 512);

    [JsonPropertyName("status")]
    public ulong Status { get; set; } // 1 for success, 0 for failure

    [JsonPropertyName("effectiveGasPrice")]
    public BigInteger EffectiveGasPrice { get; set; }
}

/// <summary>
/// Ethereum Event Log Model
/// </summary>
public sealed class TransactionLog
{
    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("topics")]
    public List<string> Topics { get; set; } = new();

    [JsonPropertyName("data")]
    public string Data { get; set; } = string.Empty;

    [JsonPropertyName("blockNumber")]
    public ulong BlockNumber { get; set; }

    [JsonPropertyName("transactionHash")]
    public string TransactionHash { get; set; } = string.Empty;

    [JsonPropertyName("transactionIndex")]
    public ulong TransactionIndex { get; set; }

    [JsonPropertyName("blockHash")]
    public string BlockHash { get; set; } = string.Empty;

    [JsonPropertyName("logIndex")]
    public ulong LogIndex { get; set; }

    [JsonPropertyName("removed")]
    public bool Removed { get; set; } = false;
}
