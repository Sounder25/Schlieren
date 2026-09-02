using System.Net.Http;
using System.Text;
using System.Text.Json;
using Schlieren.Core.Security;
using Schlieren.RPC.Models;

namespace Schlieren.RPC.Handlers;

internal sealed class OpSecHandlers
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public object HandleStatus() => new
    {
        enabled = OpSecGate.IsLocked,
        allowed = new[]
        {
            "loopback RPC",
            "local Schlieren execution",
            "local fixtures/files",
            "local EELS",
            "local exports"
        },
        blocked = new[]
        {
            "public RPC providers",
            "remote eth_getCode",
            "n8n/cloud workflows",
            "external HTTP fetches"
        }
    };

    public object HandleSet(object[] parameters)
    {
        var obj = RequireObject(parameters);
        if (!obj.TryGetProperty("enabled", out var enabled) ||
            enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "'enabled' must be a boolean");
        OpSecGate.SetLocked(enabled.GetBoolean());
        return HandleStatus();
    }

    public async Task<object> HandleImportCode(object[] parameters, CancellationToken ct)
    {
        var obj = RequireObject(parameters);
        var address = ReadString(obj, "address");
        var provider = ReadString(obj, "provider");
        OpSecGate.AssertRemoteAllowed("eth_getCode", provider);

        using var content = new StringContent(
            JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "eth_getCode",
                @params = new object[] { address, "latest" }
            }),
            Encoding.UTF8,
            "application/json");
        using var response = await Http.PostAsync(provider, content, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.TryGetProperty("error", out var error))
            throw new RpcException(JsonRpcErrorCodes.InternalError, error.GetProperty("message").GetString() ?? "eth_getCode failed");
        var code = doc.RootElement.GetProperty("result").GetString() ?? "0x";
        return new { address, code, provider };
    }

    private static JsonElement RequireObject(object[] parameters)
    {
        if (parameters is null || parameters.Length != 1 ||
            parameters[0] is not JsonElement element ||
            element.ValueKind != JsonValueKind.Object)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Expected one request object");
        return element;
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"'{name}' must be a string");
        return property.GetString()
            ?? throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"'{name}' must be a string");
    }
}
