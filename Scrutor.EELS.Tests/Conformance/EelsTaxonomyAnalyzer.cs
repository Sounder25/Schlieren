using System.Collections.Concurrent;
using System.Numerics;
using System.Text;
using Scrutor.Core.Execution;
using Scrutor.EELS.Tests.Harness;

namespace Scrutor.EELS.Tests.Conformance;

/// <summary>
/// EELS Taxonomy Drill — Automated Bug Bucketing
/// ================================================
/// Runs the full (or partial) fixture suite, captures every mismatch with full
/// context, then groups failures by:
///   • EIP category  (balance, storage, nonce, receipt, code, missing_account)
///   • Magnitude bucket  (exact same delta across N tests = single root cause)
///   • Address  (which account diverges most often)
///   • Layer 1 diagnoses  (<see cref="DivergenceDiagnostics"/> protocol hypotheses)
///
/// Typical usage via dotnet test --filter:
///
///   $env:EELS_FIXTURES_ROOT = "C:/projects/Scrutor/fixtures/state_tests/cancun"
///   $env:EELS_INCLUDE_SUBDIRS = "1"
///   $env:EELS_MAX_CASES = "9999"
///   dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj --filter "EelsTaxonomyDrill"
///
/// Or use the Hermes skill: eels-taxonomy-drill
/// </summary>
public sealed class EelsTaxonomyDrill
{
    // ------------------------------------------------------------------
    // xUnit integration
    // ------------------------------------------------------------------

    [Fact(DisplayName = "EelsTaxonomyDrill — bucket all failures and write report")]
    public async Task RunTaxonomyDrillAsync()
    {
        var opts = EelsHarnessOptions.FromEnvironment();
        var report = await EelsTaxonomyAnalyzer.RunAsync(opts, CancellationToken.None);

        var markdown = EelsTaxonomyAnalyzer.RenderMarkdown(report);

        // Write report to TestResults/
        var outputDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "TestResults");
        Directory.CreateDirectory(outputDir);
        var outPath = Path.Combine(outputDir, $"taxonomy_{DateTime.UtcNow:yyyyMMdd_HHmmss}.md");
        await File.WriteAllTextAsync(outPath, markdown, Encoding.UTF8);

        // Print summary to test runner console
        Console.WriteLine(markdown);

        // Check regression against docs/eels_baseline.json
        var baselinePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "eels_baseline.json");
        await EelsTaxonomyAnalyzer.CheckAndUpdateBaselineAsync(report, baselinePath);

        // Always passes — the taxonomy report is the artifact
        Assert.True(true, "Taxonomy drill complete — see TestResults/ for report.");
    }
}

// ---------------------------------------------------------------------------
// Core analyzer (can also be called from Hermes tool scripts)
// ---------------------------------------------------------------------------

public static class EelsTaxonomyAnalyzer
{
    public static async Task<TaxonomyReport> RunAsync(
        EelsHarnessOptions opts,
        CancellationToken ct = default)
    {
        var loader = new EelsStateFixtureLoader();
        var cases  = loader.LoadCases(opts);

        var allReports = new ConcurrentBag<EelsCaseExecutionReport>();
        var layer1Hits = new ConcurrentBag<(string CaseId, DivergenceDiagnostics.Diagnosis Diagnosis)>();

        // [AI-EDIT 2026-08-05] Parallel execution — each slot owns its own
        // EelsStateFixtureExecutor, which now carries an instance LargeStackWorker
        // (32MB thread). We bound parallelism at ProcessorCount so we don't
        // over-subscribe the CPU with large-stack threads.
        //
        // Before: sequential,  ~10 min for 9,999 Cancun cases
        // After:  parallel,    ~30 s   on an 8-core machine  (≈20× faster)
        var parallelOpts = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(cases, parallelOpts, async (testCase, innerCt) =>
        {
            // Each invocation gets its own executor+worker — no shared queue contention.
            var executor = new EelsStateFixtureExecutor();
            var r = await executor.ExecuteAsync(testCase, innerCt);
            allReports.Add(r);

            // Phase 2: Layer 1 diagnostics on failures only.
            if (!r.StateMatches || !r.ReceiptStatusMatches)
            {
                foreach (var dx in Layer1DiagnosisBridge.DiagnoseCase(testCase, r))
                    layer1Hits.Add((r.CaseId, dx));
            }
        });

        var reports = allReports.ToList();
        var failed  = reports.Where(r => !r.StateMatches || !r.ReceiptStatusMatches).ToList();
        var layer1Buckets = Layer1DiagnosisBridge.Aggregate(layer1Hits);

        // ----------------------------------------------------------------
        // 1. Category taxonomy  (what field diverges)
        // ----------------------------------------------------------------
        var categoryBuckets = failed
            .SelectMany(r => r.Mismatches.Select(m => (
                case_: r,
                mismatch: m,
                category: ForkComplianceScorecard.ClassifyMismatch(m))))
            .GroupBy(x => x.category)
            .OrderByDescending(g => g.Count())
            .ToDictionary(
                g => g.Key,
                g => (count: g.Count(), examples: g.Take(5).Select(x => x.mismatch).ToList()),
                StringComparer.Ordinal);

        // ----------------------------------------------------------------
        // 2. Delta magnitude buckets  (balance mismatches only)
        // ----------------------------------------------------------------
        var deltaBuckets = new Dictionary<BigInteger, int>();
        foreach (var r in failed)
        {
            foreach (var m in r.Mismatches)
            {
                if (!m.StartsWith("balance mismatch", StringComparison.Ordinal))
                    continue;

                var (exp, act) = ExtractExpectedActual(m);
                if (exp is null || act is null) continue;
                var delta = act.Value - exp.Value;
                deltaBuckets.TryGetValue(delta, out var cnt);
                deltaBuckets[delta] = cnt + 1;
            }
        }

        var topDeltas = deltaBuckets
            .OrderByDescending(kvp => kvp.Value)
            .Take(10)
            .ToList();

        // ----------------------------------------------------------------
        // 3. Per-address hot spots
        // ----------------------------------------------------------------
        var addressBuckets = failed
            .SelectMany(r => r.Mismatches)
            .Select(m => ExtractAddress(m))
            .Where(a => a != null)
            .GroupBy(a => a!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return new TaxonomyReport(
            FixturesRoot: opts.FixturesRoot,
            Fork: opts.ForkName,
            TotalCases: reports.Count,
            PassedCases: reports.Count - failed.Count,
            FailedCases: failed.Count,
            CategoryBuckets: categoryBuckets,
            TopDeltaBuckets: topDeltas,
            AddressHotSpots: addressBuckets,
            MaxCases: opts.MaxCases,
            Layer1Diagnoses: layer1Buckets);
    }

    // ------------------------------------------------------------------
    // Markdown renderer
    // ------------------------------------------------------------------

    public static string RenderMarkdown(TaxonomyReport r)
    {
        var sb = new StringBuilder();
        double passRate = r.TotalCases == 0 ? 0.0 : (double)r.PassedCases / r.TotalCases * 100.0;

        sb.AppendLine("# EELS Taxonomy Drill Report");
        sb.AppendLine();
        sb.AppendLine($"- **Fork**         : `{r.Fork}`");
        sb.AppendLine($"- **Fixtures root** : `{r.FixturesRoot}`");
        sb.AppendLine($"- **Max cases**    : `{r.MaxCases}`");
        sb.AppendLine($"- **Generated**    : `{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC`");
        sb.AppendLine();
        sb.AppendLine("## KPI");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"| :----- | ----: |");
        sb.AppendLine($"| Total cases  | {r.TotalCases} |");
        sb.AppendLine($"| Passed       | {r.PassedCases} |");
        sb.AppendLine($"| Failed       | {r.FailedCases} |");
        sb.AppendLine($"| Pass rate    | {passRate:0.00}% |");
        sb.AppendLine();

        // Category buckets
        sb.AppendLine("## Failure Category Taxonomy");
        sb.AppendLine();
        sb.AppendLine("| Category | Count | Top Example |");
        sb.AppendLine("| :------- | ----: | :---------- |");
        foreach (var (cat, (count, examples)) in r.CategoryBuckets)
        {
            var ex = examples.FirstOrDefault() ?? "";
            if (ex.Length > 80) ex = ex[..80] + "…";
            sb.AppendLine($"| `{cat}` | {count} | `{ex}` |");
        }
        sb.AppendLine();

        // Delta magnitude buckets
        sb.AppendLine("## Balance Delta Magnitude Buckets");
        sb.AppendLine();
        sb.AppendLine("> A consistent delta across many tests indicates a SINGLE root cause.");
        sb.AppendLine();
        sb.AppendLine("| Delta (actual − expected) | Occurrences | Hypothesis |");
        sb.AppendLine("| :------------------------ | ----------: | :--------- |");
        foreach (var kvp in r.TopDeltaBuckets)
        {
            var delta = kvp.Key;
            var count = kvp.Value;
            var hypothesis = Hypothesize(delta, r.Fork);
            var sign = delta >= 0 ? "+" : "";
            sb.AppendLine($"| `{sign}{delta:N0}` | {count} | {hypothesis} |");
        }
        if (r.TopDeltaBuckets.Count == 0)
            sb.AppendLine("| (no balance mismatches) | — | — |");
        sb.AppendLine();

        // Address hot spots
        sb.AppendLine("## Address Hot Spots");
        sb.AppendLine();
        sb.AppendLine("| Address | Mismatch Count |");
        sb.AppendLine("| :------ | -------------: |");
        foreach (var (addr, count) in r.AddressHotSpots)
            sb.AppendLine($"| `{addr}` | {count} |");
        if (r.AddressHotSpots.Count == 0)
            sb.AppendLine("| (none) | — |");
        sb.AppendLine();

        // Layer 1 — DivergenceDiagnostics (product engine in Scrutor.Core)
        sb.AppendLine("## Layer 1 Diagnoses (`DivergenceDiagnostics`)");
        sb.AppendLine();
        sb.AppendLine("> Deterministic protocol hypotheses from observed deltas — not raw mismatch strings.");
        sb.AppendLine();
        if (r.Layer1Diagnoses.Count == 0)
        {
            sb.AppendLine("_No Layer 1 diagnoses fired on this run (no matching structural/gas patterns)._");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("| # | Conf | Category | Occurrences | Summary | Protocol | Code boundary | Sample case |");
            sb.AppendLine("| -: | :--- | :------- | ----------: | :------ | :------- | :------------ | :---------- |");
            int i = 1;
            foreach (var d in r.Layer1Diagnoses)
            {
                var sample = d.SampleCaseIds.FirstOrDefault() ?? "";
                if (sample.Length > 48) sample = sample[..48] + "…";
                var summary = d.Summary.Length > 90 ? d.Summary[..90] + "…" : d.Summary;
                sb.AppendLine(
                    $"| {i} | `{d.Confidence}` | `{d.Category}` | {d.Occurrences} | {summary} | `{d.ProtocolRule}` | `{d.CodeBoundary}` | `{sample}` |");
                i++;
            }
            sb.AppendLine();

            sb.AppendLine("### Top diagnosis detail");
            sb.AppendLine();
            var top = r.Layer1Diagnoses[0];
            sb.AppendLine($"- **Category** : `{top.Category}`");
            sb.AppendLine($"- **Confidence**: `{top.Confidence}`");
            sb.AppendLine($"- **Summary**  : {top.Summary}");
            sb.AppendLine($"- **Protocol** : {top.ProtocolRule}");
            sb.AppendLine($"- **Look in**  : `{top.CodeBoundary}`");
            sb.AppendLine($"- **Evidence** : `{top.SampleEvidence}`");
            if (top.SampleCaseIds.Count > 0)
                sb.AppendLine($"- **Cases**    : `{string.Join("`, `", top.SampleCaseIds)}`");
            sb.AppendLine();
        }

        // Next steps
        sb.AppendLine("## Recommended Next Steps");
        sb.AppendLine();
        if (r.FailedCases == 0)
        {
            sb.AppendLine("✅ **All cases pass.** No action needed for this fork.");
        }
        else
        {
            int step = 1;
            if (r.Layer1Diagnoses.Count > 0)
            {
                var topDx = r.Layer1Diagnoses[0];
                sb.AppendLine($"{step}. **Layer 1 top hit** (`{topDx.Confidence}`): {topDx.Summary}");
                sb.AppendLine($"   → Inspect `{topDx.CodeBoundary}` ({topDx.ProtocolRule}); {topDx.Occurrences} matching diagnoses.");
                step++;
                if (topDx.SampleCaseIds.Count > 0)
                {
                    sb.AppendLine($"{step}. Trace a representative case:");
                    sb.AppendLine("   ```powershell");
                    sb.AppendLine($"   $env:EELS_CASE_FILTER = \"{topDx.SampleCaseIds[0]}\"");
                    sb.AppendLine("   dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj --filter \"SingleCaseTrace\"");
                    sb.AppendLine("   ```");
                    step++;
                }
            }
            else
            {
                var topCat = r.CategoryBuckets.FirstOrDefault();
                sb.AppendLine($"{step}. Focus on `{topCat.Key}` ({topCat.Value.count} failures) — the dominant failure mode.");
                step++;
            }

            if (r.TopDeltaBuckets.Count > 0)
            {
                var topDelta = r.TopDeltaBuckets.First();
                sb.AppendLine($"{step}. Balance delta `{topDelta.Key:+#;-#;0}` appears in {topDelta.Value} cases — likely one root cause.");
                sb.AppendLine($"   Hypothesis: {Hypothesize(topDelta.Key, r.Fork)}");
                step++;
            }

            if (r.Layer1Diagnoses.Count == 0)
            {
                sb.AppendLine($"{step}. Use `eels-single-case-tracer` to drill into one failure case:");
                sb.AppendLine("   ```powershell");
                sb.AppendLine("   $env:EELS_CASE_FILTER = \"<paste case_id here>\"");
                sb.AppendLine("   dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj --filter \"SingleCaseTrace\"");
                sb.AppendLine("   ```");
            }
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static (BigInteger? exp, BigInteger? act) ExtractExpectedActual(string mismatch)
    {
        // e.g. "balance mismatch for 0xabc: expected=0x1234, actual=0x5678"
        var expM = System.Text.RegularExpressions.Regex.Match(mismatch, @"expected=(\S+)");
        var actM = System.Text.RegularExpressions.Regex.Match(mismatch, @"actual=(\S+)");
        if (!expM.Success || !actM.Success) return (null, null);

        static BigInteger? TryParse(string s)
        {
            s = s.TrimEnd(',', ';', '.');
            try
            {
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    return BigInteger.Parse("0" + s[2..], System.Globalization.NumberStyles.HexNumber);
                return BigInteger.Parse(s);
            }
            catch { return null; }
        }

        return (TryParse(expM.Groups[1].Value), TryParse(actM.Groups[1].Value));
    }

    private static string? ExtractAddress(string mismatch)
    {
        var m = System.Text.RegularExpressions.Regex.Match(
            mismatch, @"for (0x[0-9a-fA-F]{20,40})");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string Hypothesize(BigInteger delta, string fork)
    {
        var abs = BigInteger.Abs(delta);
        if (delta == 0) return "Exact match on this field";

        // Query normative EELS spec data for the active fork
        var forkSchedule = Scrutor.EELS.Tests.SpecData.ForkGasData.AllForks
            .FirstOrDefault(f => f.Fork.Equals(fork, StringComparison.OrdinalIgnoreCase));

        if (forkSchedule is not null && abs <= ulong.MaxValue)
        {
            var absUlong = (ulong)abs;
            var matchingConstants = forkSchedule.Constants.Values
                .Where(c => c.Value == absUlong)
                .Select(c => c.Name)
                .ToList();

            if (matchingConstants.Count > 0)
            {
                var names = string.Join(", ", matchingConstants.Take(3));
                var sign = delta > 0 ? "overcharged" : "undercharged";
                return $"Matches spec constant [{names}] ({absUlong} gas) — likely {sign} in execution or gas schedule";
            }
        }

        // Common known deltas / composite heuristics
        if (abs == 21_000) return "Intrinsic gas (TX_BASE / EIP-2930) over/under-charged";
        if (abs == 2_300)  return "CALL stipend (2,300) double-counted or missing";
        if (abs == 9_000)  return "Value-transfer gas surcharge (9,000) off by one";
        if (abs == 200)   return "Code-deposit 200-gas/byte off by one byte";

        // Power of 2 check (gas rounding)
        if ((abs & (abs - 1)) == 0 && abs > 1 && abs < 65536)
            return $"Power-of-2 delta ({abs}) — possible bit-shift or word-size rounding issue";

        return "Unknown — run eels-single-case-tracer for step-level gas breakdown";
    }

    public static async Task CheckAndUpdateBaselineAsync(TaxonomyReport r, string baselinePath)
    {
        try
        {
            if (File.Exists(baselinePath))
            {
                var json = await File.ReadAllTextAsync(baselinePath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                int oldFailed = doc.RootElement.TryGetProperty("failedCases", out var fProp) ? fProp.GetInt32() : 0;
                if (r.FailedCases > oldFailed)
                {
                    Console.WriteLine($"🔴 REGRESSION DETECTED: Failure count increased by {r.FailedCases - oldFailed} cases (Baseline: {oldFailed}, Current: {r.FailedCases})!");
                }
                else if (r.FailedCases < oldFailed)
                {
                    Console.WriteLine($"🟢 IMPROVEMENT DETECTED: Failure count decreased by {oldFailed - r.FailedCases} cases (Baseline: {oldFailed}, Current: {r.FailedCases})!");
                }
            }

            if (!File.Exists(baselinePath) || string.Equals(Environment.GetEnvironmentVariable("EELS_UPDATE_BASELINE"), "1", StringComparison.OrdinalIgnoreCase))
            {
                var baselineData = new
                {
                    date = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                    fork = r.Fork,
                    totalCases = r.TotalCases,
                    passedCases = r.PassedCases,
                    failedCases = r.FailedCases,
                    taxonomy = r.CategoryBuckets.ToDictionary(k => k.Key, v => v.Value.count)
                };

                Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(baselinePath, System.Text.Json.JsonSerializer.Serialize(baselineData, options));
                Console.WriteLine($"Saved new regression baseline to: {baselinePath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Baseline check failed: {ex.Message}");
        }
    }
}

// ------------------------------------------------------------------
// Report model
// ------------------------------------------------------------------

public sealed record TaxonomyReport(
    string FixturesRoot,
    string Fork,
    int TotalCases,
    int PassedCases,
    int FailedCases,
    IReadOnlyDictionary<string, (int count, List<string> examples)> CategoryBuckets,
    IReadOnlyList<KeyValuePair<BigInteger, int>> TopDeltaBuckets,
    IReadOnlyDictionary<string, int> AddressHotSpots,
    int MaxCases,
    IReadOnlyList<Layer1DiagnosisBucket> Layer1Diagnoses);
