using Schlieren.Core.Execution;
using Schlieren.Core.Forks;
using Schlieren.Core.Gas;

namespace Schlieren.Tests.Gas;

public sealed class GasJournalTests
{
    [Fact]
    public void NullSink_IsDisabledAndAcceptsEntries()
    {
        var sink = NullGasJournalSink.Instance;

        sink.Append(Entry(1));

        Assert.False(sink.IsEnabled);
    }

    [Fact]
    public void InMemoryJournal_ReturnsImmutableSnapshots()
    {
        var journal = new InMemoryGasJournal();
        journal.Append(Entry(1));

        var firstSnapshot = journal.Entries;
        journal.Append(Entry(2));

        Assert.True(journal.IsEnabled);
        Assert.Single(firstSnapshot);
        Assert.Equal(2, journal.Entries.Count);
    }

    [Fact]
    public void Append_RejectsDuplicateOrDecreasingSequence()
    {
        var journal = new InMemoryGasJournal();
        journal.Append(Entry(2));

        var duplicate = Assert.Throws<GasJournalException>(() => journal.Append(Entry(2)));
        var decreasing = Assert.Throws<GasJournalException>(() => journal.Append(Entry(1)));

        Assert.Contains("strictly increasing", duplicate.Message);
        Assert.Contains("strictly increasing", decreasing.Message);
    }

    [Fact]
    public void Append_RejectsFrameAsItsOwnParent()
    {
        var journal = new InMemoryGasJournal();

        var ex = Assert.Throws<GasJournalException>(() =>
            journal.Append(Entry(1, frameId: 7, parentFrameId: 7)));

        Assert.Contains("own parent", ex.Message);
    }

    [Fact]
    public void Append_RequiresRelatedSequenceToAlreadyExist()
    {
        var journal = new InMemoryGasJournal();

        var ex = Assert.Throws<GasJournalException>(() =>
            journal.Append(Entry(2, relatedSequence: 1)));

        Assert.Contains("related sequence 1", ex.Message, StringComparison.OrdinalIgnoreCase);

        journal.Append(Entry(1));
        journal.Append(Entry(2, relatedSequence: 1));
        Assert.Equal(2, journal.Entries.Count);
    }

    private static GasJournalEntry Entry(
        long sequence,
        long frameId = 1,
        long? parentFrameId = null,
        long? relatedSequence = null)
    {
        var metadata = new GasRuleMetadata(
            new GasRuleId("OP.ADD"), "Opcode", Fork.Frontier, "Yellow Paper", "ArithmeticOpcodes.cs");
        var calculation = GasCalculation.Create(
            metadata,
            Fork.Frontier,
            3,
            0,
            GasDisposition.Charge,
            new[] { new GasComponent("base", "Base", GasComponentKind.Charge, 3) },
            Array.Empty<GasDecision>());

        return new GasJournalEntry(
            sequence,
            "tx-1",
            frameId,
            parentFrameId,
            CallType.Root,
            0,
            null,
            null,
            0,
            "ADD",
            100,
            97,
            GasMovementKind.Charge,
            relatedSequence,
            calculation,
            true,
            null);
    }
}
