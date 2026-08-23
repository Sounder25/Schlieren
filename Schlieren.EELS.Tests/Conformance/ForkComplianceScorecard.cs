using System.Text;
using System.Text.Json;
using Schlieren.Core.Execution.Causal;
using Schlieren.EELS.Tests.Harness;

namespace Schlieren.EELS.Tests.Conformance;

/// <summary>
/// Layer 3: fork-aware Compliance Scorecard.
///
/// Runs the official ethereum/execution-spec-tests fixture corpus per fork
/// label (Frontier -&gt; Cancun) and aggregates a KPI (passed / total) plus a
/// failure taxonomy for each fork. The engine is currently hardwired to Cancun
/// rules, so pre-Cancun forks are expected to report gaps; this report records
/// those gaps (matching the engine's known non-conformance inventory in
/// docs/FORK_GAS_AUDIT.md) without changing engine semantics.
/// </summary>
public static class ForkComplianceScorecard
{
    private static readonly IReadOnlyList<string> CanonicalForkOrder = new[]
    {
        "Frontier", "Homestead", "Byzantium", "ConstantinopleFix", "Istanbul",
        "Berlin", "London", "Paris", "Shanghai", "Cancun", "Prague", "Osaka"
    };

    private static readonly IReadOnlyDictionary<string, string> KnownGaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Frontier"] = "Engine charges Cancun gas (EIP-2929 warm/cold, EIP-2028 calldata 16, EIP-3860 initcode). Expect EXP 10/byte vs 50/byte, SLOAD 800 vs 2100/100, no EIP-2929.",
        ["Homestead"] = "Same as Frontier plus EIP-2/7/8 semantics; gas schedule identical to Frontier for the constants the engine applies.",
        ["Byzantium"] = "Pre-EIP-2929: BALANCE 400 vs 2600/100, RETURNDATACOPY introduced; bnadd/bnmul/bnpairing at EIP-198/197 pricing (engine uses EIP-1108/2565).",
        ["ConstantinopleFix"] = "Pre-EIP-2929; SHL/SHR/SAR and CREATE2 introduced; SSTORE net metering differs from EIP-2200.",
        ["Istanbul"] = "EIP-2028 calldata non-zero byte 68 (engine charges 16); EIP-2200 SSTORE; CHAINID/SELFBALANCE available.",
        ["Berlin"] = "EIP-2929 active (matches engine); EIP-2930 access lists; EIP-2565 modexp. Engine warm/cold matches but EIP-3860 not yet active (Shanghai).",
        ["London"] = "EIP-3529 refund 4800 + cap/5 (matches engine); EIP-1559 BASEFEE available; EIP-3860 still inactive (engine applies it unconditionally - see docs/FORK_GAS_AUDIT.md).",
        ["Paris"] = "The Merge; DIFFICULTY replaced by PREVRANDAO. Gas schedule unchanged from London.",
        ["Shanghai"] = "EIP-3860 initcode word gas + EIP-3651 warm coinbase (engine applies both); PUSH0 introduced; engine already charges these.",
        ["Cancun"] = "Target fork - engine is hardwired to Cancun rules; expected to be fully compliant on the canonical cancun fixture root."
    };

    public static async Task<ComplianceScorecardReport> RunAsync(
        string fixturesRoot,
        int maxCasesPerFork = 25,
        CancellationToken ct = default)
    {
        var forks = DiscoverForkLabels(fixturesRoot);
        var executor = new EelsStateFixtureExecutor();
        var rows = new List<ForkScoreRow>(forks.Count);

        foreach (var fork in forks)
        {
            var options = new EelsHarnessOptions(fixturesRoot, fork, maxCasesPerFork, IncludeSubdirectories: true);
            var cases = new EelsStateFixtureLoader().LoadCases(options);
            if (cases.Count == 0)
            {
                continue;
            }

            var reports = new List<EelsCaseExecutionReport>(cases.Count);
            foreach (var testCase in cases)
            {
                ct.ThrowIfCancellationRequested();
                reports.Add(await executor.ExecuteAsync(testCase, ct));
            }

            var failed = reports.Where(r => !r.StateMatches || !r.ReceiptStatusMatches).ToArray();
            var taxonomy = failed
                .SelectMany(r => r.Discrepancies ?? Array.Empty<StateDiscrepancy>())
                .GroupBy(item => item.Category, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            rows.Add(new ForkScoreRow(
                fork,
                reports.Count,
                reports.Count - failed.Length,
                failed.Length,
                taxonomy));
        }

        return new ComplianceScorecardReport(fixturesRoot, maxCasesPerFork, rows, KnownGaps);
    }

    /// <summary>
    /// Scans every fixture JSON under the root and returns the distinct fork
    /// labels found in the <c>post</c> maps, in canonical fork order.
    /// </summary>
    public static IReadOnlyList<string> DiscoverForkLabels(string fixturesRoot)
    {
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(fixturesRoot, "*.json", SearchOption.AllDirectories))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var fixtureCase in doc.RootElement.EnumerateObject())
            {
                if (!fixtureCase.Value.TryGetProperty("post", out var postNode) ||
                    postNode.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var forkProp in postNode.EnumerateObject())
                {
                    labels.Add(forkProp.Name);
                }
            }
        }

        return labels
            .OrderBy(label => CanonicalForkOrderIndex(label))
            .ThenBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int CanonicalForkOrderIndex(string label)
    {
        for (var i = 0; i < CanonicalForkOrder.Count; i++)
        {
            if (string.Equals(CanonicalForkOrder[i], label, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return CanonicalForkOrder.Count;
    }

    public static string RenderMarkdown(ComplianceScorecardReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Schlieren Compliance Scorecard");
        sb.AppendLine();
        sb.AppendLine($"- Fixtures root: `{report.FixturesRoot}`");
        sb.AppendLine($"- Max cases per fork: `{report.MaxCasesPerFork}`");
        sb.AppendLine($"- Generated: `{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss 'UTC'}`");
        sb.AppendLine();
        sb.AppendLine("## KPI: passed / total per fork (official execution-spec-tests corpus)");
        sb.AppendLine();
        sb.AppendLine("| Fork | Cases | Passed | Failed | Pass Rate |");
        sb.AppendLine("| :--- | ---: | ---: | ---: | ---: |");
        foreach (var row in report.Rows)
        {
            sb.AppendLine(
                $"| {row.Fork} | {row.TotalCases} | {row.PassedCases} | {row.FailedCases} | {row.PassRatePercent:0.0}% |");
        }

        sb.AppendLine();
        sb.AppendLine("## Failure taxonomy (top categories per fork)");
        sb.AppendLine();
        sb.AppendLine("| Fork | Category | Count |");
        sb.AppendLine("| :--- | :--- | ---: |");
        foreach (var row in report.Rows.Where(r => r.FailedCases > 0))
        {
            if (row.MismatchTaxonomy.Count == 0)
            {
                sb.AppendLine($"| {row.Fork} | (unclassified) | {row.FailedCases} |");
                continue;
            }

            foreach (var (category, count) in row.MismatchTaxonomy.OrderByDescending(kvp => kvp.Value))
            {
                sb.AppendLine($"| {row.Fork} | {category} | {count} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Known non-conformance notes (engine is Cancun-hardwired)");
        sb.AppendLine();
        foreach (var row in report.Rows)
        {
            if (report.KnownGaps.TryGetValue(row.Fork, out var note))
            {
                sb.AppendLine($"- **{row.Fork}:** {note}");
            }
        }

        return sb.ToString();
    }

    public static async Task WriteReportAsync(
        ComplianceScorecardReport report,
        string outputPath,
        CancellationToken ct = default)
    {
        await File.WriteAllTextAsync(outputPath, RenderMarkdown(report), Encoding.UTF8, ct);
    }

    public static string ClassifyMismatch(StateDiscrepancy discrepancy) => discrepancy.Category;
}

public sealed record ForkScoreRow(
    string Fork,
    int TotalCases,
    int PassedCases,
    int FailedCases,
    IReadOnlyDictionary<string, int> MismatchTaxonomy)
{
    public double PassRatePercent =>
        TotalCases == 0 ? 0.0 : (double)PassedCases / TotalCases * 100.0;
}

public sealed record ComplianceScorecardReport(
    string FixturesRoot,
    int MaxCasesPerFork,
    IReadOnlyList<ForkScoreRow> Rows,
    IReadOnlyDictionary<string, string> KnownGaps);
