using Schlieren.Harvest.Comparison;
using Schlieren.Harvest.Domain;

namespace Schlieren.Harvest.Calibration;

/// <summary>
/// The six hand-authored calibration probe kinds.
/// Each probe exercises exactly one classification signal.
/// </summary>
public enum CalibrationProbeKind
{
    ExactMatch,
    GasMismatch,
    StatusMismatch,
    StorageMismatch,
    MalformedFixture,
    KilledWorker
}

/// <summary>
/// Result of running one calibration probe.
///
/// <see cref="ExpectedStatus"/> is hand-authored test data declared before the
/// comparator runs — it must never be set to comparator output.
/// <see cref="ActualStatus"/> is what the comparator actually returned.
/// <see cref="ClassifiedCorrectly"/> is true iff they match.
/// </summary>
public sealed record CalibrationProbeResult(
    CalibrationProbeKind    Kind,
    CaseStatus              ExpectedStatus,    // hand-authored
    CaseStatus              ActualStatus,      // comparator output
    IReadOnlyList<FieldDelta> Deltas,
    string?                 Detail = null)
{
    public bool ClassifiedCorrectly => ActualStatus == ExpectedStatus ||
        // MalformedFixture accepts either FixtureInvalid or HarnessError
        (Kind == CalibrationProbeKind.MalformedFixture &&
         (ActualStatus == CaseStatus.FixtureInvalid || ActualStatus == CaseStatus.HarnessError));
}

/// <summary>
/// Immutable record of a complete six-signal calibration run.
/// The apparatus gate passes only when all six probes classify correctly.
/// </summary>
public sealed record CalibrationRecord(
    DateTime                         RunUtc,
    IReadOnlyList<CalibrationProbeResult> ProbeResults,
    bool                             ApparatusGatePassed,
    string?                          GateFailureReason = null);
