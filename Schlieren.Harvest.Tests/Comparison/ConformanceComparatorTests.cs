using Schlieren.Harvest.Comparison;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Execution;
using System.Text.Json;
using Xunit;

namespace Schlieren.Harvest.Tests.Comparison;

/// <summary>
/// ConformanceComparator tests.
///
/// Contracts per Task 7 Step 1 + acceptance points:
///
/// FIELD ORDER (stable):
///   status → gas → refund → returnData → logs(index/address/topics/data)
///   → accounts(address) → nonce/balance/code/storage(slot)
///
/// ACCUMULATION: comparator does NOT stop at first mismatch — all fields are checked.
///
/// MISSING AUTHORITY: absent expected value is never Pass.
///   - Missing EELS snapshot (null oracle result)  → HarnessError
///   - Fixture has no post-state authority          → FixtureInvalid
///   - Journal evidence cannot satisfy absent EELS  → still non-Pass
///
/// TERMINAL-STATUS RULES (exact):
///   - No deltas    → Pass
///   - Any delta    → Divergence
///   - Admission defect → FixtureInvalid
///   - Parser fault → HarnessError
///   - Timeout/crash/cancel → Aborted
///   - Explicit quarantine only → Quarantined
/// </summary>
public class ConformanceComparatorTests
{
    // ── Helpers ──────────────────────────────────────────────────────────

    private static ExecutionSnapshot MakeSnapshot(
        bool isSuccess  = true,
        ulong gasUsed   = 21_000,
        long refund     = 0,
        string returnData = "0x",
        List<SnapshotLog>? logs = null,
        List<SnapshotAccount>? postState = null)
        => new(
            IsSuccess:          isSuccess,
            GasUsed:            gasUsed,
            GasRefundCounter:   refund,
            ReturnData:         returnData,
            Logs:               logs ?? new List<SnapshotLog>(),
            PostState:          postState ?? new List<SnapshotAccount>());

    private static SnapshotAccount MakeAccount(
        string address = "0xaaaa",
        ulong nonce = 0,
        string balance = "0x0",
        string code = "0x",
        Dictionary<string, string>? storage = null)
        => new(address, nonce, balance, code, storage ?? new Dictionary<string, string>());

    // ── Pass: no deltas ───────────────────────────────────────────────────

    [Fact]
    public void Compare_IdenticalSnapshots_IsPass()
    {
        var expected = MakeSnapshot(isSuccess: true, gasUsed: 21_000);
        var actual   = MakeSnapshot(isSuccess: true, gasUsed: 21_000);

        var result = ConformanceComparator.Compare(expected, actual);

        Assert.Equal(CaseStatus.Pass, result.Status);
        Assert.Empty(result.Deltas);
    }

    // ── Status mismatch ───────────────────────────────────────────────────

    [Fact]
    public void Compare_StatusMismatch_IsDivergenceWithStatusDelta()
    {
        var expected = MakeSnapshot(isSuccess: true);
        var actual   = MakeSnapshot(isSuccess: false);

        var result = ConformanceComparator.Compare(expected, actual);

        Assert.Equal(CaseStatus.Divergence, result.Status);
        Assert.Contains(result.Deltas, d => d.Layer == DiscrepancyLayer.Validity &&
                                            d.Kind  == DiscrepancyKind.Status);
    }

    // ── Gas mismatch ──────────────────────────────────────────────────────

    [Fact]
    public void Compare_GasMismatch_IsDivergenceWithGasDelta()
    {
        var expected = MakeSnapshot(gasUsed: 21_000);
        var actual   = MakeSnapshot(gasUsed: 21_500);

        var result = ConformanceComparator.Compare(expected, actual);

        Assert.Equal(CaseStatus.Divergence, result.Status);
        Assert.Contains(result.Deltas, d => d.Layer == DiscrepancyLayer.Gas &&
                                            d.Kind  == DiscrepancyKind.GasUsed);
    }

    // ── Accumulates ALL deltas, does not stop at first ────────────────────

    [Fact]
    public void Compare_ThreeMismatches_ReturnsThreeDeltas()
    {
        var expected = MakeSnapshot(isSuccess: true, gasUsed: 21_000, returnData: "0xaabb");
        var actual   = MakeSnapshot(isSuccess: false, gasUsed: 22_000, returnData: "0xccdd");

        var result = ConformanceComparator.Compare(expected, actual);

        Assert.Equal(CaseStatus.Divergence, result.Status);
        Assert.Equal(3, result.Deltas.Count);
    }

    // ── Delta stable order ────────────────────────────────────────────────

    [Fact]
    public void Compare_DeltaOrder_StatusBeforeGasBeforeReturnData()
    {
        var expected = MakeSnapshot(isSuccess: true, gasUsed: 21_000, returnData: "0xaa");
        var actual   = MakeSnapshot(isSuccess: false, gasUsed: 22_000, returnData: "0xbb");

        var result = ConformanceComparator.Compare(expected, actual);

        Assert.Equal(3, result.Deltas.Count);
        Assert.Equal(DiscrepancyLayer.Validity, result.Deltas[0].Layer);
        Assert.Equal(DiscrepancyLayer.Gas,      result.Deltas[1].Layer);
        Assert.Equal(DiscrepancyLayer.ReturnData, result.Deltas[2].Layer);
    }

    // ── Return data mismatch ──────────────────────────────────────────────

    [Fact]
    public void Compare_ReturnDataMismatch_ProducesReturnDataDelta()
    {
        var expected = MakeSnapshot(returnData: "0xdeadbeef");
        var actual   = MakeSnapshot(returnData: "0xcafebabe");

        var result = ConformanceComparator.Compare(expected, actual);

        Assert.Equal(CaseStatus.Divergence, result.Status);
        Assert.Contains(result.Deltas, d => d.Layer == DiscrepancyLayer.ReturnData &&
                                            d.Kind  == DiscrepancyKind.ReturnData);
    }

    // ── Log count mismatch ────────────────────────────────────────────────

    [Fact]
    public void Compare_LogCountMismatch_ProducesLogCountDelta()
    {
        var expected = MakeSnapshot(logs: new List<SnapshotLog>
        {
            new("0xaddr1", new[]{"0xtopic1"}, "0xdata1")
        });
        var actual = MakeSnapshot(logs: new List<SnapshotLog>());

        var result = ConformanceComparator.Compare(expected, actual);

        Assert.Equal(CaseStatus.Divergence, result.Status);
        Assert.Contains(result.Deltas, d => d.Layer == DiscrepancyLayer.Logs &&
                                            d.Kind  == DiscrepancyKind.LogCount);
    }

    // ── Log address mismatch ──────────────────────────────────────────────

    [Fact]
    public void Compare_LogAddressMismatch_ProducesLogAddressDelta()
    {
        var log1 = new SnapshotLog("0xaaaa", Array.Empty<string>(), "0x");
        var log2 = new SnapshotLog("0xbbbb", Array.Empty<string>(), "0x");

        var expected = MakeSnapshot(logs: new List<SnapshotLog> { log1 });
        var actual   = MakeSnapshot(logs: new List<SnapshotLog> { log2 });

        var result = ConformanceComparator.Compare(expected, actual);

        Assert.Equal(CaseStatus.Divergence, result.Status);
        Assert.Contains(result.Deltas, d => d.Layer == DiscrepancyLayer.Logs &&
                                            d.Kind  == DiscrepancyKind.LogAddress);
    }

    // ── Storage mismatch ──────────────────────────────────────────────────

    [Fact]
    public void Compare_StorageMismatch_ProducesStorageDelta()
    {
        var expAcct = MakeAccount(storage: new Dictionary<string, string> { ["0x1"] = "0xdeadbeef" });
        var actAcct = MakeAccount(storage: new Dictionary<string, string> { ["0x1"] = "0xcafebabe" });

        var expected = MakeSnapshot(postState: new List<SnapshotAccount> { expAcct });
        var actual   = MakeSnapshot(postState: new List<SnapshotAccount> { actAcct });

        var result = ConformanceComparator.Compare(expected, actual);

        Assert.Equal(CaseStatus.Divergence, result.Status);
        Assert.Contains(result.Deltas, d => d.Layer == DiscrepancyLayer.Storage &&
                                            d.Kind  == DiscrepancyKind.StorageValue);
    }

    // ── Nonce mismatch ────────────────────────────────────────────────────

    [Fact]
    public void Compare_NonceMismatch_ProducesNonceDelta()
    {
        var expAcct = MakeAccount(nonce: 1);
        var actAcct = MakeAccount(nonce: 2);

        var expected = MakeSnapshot(postState: new List<SnapshotAccount> { expAcct });
        var actual   = MakeSnapshot(postState: new List<SnapshotAccount> { actAcct });

        var result = ConformanceComparator.Compare(expected, actual);

        Assert.Equal(CaseStatus.Divergence, result.Status);
        Assert.Contains(result.Deltas, d => d.Kind == DiscrepancyKind.Nonce);
    }

    // ── Missing EELS oracle snapshot → HarnessError, never Pass ──────────

    [Fact]
    public void Compare_NullOracleSnapshot_IsHarnessError()
    {
        var actual = MakeSnapshot();
        var result = ConformanceComparator.CompareWithOracle(
            oracleSnapshot: null,
            schlierenSnapshot: actual,
            fixtureIsValid: true);

        Assert.Equal(CaseStatus.HarnessError, result.Status);
        Assert.NotEqual(CaseStatus.Pass, result.Status);
    }

    // ── Missing fixture post-state authority → FixtureInvalid ────────────

    [Fact]
    public void Compare_NullFixtureSnapshot_IsFixtureInvalid()
    {
        var result = ConformanceComparator.CompareWithOracle(
            oracleSnapshot: null,
            schlierenSnapshot: MakeSnapshot(),
            fixtureIsValid: false);

        Assert.Equal(CaseStatus.FixtureInvalid, result.Status);
    }

    // ── Journal evidence cannot satisfy missing EELS expectation ─────────

    [Fact]
    public void Compare_JournalOnlyWithNoOracleExpected_IsHarnessError()
    {
        // Journal evidence is present (non-null JournalEvidence in actual)
        // but oracle is absent — must still be HarnessError, not Pass
        var actualWithJournal = MakeSnapshot() with
        {
            JournalEvidence = new object() // pretend journal was recorded
        };

        var result = ConformanceComparator.CompareWithOracle(
            oracleSnapshot: null,
            schlierenSnapshot: actualWithJournal,
            fixtureIsValid: true);

        Assert.Equal(CaseStatus.HarnessError, result.Status);
    }

    // ── Terminal status: Aborted (never pass/divergence) ─────────────────

    [Fact]
    public void TerminalStatus_Aborted_IsAborted()
    {
        var result = ConformanceComparator.Aborted("process killed");
        Assert.Equal(CaseStatus.Aborted, result.Status);
        Assert.Empty(result.Deltas);
    }

    // ── Terminal status: Quarantined requires explicit approval ───────────

    [Fact]
    public void TerminalStatus_Quarantined_RequiresExplicitRecord()
    {
        var result = ConformanceComparator.Quarantined("independent evidence of fixture defect");
        Assert.Equal(CaseStatus.Quarantined, result.Status);
    }

    // ── Log topics mismatch ───────────────────────────────────────────────

    [Fact]
    public void Compare_LogTopicsMismatch_ProducesTopicsDelta()
    {
        var log1 = new SnapshotLog("0xaaaa", new[] { "0xtopic1" }, "0x");
        var log2 = new SnapshotLog("0xaaaa", new[] { "0xtopic2" }, "0x");

        var expected = MakeSnapshot(logs: new List<SnapshotLog> { log1 });
        var actual   = MakeSnapshot(logs: new List<SnapshotLog> { log2 });

        var result = ConformanceComparator.Compare(expected, actual);

        Assert.Equal(CaseStatus.Divergence, result.Status);
        Assert.Contains(result.Deltas, d => d.Kind == DiscrepancyKind.LogTopics);
    }

    // ── Log data mismatch ─────────────────────────────────────────────────

    [Fact]
    public void Compare_LogDataMismatch_ProducesLogDataDelta()
    {
        var log1 = new SnapshotLog("0xaaaa", Array.Empty<string>(), "0xaabb");
        var log2 = new SnapshotLog("0xaaaa", Array.Empty<string>(), "0xccdd");

        var expected = MakeSnapshot(logs: new List<SnapshotLog> { log1 });
        var actual   = MakeSnapshot(logs: new List<SnapshotLog> { log2 });

        var result = ConformanceComparator.Compare(expected, actual);

        Assert.Contains(result.Deltas, d => d.Kind == DiscrepancyKind.LogData);
    }

    // ── Balance mismatch ──────────────────────────────────────────────────

    [Fact]
    public void Compare_BalanceMismatch_ProducesBalanceDelta()
    {
        var expAcct = MakeAccount(balance: "0x100");
        var actAcct = MakeAccount(balance: "0x200");

        var expected = MakeSnapshot(postState: new List<SnapshotAccount> { expAcct });
        var actual   = MakeSnapshot(postState: new List<SnapshotAccount> { actAcct });

        var result = ConformanceComparator.Compare(expected, actual);

        Assert.Contains(result.Deltas, d => d.Kind == DiscrepancyKind.Balance);
    }

    [Theory]
    [InlineData("0x00", "0x0")]
    [InlineData("0x0281ca", "0x281ca")]
    [InlineData("0X000ABC", "0xabc")]
    public void Compare_EquivalentHexQuantityBalances_DoNotProduceDelta(
        string expectedBalance,
        string actualBalance)
    {
        var expected = MakeSnapshot(postState: new List<SnapshotAccount>
        {
            MakeAccount(balance: expectedBalance)
        });
        var actual = MakeSnapshot(postState: new List<SnapshotAccount>
        {
            MakeAccount(balance: actualBalance)
        });

        var result = ConformanceComparator.Compare(expected, actual);

        Assert.Equal(CaseStatus.Pass, result.Status);
        Assert.DoesNotContain(result.Deltas, d => d.Kind == DiscrepancyKind.Balance);
    }

    [Fact]
    public void Compare_EquivalentHexQuantityStorageValues_DoNotProduceDelta()
    {
        var expected = MakeSnapshot(postState: new List<SnapshotAccount>
        {
            MakeAccount(storage: new Dictionary<string, string> { ["0x01"] = "0x000a" })
        });
        var actual = MakeSnapshot(postState: new List<SnapshotAccount>
        {
            MakeAccount(storage: new Dictionary<string, string> { ["0x01"] = "0xa" })
        });

        var result = ConformanceComparator.Compare(expected, actual);

        Assert.Equal(CaseStatus.Pass, result.Status);
        Assert.DoesNotContain(result.Deltas, d => d.Kind == DiscrepancyKind.StorageValue);
    }
}
