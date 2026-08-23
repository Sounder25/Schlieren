using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.State;
using System.Text.Json;

namespace Schlieren.Tests.Execution;

public sealed class ExecutionJournalTests
{
    [Fact]
    public void Journal_AssignsStableFrameIdsAndStrictEventSequence()
    {
        var journal = new ExecutionJournal();
        long rootFrame = journal.OpenFrame(parentFrameId: null);
        long childFrame = journal.OpenFrame(rootFrame);

        journal.Record(new TransactionStartedEvent
        {
            FrameId = rootFrame,
            ParentFrameId = null,
            GasLimit = 0,
            IsInternal = false
        });
        journal.Record(new TransactionStartedEvent
        {
            FrameId = childFrame,
            ParentFrameId = rootFrame,
            GasLimit = 0,
            IsInternal = true
        });

        Assert.Equal(1L, rootFrame);
        Assert.Equal(2L, childFrame);
        Assert.Equal(new long[] { 0, 1 }, journal.Events.Select(entry => entry.Sequence));
        Assert.IsNotType<List<ExecutionJournalEvent>>(journal.Events);
    }

    [Fact]
    public void JournalFlags_DefaultToDisabledAndAbsent()
    {
        Assert.False(new Transaction().EnableJournal);
        Assert.Null(ExecutionResult.Success(0).Journal);
    }

    [Fact]
    public void Journal_IsExcludedFromExecutionResultJson()
    {
        var result = ExecutionResult.Success(0) with
        {
            Journal = new ExecutionJournal()
        };

        var json = JsonSerializer.Serialize(result);

        Assert.DoesNotContain("\"Journal\"", json, StringComparison.Ordinal);
    }
}
