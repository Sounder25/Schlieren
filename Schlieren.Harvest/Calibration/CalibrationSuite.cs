using Schlieren.Harvest.Comparison;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Execution;

namespace Schlieren.Harvest.Calibration;

/// <summary>
/// Runs the six hand-authored calibration probes and returns a CalibrationRecord.
///
/// Each probe uses FIXED inputs that are independent of any live fixture corpus,
/// EELS executable, or running Schlieren instance. The expected classification for
/// each probe is declared here as static test data BEFORE the comparator runs —
/// it is never set to, or derived from, comparator output.
///
/// The apparatus gate passes only when all six probes classify exactly as expected.
/// </summary>
public static class CalibrationSuite
{
    // ── Hand-authored expected classifications (test data) ────────────────
    //
    // These are the GROUND TRUTH for the calibration gate.
    // They must NOT be changed to match comparator output.
    // If a probe fails, the comparator is broken — not the expectation.

    private static readonly IReadOnlyDictionary<CalibrationProbeKind, CaseStatus> ExpectedClassifications =
        new Dictionary<CalibrationProbeKind, CaseStatus>
        {
            [CalibrationProbeKind.ExactMatch]       = CaseStatus.Pass,
            [CalibrationProbeKind.GasMismatch]      = CaseStatus.Divergence,
            [CalibrationProbeKind.StatusMismatch]   = CaseStatus.Divergence,
            [CalibrationProbeKind.StorageMismatch]  = CaseStatus.Divergence,
            [CalibrationProbeKind.MalformedFixture] = CaseStatus.FixtureInvalid,  // or HarnessError — both accepted
            [CalibrationProbeKind.KilledWorker]     = CaseStatus.Aborted,
        };

    public static Task<CalibrationRecord> RunAsync(CancellationToken ct = default)
    {
        var results = new List<CalibrationProbeResult>
        {
            RunProbe1_ExactMatch(),
            RunProbe2_GasMismatch(),
            RunProbe3_StatusMismatch(),
            RunProbe4_StorageMismatch(),
            RunProbe5_MalformedFixture(),
            RunProbe6_KilledWorker(),
        };

        var allCorrect = results.All(r => r.ClassifiedCorrectly);
        var failReason = allCorrect ? null :
            string.Join("; ", results
                .Where(r => !r.ClassifiedCorrectly)
                .Select(r => $"{r.Kind}: expected {r.ExpectedStatus}, got {r.ActualStatus}"));

        var record = new CalibrationRecord(
            RunUtc:               DateTime.UtcNow,
            ProbeResults:         results,
            ApparatusGatePassed:  allCorrect,
            GateFailureReason:    failReason);

        return Task.FromResult(record);
    }

    // ── Probe 1: ExactMatch ───────────────────────────────────────────────
    // Expected: Pass
    // Input: identical expected and actual snapshots

    private static CalibrationProbeResult RunProbe1_ExactMatch()
    {
        var snapshot = MakeSnapshot(isSuccess: true, gasUsed: 21_000, returnData: "0x");
        var result   = ConformanceComparator.Compare(snapshot, snapshot);

        return MakeResult(CalibrationProbeKind.ExactMatch, result);
    }

    // ── Probe 2: GasMismatch ──────────────────────────────────────────────
    // Expected: Divergence
    // Input: expected gas=21000, actual gas=21500

    private static CalibrationProbeResult RunProbe2_GasMismatch()
    {
        var expected = MakeSnapshot(isSuccess: true, gasUsed: 21_000);
        var actual   = MakeSnapshot(isSuccess: true, gasUsed: 21_500);
        var result   = ConformanceComparator.Compare(expected, actual);

        return MakeResult(CalibrationProbeKind.GasMismatch, result);
    }

    // ── Probe 3: StatusMismatch ───────────────────────────────────────────
    // Expected: Divergence
    // Input: expected status=success, actual status=failure

    private static CalibrationProbeResult RunProbe3_StatusMismatch()
    {
        var expected = MakeSnapshot(isSuccess: true,  gasUsed: 21_000);
        var actual   = MakeSnapshot(isSuccess: false, gasUsed: 21_000);
        var result   = ConformanceComparator.Compare(expected, actual);

        return MakeResult(CalibrationProbeKind.StatusMismatch, result);
    }

    // ── Probe 4: StorageMismatch ──────────────────────────────────────────
    // Expected: Divergence
    // Input: expected storage slot 0x1=0xdeadbeef, actual slot 0x1=0xcafebabe

    private static CalibrationProbeResult RunProbe4_StorageMismatch()
    {
        var expAcct  = MakeAccount(storage: new Dictionary<string, string> { ["0x1"] = "0xdeadbeef" });
        var actAcct  = MakeAccount(storage: new Dictionary<string, string> { ["0x1"] = "0xcafebabe" });
        var expected = MakeSnapshot(postState: new List<SnapshotAccount> { expAcct });
        var actual   = MakeSnapshot(postState: new List<SnapshotAccount> { actAcct });
        var result   = ConformanceComparator.Compare(expected, actual);

        return MakeResult(CalibrationProbeKind.StorageMismatch, result);
    }

    // ── Probe 5: MalformedFixture ─────────────────────────────────────────
    // Expected: FixtureInvalid (or HarnessError — both accepted per spec)
    // Input: null oracle snapshot (apparatus could not parse fixture),
    //        fixtureIsValid=false (fixture failed admission)

    private static CalibrationProbeResult RunProbe5_MalformedFixture()
    {
        var result = ConformanceComparator.CompareWithOracle(
            oracleSnapshot:     null,
            schlierenSnapshot:  MakeSnapshot(),
            fixtureIsValid:     false);   // explicitly marked invalid

        return MakeResult(CalibrationProbeKind.MalformedFixture, result);
    }

    // ── Probe 6: KilledWorker ─────────────────────────────────────────────
    // Expected: Aborted
    // Input: worker process was killed (simulated via Aborted factory method)

    private static CalibrationProbeResult RunProbe6_KilledWorker()
    {
        var result = ConformanceComparator.Aborted("Worker process killed during execution");
        return MakeResult(CalibrationProbeKind.KilledWorker, result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static CalibrationProbeResult MakeResult(
        CalibrationProbeKind kind,
        ComparisonResult     result)
    {
        var expected = ExpectedClassifications[kind];
        return new CalibrationProbeResult(
            Kind:           kind,
            ExpectedStatus: expected,
            ActualStatus:   result.Status,
            Deltas:         result.Deltas,
            Detail:         result.Detail);
    }

    private static ExecutionSnapshot MakeSnapshot(
        bool isSuccess    = true,
        ulong gasUsed     = 21_000,
        string returnData = "0x",
        List<SnapshotAccount>? postState = null)
        => new(
            IsSuccess:          isSuccess,
            GasUsed:            gasUsed,
            GasRefundCounter:   0,
            ReturnData:         returnData,
            Logs:               Array.Empty<SnapshotLog>(),
            PostState:          (IReadOnlyList<SnapshotAccount>?)postState ?? Array.Empty<SnapshotAccount>());

    private static SnapshotAccount MakeAccount(
        string address = "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        Dictionary<string, string>? storage = null)
        => new(address, 0, "0x0", "0x",
            (IReadOnlyDictionary<string, string>?)storage ?? new Dictionary<string, string>());
}
