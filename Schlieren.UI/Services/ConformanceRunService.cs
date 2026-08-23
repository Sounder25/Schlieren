using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schlieren.Core.Execution;
using Schlieren.EELS.Tests.Conformance;
using Schlieren.EELS.Tests.Harness;

namespace Schlieren.UI.Services;

/// <summary>
/// One Layer 1 diagnosis ready for UI binding (no Core enum leakage).
/// </summary>
public sealed record Layer1DiagnosisInfo(
    string Confidence,
    string Category,
    string Summary,
    string ProtocolRule,
    string CodeBoundary,
    string Evidence,
    string? InspectorBody = null,
    string? Fingerprint = null,
    string? Phase = null,
    int Score = 0);

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
    string ClusterKey,
    IReadOnlyList<Layer1DiagnosisInfo>? Layer1Diagnoses = null);

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

    private static string MapLegacyConfidence(DivergenceDiagnostics.Confidence c) => c switch
    {
        DivergenceDiagnostics.Confidence.Certain => "PROVEN",
        DivergenceDiagnostics.Confidence.High => "STRONG",
        _ => "POSSIBLE"
    };

    private static int ScoreFromGrade(string grade) => grade switch
    {
        "PROVEN" => 92,
        "STRONG" => 70,
        _ => 38
    };

    /// <summary>
    /// Run all cases for the given fork, streaming progress updates.
    /// Returns (passed, failed, total).
    /// </summary>
    public static async Task<(int Passed, int Failed, int Total)> RunAsync(
        string fixtureRoot,
        string forkName,
        IProgress<ConformanceProgress> progress,
        CancellationToken ct,
        string? excludeFolder = null)
    {
        var loader   = new EelsStateFixtureLoader();
        var executor = new EelsStateFixtureExecutor();

        var options = new EelsHarnessOptions(
            FixturesRoot:          fixtureRoot,
            ForkName:              forkName,
            MaxCases:              int.MaxValue,
            IncludeSubdirectories: true,
            ExcludeFolder:         excludeFolder
        );

        progress.Report(LoadTick(0, "Loading fixtures from disk…", fixtureRoot));

        List<EelsStateCase> cases;
        try
        {
            // Parse JSON off the UI thread. Osaka v20 is ~14k cases and otherwise
            // freezes the window for ~20–30s with no painted status.
            cases = await Task.Run(() =>
            {
                var loadProgress = new Progress<EelsLoadProgress>(lp =>
                {
                    var name = string.IsNullOrEmpty(lp.CurrentFile) ? "scanning" : lp.CurrentFile;
                    progress.Report(LoadTick(
                        0,
                        $"Loading fixtures… {lp.FilesDone:N0}/{lp.FilesTotal:N0} files · {lp.CasesLoaded:N0} cases · {name}",
                        fixtureRoot));
                });
                return loader.LoadCases(options, loadProgress).ToList();
            }, ct).ConfigureAwait(false);
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

        progress.Report(LoadTick(cases.Count, $"Loaded {cases.Count:N0} cases — starting {forkName}…", fixtureRoot));
        return await RunCasesAsync(cases, executor.ExecuteAsync, progress, ct);
    }

    /// <summary>
    /// Runs a pre-built list of cases against an executor delegate, streaming progress.
    /// Extracted from <see cref="RunAsync"/> so the crash-isolation behavior (one case
    /// throwing must not lose the rest of the batch) can be exercised directly in tests
    /// without needing real fixture files on disk to force a genuine executor exception.
    /// </summary>
    public static async Task<(int Passed, int Failed, int Total)> RunCasesAsync(
        IReadOnlyList<EelsStateCase> cases,
        Func<EelsStateCase, CancellationToken, Task<EelsCaseExecutionReport>> execute,
        IProgress<ConformanceProgress> progress,
        CancellationToken ct)
    {
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
                progress.Report(new ConformanceProgress(
                    Volatile.Read(ref passed),
                    Volatile.Read(ref failed),
                    total,
                    $"Running {c.CaseId}",
                    null, null, c.FixturePath,
                    0, 0, string.Empty, string.Empty, string.Empty));

                EelsCaseExecutionReport report;
                try
                {
                    report = await execute(c, ct);
                }
                catch (OperationCanceledException)
                {
                    throw; // real cancellation must still abort the whole run
                }
                catch (Exception ex)
                {
                    // A crash in a single fixture (e.g. a NullReferenceException deep in the
                    // executor) must not take down the entire fork sweep via Task.WhenAll and
                    // silently lose every other case's results — tally it as a failure instead.
                    int fCrash = Interlocked.Increment(ref failed);
                    int pCrash = Volatile.Read(ref passed);
                    progress.Report(new ConformanceProgress(
                        pCrash, fCrash, total,
                        c.CaseId,
                        $"executor crashed: {ex.Message}",
                        new[] { ex.ToString() },
                        c.FixturePath,
                        0, 0,
                        "other", string.Empty,
                        BuildClusterKey("other", "executor_crash")));
                    return;
                }

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
                var categories = (report.Discrepancies ?? Array.Empty<Schlieren.Core.Execution.Causal.StateDiscrepancy>())
                    .Select(item => item.Category)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToList();
                var bundle = Layer1DiagnosisBridge.DiagnoseCausal(c, report);
                var primary = string.IsNullOrEmpty(bundle.Title)
                    ? (categories.FirstOrDefault() ?? "other")
                    : bundle.Title;
                var eip = $"{c.ForkName} · {bundle.Phase} · {bundle.RuleId}";
                var cluster = string.IsNullOrEmpty(bundle.Fingerprint) || bundle.Fingerprint == "none"
                    ? BuildClusterKey(categories.FirstOrDefault() ?? "other", ExtractEipCluster(c.FixturePath))
                    : bundle.Fingerprint;
                var summary = mismatches.Count == 0
                    ? "state/receipt mismatch"
                    : string.Join("; ", mismatches.Take(2));

                var layer1 = bundle.Diagnoses.Select((d, i) => new Layer1DiagnosisInfo(
                        Confidence: i == 0 ? bundle.Grade : MapLegacyConfidence(d.Confidence),
                        Category: d.Category,
                        Summary: d.Summary,
                        ProtocolRule: d.ProtocolRule,
                        CodeBoundary: d.CodeBoundary,
                        Evidence: d.Evidence,
                        InspectorBody: i == 0 ? bundle.InspectorBody : null,
                        Fingerprint: bundle.Fingerprint,
                        Phase: bundle.Phase,
                        Score: i == 0 ? ScoreFromGrade(bundle.Grade) : 0))
                    .ToList();

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
                    cluster,
                    layer1));
            }
            finally { semaphore.Release(); }
        }).ToList();

        await Task.WhenAll(tasks);
        return (passed, failed, total);
    }

    private static ConformanceProgress LoadTick(int casesSoFar, string message, string fixtureRoot)
        => new(
            0, 0, casesSoFar,
            message,
            null, null, fixtureRoot,
            0, 0,
            string.Empty, string.Empty, string.Empty);
}
