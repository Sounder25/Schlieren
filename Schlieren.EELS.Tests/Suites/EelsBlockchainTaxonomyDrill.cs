using System.Text;
using Schlieren.EELS.Tests.Conformance;
using Schlieren.EELS.Tests.Harness;

namespace Schlieren.EELS.Tests.Suites;

/// <summary>
/// Osaka official-gate is mostly state_tests. This drill runs the sibling
/// blockchain_tests tree (prelude, receipts, multi-tx blocks).
/// </summary>
public sealed class EelsBlockchainTaxonomyDrill
{
    [Fact(DisplayName = "EelsBlockchainTaxonomyDrill — Osaka blockchain_tests bucket report")]
    public async Task RunOsakaBlockchainTaxonomyAsync()
    {
        var defaultRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "fixtures", "blockchain_tests", "for_osaka"));

        var root = Environment.GetEnvironmentVariable("EELS_FIXTURES_ROOT");
        if (string.IsNullOrWhiteSpace(root))
            root = defaultRoot;

        var maxRaw = Environment.GetEnvironmentVariable("EELS_MAX_CASES");
        if (!int.TryParse(maxRaw, out var maxCases) || maxCases <= 0)
            maxCases = 20_000;

        var exclude = Environment.GetEnvironmentVariable("EELS_FIXTURES_EXCLUDE");
        if (string.IsNullOrWhiteSpace(exclude))
            exclude = "ported_static";

        var fork = Environment.GetEnvironmentVariable("EELS_REQUIRED_FORK");
        if (string.IsNullOrWhiteSpace(fork))
            fork = "Osaka";

        var opts = new EelsHarnessOptions(root, fork, maxCases, IncludeSubdirectories: true, exclude);
        var report = await EelsTaxonomyAnalyzer.RunBlockchainAsync(opts);

        var markdown = EelsTaxonomyAnalyzer.RenderMarkdown(report)
            .Replace("# EELS Taxonomy Drill Report", "# EELS Blockchain Taxonomy Drill (Osaka)");

        var outputDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults");
        Directory.CreateDirectory(outputDir);
        var outPath = Path.Combine(outputDir, $"blockchain_taxonomy_{DateTime.UtcNow:yyyyMMdd_HHmmss}.md");
        await File.WriteAllTextAsync(outPath, markdown, Encoding.UTF8);

        Console.WriteLine(markdown);
        Console.WriteLine();
        Console.WriteLine($"Wrote {outPath}");

        Assert.True(true, "Blockchain taxonomy drill complete — see TestResults/.");
    }
}
