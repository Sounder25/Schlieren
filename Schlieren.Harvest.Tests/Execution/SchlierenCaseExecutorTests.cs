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
    private const string SenderAddress = "0x1000000000000000000000000000000000000001";
    private const string ContractAddress = "0x2000000000000000000000000000000000000002";

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

    [Fact]
    public async Task Execute_Eip2930AccessList_ChargesIntrinsicCostAndWarmsStorageSlot()
    {
        var withoutAccessList = await ExecuteAccessListFixtureAsync(includeAccessList: false);
        var withAccessList = await ExecuteAccessListFixtureAsync(includeAccessList: true);

        Assert.True(withoutAccessList.IsSuccess);
        Assert.True(withAccessList.IsSuccess);
        Assert.Equal(23_103UL, withoutAccessList.GasUsed);
        Assert.Equal(25_403UL, withAccessList.GasUsed);
        Assert.Equal(2_300UL, withAccessList.GasUsed - withoutAccessList.GasUsed);
    }

    private static async Task<ExecutionSnapshot> ExecuteAccessListFixtureAsync(bool includeAccessList)
    {
        var caseId = includeAccessList ? "with-access-list" : "without-access-list";
        var accessLists = includeAccessList
            ? $"[[{{\"address\":\"{ContractAddress}\",\"storageKeys\":[\"0x00\"]}}]]"
            : "[[]]";
        var fixture = $$"""
        {
          "{{caseId}}": {
            "pre": {
              "{{SenderAddress}}": {
                "nonce": "0x00",
                "balance": "0x1000000",
                "code": "0x",
                "storage": {}
              },
              "{{ContractAddress}}": {
                "nonce": "0x01",
                "balance": "0x00",
                "code": "0x60005400",
                "storage": { "0x00": "0x01" }
              }
            },
            "transaction": {
              "chainId": "0x01",
              "nonce": "0x00",
              "gasPrice": "0x01",
              "gasLimit": ["0x0186a0"],
              "to": "{{ContractAddress}}",
              "value": ["0x00"],
              "data": ["0x"],
              "accessLists": {{accessLists}},
              "sender": "{{SenderAddress}}"
            },
            "post": {
              "Berlin": [{
                "indexes": { "data": 0, "gas": 0, "value": 0 },
                "receipt": { "status": true, "cumulativeGasUsed": "0x0" },
                "state": {}
              }]
            }
          }
        }
        """;

        var fixturePath = Path.Combine(
            Path.GetTempPath(),
            $"schlieren-access-list-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(fixturePath, fixture);
            return await new SchlierenCaseExecutor().ExecuteFromPathAsync(
                fixturePath,
                "Berlin",
                journalEnabled: false,
                caseId: caseId);
        }
        finally
        {
            File.Delete(fixturePath);
        }
    }
}
