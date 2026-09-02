using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Schlieren.Core.Forking;
using Schlieren.Core.Primitives;
using Schlieren.Guard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureLogging(logging => logging.AddConsole());
builder.Services.AddHttpClient();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();
app.UseCors();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

var clientFactory = app.Services.GetRequiredService<IHttpClientFactory>();

// WebSocket endpoint for persistent Guard sessions
app.MapGet("/ws", async (HttpContext context, ILogger<Program> logger) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    using var ws = await context.WebSockets.AcceptWebSocketAsync();
    logger.LogInformation("[WS] Client connected from {IP}", context.Connection.RemoteIpAddress);

    var buffer = new byte[8192];

    try
    {
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closed", CancellationToken.None);
                break;
            }

            if (result.MessageType != WebSocketMessageType.Text)
                continue;

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);

            // Parse request
            GuardRequest? req;
            try
            {
                req = JsonSerializer.Deserialize<GuardRequest>(json);
                if (req is null || string.IsNullOrEmpty(req.Token))
                {
                    await SendErrorAsync(ws, "Invalid request: missing token", null);
                    continue;
                }
            }
            catch (JsonException ex)
            {
                await SendErrorAsync(ws, $"Invalid JSON: {ex.Message}", null);
                continue;
            }

            var runId = Guid.NewGuid().ToString("N")[..8];
            logger.LogInformation("[WS] Run {RunId}: token={Token}", runId, req.Token);

            try
            {
                // Execute Guard scan
                var rpcUrl = req.Rpc ?? "http://localhost:8545";
                if (!rpcUrl.StartsWith("http"))
                    rpcUrl = "http://" + rpcUrl;

                var httpClient = clientFactory.CreateClient();
                httpClient.BaseAddress = new Uri(rpcUrl);
                httpClient.Timeout = TimeSpan.FromSeconds(60);

                var cache = new BlockCache();
                var forkProvider = new ForkProvider(httpClient, cache);
                var checker = new TokenRiskChecker(forkProvider, "Osaka");

                var token = Address.FromHex(req.Token);
                var report = await checker.EvaluateUniswapV2Async(token, req.Block, ct: CancellationToken.None);

                var bundle = WorkbenchEvidence.WriteBundle(report);
                var response = new
                {
                    runId,
                    success = true,
                    result = JsonDocument.Parse(bundle).RootElement
                };

                await ws.SendAsync(
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response)),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[WS] Run {RunId} failed", runId);
                await SendErrorAsync(ws, ex.Message, runId);
            }
        }
    }
    catch (WebSocketException ex)
    {
        logger.LogError(ex, "[WS] WebSocket error");
    }

    logger.LogInformation("[WS] Client disconnected");
});

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.Run();

static async Task SendErrorAsync(WebSocket ws, string message, string? runId)
{
    var error = JsonSerializer.Serialize(new { runId, success = false, error = message });
    await ws.SendAsync(
        Encoding.UTF8.GetBytes(error),
        WebSocketMessageType.Text,
        true,
        CancellationToken.None);
}

record GuardRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("token")] string? Token,
    [property: System.Text.Json.Serialization.JsonPropertyName("rpc")] string? Rpc,
    [property: System.Text.Json.Serialization.JsonPropertyName("block")] ulong? Block
);
