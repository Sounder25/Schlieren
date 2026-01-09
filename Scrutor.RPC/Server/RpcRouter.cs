using Scrutor.RPC.Handlers;
using Scrutor.RPC.Models;
using System.Text.Json;
using Scrutor.RPC;
using Microsoft.Extensions.Logging;

namespace Scrutor.RPC.Server;

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
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
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
            "eth_call",
            "eth_getBlockByNumber",
            "eth_getTransactionByHash",
            "eth_getTransactionReceipt",
            "eth_getLogs",
            "evm_increaseTime",
            "anvil_setNextBlockTimestamp",
            "evm_setNextBlockTimestamp",
            "evm_snapshot",
            "evm_revert"
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
            "eth_sendRawTransaction" => _ethHandlers.HandleSendRawTransaction(parameters),
            "eth_getTransactionCount" => await _ethHandlers.HandleGetTransactionCount(parameters, ct),
            "eth_call" => await _ethHandlers.HandleEthCall(parameters, ct),
            "eth_getBlockByNumber" => (object)_ethHandlers.HandleGetBlockByNumber(parameters)!,
            "eth_getTransactionByHash" => (object)_ethHandlers.HandleGetTransactionByHash(parameters)!,
            "eth_getTransactionReceipt" => (object)_ethHandlers.HandleGetTransactionReceipt(parameters)!,
            "eth_getLogs" => (object)_ethHandlers.HandleGetLogs(parameters)!,
            "eth_sendTransaction" => await _ethHandlers.HandleSendTransaction(parameters, ct),
            "anvil_setBalance" => _ethHandlers.HandleAnvilSetBalance(parameters),
            "anvil_setNonce" => _ethHandlers.HandleAnvilSetNonce(parameters),
            "anvil_setCode" => _ethHandlers.HandleAnvilSetCode(parameters),
            "anvil_setStorageAt" => _ethHandlers.HandleAnvilSetStorageAt(parameters),
            "anvil_mine" => await _ethHandlers.HandleAnvilMine(parameters),
            "anvil_impersonateAccount" => _ethHandlers.HandleAnvilImpersonateAccount(parameters),
            "anvil_stopImpersonatingAccount" => _ethHandlers.HandleAnvilStopImpersonatingAccount(parameters),
            "anvil_showPrivateKey" => _ethHandlers.HandleAnvilShowPrivateKey(parameters),
            "anvil_showMnemonic" => _ethHandlers.HandleAnvilShowMnemonic(),
            "evm_impersonateAccount" => _ethHandlers.HandleAnvilImpersonateAccount(parameters),
            "evm_stopImpersonatingAccount" => _ethHandlers.HandleAnvilStopImpersonatingAccount(parameters),
            "evm_increaseTime" => _ethHandlers.HandleEvmIncreaseTime(parameters),
            "evm_mine" => await _ethHandlers.HandleAnvilMine(parameters),
            "evm_snapshot" => _ethHandlers.HandleEvmSnapshot(parameters),
            "evm_revert" => _ethHandlers.HandleEvmRevert(parameters),
            "evm_setNextBlockTimestamp" => _ethHandlers.HandleAnvilSetNextBlockTimestamp(parameters),
            
            // Method not found
            _ => throw new RpcException(JsonRpcErrorCodes.MethodNotFound, $"Method not found: {method}")
        };
    }

    private string CreateSuccessResponse(object? id, object? result)
    {
        var response = new JsonRpcResponse
        {
            Id = id,
            Result = result,
            Error = null
        };

        return JsonSerializer.Serialize(response, _jsonOptions);
    }

    private string CreateErrorResponse(object? id, int errorCode, string message)
    {
        var response = new JsonRpcResponse
        {
            Id = id,
            Result = null,
            Error = new JsonRpcError
            {
                Code = errorCode,
                Message = message
            }
        };

        return JsonSerializer.Serialize(response, _jsonOptions);
    }
}
