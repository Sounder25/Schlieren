using System.Text.Json.Serialization;

namespace Scrutor.RPC.Models;

/// <summary>
/// JSON-RPC 2.0 Request envelope
/// </summary>
public sealed class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public object[]? Params { get; set; }
}

/// <summary>
/// JSON-RPC 2.0 Response envelope
/// </summary>
public sealed class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("result")]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; set; }
}

/// <summary>
/// JSON-RPC 2.0 Error object
/// </summary>
public sealed class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

/// <summary>
/// Standard JSON-RPC error codes
/// </summary>
public static class JsonRpcErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;

    // Custom Ethereum Errors
    public const int ExecutionError = 3;
}

/// <summary>
/// Ethereum hex-encoded data types
/// </summary>
public static class EthereumTypes
{
    /// <summary>
    /// Converts a uint to Ethereum hex format (0x-prefixed)
    /// </summary>
    public static string ToEthHex(ulong value)
    {
        return $"0x{value:x}";
    }

    /// <summary>
    /// Converts a BigInteger to Ethereum hex format (0x-prefixed)
    /// </summary>
    public static string ToEthHex(System.Numerics.BigInteger value)
    {
        if (value.Sign < 0) throw new ArgumentException("Negative values not supported for Ethereum hex", nameof(value));
        return $"0x{value:x}";
    }

    /// <summary>
    /// Converts a byte array to Ethereum hex format (0x-prefixed)
    /// </summary>
    public static string ToEthHex(byte[] data)
    {
        return "0x" + Convert.ToHexString(data).ToLowerInvariant();
    }

    /// <summary>
    /// Converts Ethereum hex string to ulong
    /// </summary>
    public static ulong FromEthHex(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            throw new ArgumentException("Hex string cannot be null or empty", nameof(hex));

        var cleanHex = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) 
            ? hex[2..] 
            : hex;

        return Convert.ToUInt64(cleanHex, 16);
    }

    /// <summary>
    /// Validates Ethereum address format (0x + 40 hex chars)
    /// </summary>
    public static bool IsValidAddress(string address)
    {
        if (string.IsNullOrEmpty(address))
            return false;

        if (!address.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return false;

        if (address.Length != 42)
            return false;

        return address[2..].All(c => "0123456789abcdefABCDEF".Contains(c));
    }

    /// <summary>
    /// Normalizes Ethereum address to lowercase with 0x prefix
    /// </summary>
    public static string NormalizeAddress(string address)
    {
        if (!IsValidAddress(address))
            throw new ArgumentException("Invalid Ethereum address format", nameof(address));

        return address.ToLowerInvariant();
    }
}
