using System.Text.Json;
using Schlieren.EELS.Tests.Conformance;
using Schlieren.EELS.Tests.Harness;
using Schlieren.RPC.Models;

namespace Schlieren.RPC.Handlers;

internal sealed class ConformanceFailureDto
{
    public required string CaseId { get; init; }
    public required string FixturePath { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> Mismatches { get; init; }
    public required string PrimaryCategory { get; init; }
    public required string EipCluster { get; init; }
    public required string ClusterKey { get; init; }
    public required string Layer1Headline { get; init; }
    public required string Layer1Body { get; init; }
    public required ulong GasUsed { get; init; }
}

internal sealed class ConformanceClusterDto
{
    public required string Key { get; init; }
    public required string PrimaryCategory { get; init; }
    public required string EipCluster { get; init; }
    public required int Count { get; init; }
}

/// <summary>
/// In-process EELS state-test suite for the React Conformance view.
/// Does not parse fixtures into journal requests — Open in Workbench reads the file
/// and the React adapter produces LoadedFixture.
/// </summary>
internal sealed class ConformanceHandlers
{
    internal static readonly string[] SupportedForks =
    [
        "Prague", "Cancun", "Osaka", "Shanghai", "Paris", "London",
        "Berlin", "Istanbul", "Byzantium", "Homestead", "Frontier"
    ];

    private readonly object _gate = new();
    private ConformanceRun? _run;

    public object HandlePrepare(object[] parameters)
    {
        var req = ParseRequest(parameters);
        var root = ResolveRoot(req);
        var files = root is null ? 0 : CountJson(root, req.ExcludePortedStatic);
        return new
        {
            valid = root != null,
            resolvedRoot = root ?? "",
            fileCount = files,
            forks = SupportedForks
        };
    }

    public object HandleStart(object[] parameters)
    {
        var req = ParseRequest(parameters);
        var root = ResolveRoot(req)
            ?? throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Fixture folder not found");

        lock (_gate)
        {
            _run?.Cts.Cancel();
            var run = new ConformanceRun
            {
                Id = Guid.NewGuid().ToString("n")[..12],
                Fork = req.Fork,
                Root = root
            };
            _run = run;
            run.Task = Task.Run(() => ExecuteAsync(run, req), run.Cts.Token);
            return new { runId = run.Id };
        }
    }

    public object HandlePoll(object[] parameters)
    {
        var runId = ReadString(ParseObject(parameters), "runId", optional: true);
        lock (_gate)
        {
            var run = _run;
            if (run is null || (!string.IsNullOrEmpty(runId) && run.Id != runId))
            {
                return new
                {
                    found = false,
                    done = true,
                    passed = 0,
                    failed = 0,
                    total = 0,
                    currentCase = "",
                    status = "No active run",
                    failures = Array.Empty<ConformanceFailureDto>(),
                    clusters = Array.Empty<ConformanceClusterDto>()
                };
            }

            return new
            {
                found = true,
                runId = run.Id,
                done = run.Done,
                cancelled = run.Cancelled,
                passed = run.Passed,
                failed = run.Failed,
                total = run.Total,
                currentCase = run.CurrentCase,
                status = run.Status,
                failures = run.Failures.ToArray(),
                clusters = BuildClusters(run.Failures)
            };
        }
    }

    public object HandleCancel(object[] parameters)
    {
        var runId = ReadString(ParseObject(parameters), "runId", optional: true);
        lock (_gate)
        {
            if (_run is null) return new { cancelled = false };
            if (!string.IsNullOrEmpty(runId) && _run.Id != runId) return new { cancelled = false };
            _run.Cts.Cancel();
            _run.Cancelled = true;
            _run.Status = "Cancelled";
            return new { cancelled = true, runId = _run.Id };
        }
    }

    public object HandleReadFixture(object[] parameters)
    {
        var obj = ParseObject(parameters);
        var path = ReadString(obj, "path", optional: false)
            ?? throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing 'path'");
        if (!File.Exists(path))
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Fixture file not found");
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Fixture path must be a .json file");
        var info = new FileInfo(path);
        if (info.Length > 10 * 1024 * 1024)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "File exceeds 10 MB limit");
        return new
        {
            path,
            name = Path.GetFileName(path),
            text = File.ReadAllText(path)
        };
    }

    private async Task ExecuteAsync(ConformanceRun run, ConformanceRequest req)
    {
        try
        {
            Update(run, r => r.Status = "Loading fixtures…");
            var loader = new EelsStateFixtureLoader();
            var executor = new EelsStateFixtureExecutor();
            var options = new EelsHarnessOptions(
                FixturesRoot: run.Root,
                ForkName: req.Fork,
                MaxCases: req.MaxCases ?? int.MaxValue,
                IncludeSubdirectories: true,
                ExcludeFolder: req.ExcludePortedStatic ? "ported_static" : null);

            var cases = await Task.Run(() => loader.LoadCases(options).ToList(), run.Cts.Token);
            Update(run, r =>
            {
                r.Total = cases.Count;
                r.Status = $"Loaded {cases.Count:N0} cases — starting {req.Fork}…";
            });

            var semaphore = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount));
            var tasks = cases.Select(async testCase =>
            {
                await semaphore.WaitAsync(run.Cts.Token);
                try
                {
                    run.Cts.Token.ThrowIfCancellationRequested();
                    Update(run, r => r.CurrentCase = testCase.CaseId);
                    var report = await executor.ExecuteAsync(testCase, run.Cts.Token);
                    var ok = report.StateMatches && report.ReceiptStatusMatches;
                    Update(run, r =>
                    {
                        if (ok) r.Passed++;
                        else r.Failed++;
                        if (!ok && r.Failures.Count < 500)
                            r.Failures.Add(ToFailure(testCase, report));
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Update(run, r =>
                    {
                        r.Failed++;
                        if (r.Failures.Count < 500)
                        {
                            r.Failures.Add(new ConformanceFailureDto
                            {
                                CaseId = testCase.CaseId,
                                FixturePath = testCase.FixturePath ?? "",
                                Summary = "executor crashed: " + ex.Message,
                                Mismatches = [ex.ToString()],
                                PrimaryCategory = "other",
                                EipCluster = ExtractEipCluster(testCase.FixturePath),
                                ClusterKey = "other · " + ExtractEipCluster(testCase.FixturePath),
                                Layer1Headline = "",
                                Layer1Body = "",
                                GasUsed = 0
                            });
                        }
                    });
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            Update(run, r =>
            {
                r.Done = true;
                r.Status = r.Failed == 0
                    ? $"100% ({r.Passed:N0} / {r.Total:N0})"
                    : $"{r.Passed:N0} / {r.Total:N0} passed ({r.Failed:N0} failures)";
            });
        }
        catch (OperationCanceledException)
        {
            Update(run, r =>
            {
                r.Done = true;
                r.Cancelled = true;
                r.Status = "Cancelled";
            });
        }
        catch (Exception ex)
        {
            Update(run, r =>
            {
                r.Done = true;
                r.Status = "Error: " + ex.Message;
            });
        }
    }

    private static ConformanceFailureDto ToFailure(EelsStateCase testCase, EelsCaseExecutionReport report)
    {
        var mismatches = report.Mismatches?.ToList() ?? [];
        var bundle = Layer1DiagnosisBridge.DiagnoseCausal(testCase, report);
        var eip = ExtractEipCluster(testCase.FixturePath);
        var primary = string.IsNullOrEmpty(bundle.Title) ? "other" : bundle.Title;
        var cluster = string.IsNullOrEmpty(bundle.Fingerprint) || bundle.Fingerprint == "none"
            ? $"{primary} · {eip}"
            : bundle.Fingerprint;
        return new ConformanceFailureDto
        {
            CaseId = testCase.CaseId,
            FixturePath = testCase.FixturePath ?? "",
            Summary = mismatches.Count == 0 ? "state/receipt mismatch" : string.Join("; ", mismatches.Take(2)),
            Mismatches = mismatches,
            PrimaryCategory = primary,
            EipCluster = $"{testCase.ForkName} · {bundle.Phase} · {bundle.RuleId}",
            ClusterKey = cluster,
            Layer1Headline = bundle.Grade + (string.IsNullOrEmpty(bundle.Title) ? "" : " · " + bundle.Title),
            Layer1Body = bundle.InspectorBody ?? "",
            GasUsed = report.GasUsed
        };
    }

    private static ConformanceClusterDto[] BuildClusters(IReadOnlyList<ConformanceFailureDto> failures) =>
        failures
            .GroupBy(f => f.ClusterKey, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => new ConformanceClusterDto
            {
                Key = g.Key,
                PrimaryCategory = g.First().PrimaryCategory,
                EipCluster = g.First().EipCluster,
                Count = g.Count()
            })
            .ToArray();

    private void Update(ConformanceRun run, Action<ConformanceRun> mutate)
    {
        lock (_gate) mutate(run);
    }

    internal static string? ResolveRoot(ConformanceRequest req)
    {
        if (!string.IsNullOrWhiteSpace(req.FixturesRoot) && Directory.Exists(req.FixturesRoot))
            return Path.GetFullPath(req.FixturesRoot);

        var basePath = req.FixturesBasePath;
        if (string.IsNullOrWhiteSpace(basePath))
            basePath = Environment.GetEnvironmentVariable("EELS_FIXTURES_ROOT") is { Length: > 0 } env
                ? env
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "state_tests"));

        var fork = req.Fork.ToLowerInvariant();
        var modern = Path.Combine(basePath, "for_" + fork);
        if (Directory.Exists(modern)) return Path.GetFullPath(modern);
        var legacy = Path.Combine(basePath, fork);
        return Directory.Exists(legacy) ? Path.GetFullPath(legacy) : null;
    }

    private static int CountJson(string root, bool excludePortedStatic) =>
        Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Count(path => !excludePortedStatic ||
                           !path.Replace('\\', '/').Contains("/ported_static/", StringComparison.OrdinalIgnoreCase));

    internal static string ExtractEipCluster(string? fixturePath)
    {
        if (string.IsNullOrWhiteSpace(fixturePath)) return "unknown";
        var parts = fixturePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith("eip", StringComparison.OrdinalIgnoreCase))
                return part;
        }
        return parts.Length >= 2 ? parts[^2] : parts[^1];
    }

    private static ConformanceRequest ParseRequest(object[] parameters)
    {
        var obj = ParseObject(parameters);
        var fork = ReadString(obj, "fork", optional: false)
            ?? throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Missing 'fork'");
        return new ConformanceRequest(
            fork,
            ReadString(obj, "fixturesBasePath", optional: true),
            ReadString(obj, "fixturesRoot", optional: true),
            ReadBool(obj, "excludePortedStatic", true),
            ReadInt(obj, "maxCases"));
    }

    private static JsonElement ParseObject(object[] parameters)
    {
        if (parameters is null || parameters.Length != 1 ||
            parameters[0] is not JsonElement element ||
            element.ValueKind != JsonValueKind.Object)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Expected one request object");
        return element;
    }

    private static string? ReadString(JsonElement element, string name, bool optional)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
            return optional ? null : throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"Missing '{name}'");
        if (property.ValueKind != JsonValueKind.String)
            throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"'{name}' must be a string");
        return property.GetString();
    }

    private static bool ReadBool(JsonElement element, string name, bool fallback)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
            return fallback;
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"'{name}' must be a boolean")
        };
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var n))
            return n;
        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out n))
            return n;
        throw new RpcException(JsonRpcErrorCodes.InvalidParams, $"'{name}' must be an integer");
    }

    internal sealed record ConformanceRequest(
        string Fork,
        string? FixturesBasePath,
        string? FixturesRoot,
        bool ExcludePortedStatic,
        int? MaxCases);

    private sealed class ConformanceRun
    {
        public required string Id { get; init; }
        public required string Fork { get; init; }
        public required string Root { get; init; }
        public CancellationTokenSource Cts { get; } = new();
        public Task? Task { get; set; }
        public bool Done { get; set; }
        public bool Cancelled { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int Total { get; set; }
        public string CurrentCase { get; set; } = "";
        public string Status { get; set; } = "Starting";
        public List<ConformanceFailureDto> Failures { get; } = [];
    }
}
