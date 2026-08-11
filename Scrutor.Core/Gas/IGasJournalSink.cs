namespace Scrutor.Core.Gas;

public interface IGasJournalSink
{
    bool IsEnabled { get; }
    void Append(GasJournalEntry entry);
}
