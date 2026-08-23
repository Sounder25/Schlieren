using System.Text.RegularExpressions;
using Schlieren.Core.Gas;

namespace Schlieren.Tests.Gas;

public sealed partial class GasRuleCatalogTests
{
    [Fact]
    public void Catalog_ContainsEveryInventoryRuleExactlyOnce()
    {
        var catalogIds = GasRuleCatalog.All
            .Select(entry => entry.RuleId.Value)
            .ToArray();
        var inventoryIds = ReadInventoryIds();

        Assert.Equal(177, catalogIds.Length);
        Assert.Equal(177, catalogIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(inventoryIds.Order(StringComparer.Ordinal), catalogIds);
    }

    [Fact]
    public void Catalog_SeparatesProtocolAndDiagnosticRules()
    {
        Assert.Equal(168, GasRuleCatalog.ProtocolRules.Count);
        Assert.Equal(9, GasRuleCatalog.DiagnosticRules.Count);
        Assert.All(GasRuleCatalog.DiagnosticRules,
            entry => Assert.StartsWith("DIAG.", entry.RuleId.Value, StringComparison.Ordinal));
        Assert.DoesNotContain(GasRuleCatalog.ProtocolRules,
            entry => entry.DiagnosticOnly);
    }

    [Fact]
    public void GetRequired_ReportsUnknownRuleId()
    {
        var ex = Assert.Throws<GasScheduleException>(() =>
            GasRuleCatalog.GetRequired(new GasRuleId("UNKNOWN.RULE")));

        Assert.Contains("UNKNOWN.RULE", ex.Message);
    }

    private static string[] ReadInventoryIds()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "docs", "gas", "GAS_RULE_INVENTORY.md"));

        return File.ReadLines(path)
            .Select(line => InventoryRow().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    [GeneratedRegex(@"^\| ([A-Z][A-Z0-9_.-]+) \|")]
    private static partial Regex InventoryRow();
}
