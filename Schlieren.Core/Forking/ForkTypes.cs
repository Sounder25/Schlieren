using System.Numerics;
using System.Text.Json.Serialization;
using System.Globalization;
using Schlieren.Core.Models;

namespace Schlieren.Core.Forking;

public class RpcResponse<T>
{
    [JsonPropertyName("result")]
    public T? Result { get; set; }
    
    [JsonPropertyName("error")]
    public object? Error { get; set; }
}

/// <summary>
/// Raw DTO for JSON-RPC Block responses (Hex strings)
/// </summary>
public class ForkBlockDto
{
    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("hash")]
    public string? Hash { get; set; }

    [JsonPropertyName("parentHash")]
    public string? ParentHash { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("difficulty")]
    public string? Difficulty { get; set; }

    [JsonPropertyName("gasLimit")]
    public string? GasLimit { get; set; }

    [JsonPropertyName("baseFeePerGas")]
    public string? BaseFeePerGas { get; set; }

    [JsonPropertyName("miner")]
    public string? Miner { get; set; }

    public Block ToCanonical()
    {
        return new Block
        {
            Number = ParseUlong(Number),
            Hash = Hash ?? string.Empty,
            ParentHash = ParentHash ?? string.Empty,
            Timestamp = ParseUlong(Timestamp),
            Difficulty = ParseBigInt(Difficulty),
            GasLimit = ParseUlong(GasLimit),
            BaseFeePerGas = ParseUlong(BaseFeePerGas),
            Miner = Miner ?? string.Empty
        };
    }

    private static ulong ParseUlong(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return 0;
        var clean = hex.StartsWith("0x") ? hex[2..] : hex;
        return ulong.TryParse(clean, NumberStyles.HexNumber, null, out var val) ? val : 0;
    }

    private static BigInteger ParseBigInt(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return BigInteger.Zero;
        var clean = hex.StartsWith("0x") ? hex[2..] : hex;
        // Prepend 00 to ensure positive BigInteger if high bit set
        return BigInteger.Parse("00" + clean, NumberStyles.HexNumber);
    }
}
