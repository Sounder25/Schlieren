using Schlieren.RPC.Models;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Schlieren.RPC.Middleware;

/// <summary>
/// Global exception handler for JSON-RPC requests
/// Catches unhandled exceptions and returns proper JSON-RPC error responses
/// </summary>
public class JsonRpcExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<JsonRpcExceptionMiddleware> _logger;

    public JsonRpcExceptionMiddleware(RequestDelegate next, ILogger<JsonRpcExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in RPC pipeline");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 200; // JSON-RPC always returns 200

        // The full exception is already logged server-side (see InvokeAsync). Putting ex.Message
        // into the client-facing response can leak internal details (file paths, type names,
        // connection strings) to whoever sent the request — keep the response generic.
        var errorResponse = new JsonRpcResponse
        {
            JsonRpc = "2.0",
            Id = null,
            Error = new JsonRpcError
            {
                Code = JsonRpcErrorCodes.InternalError,
                Message = "Internal server error"
            }
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse));
    }
}
