using System.Diagnostics;
using System.Text.Json;

namespace Schlieren.PerfHarness;

/// <summary>
/// Standalone performance harness for Guard latency characterization.
/// Executes controlled Guard runs against an RPC endpoint and captures
/// structured timing data per §7.5 of the campaign plan.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help")
        {
            PrintUsage();
            return 0;
        }

        var cmd = args[0];
        return cmd switch
        {
            "scan" => await RunScanAsync(args[1..]),
            "campaign" => await RunCampaignAsync(args[1..]),
            "validate" => ValidateManifest(args[1..]),
            _ => Fail($"Unknown command: {cmd}")
        };
    }

    static void PrintUsage()
    {
        Console.WriteLine("Schlieren Guard Performance Harness");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  scan     Run a single Guard check with timing");
        Console.WriteLine("  campaign Run a full performance campaign");
        Console.WriteLine("  validate Validate manifest without running");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  scan --rpc http://localhost:8545 --token 0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48");
        Console.WriteLine("  campaign --manifest manifests/guard-perf-v1.json --rpc http://localhost:8545");
        Console.WriteLine("  validate --manifest manifests/guard-perf-v1.json");
    }

    static int Fail(string message)
    {
        Console.Error.WriteLine($"ERROR: {message}");
        return 1;
    }

    // ── scan ───────────────────────────────────────────────────────────────

    static async Task<int> RunScanAsync(string[] args)
    {
        string? rpc = null;
        string? token = null;
        string? output = null;
        int warmup = 0;
        int repeat = 1;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--rpc":
                    rpc = args[++i];
                    break;
                case "--token":
                    token = args[++i];
                    break;
                case "--out":
                    output = args[++i];
                    break;
                case "--warmup":
                    warmup = int.Parse(args[++i]);
                    break;
                case "--repeat":
                    repeat = int.Parse(args[++i]);
                    break;
            }
        }

        if (string.IsNullOrEmpty(rpc) || string.IsNullOrEmpty(token))
            return Fail("--rpc and --token are required");

        var results = new List<ScanResult>();

        // Warm-up
        for (int w = 0; w < warmup; w++)
        {
            Console.WriteLine($"[WARMUP {w + 1}/{warmup}]");
            var _ = await ExecuteGuardRpcAsync(rpc, token, $"warmup-{w}");
        }

        // Measured runs
        for (int r = 0; r < repeat; r++)
        {
            Console.WriteLine($"[RUN {r + 1}/{repeat}]");
            var result = await ExecuteGuardRpcAsync(rpc, token, $"run-{r}");
            results.Add(result);
        }

        // Output
        var json = JsonSerializer.Serialize(new { results }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, json);
            Console.WriteLine($"Results written to {output}");
        }
        else
        {
            Console.WriteLine(json);
        }

        // Summary
        var times = results.Select(r => r.TotalMs).ToList();
        Console.WriteLine();
        Console.WriteLine($"SUMMARY ({results.Count} runs)");
        Console.WriteLine($"  P50:  {Percentile(times, 50):F1} ms");
        Console.WriteLine($"  P95:  {Percentile(times, 95):F1} ms");
        Console.WriteLine($"  P99:  {Percentile(times, 99):F1} ms");
        Console.WriteLine($"  Mean: {times.Average():F1} ms");
        Console.WriteLine($"  Min:  {times.Min():F1} ms");
        Console.WriteLine($"  Max:  {times.Max():F1} ms");

        return results.Any(r => r.Error != null) ? 2 : 0;
    }

    static async Task<ScanResult> ExecuteGuardRpcAsync(string rpcUrl, string token, string runId)
    {
        var sw = Stopwatch.StartNew();
        var result = new ScanResult
        {
            RunId = runId,
            Token = token,
            RpcUrl = rpcUrl,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        };

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            var request = new
            {
                jsonrpc = "2.0",
                id = Guid.NewGuid(),
                method = "schlieren_guard",
                @params = new[] { new { token, rpc = rpcUrl } }
            };

            var json = JsonSerializer.Serialize(request);
            result.RequestBytes = json.Length;

            var httpStart = sw.ElapsedMilliseconds;

            var response = await http.PostAsync(rpcUrl,
                new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

            var httpEnd = sw.ElapsedMilliseconds;
            result.DnsTcpTlsMs = httpStart; // coarse approximation
            result.RequestTxMs = httpEnd - httpStart;

            var body = await response.Content.ReadAsStringAsync();
            var responseEnd = sw.ElapsedMilliseconds;

            result.ResponseBytes = body.Length;
            result.ResponseRxMs = responseEnd - httpEnd;
            result.TotalMs = responseEnd;

            if (!response.IsSuccessStatusCode)
            {
                result.Error = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
                return result;
            }

            // Parse to check for RPC error
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                result.Error = err.GetProperty("message").GetString();
            }
            else if (doc.RootElement.TryGetProperty("result", out var res))
            {
                // Extract verdict if present
                if (res.TryGetProperty("verdict", out var verdict))
                {
                    result.Outcome = verdict.TryGetProperty("kind", out var kind)
                        ? kind.GetString()
                        : "unknown";
                }
            }
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            result.TotalMs = sw.ElapsedMilliseconds;
        }

        return result;
    }

    // ── campaign ─────────────────────────────────────────────────────────────

    static async Task<int> RunCampaignAsync(string[] args)
    {
        string? manifestPath = null;
        string? rpc = null;
        string? outputDir = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--manifest":
                    manifestPath = args[++i];
                    break;
                case "--rpc":
                    rpc = args[++i];
                    break;
                case "--out":
                    outputDir = args[++i];
                    break;
            }
        }

        if (string.IsNullOrEmpty(manifestPath) || string.IsNullOrEmpty(rpc))
            return Fail("--manifest and --rpc are required");

        if (!File.Exists(manifestPath))
            return Fail($"Manifest not found: {manifestPath}");

        var manifestJson = await File.ReadAllTextAsync(manifestPath);
        using var doc = JsonDocument.Parse(manifestJson);
        var cases = doc.RootElement.GetProperty("cases");

        outputDir ??= $"artifacts/guard-performance/{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        Directory.CreateDirectory(outputDir);

        Console.WriteLine($"CAMPAIGN STARTED");
        Console.WriteLine($"  Manifest: {manifestPath}");
        Console.WriteLine($"  Cases: {cases.GetArrayLength()}");
        Console.WriteLine($"  RPC: {rpc}");
        Console.WriteLine($"  Output: {outputDir}");
        Console.WriteLine();

        var allResults = new List<object>();

        foreach (var c in cases.EnumerateArray())
        {
            var caseId = c.GetProperty("case_id").GetString();
            var token = c.GetProperty("token").GetString();

            Console.WriteLine($"[{caseId}] {token}");

            // Run 5 repetitions per case (per §10.1)
            for (int rep = 0; rep < 5; rep++)
            {
                var result = await ExecuteGuardRpcAsync(rpc, token!, $"{caseId}-{rep}");
                allResults.Add(new { case_id = caseId, repetition = rep, result });
                Console.WriteLine($"  rep {rep + 1}: {result.TotalMs:F0}ms {result.Outcome ?? result.Error}");
            }
        }

        var summaryPath = Path.Combine(outputDir, "campaign-results.json");
        await File.WriteAllTextAsync(summaryPath,
            JsonSerializer.Serialize(new { completed = DateTime.UtcNow, results = allResults },
                new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine();
        Console.WriteLine($"CAMPAIGN COMPLETE");
        Console.WriteLine($"  Results: {summaryPath}");

        return 0;
    }

    // ── validate ────────────────────────────────────────────────────────────

    static int ValidateManifest(string[] args)
    {
        string? manifestPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--manifest")
                manifestPath = args[++i];
        }

        if (string.IsNullOrEmpty(manifestPath))
            return Fail("--manifest required");

        if (!File.Exists(manifestPath))
            return Fail($"Manifest not found: {manifestPath}");

        try
        {
            var json = File.ReadAllText(manifestPath);
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            var cases = root.GetProperty("cases");

            Console.WriteLine($"MANIFEST VALID");
            Console.WriteLine($"  Manifest ID: {root.GetProperty("manifest_id").GetString()}");
            Console.WriteLine($"  Campaign ID:  {root.GetProperty("campaign_id").GetString()}");
            Console.WriteLine($"  Cases:        {cases.GetArrayLength()}");
            Console.WriteLine();

            foreach (var c in cases.EnumerateArray())
            {
                var caseId = c.GetProperty("case_id").GetString();
                var token = c.GetProperty("token").GetString();
                var label = c.GetProperty("label").GetString();
                Console.WriteLine($"  [{caseId}] {label}");
                Console.WriteLine($"    Token: {token}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            return Fail($"Invalid manifest: {ex.Message}");
        }
    }

    // ── utils ────────────────────────────────────────────────────────────────

    static double Percentile(List<long> values, int percentile)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        var idx = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        idx = Math.Max(0, Math.Min(idx, sorted.Count - 1));
        return sorted[idx];
    }
}

class ScanResult
{
    public string RunId { get; set; } = "";
    public string Token { get; set; } = "";
    public string RpcUrl { get; set; } = "";
    public string TimestampUtc { get; set; } = "";
    public long DnsTcpTlsMs { get; set; }
    public long RequestTxMs { get; set; }
    public long ResponseRxMs { get; set; }
    public long TotalMs { get; set; }
    public int RequestBytes { get; set; }
    public int ResponseBytes { get; set; }
    public string? Outcome { get; set; }
    public string? Error { get; set; }
}
