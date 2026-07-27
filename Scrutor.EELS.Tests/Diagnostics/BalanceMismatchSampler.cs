// Diagnostics/BalanceMismatchSampler.cs
// Run with:
//   EELS_FIXTURES_ROOT=C:/projects/Scrutor/fixtures/state_tests/cancun
//   EELS_INCLUDE_SUBDIRS=1
// Purpose: dump first N balance-mismatch cases with all relevant values so we
// can identify the exact accounting delta.

using System.Numerics;
using Scrutor.EELS.Tests.Harness;

namespace Scrutor.EELS.Tests.Diagnostics;

public sealed class BalanceMismatchSampler
{
    [Fact]
    public async Task Dump_First5_BalanceMismatches()
    {
        var options = EelsHarnessOptions.FromEnvironment() with
        {
            IncludeSubdirectories = true,
            MaxCases = int.MaxValue
        };

        var loader   = new EelsStateFixtureLoader();
        var cases    = loader.LoadCases(options);
        if (cases.Count == 0)
        {
            Assert.Fail("No cases loaded — set EELS_FIXTURES_ROOT.");
            return;
        }

        var executor = new EelsStateFixtureExecutor();
        var reports = new List<EelsCaseExecutionReport>();
        foreach (var tc in cases)
        {
            reports.Add(await executor.ExecuteAsync(tc));
        }

        var groups = new Dictionary<string, List<string>>();

        foreach (var (tc, report) in cases.Zip(reports))
        {
            var balHits = report.Mismatches
                .Where(m => m.StartsWith("balance mismatch", StringComparison.Ordinal))
                .ToList();

            if (balHits.Count == 0) continue;

            foreach (var m in balHits)
            {
                // extract expected and actual
                var parts = m.Split(new[] { "expected=", ", actual=" }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3)
                {
                    var expectedHex = parts[1];
                    var actualHex = parts[2];
                    var expInt = expectedHex.StartsWith("0x") ? BigInteger.Parse(expectedHex.Substring(2), System.Globalization.NumberStyles.HexNumber) : BigInteger.Parse(expectedHex);
                    var actInt = actualHex.StartsWith("0x") ? BigInteger.Parse(actualHex.Substring(2), System.Globalization.NumberStyles.HexNumber) : BigInteger.Parse(actualHex);
                    var delta = expInt - actInt;
                    var key = delta.ToString();

                    if (!groups.ContainsKey(key)) groups[key] = new List<string>();
                    groups[key].Add(tc.CaseId);
                }
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== BALANCE MISMATCH CLUSTERS ===");
        foreach (var kvp in groups.OrderByDescending(g => g.Value.Count))
        {
            sb.AppendLine($"Delta: {kvp.Key} | Count: {kvp.Value.Count}");
            sb.AppendLine($"Sample Families: {string.Join(", ", kvp.Value.Take(3))}");
            sb.AppendLine();
        }

        Assert.Fail(sb.ToString());
    }
}
