namespace Scrutor.Core.Gas;

/// <summary>No-allocation storage sink for normal execution.</summary>
public sealed class NullGasJournalSink : IGasJournalSink
{
    public static NullGasJournalSink Instance { get; } = new();

    private NullGasJournalSink()
    {
    }

    public bool IsEnabled => false;

    public void Append(GasJournalEntry entry)
    {
    }
}
