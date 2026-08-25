using Schlieren.Harvest.Execution;
using Xunit;

namespace Schlieren.Harvest.Tests.Execution;

/// <summary>
/// WorkerExitClassifier tests.
///
/// Contract per Task 6 Step 6:
///   WorkerTerminationKind values: Completed, TimedOut, Cancelled, Crashed, ProtocolError.
///   The classifier maps (exitCode, hasValidResponse, timedOut, cancelled) → kind.
///   Every non-Completed kind must produce a non-pass case artifact.
/// </summary>
public class WorkerExitClassifierTests
{
    // ── Completed (exit 0 + valid response) ──────────────────────────────

    [Fact]
    public void Classify_ZeroExitValidResponse_IsCompleted()
    {
        var kind = WorkerExitClassifier.Classify(
            exitCode: 0,
            hasValidResponse: true,
            timedOut: false,
            cancelled: false);

        Assert.Equal(WorkerTerminationKind.Completed, kind);
    }

    // ── ProtocolError (exit 0 but missing / invalid response) ────────────

    [Fact]
    public void Classify_ZeroExitMissingResponse_IsProtocolError()
    {
        var kind = WorkerExitClassifier.Classify(
            exitCode: 0,
            hasValidResponse: false,
            timedOut: false,
            cancelled: false);

        Assert.Equal(WorkerTerminationKind.ProtocolError, kind);
    }

    // ── Crashed (nonzero exit) ────────────────────────────────────────────

    [Fact]
    public void Classify_NonzeroExit_IsCrashed()
    {
        var kind = WorkerExitClassifier.Classify(
            exitCode: 1,
            hasValidResponse: false,
            timedOut: false,
            cancelled: false);

        Assert.Equal(WorkerTerminationKind.Crashed, kind);
    }

    [Fact]
    public void Classify_NegativeExit_IsCrashed()
    {
        // Negative exit codes (e.g. -1073741819 on Windows for access violation)
        var kind = WorkerExitClassifier.Classify(
            exitCode: -1,
            hasValidResponse: false,
            timedOut: false,
            cancelled: false);

        Assert.Equal(WorkerTerminationKind.Crashed, kind);
    }

    // ── TimedOut ─────────────────────────────────────────────────────────

    [Fact]
    public void Classify_TimedOut_IsTimedOut()
    {
        var kind = WorkerExitClassifier.Classify(
            exitCode: 0,
            hasValidResponse: false,
            timedOut: true,
            cancelled: false);

        Assert.Equal(WorkerTerminationKind.TimedOut, kind);
    }

    // ── Cancelled ────────────────────────────────────────────────────────

    [Fact]
    public void Classify_Cancelled_IsCancelled()
    {
        var kind = WorkerExitClassifier.Classify(
            exitCode: 0,
            hasValidResponse: false,
            timedOut: false,
            cancelled: true);

        Assert.Equal(WorkerTerminationKind.Cancelled, kind);
    }

    // ── Non-pass contract: every non-Completed kind is non-pass ──────────

    [Theory]
    [InlineData(WorkerTerminationKind.TimedOut)]
    [InlineData(WorkerTerminationKind.Cancelled)]
    [InlineData(WorkerTerminationKind.Crashed)]
    [InlineData(WorkerTerminationKind.ProtocolError)]
    public void NonCompletedKind_IsNonPass(WorkerTerminationKind kind)
    {
        Assert.True(WorkerExitClassifier.IsNonPass(kind),
            $"{kind} must produce a non-pass case artifact");
    }

    [Fact]
    public void Completed_IsNotNonPass()
    {
        Assert.False(WorkerExitClassifier.IsNonPass(WorkerTerminationKind.Completed));
    }
}
