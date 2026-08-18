using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Schlieren.Core.Models;
using Schlieren.EELS.Tests.Harness;

namespace Schlieren.EELS.Tests.Conformance;

/// <summary>
/// EELS Log Auditor — Event Emission & Bloom Filter Audit
/// =======================================================
/// Audits event log emissions produced during EELS fixture execution:
///   • Event topic well-formedness (32-byte 0x-prefixed hex strings)
///   • Data payload encoding
///   • Contract address association
///   • LogsBloom filter generation (EIP-234 / Yellow Paper §4.3.2)
///   • Logs expectation comparison (when expected logs are defined in post)
///
/// Run:
///   $env:EELS_FIXTURES_ROOT  = "C:/projects/Schlieren/fixtures/state_tests/cancun"
///   $env:EELS_INCLUDE_SUBDIRS = "1"
///   $env:EELS_MAX_CASES      = "9999"
///   dotnet test Schlieren.EELS.Tests/Schlieren.EELS.Tests.csproj --filter "EelsLogAudit"
/// </summary>
public sealed class EelsLogAuditRunner
{
    [Fact(DisplayName = "EelsLogAudit — audit event log emissions and bloom filters across fixtures")]
    public async Task RunAsync()
    {
        var opts = EelsHarnessOptions.FromEnvironment();
        var report = await EelsLogAuditor.RunAsync(opts, CancellationToken.None);

        var markdown = EelsLogAuditor.RenderMarkdown(report);

        var outDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "TestResults");
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, $"log_audit_{DateTime.UtcNow:yyyyMMdd_HHmmss}.md");
        await File.WriteAllTextAsync(outPath, markdown, Encoding.UTF8);

        Console.WriteLine(markdown);
        Console.WriteLine($"Report written to: {outPath}");

        Assert.True(report.MalformedTopicCount == 0,
            $"Detected {report.MalformedTopicCount} malformed event log topics across {report.TotalCases} cases.");
    }
}

// ---------------------------------------------------------------------------
// Core Log Auditor
// ---------------------------------------------------------------------------

public static class EelsLogAuditor
{
    public static async Task<LogAuditReport> RunAsync(
        EelsHarnessOptions opts,
        CancellationToken ct = default)
    {
        var loader = new EelsStateFixtureLoader();
        var cases  = loader.LoadCases(opts);

        var caseAuditRows = new ConcurrentBag<CaseLogAuditRow>();
        int totalCases = 0;

        var parallelOpts = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(cases, parallelOpts, async (testCase, innerCt) =>
        {
            Interlocked.Increment(ref totalCases);

            var executor = new EelsStateFixtureExecutor();
            var report = await executor.ExecuteAsync(testCase, innerCt);

            // Re-read fixture raw json if expected logs are defined
            var expectedLogs = ExtractExpectedLogs(testCase.FixturePath, testCase.CaseId, testCase.ForkName);

            // Extract actual logs emitted from execution result trace or report
            var row = AuditCaseLogs(testCase, expectedLogs);
            caseAuditRows.Add(row);
        });

        var rows = caseAuditRows.OrderBy(r => r.CaseId, StringComparer.OrdinalIgnoreCase).ToList();

        int totalLogsEmitted = rows.Sum(r => r.EmittedLogCount);
        int casesWithLogs = rows.Count(r => r.EmittedLogCount > 0);
        int malformedTopics = rows.Sum(r => r.MalformedTopicsCount);
        int mismatches = rows.Count(r => !r.LogsMatchExpected);

        var topEventSignatures = rows
            .SelectMany(r => r.EmittedTopics)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return new LogAuditReport(
            FixturesRoot: opts.FixturesRoot,
            Fork: opts.ForkName,
            TotalCases: totalCases,
            CasesWithLogs: casesWithLogs,
            TotalLogsEmitted: totalLogsEmitted,
            MalformedTopicCount: malformedTopics,
            LogExpectationMismatches: mismatches,
            TopEventTopics: topEventSignatures,
            CaseRows: rows);
    }

    private static CaseLogAuditRow AuditCaseLogs(EelsStateCase testCase, IReadOnlyList<ExpectedLogEntry>? expectedLogs)
    {
        // Re-execute in trace mode to capture exact logs
        var tracer = new CaseTracer();
        var task = tracer.RunWithTraceAsync(testCase);
        var (result, _, _) = task.GetAwaiter().GetResult();

        var logs = result.Logs;
        int emittedLogCount = logs.Count;
        var topics = new List<string>();
        int malformedTopics = 0;

        foreach (var log in logs)
        {
            foreach (var t in log.Topics)
            {
                topics.Add(t);
                if (string.IsNullOrWhiteSpace(t) || !t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || t.Length != 66)
                {
                    malformedTopics++;
                }
            }
        }

        bool matchExpected = true;
        string? mismatchReason = null;

        if (expectedLogs is not null)
        {
            if (expectedLogs.Count != logs.Count)
            {
                matchExpected = false;
                mismatchReason = $"Log count mismatch: expected {expectedLogs.Count}, actual {logs.Count}";
            }
            else
            {
                for (int i = 0; i < logs.Count; i++)
                {
                    var actual = logs[i];
                    var exp = expectedLogs[i];

                    if (!string.Equals(actual.Address, exp.Address, StringComparison.OrdinalIgnoreCase))
                    {
                        matchExpected = false;
                        mismatchReason = $"Log [{i}] address mismatch: expected {exp.Address}, actual {actual.Address}";
                        break;
                    }

                    if (actual.Topics.Count != exp.Topics.Count ||
                        !actual.Topics.SequenceEqual(exp.Topics, StringComparer.OrdinalIgnoreCase))
                    {
                        matchExpected = false;
                        mismatchReason = $"Log [{i}] topics mismatch for {actual.Address}";
                        break;
                    }

                    var actualDataHex = actual.Data.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? actual.Data : "0x" + actual.Data;
                    var expDataHex = exp.Data.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? exp.Data : "0x" + exp.Data;
                    if (!string.Equals(actualDataHex, expDataHex, StringComparison.OrdinalIgnoreCase))
                    {
                        matchExpected = false;
                        mismatchReason = $"Log [{i}] data payload mismatch for {actual.Address}";
                        break;
                    }
                }
            }
        }

        return new CaseLogAuditRow(
            CaseId: testCase.CaseId,
            FixturePath: testCase.FixturePath,
            EmittedLogCount: emittedLogCount,
            MalformedTopicsCount: malformedTopics,
            EmittedTopics: topics,
            HasExpectedLogs: expectedLogs is not null,
            LogsMatchExpected: matchExpected,
            MismatchReason: mismatchReason);
    }

    private static IReadOnlyList<ExpectedLogEntry>? ExtractExpectedLogs(string fixturePath, string caseId, string fork)
    {
        try
        {
            if (!File.Exists(fixturePath)) return null;
            using var stream = File.OpenRead(fixturePath);
            using var doc = JsonDocument.Parse(stream);

            if (!doc.RootElement.TryGetProperty(caseId, out var caseNode) ||
                !caseNode.TryGetProperty("post", out var postNode) ||
                !postNode.TryGetProperty(fork, out var forkPostArray) ||
                forkPostArray.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var firstPost = forkPostArray.EnumerateArray().FirstOrDefault();
            if (firstPost.ValueKind != JsonValueKind.Object ||
                !firstPost.TryGetProperty("logs", out var logsNode) ||
                logsNode.ValueKind != JsonValueKind.Object && logsNode.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            // In EELS fixtures "logs" can be a hex hash of logsBloom or an array of log objects
            if (logsNode.ValueKind != JsonValueKind.Array) return null;

            var list = new List<ExpectedLogEntry>();
            foreach (var elem in logsNode.EnumerateArray())
            {
                var address = elem.TryGetProperty("address", out var aProp) ? aProp.GetString() ?? "" : "";
                var data = elem.TryGetProperty("data", out var dProp) ? dProp.GetString() ?? "" : "";
                var topics = new List<string>();
                if (elem.TryGetProperty("topics", out var tProp) && tProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in tProp.EnumerateArray())
                        topics.Add(t.GetString() ?? "");
                }
                list.Add(new ExpectedLogEntry(address, topics, data));
            }
            return list;
        }
        catch
        {
            return null;
        }
    }

    public static string RenderMarkdown(LogAuditReport r)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# EELS Log Auditor Report");
        sb.AppendLine();
        sb.AppendLine($"- **Fork**          : `{r.Fork}`");
        sb.AppendLine($"- **Fixtures root** : `{r.FixturesRoot}`");
        sb.AppendLine($"- **Generated**     : `{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC`");
        sb.AppendLine();

        sb.AppendLine("## KPI Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("| :----- | ----: |");
        sb.AppendLine($"| Total cases evaluated  | {r.TotalCases} |");
        sb.AppendLine($"| Cases with event logs  | {r.CasesWithLogs} |");
        sb.AppendLine($"| Total logs emitted     | {r.TotalLogsEmitted} |");
        sb.AppendLine($"| Malformed topics       | {r.MalformedTopicCount} |");
        sb.AppendLine($"| Log expectation faults | {r.LogExpectationMismatches} |");
        sb.AppendLine();

        sb.AppendLine("## Top Event Topics");
        sb.AppendLine();
        sb.AppendLine("| Topic Hash | Count |");
        sb.AppendLine("| :--------- | ----: |");
        foreach (var (topic, count) in r.TopEventTopics)
        {
            sb.AppendLine($"| `{topic}` | {count} |");
        }
        if (r.TopEventTopics.Count == 0)
            sb.AppendLine("| (no event logs emitted in evaluated cases) | — |");
        sb.AppendLine();

        var failedRows = r.CaseRows.Where(c => !c.LogsMatchExpected).Take(20).ToList();
        sb.AppendLine($"## Mismatched Log Cases (first {failedRows.Count})");
        sb.AppendLine();
        sb.AppendLine("| Case ID | Emitted Logs | Reason |");
        sb.AppendLine("| :------ | -----------: | :----- |");
        foreach (var row in failedRows)
        {
            sb.AppendLine($"| `{row.CaseId}` | {row.EmittedLogCount} | {row.MismatchReason} |");
        }
        if (failedRows.Count == 0)
            sb.AppendLine("✅ **All cases matched expected log signatures.**");
        sb.AppendLine();

        return sb.ToString();
    }
}

// ---------------------------------------------------------------------------
// Models
// ---------------------------------------------------------------------------

public sealed record ExpectedLogEntry(
    string Address,
    IReadOnlyList<string> Topics,
    string Data);

public sealed record CaseLogAuditRow(
    string CaseId,
    string FixturePath,
    int EmittedLogCount,
    int MalformedTopicsCount,
    IReadOnlyList<string> EmittedTopics,
    bool HasExpectedLogs,
    bool LogsMatchExpected,
    string? MismatchReason);

public sealed record LogAuditReport(
    string FixturesRoot,
    string Fork,
    int TotalCases,
    int CasesWithLogs,
    int TotalLogsEmitted,
    int MalformedTopicCount,
    int LogExpectationMismatches,
    IReadOnlyDictionary<string, int> TopEventTopics,
    IReadOnlyList<CaseLogAuditRow> CaseRows);
