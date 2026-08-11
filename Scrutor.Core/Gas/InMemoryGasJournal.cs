using System.Collections.ObjectModel;

namespace Scrutor.Core.Gas;

/// <summary>Validated append-only journal used by diagnostic execution.</summary>
public sealed class InMemoryGasJournal : IGasJournalSink
{
    private readonly object _sync = new();
    private readonly List<GasJournalEntry> _entries = new();
    private readonly HashSet<long> _sequences = new();

    public bool IsEnabled => true;

    public IReadOnlyList<GasJournalEntry> Entries
    {
        get
        {
            lock (_sync)
            {
                return new ReadOnlyCollection<GasJournalEntry>(_entries.ToArray());
            }
        }
    }

    public void Append(GasJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_sync)
        {
            Validate(entry);
            _entries.Add(entry);
            _sequences.Add(entry.Sequence);
        }
    }

    private void Validate(GasJournalEntry entry)
    {
        if (entry.Sequence < 0)
            throw new GasJournalException("Gas journal sequence cannot be negative.");
        if (_entries.Count > 0 && entry.Sequence <= _entries[^1].Sequence)
        {
            throw new GasJournalException(
                $"Gas journal sequences must be strictly increasing; received {entry.Sequence} " +
                $"after {_entries[^1].Sequence}.");
        }

        if (string.IsNullOrWhiteSpace(entry.TransactionId))
            throw new GasJournalException("Gas journal transaction ID cannot be blank.");
        if (entry.FrameId < 0)
            throw new GasJournalException("Gas journal frame ID cannot be negative.");
        if (entry.ParentFrameId == entry.FrameId)
            throw new GasJournalException($"Frame {entry.FrameId} cannot be its own parent.");
        if (entry.Depth < 0)
            throw new GasJournalException("Gas journal depth cannot be negative.");
        if (entry.Calculation is null)
            throw new GasJournalException("Gas journal calculation cannot be null.");

        if (entry.RelatedSequence is long related && !_sequences.Contains(related))
        {
            throw new GasJournalException(
                $"Gas journal related sequence {related} must refer to an earlier entry.");
        }
    }
}
