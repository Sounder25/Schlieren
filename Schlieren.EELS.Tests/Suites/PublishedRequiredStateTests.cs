using System.Collections.Concurrent;
using Schlieren.EELS.Tests.Harness;

namespace Schlieren.EELS.Tests.Suites;

public sealed class PublishedRequiredStateTests
{
    [Fact]
    public void Harness_DiscoversPublishedStateTests()
    {
        var options = EelsHarnessOptions.FromEnvironment() with
        {
            IncludeSubdirectories = true,
            MaxCases = 10
        };

        var loader = new EelsStateFixtureLoader();
        var cases = loader.LoadCases(options);

        Assert.NotEmpty(cases);
        Assert.All(cases, testCase =>
        {
            Assert.False(string.IsNullOrWhiteSpace(testCase.CaseId));
            Assert.Equal(options.ForkName, testCase.ForkName);
        });
    }

    [Fact]
    public async Task Harness_ExecutesPublishedCases_AndProducesDeterministicReport()
    {
        var options = EelsHarnessOptions.FromEnvironment() with
        {
            IncludeSubdirectories = true,
            MaxCases = 5
        };

        var loader = new EelsStateFixtureLoader();
        var cases = loader.LoadCases(options);
        Assert.NotEmpty(cases);

        var reports = await RunParallelAsync(cases);

        Assert.Equal(cases.Count, reports.Count);
        Assert.All(reports, report =>
        {
            Assert.False(string.IsNullOrWhiteSpace(report.CaseId));
            Assert.NotNull(report.Mismatches);
        });
    }

    [Fact]
    public async Task Harness_StrictTruthMode_FailsOnPublishedMismatch()
    {
        var strictMode = Environment.GetEnvironmentVariable("EELS_ENFORCE_TRUTH");
        if (!string.Equals(strictMode, "1", StringComparison.Ordinal) &&
            !string.Equals(strictMode, "true", StringComparison.OrdinalIgnoreCase))
            return;

        var options = EelsHarnessOptions.FromEnvironment() with { IncludeSubdirectories = true };
        var loader  = new EelsStateFixtureLoader();
        var cases   = loader.LoadCases(options);
        Assert.NotEmpty(cases);

        var reports = await RunParallelAsync(cases);
        var failed  = reports.Where(r => !r.StateMatches || !r.ReceiptStatusMatches).ToArray();
        if (failed.Length == 0) return;

        var taxonomy = BuildTaxonomy(failed);
        var samples  = BuildSamples(failed, 10);

        Assert.Fail(
            $"TotalCases={reports.Count}, FailedCases={failed.Length}, " +
            $"Taxonomy=[{taxonomy}]\n\nSAMPLES:\n{samples}");
    }

    /// <summary>
    /// Parallel full-suite sweep. Reports taxonomy + samples so balance root cause is visible.
    /// </summary>
    [Fact]
    public async Task BENCHMARK_TaxonomySnapshot_AlwaysReportsCurrentMismatchCounts()
    {
        var options  = EelsHarnessOptions.FromEnvironment() with { IncludeSubdirectories = true, MaxCases = int.MaxValue };
        var loader   = new EelsStateFixtureLoader();
        var cases    = loader.LoadCases(options);
        if (cases.Count == 0)
        {
            Assert.Fail("No fixture cases loaded from EELS_FIXTURES_ROOT - cannot produce taxonomy.");
            return;
        }

        var reports = await RunParallelAsync(cases);
        var failed  = reports.Where(r => !r.StateMatches || !r.ReceiptStatusMatches).ToArray();

        var taxonomy = BuildTaxonomy(failed);
        var samples  = BuildSamples(failed, 20);

        var summary =
            $"TotalCases={reports.Count}, FailedCases={failed.Length}, " +
            $"Taxonomy=[{taxonomy}]\n\nSAMPLES (first 20 failures):\n{samples}";

        Assert.True(failed.Length == 0, summary);
    }

    // ── Parallel runner ─────────────────────────────────────────────────────
    // Each test case is fully isolated (own GlobalState, own StateTransition).
    // Safe to run on all logical cores — no shared mutable state.

    private static async Task<List<EelsCaseExecutionReport>> RunParallelAsync(
        IReadOnlyList<EelsStateCase> cases)
    {
        var bag       = new ConcurrentBag<EelsCaseExecutionReport>();
        var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);

        await Parallel.ForEachAsync(cases, async (testCase, ct) =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var executor = new EelsStateFixtureExecutor(); // one per task — thread-safe
                bag.Add(await executor.ExecuteAsync(testCase));
            }
            finally
            {
                semaphore.Release();
            }
        });

        // Preserve original load order for deterministic output
        var index = cases.Select((c, i) => (c.CaseId, i)).ToDictionary(x => x.CaseId, x => x.i);
        return bag.OrderBy(r => index.TryGetValue(r.CaseId, out var i) ? i : int.MaxValue).ToList();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string BuildTaxonomy(EelsCaseExecutionReport[] failed) =>
        string.Join(", ", failed
            .SelectMany(f => f.Mismatches)
            .GroupBy(ClassifyMismatch, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key}:{g.Count()}"));

    private static string BuildSamples(EelsCaseExecutionReport[] failed, int count) =>
        string.Join("\n", failed
            .Take(count)
            .Select(f => $"  {f.CaseId}\n    {string.Join("\n    ", f.Mismatches.Take(4))}"));

    private static string ClassifyMismatch(string mismatch)
    {
        if (mismatch.StartsWith("nonce mismatch",        StringComparison.Ordinal)) return "nonce";
        if (mismatch.StartsWith("balance mismatch",      StringComparison.Ordinal)) return "balance";
        if (mismatch.StartsWith("code mismatch",         StringComparison.Ordinal)) return "code";
        if (mismatch.StartsWith("storage mismatch",      StringComparison.Ordinal)) return "storage";
        if (mismatch.StartsWith("receipt.status mismatch", StringComparison.Ordinal)) return "receipt_status";
        if (mismatch.StartsWith("missing account",       StringComparison.Ordinal)) return "missing_account";
        return "other";
    }
}
