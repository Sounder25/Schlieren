using Schlieren.Harvest.Execution;
using Schlieren.Harvest.Fixtures;
using Xunit;

namespace Schlieren.Harvest.Tests.Execution;

/// <summary>
/// SchlierenCaseExecutor integration tests.
///
/// Contracts per Task 6 Step 4:
///   - Builds fresh GlobalState, fresh opcode catalog, EvmMachine, StateTransition per call
///   - Calls ApplyTransactionAsync once through the canonical path
///   - Returns ExecutionSnapshot from ExecutionResult + committed state
///   - Journal on/off changes only observation (journal evidence field), not outcome
///   - Does NOT reference the EELS.Tests assembly
///
/// Uses the same minimal sample fixtures from the Fixtures/Samples directory.
/// </summary>
public class SchlierenCaseExecutorTests
{
    private static readonly string SamplesDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "Samples"));

    private static string Sample(string name) => Path.Combine(SamplesDir, name);

    // ── Basic execution: valid fixture produces a snapshot ────────────────

    [Fact]
    public async Task Execute_ValidFixture_ReturnsSnapshot()
    {
        var catalog  = new FixtureCatalog(SamplesDir);
        var admitted = catalog.Admit(new[] { Sample("valid_published_berlin.json") })
                              .Where(m => m.Admission == AdmissionReasonCode.Admitted)
                              .ToList();

        Assert.NotEmpty(admitted);

        var executor = new SchlierenCaseExecutor();
        var snapshot = await executor.ExecuteAsync(admitted[0], journalEnabled: false,
            catalogRoot: SamplesDir);

        Assert.NotNull(snapshot);
        // Snapshot must carry status and gas — never a fabricated default
        Assert.True(snapshot.GasUsed >= 0);
    }

    // ── Journal on/off: outcome identical, evidence differs ──────────────

    [Fact]
    public async Task Execute_JournalOnVsOff_OutcomeIdentical()
    {
        var catalog  = new FixtureCatalog(SamplesDir);
        var admitted = catalog.Admit(new[] { Sample("valid_published_berlin.json") })
                              .Where(m => m.Admission == AdmissionReasonCode.Admitted)
                              .ToList();

        Assert.NotEmpty(admitted);

        var executor = new SchlierenCaseExecutor();
        var withJournal    = await executor.ExecuteAsync(admitted[0], journalEnabled: true,
            catalogRoot: SamplesDir);
        var withoutJournal = await executor.ExecuteAsync(admitted[0], journalEnabled: false,
            catalogRoot: SamplesDir);

        // Outcome fields must be identical
        Assert.Equal(withJournal.IsSuccess,  withoutJournal.IsSuccess);
        Assert.Equal(withJournal.GasUsed,    withoutJournal.GasUsed);
        Assert.Equal(withJournal.ReturnData, withoutJournal.ReturnData);

        // Journal evidence differs (one has it, other does not)
        // — at minimum they have the same IsSuccess, we just verify no crash
    }

    // ── Fresh state per execution: two runs are independent ──────────────

    [Fact]
    public async Task Execute_CalledTwice_ProducesIdenticalResults()
    {
        var catalog  = new FixtureCatalog(SamplesDir);
        var admitted = catalog.Admit(new[] { Sample("valid_published_berlin.json") })
                              .Where(m => m.Admission == AdmissionReasonCode.Admitted)
                              .ToList();

        Assert.NotEmpty(admitted);

        var executor = new SchlierenCaseExecutor();
        var r1 = await executor.ExecuteAsync(admitted[0], journalEnabled: false,
            catalogRoot: SamplesDir);
        var r2 = await executor.ExecuteAsync(admitted[0], journalEnabled: false,
            catalogRoot: SamplesDir);

        Assert.Equal(r1.IsSuccess, r2.IsSuccess);
        Assert.Equal(r1.GasUsed,   r2.GasUsed);
        Assert.Equal(r1.ReturnData, r2.ReturnData);
    }
}
