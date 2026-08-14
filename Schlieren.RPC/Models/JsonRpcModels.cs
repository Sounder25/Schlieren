using System.Text.Json.Serialization;

namespace Schlieren.RPC.Models;

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
/// JSON-RPC 2.0 Response envelope (error path).
/// Success envelopes are written by <c>RpcRouter.CreateSuccessResponse</c> so that
/// <c>"result":null</c> is always present (Hardhat requires it for pending receipts).
/// </summary>
public sealed class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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
    /// Converts a BigInteger to Ethereum hex format (0x-prefixed, no padded leading zeros).
    /// </summary>
    public static string ToEthHex(System.Numerics.BigInteger value)
    {
        if (value.Sign < 0) throw new ArgumentException("Negative values not supported for Ethereum hex", nameof(value));
        if (value.IsZero) return "0x0";
        // BigInteger format "x" may emit a leading zero nibble for positive values; strip it.
        var hex = value.ToString("x").TrimStart('0');
        if (hex.Length == 0) hex = "0";
        return "0x" + hex;
    }

    /// <summary>
    /// Converts a byte array to Ethereum hex format (0x-prefixed)
    /// </summary>
    public static string ToEthHex(byte[] data)
    {
        return "0x" + Convert.ToHexString(data).ToLowerInvariant();
    }

    /// <summary>
    /// Converts Ethereum hex string to BigInteger (correct for balances, storage, fees).
    /// </summary>
    public static System.Numerics.BigInteger FromEthHexBigInteger(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            throw new ArgumentException("Hex string cannot be null or empty", nameof(hex));

        var cleanHex = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? hex[2..]
            : hex;

        if (cleanHex.Length == 0)
            return System.Numerics.BigInteger.Zero;

        // BigInteger(byte[], isUnsigned, isBigEndian) requires even-length hex → bytes
        if ((cleanHex.Length & 1) == 1)
            cleanHex = "0" + cleanHex;

        var bytes = Convert.FromHexString(cleanHex);
        return new System.Numerics.BigInteger(bytes, isUnsigned: true, isBigEndian: true);
    }

    /// <summary>
    /// Converts Ethereum hex string to ulong (block numbers, nonces, gas limits only).
    /// Throws if the value does not fit in UInt64 — use <see cref="FromEthHexBigInteger"/> for wei amounts.
    /// </summary>
    public static ulong FromEthHex(string hex)
    {
        var value = FromEthHexBigInteger(hex);
        if (value < System.Numerics.BigInteger.Zero || value > ulong.MaxValue)
            throw new OverflowException($"Hex value does not fit in UInt64: {hex}");
        return (ulong)value;
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
