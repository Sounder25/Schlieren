using Schlieren.Harvest.Domain;

namespace Schlieren.Harvest.Execution;

/// <summary>
/// Abstraction over an external execution oracle (EELS executable).
/// Returned by EelsProcessOracle; injectable for testing.
/// </summary>
public interface IReferenceOracle
{
    /// <summary>
    /// Runs the oracle against the given fixture file and returns the raw
    /// stdout output plus the process exit code. Never throws on process
    /// failure — callers classify via EelsOutputParser + WorkerExitClassifier.
    /// </summary>
    Task<OracleRunResult> RunAsync(
        string fixturePath,
        CancellationToken ct = default);
}

/// <summary>Raw result from an oracle process invocation.</summary>
public sealed record OracleRunResult(
    string Stdout,
    string Stderr,
    int    ExitCode,
    bool   TimedOut,
    ExecutionAttemptEvidence? AttemptEvidence = null);
