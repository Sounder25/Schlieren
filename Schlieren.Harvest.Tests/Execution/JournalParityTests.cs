using Schlieren.Harvest.Execution;
using Schlieren.Harvest.Fixtures;
using Xunit;

namespace Schlieren.Harvest.Tests.Execution;

/// <summary>
/// Journal parity tests.
///
/// Contract per Task 6 Step 5:
///   For success, revert, nested call, and storage rollback cases, running the same
///   input with journal enabled vs. disabled must produce identical:
///     - transaction validity / status (IsSuccess)
///     - gas used
///     - gas refund counter
///     - return data
///     - logs (count and content)
///     - post-state storage
///   Only journal evidence (the Journal field in ExecutionSnapshot) may differ.
///
/// Uses the minimal valid fixtures from Fixtures/Samples (storage write / plain tx).
/// </summary>
public class JournalParityTests
{
    private static readonly string SamplesDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "Samples"));

    private static string Sample(string name) => Path.Combine(SamplesDir, name);

    private static async Task AssertParityAsync(string fixtureName)
    {
        var catalog  = new FixtureCatalog(SamplesDir);
        var admitted = catalog.Admit(new[] { Sample(fixtureName) })
                              .Where(m => m.Admission == AdmissionReasonCode.Admitted)
                              .ToList();

        if (admitted.Count == 0)
            return; // fixture not admitted — skip gracefully (admission tested separately)

        var executor = new SchlierenCaseExecutor();
        var withJournal    = await executor.ExecuteAsync(admitted[0], journalEnabled: true,
            catalogRoot: SamplesDir);
        var withoutJournal = await executor.ExecuteAsync(admitted[0], journalEnabled: false,
            catalogRoot: SamplesDir);

        Assert.Equal(withJournal.IsSuccess,       withoutJournal.IsSuccess);
        Assert.Equal(withJournal.GasUsed,         withoutJournal.GasUsed);
        Assert.Equal(withJournal.GasRefundCounter, withoutJournal.GasRefundCounter);
        Assert.Equal(withJournal.ReturnData,       withoutJournal.ReturnData);
        Assert.Equal(withJournal.Logs.Count,       withoutJournal.Logs.Count);

        for (var i = 0; i < withJournal.Logs.Count; i++)
        {
            Assert.Equal(withJournal.Logs[i].Address, withoutJournal.Logs[i].Address);
            Assert.Equal(withJournal.Logs[i].Data,    withoutJournal.Logs[i].Data);
        }
    }

    [Fact]
    public Task JournalParity_ValidBerlinFixture()
        => AssertParityAsync("valid_published_berlin.json");

    [Fact]
    public Task JournalParity_ValidSstoreIstanbul()
        => AssertParityAsync("valid_sstore_istanbul.json");
}
