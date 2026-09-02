using System.Net.Http;
using System.Text.Json;
using Schlieren.Core.Forking;
using Schlieren.Core.Primitives;
using Schlieren.Guard;
using Schlieren.RPC.Models;

namespace Schlieren.RPC.Handlers;

/// <summary>
/// Handles schlieren_guard RPC method.
/// Accepts { token, rpc, block? } — runs the full Guard buy→sell loop
/// against pinned mainnet state and returns the serialized GuardReport.
/// </summary>
public sealed class GuardHandlers
{
    private readonly IHttpClientFactory _httpClientFactory;

    public GuardHandlers(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// schlieren_guard({ token: "0x...", rpc: "https://...", block?: number })
    /// Returns the full WorkbenchEvidence JSON bundle.
    /// </summary>
    public async Task<object> HandleGuard(object[] parameters, CancellationToken ct = default)
    {
        if (parameters is null || parameters.Length == 0)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "schlieren_guard requires a params object: { token, rpc, block? }");

        var raw = parameters[0];
        string? tokenHex;
        string? rpcUrl;
        ulong? blockNumber = null;

        if (raw is JsonElement je)
        {
            tokenHex = je.TryGetProperty("token", out var t) ? t.GetString() : null;
            rpcUrl = je.TryGetProperty("rpc", out var r) ? r.GetString() : null;
            if (je.TryGetProperty("block", out var b))
            {
                if (b.ValueKind == JsonValueKind.Number && b.TryGetUInt64(out var bn))
                    blockNumber = bn;
                else if (b.ValueKind == JsonValueKind.String && b.GetString() is { } bs &&
                         bs != "latest" &&
                         ulong.TryParse(bs.StartsWith("0x") ? bs[2..] : bs,
                             bs.StartsWith("0x")
                                 ? System.Globalization.NumberStyles.HexNumber
                                 : System.Globalization.NumberStyles.Integer,
                             null, out var bsp))
                    blockNumber = bsp;
            }
        }
        else
        {
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "schlieren_guard params[0] must be a JSON object.");
        }

        if (string.IsNullOrWhiteSpace(tokenHex))
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing required field: token");
        if (string.IsNullOrWhiteSpace(rpcUrl) || rpcUrl == "/rpc")
            rpcUrl = "http://localhost:8545"; // Default: use the tunneled Reth node (SSM port-forward)

        // Normalize: add http:// if no scheme present
        if (!rpcUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !rpcUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            rpcUrl = "http://" + rpcUrl;

        Address token;
        try { token = Address.FromHex(tokenHex); }
        catch { throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"Invalid token address: {tokenHex}"); }

        // Build a one-off ForkProvider pointed at the caller-supplied RPC endpoint.
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(rpcUrl);
        client.Timeout = TimeSpan.FromSeconds(30);

        var cache = new BlockCache();
        var forkProvider = new ForkProvider(client, cache);
        var checker = new TokenRiskChecker(forkProvider, "Osaka");

        GuardReport report;
        try
        {
            report = await checker.EvaluateUniswapV2Async(token, blockNumber, ct: ct);
        }
        catch (Exception ex)
        {
            throw new RpcException(JsonRpcErrorCodes.InternalError, $"Guard evaluation failed: {ex.Message}");
        }

        // Return the WorkbenchEvidence bundle as a raw JSON object so the router
        // serializes it cleanly.  Parse back through JsonDocument to avoid double-encoding.
        var bundle = WorkbenchEvidence.WriteBundle(report);
        return JsonDocument.Parse(bundle).RootElement;
    }
}
