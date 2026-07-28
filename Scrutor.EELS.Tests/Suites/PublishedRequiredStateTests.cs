using Scrutor.EELS.Tests.Harness;

namespace Scrutor.EELS.Tests.Suites;

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

        var executor = new EelsStateFixtureExecutor();
        var reports = new List<EelsCaseExecutionReport>(cases.Count);
        foreach (var testCase in cases)
        {
            reports.Add(await executor.ExecuteAsync(testCase));
        }

        // [AI-EDIT 2026-01-10] This asserts harness stability (same input ->
        // structured report), even before full EELS conformance is complete.
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
        {
            return;
        }

        var options = EelsHarnessOptions.FromEnvironment() with
        {
            IncludeSubdirectories = true
        };

        var loader = new EelsStateFixtureLoader();
        var cases = loader.LoadCases(options);
        Assert.NotEmpty(cases);

        var executor = new EelsStateFixtureExecutor();
        var reports = new List<EelsCaseExecutionReport>(cases.Count);
        foreach (var testCase in cases)
        {
            reports.Add(await executor.ExecuteAsync(testCase));
        }

        var failed = reports.Where(r => !r.StateMatches || !r.ReceiptStatusMatches).ToArray();
        if (failed.Length == 0)
        {
            return;
        }

        // [AI-EDIT 2026-01-10] Bucket mismatches into actionable categories so
        // each correction slice can target the highest-volume failure class first.
        var taxonomy = failed
            .SelectMany(f => f.Mismatches)
            .GroupBy(ClassifyMismatch, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key}:{g.Count()}")
            .ToArray();

        var sampleFailures = failed
            .Take(5)
            .Select(f => $"{f.CaseId} => {string.Join(" | ", f.Mismatches.Take(3))}")
            .ToArray();

        var message =
            $"Strict EELS truth mismatch detected. " +
            $"TotalCases={reports.Count}, FailedCases={failed.Length}, " +
            $"Taxonomy=[{string.Join(", ", taxonomy)}], " +
            $"Sample=[{string.Join(" || ", sampleFailures)}]";

        Assert.Fail(message);
    }

    /// <summary>
    /// Always runs (no env-var guard) and requires a zero-mismatch taxonomy.
    /// </summary>
    [Fact]
    public async Task BENCHMARK_TaxonomySnapshot_AlwaysReportsCurrentMismatchCounts()
    {
        var options = EelsHarnessOptions.FromEnvironment() with { IncludeSubdirectories = true, MaxCases = int.MaxValue };
        var loader   = new EelsStateFixtureLoader();
        var cases    = loader.LoadCases(options);
        if (cases.Count == 0)
        {
            Assert.Fail("No fixture cases loaded from EELS_FIXTURES_ROOT - cannot produce taxonomy.");
            return;
        }

        var executor = new EelsStateFixtureExecutor();
        var reports  = new List<EelsCaseExecutionReport>(cases.Count);
        foreach (var testCase in cases)
            reports.Add(await executor.ExecuteAsync(testCase));

        var failed = reports.Where(r => !r.StateMatches || !r.ReceiptStatusMatches).ToArray();

        var taxonomy = failed
            .SelectMany(f => f.Mismatches)
            .GroupBy(ClassifyMismatch, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key}:{g.Count()}")
            .ToArray();

        var summary = $"TotalCases={reports.Count}, FailedCases={failed.Length}, Taxonomy=[{string.Join(", ", taxonomy)}]";
        Assert.True(failed.Length == 0, summary);
    }

    private static string ClassifyMismatch(string mismatch)
    {
        if (mismatch.StartsWith("nonce mismatch", StringComparison.Ordinal))
            return "nonce";
        if (mismatch.StartsWith("balance mismatch", StringComparison.Ordinal))
            return "balance";
        if (mismatch.StartsWith("code mismatch", StringComparison.Ordinal))
            return "code";
        if (mismatch.StartsWith("storage mismatch", StringComparison.Ordinal))
            return "storage";
        if (mismatch.StartsWith("receipt.status mismatch", StringComparison.Ordinal))
            return "receipt_status";
        if (mismatch.StartsWith("missing account", StringComparison.Ordinal))
            return "missing_account";
        return "other";
    }
}
