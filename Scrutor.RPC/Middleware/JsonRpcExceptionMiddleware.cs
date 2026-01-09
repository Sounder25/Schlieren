using Scrutor.RPC.Models;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Scrutor.RPC.Middleware;

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

        var errorResponse = new JsonRpcResponse
        {
            JsonRpc = "2.0",
            Id = null,
            Error = new JsonRpcError
            {
                Code = JsonRpcErrorCodes.InternalError,
                Message = "Internal server error",
                Data = ex.Message
            }
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse));
    }
}
