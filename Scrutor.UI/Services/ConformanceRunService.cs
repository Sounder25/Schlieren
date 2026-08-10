using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Scrutor.EELS.Tests.Conformance;
using Scrutor.EELS.Tests.Harness;

namespace Scrutor.UI.Services;

/// <summary>
/// One update fired per test case — drives the live conformance panel.
/// </summary>
public sealed record ConformanceProgress(
    int Passed,
    int Failed,
    int Total,
    string CurrentCase,
    string? FailureDetail,              // null when the case passed (short summary)
    IReadOnlyList<string>? Mismatches,  // full mismatch list when failed
    string? FixturePath,
    ulong GasUsed,
    long GasRefundCounter,
    string PrimaryCategory,
    string EipCluster,
    string ClusterKey);

/// <summary>
/// Runs EELS state-test fixtures directly inside the UI process,
/// firing IProgress callbacks so the panel updates in real time.
/// </summary>
public static class ConformanceRunService
{
    public static readonly IReadOnlyList<string> SupportedForks = new[]
    {
        "Prague", "Cancun", "Osaka",
        "Shanghai", "Paris", "London",
        "Berlin", "Istanbul", "Byzantium",
        "Homestead", "Frontier"
    };

    /// <summary>
    /// Discover fixture root for a given fork under a base fixtures directory.
    /// Tries the new v20 layout (for_prague) then the legacy layout (prague).
    /// </summary>
    public static string? ResolveFixtureRoot(string baseFixturesDir, string forkName)
    {
        var newPath = Path.Combine(baseFixturesDir, $"for_{forkName.ToLowerInvariant()}");
        if (Directory.Exists(newPath)) return newPath;

        var legacyPath = Path.Combine(baseFixturesDir, forkName.ToLowerInvariant());
        if (Directory.Exists(legacyPath)) return legacyPath;

        return null;
    }

    /// <summary>
    /// Classify a mismatch string into taxonomy buckets (balance, storage, nonce, …).
    /// </summary>
    public static string ClassifyMismatch(string mismatch)
        => ForkComplianceScorecard.ClassifyMismatch(mismatch);

    /// <summary>
    /// Pull EIP / feature folder name from a fixture path for clustering.
    /// </summary>
    public static string ExtractEipCluster(string? fixturePath)
    {
        if (string.IsNullOrWhiteSpace(fixturePath))
            return "unknown";

        var parts = fixturePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith("eip", StringComparison.OrdinalIgnoreCase))
                return part;
        }

        // Prefer the folder under the fork label when present
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].StartsWith("for_", StringComparison.OrdinalIgnoreCase) && i + 2 < parts.Length)
                return parts[i + 2]; // for_osaka / prague / <feature>
        }

        return parts.Length >= 2 ? parts[^2] : parts[^1];
    }

    public static string BuildClusterKey(string primaryCategory, string eipCluster)
        => $"{primaryCategory} · {eipCluster}";

    /// <summary>
    /// Run all cases for the given fork, streaming progress updates.
    /// Returns (passed, failed, total).
    /// </summary>
    public static async Task<(int Passed, int Failed, int Total)> RunAsync(
        string fixtureRoot,
        string forkName,
        IProgress<ConformanceProgress> progress,
        CancellationToken ct)
    {
        var loader   = new EelsStateFixtureLoader();
        var executor = new EelsStateFixtureExecutor();

        var options = new EelsHarnessOptions(
            FixturesRoot:          fixtureRoot,
            ForkName:              forkName,
            MaxCases:              int.MaxValue,
            IncludeSubdirectories: true
        );

        List<EelsStateCase> cases;
        try
        {
            cases = loader.LoadCases(options).ToList();
        }
        catch (Exception ex)
        {
            progress.Report(new ConformanceProgress(
                0, 1, 1,
                "Failed to load fixtures",
                ex.Message,
                new[] { ex.Message },
                fixtureRoot,
                0, 0,
                "other",
                "load_error",
                BuildClusterKey("other", "load_error")));
            return (0, 1, 1);
        }

        int total  = cases.Count;
        int passed = 0;
        int failed = 0;

        // Bound concurrency so large-stack workers don't thrash the machine
        var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);
        var tasks = cases.Select(async c =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                var report = await executor.ExecuteAsync(c, ct);
                bool ok = report.StateMatches && report.ReceiptStatusMatches;

                int p, f;
                if (ok) { p = Interlocked.Increment(ref passed); f = Volatile.Read(ref failed); }
                else     { f = Interlocked.Increment(ref failed); p = Volatile.Read(ref passed); }

                if (ok)
                {
                    progress.Report(new ConformanceProgress(
                        p, f, total, c.CaseId,
                        null, null, c.FixturePath,
                        report.GasUsed, report.GasRefundCounter,
                        string.Empty, string.Empty, string.Empty));
                    return;
                }

                var mismatches = report.Mismatches?.ToList() ?? new List<string>();
                var categories = mismatches
                    .Select(ClassifyMismatch)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToList();
                var primary = categories.FirstOrDefault() ?? "other";
                var eip = ExtractEipCluster(c.FixturePath);
                var cluster = BuildClusterKey(primary, eip);
                var summary = mismatches.Count == 0
                    ? "state/receipt mismatch"
                    : string.Join("; ", mismatches.Take(2));

                progress.Report(new ConformanceProgress(
                    p, f, total,
                    c.CaseId,
                    summary,
                    mismatches,
                    c.FixturePath,
                    report.GasUsed,
                    report.GasRefundCounter,
                    primary,
                    eip,
                    cluster));
            }
            finally { semaphore.Release(); }
        }).ToList();

        await Task.WhenAll(tasks);
        return (passed, failed, total);
    }
}
