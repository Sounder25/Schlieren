using System.Numerics;
using System.Text.Json;
using Schlieren.Core.Primitives;
using Schlieren.RPC.Models;

namespace Schlieren.RPC.Handlers;

internal sealed record JournalTraceRequest(
    Address From,
    Address To,
    ulong Gas,
    BigInteger GasPrice,
    BigInteger Value,
    byte[] Data,
    byte[]? Code,
    string Fork,
    ulong? Nonce,
    bool DisableStack,
    bool DisableMemory,
    bool DisableStorage);

internal static class JournalTraceRequestParser
{
    public static JournalTraceRequest Parse(object[] parameters, ulong defaultGas)
    {
        if (parameters is null || parameters.Length != 1 ||
            parameters[0] is not JsonElement element ||
            element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("Expected exactly one journal trace request object");
        }

        var code = ReadBytes(element, "code", optional: true);
        var toText = ReadString(element, "to", optional: true);
        if (string.IsNullOrWhiteSpace(toText))
            throw Invalid(code is null ? "Missing 'to' address" : "'to' is required when 'code' is present");

        return new JournalTraceRequest(
            ParseAddress(ReadString(element, "from", optional: true) ?? Address.Zero.ToString(), "from"),
            ParseAddress(toText, "to"),
            ReadQuantity(element, "gas") ?? defaultGas,
            ReadBigQuantity(element, "gasPrice") ?? BigInteger.Zero,
            ReadBigQuantity(element, "value") ?? BigInteger.Zero,
            ReadBytes(element, "data", optional: true) ?? Array.Empty<byte>(),
            code,
            ReadString(element, "fork", optional: true) ?? "Osaka",
            ReadQuantity(element, "nonce"),
            ReadBoolean(element, "disableStack"),
            ReadBoolean(element, "disableMemory"),
            ReadBoolean(element, "disableStorage"));
    }

    private static Address ParseAddress(string value, string name)
    {
        try
        {
            if (!EthereumTypes.IsValidAddress(value))
                throw new FormatException();
            return Address.FromHex(value);
        }
        catch
        {
            throw Invalid($"Invalid '{name}' address");
        }
    }

    private static string? ReadString(JsonElement element, string name, bool optional)
    {
        if (!element.TryGetProperty(name, out var property))
            return optional ? null : throw Invalid($"Missing '{name}'");
        if (property.ValueKind == JsonValueKind.Null && optional)
            return null;
        if (property.ValueKind != JsonValueKind.String)
            throw Invalid($"'{name}' must be a string");
        return property.GetString();
    }

    private static byte[]? ReadBytes(JsonElement element, string name, bool optional)
    {
        var value = ReadString(element, name, optional);
        if (value is null)
            return null;
        var clean = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        if (clean.Length % 2 != 0)
            throw Invalid($"Invalid '{name}' hex");
        try
        {
            return clean.Length == 0 ? Array.Empty<byte>() : Convert.FromHexString(clean);
        }
        catch
        {
            throw Invalid($"Invalid '{name}' hex");
        }
    }

    private static ulong? ReadQuantity(JsonElement element, string name)
    {
        var value = ReadString(element, name, optional: true);
        if (value is null)
            return null;
        try { return EthereumTypes.FromEthHex(value); }
        catch { throw Invalid($"Invalid '{name}' quantity"); }
    }

    private static BigInteger? ReadBigQuantity(JsonElement element, string name)
    {
        var value = ReadString(element, name, optional: true);
        if (value is null)
            return null;
        try { return EthereumTypes.FromEthHexBigInteger(value); }
        catch { throw Invalid($"Invalid '{name}' quantity"); }
    }

    private static bool ReadBoolean(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
            return false;
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Invalid($"'{name}' must be a boolean")
        };
    }

    private static RpcException Invalid(string message) =>
        new(JsonRpcErrorCodes.InvalidParams, message);
}
