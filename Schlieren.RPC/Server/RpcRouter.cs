using Schlieren.RPC.Handlers;
using Schlieren.RPC.Models;
using System.Linq;
using System.Text.Json;
using Schlieren.RPC;
using Microsoft.Extensions.Logging;

namespace Schlieren.RPC.Server;

/// <summary>
/// Routes JSON-RPC requests to appropriate handlers
/// Thread-safe and optimized for high-concurrency workloads
/// </summary>
public sealed class RpcRouter : IJsonRpcRouter
{
    private readonly EthHandlers _ethHandlers;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<RpcRouter>? _logger;
    private readonly IReadOnlyList<string> _registeredMethods;

    public RpcRouter(EthHandlers ethHandlers, ILogger<RpcRouter>? logger = null)
    {
        _ethHandlers = ethHandlers;
        _logger = logger;
        
        // Do not use WhenWritingNull globally: eth JSON-RPC requires "result": null for
        // pending receipts / missing txs. Error field uses per-property WhenWritingNull.
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        _registeredMethods = new List<string>
        {
            "eth_chainId",
            "eth_blockNumber",
            "eth_getBalance",
            "eth_getCode",
            "eth_getStorageAt",
            "eth_accounts",
            "eth_sendRawTransaction",
            "eth_getTransactionCount",
            "eth_gasPrice",
            "eth_maxPriorityFeePerGas",
            "eth_feeHistory",
            "eth_estimateGas",
            "eth_call",
            "eth_getBlockByNumber",
            "eth_getTransactionByHash",
            "eth_getTransactionReceipt",
            "eth_getLogs",
            "net_version",
            "net_listening",
            "net_peerCount",
            "web3_clientVersion",
            "evm_increaseTime",
            "anvil_getAutomine",
            "anvil_setAutomine",
            "anvil_setNextBlockTimestamp",
            "evm_setNextBlockTimestamp",
            "evm_snapshot",
            "evm_revert",
            "debug_traceTransaction",
            "debug_traceCall",
            "debug_traceBlockByNumber",
            "debug_traceBlockByHash",
            "debug_inspect",
            "debug_whyNot",
            "schlieren_traceJournal"
        }.AsReadOnly();
    }

    /// <summary>
    /// Returns a read-only list of all registered RPC method names.
    /// </summary>
    public IReadOnlyList<string> GetRegisteredMethods() => _registeredMethods;

    /// <summary>
    /// Processes a JSON-RPC request and returns a response
    /// </summary>
    public async Task<string> ProcessRequest(string requestBody, CancellationToken ct = default)
    {
        JsonRpcRequest? request = null;
        object? requestId = null;

        try
        {
            request = JsonSerializer.Deserialize<JsonRpcRequest>(requestBody, _jsonOptions);
            
            if (request == null)
            {
                return CreateErrorResponse(null, JsonRpcErrorCodes.InvalidRequest, "Invalid JSON-RPC request");
            }

            requestId = request.Id;

            if (request.JsonRpc != "2.0")
            {
                return CreateErrorResponse(requestId, JsonRpcErrorCodes.InvalidRequest, "JSON-RPC version must be 2.0");
            }

            if (string.IsNullOrWhiteSpace(request.Method))
            {
                return CreateErrorResponse(requestId, JsonRpcErrorCodes.InvalidRequest, "Method name is required");
            }

            var result = await RouteToHandler(request.Method, request.Params ?? Array.Empty<object>(), ct);
            
            return CreateSuccessResponse(requestId, result);
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "JSON parse error in RPC request");
            return CreateErrorResponse(null, JsonRpcErrorCodes.ParseError, $"JSON parse error: {ex.Message}");
        }
        catch (RpcException ex)
        {
            _logger?.LogInformation("RPC error: {ErrorCode} - {Message}", ex.ErrorCode, ex.Message);
            return CreateErrorResponse(requestId, ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unhandled exception in RPC request processing");
            return CreateErrorResponse(requestId, JsonRpcErrorCodes.InternalError, "Internal server error");
        }
    }

    /// <summary>
    /// Routes method calls to appropriate handlers
    /// </summary>
    private async Task<object?> RouteToHandler(string method, object[] parameters, CancellationToken ct)
    {
        return method switch
        {
            "eth_chainId" => _ethHandlers.HandleChainId(),
            "eth_blockNumber" => _ethHandlers.HandleBlockNumber(),
            "eth_getBalance" => await _ethHandlers.HandleGetBalance(parameters, ct),
            "eth_getCode" => await _ethHandlers.HandleGetCode(parameters, ct),
            "eth_getStorageAt" => await _ethHandlers.HandleGetStorageAt(parameters, ct),
            "eth_accounts" => _ethHandlers.HandleAccounts(),
            "eth_sendRawTransaction" => await _ethHandlers.HandleSendRawTransaction(parameters, ct),
            "eth_getTransactionCount" => await _ethHandlers.HandleGetTransactionCount(parameters, ct),
            "eth_gasPrice" => _ethHandlers.HandleGasPrice(),
            "eth_maxPriorityFeePerGas" => _ethHandlers.HandleMaxPriorityFeePerGas(),
            "eth_feeHistory" => _ethHandlers.HandleFeeHistory(parameters),
            "eth_estimateGas" => await _ethHandlers.HandleEstimateGas(parameters, ct),
            "eth_call" => await _ethHandlers.HandleEthCall(parameters, ct),
            "eth_getBlockByNumber" => (object)_ethHandlers.HandleGetBlockByNumber(parameters)!,
            "eth_getTransactionByHash" => (object)_ethHandlers.HandleGetTransactionByHash(parameters)!,
            "eth_getTransactionReceipt" => (object)_ethHandlers.HandleGetTransactionReceipt(parameters)!,
            "eth_getLogs" => (object)_ethHandlers.HandleGetLogs(parameters).Select(EthHandlers.ToEthLogJson).ToList(),
            "net_version" => _ethHandlers.HandleNetVersion(),
            "net_listening" => _ethHandlers.HandleNetListening(),
            "net_peerCount" => _ethHandlers.HandleNetPeerCount(),
            "web3_clientVersion" => _ethHandlers.HandleWeb3ClientVersion(),
            "eth_sendTransaction" => await _ethHandlers.HandleSendTransaction(parameters, ct),
            "anvil_setBalance" => _ethHandlers.HandleAnvilSetBalance(parameters),
            "anvil_setNonce" => _ethHandlers.HandleAnvilSetNonce(parameters),
            "anvil_setCode" => _ethHandlers.HandleAnvilSetCode(parameters),
            "anvil_setStorageAt" => _ethHandlers.HandleAnvilSetStorageAt(parameters),
            "anvil_mine" => await _ethHandlers.HandleAnvilMine(parameters),
            "anvil_getAutomine" => _ethHandlers.HandleAnvilGetAutomine(),
            "anvil_setAutomine" => _ethHandlers.HandleAnvilSetAutomine(parameters),
            "anvil_impersonateAccount" => _ethHandlers.HandleAnvilImpersonateAccount(parameters),
            "anvil_stopImpersonatingAccount" => _ethHandlers.HandleAnvilStopImpersonatingAccount(parameters),
            "anvil_showPrivateKey" => _ethHandlers.HandleAnvilShowPrivateKey(parameters),
            "anvil_showMnemonic" => _ethHandlers.HandleAnvilShowMnemonic(),
            "evm_impersonateAccount" => _ethHandlers.HandleAnvilImpersonateAccount(parameters),
            "evm_stopImpersonatingAccount" => _ethHandlers.HandleAnvilStopImpersonatingAccount(parameters),
            "evm_setAutomine" => _ethHandlers.HandleAnvilSetAutomine(parameters),
            "evm_increaseTime" => _ethHandlers.HandleEvmIncreaseTime(parameters),
            "evm_mine" => await _ethHandlers.HandleAnvilMine(parameters),
            "evm_snapshot" => _ethHandlers.HandleEvmSnapshot(parameters),
            "evm_revert" => _ethHandlers.HandleEvmRevert(parameters),
            "evm_setNextBlockTimestamp" => _ethHandlers.HandleAnvilSetNextBlockTimestamp(parameters),
            "debug_traceTransaction" => await _ethHandlers.HandleDebugTraceTransaction(parameters, ct),
            "debug_traceCall" => await _ethHandlers.HandleDebugTraceCall(parameters, ct),
            "debug_traceBlockByNumber" => await _ethHandlers.HandleDebugTraceBlockByNumber(parameters, ct),
            "debug_traceBlockByHash" => await _ethHandlers.HandleDebugTraceBlockByHash(parameters, ct),
            "debug_inspect" => await _ethHandlers.HandleDebugInspect(parameters, ct),
            "debug_whyNot" => await _ethHandlers.HandleDebugWhyNot(parameters, ct),
            "schlieren_traceJournal" => await _ethHandlers.HandleTraceJournal(parameters, ct),
            
            // Method not found
            _ => throw new RpcException(JsonRpcErrorCodes.MethodNotFound, $"Method not found: {method}")
        };
    }

    private string CreateSuccessResponse(object? id, object? result)
    {
        // Always include "result", even when null (pending receipt / unknown tx).
        // System.Text.Json omits nulls under WhenWritingNull; build the envelope with Utf8JsonWriter
        // so Hardhat/ethers get a valid JSON-RPC success body.
        using var stream = new System.IO.MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WritePropertyName("id");
            if (id is null)
                writer.WriteNullValue();
            else if (id is System.Text.Json.JsonElement je)
                je.WriteTo(writer);
            else if (id is string s)
                writer.WriteStringValue(s);
            else if (id is long l)
                writer.WriteNumberValue(l);
            else if (id is int i)
                writer.WriteNumberValue(i);
            else if (id is System.Text.Json.Nodes.JsonNode jn)
                jn.WriteTo(writer);
            else
                JsonSerializer.Serialize(writer, id, _jsonOptions);

            writer.WritePropertyName("result");
            if (result is null)
                writer.WriteNullValue();
            else
                JsonSerializer.Serialize(writer, result, _jsonOptions);

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private string CreateErrorResponse(object? id, int errorCode, string message)
    {
        var response = new JsonRpcResponse
        {
            Id = id,
            Error = new JsonRpcError
            {
                Code = errorCode,
                Message = message
            }
        };

        return JsonSerializer.Serialize(response, _jsonOptions);
    }
}
