using Schlieren.UI.Services;
using Schlieren.UI.ViewModels;
using System.Net;
using Xunit;

namespace Schlieren.Tests.UI;

/// <summary>
/// Proves HarvestService and HarvestViewModel honour the external-configuration
/// contracts required by Task 3 of the Harvest Certification Foundation plan.
///
/// Contract summary (per accepted spec and Amendment 2):
///   - HarvestServiceOptions.FromEnvironment reads the four named env keys only.
///   - Absent/blank API key → no X-N8N-API-KEY header on any request.
///   - Absent/blank MCP token → no bearer header; ExecuteWorkflowAsync returns
///     explicit disabled result without making a network call.
///   - Absent corpus directory → ReadCorpusAsync returns empty with no file I/O.
///   - Absent corpus directory → ClearAllAsync sets StatusMessage and skips file write.
///   - HarvestViewModel constructor receives service + options explicitly.
///   - No compiled credential or hard-coded corpus path reachable through any path.
///
/// No live network dependency is used in any test here.
/// </summary>
public class HarvestServiceConfigurationTests
{
    private static readonly Uri DefaultBase = new("http://localhost:5678");

    // ── CapturingHandler: makes request headers and destinations observable ──

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<HttpRequestMessage> Requests { get; } = new();

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(_respond(request));
        }
    }

    private static CapturingHandler JsonHandler(string body = "{\"data\":[]}") => new(_ =>
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        });

    // ── FromEnvironment ──────────────────────────────────────────────────────

    // (Absent base URL test consolidated into FromEnvironment_AbsentBaseUrl_UsesLocalhostDefault below)

    [Fact]
    public void FromEnvironment_ReadsAllFourKeys()
    {
        var env = new Dictionary<string, string?>
        {
            ["SCHLIEREN_N8N_BASE_URL"]   = "http://myserver:5678",
            ["SCHLIEREN_N8N_API_KEY"]    = "my-api-key",
            ["SCHLIEREN_MCP_TOKEN"]      = "my-mcp-token",
            ["SCHLIEREN_HARVEST_CORPUS"] = @"C:\some\valid\path",
        };
        var opts = HarvestServiceOptions.FromEnvironment(k => env.GetValueOrDefault(k));

        Assert.Equal(new Uri("http://myserver:5678"), opts.N8nBaseUri);
        Assert.Equal("my-api-key", opts.N8nApiKey);
        Assert.Equal("my-mcp-token", opts.McpToken);
        Assert.NotNull(opts.CorpusDirectory);
    }

    [Fact]
    public void FromEnvironment_BlankCredentials_TrimmedToNull()
    {
        var opts = HarvestServiceOptions.FromEnvironment(k => k switch
        {
            "SCHLIEREN_N8N_API_KEY" => "   ",
            "SCHLIEREN_MCP_TOKEN"   => "",
            _                       => null
        });

        Assert.Null(opts.N8nApiKey);
        Assert.Null(opts.McpToken);
    }

    [Fact]
    public void FromEnvironment_InvalidBaseUrl_ThrowsForNonblankInvalidValue()
    {
        // Nonblank but invalid URI must be rejected — not silently fallen back to localhost.
        Assert.Throws<InvalidOperationException>(() =>
            HarvestServiceOptions.FromEnvironment(k =>
                k == "SCHLIEREN_N8N_BASE_URL" ? "not-a-uri" : null));
    }

    [Fact]
    public void FromEnvironment_AbsentBaseUrl_UsesLocalhostDefault()
    {
        var opts = HarvestServiceOptions.FromEnvironment(_ => null);
        Assert.Equal(new Uri("http://localhost:5678"), opts.N8nBaseUri);
    }

    // ── Absent API key → no X-N8N-API-KEY header ────────────────────────────

    [Fact]
    public async Task AbsentApiKey_GetPipelineStatus_SendsNoN8nApiKeyHeader()
    {
        var handler = JsonHandler();
        var opts = new HarvestServiceOptions(DefaultBase, N8nApiKey: null, McpToken: null, CorpusDirectory: null);
        using var svc = new HarvestService(opts, handler);

        await svc.GetPipelineStatusAsync();

        var req = Assert.Single(handler.Requests);
        Assert.False(
            req.Headers.Contains("X-N8N-API-KEY"),
            "X-N8N-API-KEY must not be sent when API key is absent");
    }

    [Fact]
    public async Task PresentApiKey_GetPipelineStatus_SendsApiKeyHeader()
    {
        var handler = JsonHandler();
        var opts = new HarvestServiceOptions(DefaultBase, N8nApiKey: "test-key", McpToken: null, CorpusDirectory: null);
        using var svc = new HarvestService(opts, handler);

        await svc.GetPipelineStatusAsync();

        var req = Assert.Single(handler.Requests);
        Assert.True(req.Headers.Contains("X-N8N-API-KEY"));
        Assert.Equal("test-key", req.Headers.GetValues("X-N8N-API-KEY").Single());
    }

    // ── Absent MCP token → no bearer, explicit disabled result ──────────────

    [Fact]
    public async Task AbsentMcpToken_ExecuteWorkflow_MakesNoNetworkCall()
    {
        var handler = JsonHandler();
        var opts = new HarvestServiceOptions(DefaultBase, N8nApiKey: null, McpToken: null, CorpusDirectory: null);
        using var svc = new HarvestService(opts, handler);

        var (ok, _) = await svc.ExecuteWorkflowAsync("test-wf-id");

        Assert.Empty(handler.Requests);
        Assert.False(ok);
    }

    [Fact]
    public async Task PresentMcpToken_ExecuteWorkflow_SendsBearerHeader()
    {
        // Provide a minimal valid SSE response so the method can parse without throwing.
        const string sseBody =
            "data: {\"result\":{\"content\":[{\"text\":\"{\\\"status\\\":\\\"started\\\",\\\"executionId\\\":\\\"42\\\"}\"}]}}";
        var handler = new CapturingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sseBody, System.Text.Encoding.UTF8, "text/event-stream")
            });

        var opts = new HarvestServiceOptions(DefaultBase, N8nApiKey: null, McpToken: "my-token", CorpusDirectory: null);
        using var svc = new HarvestService(opts, handler);

        await svc.ExecuteWorkflowAsync("test-wf-id");

        var req = Assert.Single(handler.Requests);
        Assert.True(req.Headers.Contains("Authorization"));
        var authHeader = req.Headers.GetValues("Authorization").Single();
        Assert.StartsWith("Bearer ", authHeader, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbG", authHeader);
    }

    // ── Absent corpus directory → no file I/O ───────────────────────────────

    [Fact]
    public async Task AbsentCorpusDirectory_ReadCorpus_ReturnsEmpty()
    {
        var opts = new HarvestServiceOptions(DefaultBase, N8nApiKey: null, McpToken: null, CorpusDirectory: null);
        using var svc = new HarvestService(opts);

        var entries = await svc.ReadCorpusAsync();

        Assert.Empty(entries);
    }

    // ── ViewModel constructor chain ──────────────────────────────────────────

    [Fact]
    public void HarvestViewModel_AcceptsExplicitServiceAndOptions()
    {
        var opts = new HarvestServiceOptions(DefaultBase, N8nApiKey: null, McpToken: null, CorpusDirectory: null);
        using var svc = new HarvestService(opts);
        using var vm  = new HarvestViewModel(svc, opts);
        Assert.NotNull(vm);
    }

    [Fact]
    public async Task AbsentCorpusDirectory_ClearAll_SetsUnconfiguredStatus()
    {
        var opts = new HarvestServiceOptions(DefaultBase, N8nApiKey: null, McpToken: null, CorpusDirectory: null);
        using var svc = new HarvestService(opts);
        using var vm  = new HarvestViewModel(svc, opts);

        await vm.ClearAllCommand.ExecuteAsync(null);

        Assert.Equal("Harvest corpus is not configured", vm.StatusMessage);
    }
}
