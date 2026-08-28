namespace Schlieren.Harvest.Execution;

using Schlieren.Harvest.Domain;

/// <summary>
/// How a worker child process terminated.
/// Defined here (in Schlieren.Harvest) so the domain stays self-contained.
/// Note: WorkerProtocol.cs in Schlieren.Harvest/Worker/ defines the same enum
/// for the wire protocol; they are intentionally separate to avoid coupling.
/// </summary>
public enum WorkerTerminationKind
{
    Completed,
    TimedOut,
    Cancelled,
    Crashed,
    ProtocolError
}

/// <summary>
/// Classifies a worker process termination into a <see cref="WorkerTerminationKind"/>.
///
/// Rules (per Task 6 Step 6):
///   - exit 0 + valid response → Completed
///   - exit 0 + missing/invalid response → ProtocolError
///   - timedOut=true → TimedOut  (checked before exit code)
///   - cancelled=true → Cancelled
///   - nonzero exit → Crashed
/// </summary>
public static class WorkerExitClassifier
{
    public static WorkerTerminationKind Classify(
        int  exitCode,
        bool hasValidResponse,
        bool timedOut,
        bool cancelled)
    {
        if (timedOut)   return WorkerTerminationKind.TimedOut;
        if (cancelled)  return WorkerTerminationKind.Cancelled;
        if (exitCode != 0) return WorkerTerminationKind.Crashed;
        return hasValidResponse
            ? WorkerTerminationKind.Completed
            : WorkerTerminationKind.ProtocolError;
    }

    /// <summary>
    /// Returns true for every kind that must produce a non-pass case artifact.
    /// Only <see cref="WorkerTerminationKind.Completed"/> is not a non-pass.
    /// </summary>
    public static bool IsNonPass(WorkerTerminationKind kind) =>
        kind != WorkerTerminationKind.Completed;

    public static ApparatusFailureKind ToApparatusFailure(WorkerTerminationKind kind) => kind switch
    {
        WorkerTerminationKind.TimedOut => ApparatusFailureKind.WorkerTimeout,
        WorkerTerminationKind.Cancelled => ApparatusFailureKind.Cancelled,
        WorkerTerminationKind.Crashed => ApparatusFailureKind.WorkerCrash,
        WorkerTerminationKind.ProtocolError => ApparatusFailureKind.WorkerProtocol,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind,
            "Completed execution has no apparatus failure classification.")
    };
}
