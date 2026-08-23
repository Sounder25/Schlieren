using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Schlieren.UI.ViewModels;

namespace Schlieren.UI.Services;

/// <summary>
/// Talks to n8n via the public REST API (X-N8N-API-KEY).
/// Base: http://localhost:5678/api/v1
/// </summary>
public sealed class HarvestService : IDisposable
{
    private const string N8nBase   = "http://localhost:5678";
    private const string N8nApiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJjNDc0ODZhOS1iZDNiLTQ2N2UtOTI3NC1jODczODI5ZGFjOTEiLCJpc3MiOiJuOG4iLCJhdWQiOiJwdWJsaWMtYXBpIiwianRpIjoiNTBmNzE4YTAtNjhjMy00Y2I0LWI3NTMtMzQyYjgxZDlhOTliIiwiaWF0IjoxNzg3MTIyNzcwLCJleHAiOjE3ODk3MDc2MDB9.TFFmx9336vQR2LE9diMHMC_4RQlEPPravpTx0UsIdzw";
    private const string McpToken  = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJjNDc0ODZhOS1iZDNiLTQ2N2UtOTI3NC1jODczODI5ZGFjOTEiLCJpc3MiOiJuOG4iLCJhdWQiOiJtY3Atc2VydmVyLWFwaSIsImp0aSI6IjllYzM0MjZlLWFmMDYtNGQ4OC1iNjhjLTRmNmZiOTUwZjA1NyIsImlhdCI6MTc4NzExNjM1MX0.isubWPXKR2uuB0xrba4S76SKG6RRHsm0Sz4a5xbzMFE";

    public const string WfAId = "A1b2c3d4E5f6G7h8";
    public const string WfBId = "B1c2d3e4F5g6H7i8";

    private const string CorpusDir = @"C:\projects\Schlieren\muscle\corpus";

    private readonly HttpClient _http;

    public HarvestService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Add("X-N8N-API-KEY", N8nApiKey);
    }

    // ─── Pipeline status ──────────────────────────────────────────────────

    public async Task<(bool wfAActive, bool wfBActive)> GetPipelineStatusAsync()
    {
        try
        {
            var list = await _http.GetFromJsonAsync<N8nWorkflowList>($"{N8nBase}/api/v1/workflows");
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

    public async Task<(bool ok, string? executionId)> ExecuteWorkflowAsync(string workflowId)
    {
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
            var req  = new HttpRequestMessage(HttpMethod.Post, $"{N8nBase}/mcp-server/http");
            req.Headers.Add("Authorization", $"Bearer {McpToken}");
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
                $"{N8nBase}/api/v1/executions?workflowId={workflowId}&limit=1");

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

    public async Task<List<HarvestEntry>> ReadCorpusAsync(int maxEntries = 500)
    {
        var entries = new List<HarvestEntry>();

        // Primary: read harvest_index.json written by harvester.py
        var indexFile = Path.Combine(CorpusDir, "harvest_index.json");
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
        if (!Directory.Exists(CorpusDir)) return entries;

        var files = Directory.GetFiles(CorpusDir, "*.json", SearchOption.TopDirectoryOnly)
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
