namespace Schlieren.Core.Execution.Journal;

/// <summary>
/// Projects the canonical journal gas tree into the frozen legacy display shape.
/// It copies journal values and performs no gas inference.
/// </summary>
public static class LegacyGasTreeProjection
{
    public static GasTreeNode FromCanonical(ExecutionResult result)
    {
        var journal = result.Journal ?? throw new InvalidOperationException(
            "Canonical execution journal is required to build a diagnostic gas tree.");
        return Map(JournalGasTree.Build(journal, result).Root);
    }

    private static GasTreeNode Map(JournalGasNode source)
    {
        var target = new GasTreeNode
        {
            Label = source.Label,
            Gas = source.Amount,
            RecordedTotalGas = source.TotalGas
        };
        foreach (var child in source.Children)
            target.Children.Add(Map(child));
        return target;
    }
}
