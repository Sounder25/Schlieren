using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Schlieren.UI.ViewModels;

namespace Schlieren.UI.Services;

/// <summary>
/// Talks to n8n via the public REST API (X-N8N-API-KEY).
/// Base: configured via SCHLIEREN_N8N_BASE_URL (default http://localhost:5678)
///
/// Credentials are supplied exclusively through HarvestServiceOptions loaded
/// from environment variables at the application composition root. No credential
/// or corpus path has a compiled default.
/// </summary>
public sealed class HarvestService : IDisposable
{
    public const string WfAId = "A1b2c3d4E5f6G7h8";
    public const string WfBId = "B1c2d3e4F5g6H7i8";

    private readonly HarvestServiceOptions _options;
    private readonly HttpClient _http;

    /// <summary>
    /// Primary constructor. Accepts an optional <paramref name="handler"/> so
    /// tests can observe request headers without live network access.
    /// </summary>
    public HarvestService(HarvestServiceOptions options, HttpMessageHandler? handler = null)
    {
        _options = options;
        _http = handler is not null
            ? new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) }
            : new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        // Send the API key on every request when configured; omit the header entirely when absent.
        if (!string.IsNullOrEmpty(options.N8nApiKey))
            _http.DefaultRequestHeaders.Add("X-N8N-API-KEY", options.N8nApiKey);
    }

    // ─── Pipeline status ──────────────────────────────────────────────────

    public async Task<(bool wfAActive, bool wfBActive)> GetPipelineStatusAsync()
    {
        try
        {
            var list = await _http.GetFromJsonAsync<N8nWorkflowList>(
                $"{_options.N8nBaseUri}api/v1/workflows");
            if (list?.Data is null) return (false, false);

            bool a = false, b = false;
            foreach (var wf in list.Data)
            {
                if (wf.Id == WfAId) a = wf.Active;
                if (wf.Id == WfBId) b = wf.Active;
            }
            return (a, b);
        }
        catch { return (false, false); }
    }

    // ─── Execute workflow via MCP ─────────────────────────────────────────

    /// <summary>
    /// Executes a workflow via MCP. When <c>McpToken</c> is absent, returns an
    /// explicit disabled result without making any network call, per the Task 3
    /// contract: absent token → no bearer header, explicit disabled result.
    /// </summary>
    public async Task<(bool ok, string? executionId)> ExecuteWorkflowAsync(string workflowId)
    {
        if (string.IsNullOrEmpty(_options.McpToken))
            return (false, null);

        try
        {
            var payload = new
            {
                jsonrpc = "2.0",
                method  = "tools/call",
                id      = 1,
                @params = new
                {
                    name      = "execute_workflow",
                    arguments = new { workflowId, executionMode = "manual" }
                }
            };

            var body = System.Text.Json.JsonSerializer.Serialize(payload);
            var req  = new HttpRequestMessage(HttpMethod.Post, $"{_options.N8nBaseUri}mcp-server/http");
            req.Headers.Add("Authorization", $"Bearer {_options.McpToken}");
            req.Headers.Add("Accept", "application/json, text/event-stream");
            req.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

            var resp = await _http.SendAsync(req);
            var raw  = await resp.Content.ReadAsStringAsync();

            // SSE response: "data: {...}"
            var dataLine = raw.Split('\n').FirstOrDefault(l => l.StartsWith("data: "));
            if (dataLine is null) return (false, null);

            using var outer = JsonDocument.Parse(dataLine["data: ".Length..]);
            var text = outer.RootElement
                            .GetProperty("result")
                            .GetProperty("content")[0]
                            .GetProperty("text")
                            .GetString() ?? "{}";

            using var inner = JsonDocument.Parse(text);
            var status = inner.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
            var execId = inner.RootElement.TryGetProperty("executionId", out var e) ? e.GetString() : null;

            return (status == "started", execId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MCP execute error: {ex.Message}");
            return (false, null);
        }
    }

    // ─── Last run text ────────────────────────────────────────────────────

    public async Task<string?> GetLastRunTextAsync(string workflowId)
    {
        try
        {
            var list = await _http.GetFromJsonAsync<N8nExecutionList>(
                $"{_options.N8nBaseUri}api/v1/executions?workflowId={workflowId}&limit=1");

            var exec = list?.Data?.FirstOrDefault();
            if (exec is null) return "never";

            if (DateTime.TryParse(exec.StartedAt, out var dt))
            {
                var diff = DateTime.UtcNow - dt.ToUniversalTime();
                if (diff.TotalSeconds < 90)  return "just now";
                if (diff.TotalMinutes < 60)  return $"{(int)diff.TotalMinutes} min ago";
                if (diff.TotalHours < 24)    return $"{(int)diff.TotalHours} hr ago";
                return $"{(int)diff.TotalDays}d ago";
            }
            return null;
        }
        catch { return null; }
    }

    // ─── Corpus reader ────────────────────────────────────────────────────

    /// <summary>
    /// Reads the harvest corpus. When <c>CorpusDirectory</c> is absent (not
    /// configured), returns an empty list with no file I/O, per the Task 3
    /// contract: absent corpus path → no fallback read/write.
    /// </summary>
    public async Task<List<HarvestEntry>> ReadCorpusAsync(int maxEntries = 500)
    {
        var entries = new List<HarvestEntry>();

        if (string.IsNullOrEmpty(_options.CorpusDirectory))
            return entries;

        // Primary: read harvest_index.json written by harvester.py
        var indexFile = Path.Combine(_options.CorpusDirectory, "harvest_index.json");
        if (File.Exists(indexFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(indexFile);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("candidates", out var candidates))
                {
                    foreach (var c in candidates.EnumerateArray().Take(maxEntries))
                    {
                        var txHash = c.GetStr("txHash");
                        if (string.IsNullOrEmpty(txHash)) continue;

                        entries.Add(new HarvestEntry
                        {
                            TxHash        = txHash,
                            BlockNumber   = c.GetLng("blockNumber"),
                            Fork          = c.GetStr("fork"),
                            CandidateType = c.GetStr("candidateType"),
                            Outcome       = "DISCOVERED",
                            GasMainnet    = c.GetLng("gasLimit"),
                            GasSchlieren  = 0,
                            InputData     = c.GetStr("inputData"),
                            FromAddress   = c.GetStr("fromAddress"),
                            ToAddress     = c.GetStr("toAddress"),
                            PriorityScore = (int)c.GetLng("priorityScore"),
                            ContractName  = c.GetStrNull("contractName"),
                            IsVerified    = c.GetBoolNull("isVerified"),
                            Deployer      = c.GetStrNull("deployer"),
                            DeployedAt    = c.GetStrNull("deployedAt"),
                            DeployedBlock = c.GetLng("deployedBlock") is > 0 and var db ? db : null,
                            FunctionName  = c.GetStrNull("functionName"),
                            BlockDate     = c.GetStr("blockDate"),
                            ValueEth      = c.GetDbl("valueEth"),
                            FixturePath   = "",
                            HarvestedAt   = DateTime.TryParse(c.GetStr("discoveredAt"), out var dt)
                                            ? dt : DateTime.UtcNow
                        });
                    }
                }
                return entries;
            }
            catch { }
        }

        // Fallback: scan corpus directory for individual fixture files
        if (!Directory.Exists(_options.CorpusDirectory)) return entries;

        var files = Directory.GetFiles(_options.CorpusDirectory, "*.json", SearchOption.TopDirectoryOnly)
                             .Where(f => !f.EndsWith("_state.json") && !f.EndsWith("harvest_index.json"))
                             .OrderByDescending(File.GetLastWriteTimeUtc)
                             .Take(maxEntries);

        foreach (var file in files)
        {
            try
            {
                var json  = await File.ReadAllTextAsync(file);
                var entry = ParseCorpusFile(json, file);
                if (entry is not null) entries.Add(entry);
            }
            catch { }
        }

        return entries;
    }

    private static HarvestEntry? ParseCorpusFile(string json, string filePath)
    {
        using var doc  = JsonDocument.Parse(json);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!prop.Value.TryGetProperty("_provenance", out var prov)) continue;

            var txHash  = prov.GetStr("txHash");
            if (string.IsNullOrEmpty(txHash)) continue;

            DateTime.TryParse(prov.GetStr("harvestedAt"), out var harvestedAt);

            return new HarvestEntry
            {
                TxHash        = txHash,
                BlockNumber   = prov.GetLng("blockNumberDec"),
                Fork          = prov.GetStr("fork"),
                CandidateType = prov.GetStr("candidateType"),
                Outcome       = prov.GetStr("schlierenOutcome") is { Length: > 0 } o ? o : "FIXTURE_WRITTEN",
                GasMainnet    = prov.GetLng("gasUsedMainnet"),
                GasSchlieren  = prov.GetLng("gasUsedSchlieren"),
                FixturePath   = filePath,
                HarvestedAt   = harvestedAt == default
                                ? File.GetLastWriteTimeUtc(filePath)
                                : harvestedAt
            };
        }
        return null;
    }

    public void Dispose() => _http.Dispose();

    // ─── DTOs ─────────────────────────────────────────────────────────────

    private sealed class N8nWorkflowList
    {
        [JsonPropertyName("data")] public List<N8nWorkflow>? Data { get; set; }
    }
    private sealed class N8nWorkflow
    {
        [JsonPropertyName("id")]     public string Id     { get; set; } = "";
        [JsonPropertyName("active")] public bool   Active { get; set; }
    }
    private sealed class N8nExecutionList
    {
        [JsonPropertyName("data")] public List<N8nExecution>? Data { get; set; }
    }
    private sealed class N8nExecution
    {
        [JsonPropertyName("startedAt")] public string? StartedAt { get; set; }
        [JsonPropertyName("status")]    public string? Status    { get; set; }
    }
}

file static class JsonElementEx
{
    public static string GetStr(this JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString() ?? "";
        return "";
    }

    public static string? GetStrNull(this JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v))
        {
            if (v.ValueKind == JsonValueKind.String) return v.GetString();
            if (v.ValueKind == JsonValueKind.Null)   return null;
        }
        return null;
    }

    public static long GetLng(this JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s)) return s;
        return 0;
    }

    public static double GetDbl(this JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        return 0;
    }

    public static bool? GetBoolNull(this JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.True)  return true;
        if (v.ValueKind == JsonValueKind.False) return false;
        return null;
    }
}
