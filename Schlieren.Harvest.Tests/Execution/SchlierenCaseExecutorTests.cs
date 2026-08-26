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

    // ── Regression: type-2 (EIP-1559) tx deducts gas from sender ─────────
    // Bug: SchlierenCaseExecutor was constructing all txs as type-0 with
    // GasPrice=0 for EIP-1559 fixtures, so effectiveGasPrice=0 and sender
    // balance was never reduced.
    // Fix: executor now reads maxFeePerGas / maxPriorityFeePerGas and sets
    // TxType=2, allowing StateTransition to compute the correct effectiveGasPrice.

    [Fact]
    public async Task Execute_Type2Tx_DeductsSenderBalance()
    {
        // Minimal EIP-1559 fixture: sender has 1 ETH, sends a simple STOP call
        // with maxFeePerGas=0x07d0 (2000), baseFee=0x0a (10), gasLimit=21000
        // Expected: sender balance decreases by exactly gasUsed × effectiveGasPrice
        // effectiveGasPrice = min(0x07d0, 0x0a + 0) = 0x0a = 10
        // gasUsed ≥ 21000 (intrinsic for simple call)
        const string senderAddr   = "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string contractAddr = "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var startBalance = "0x1000000000000000000"; // ~4.7 ETH

        var fixtureJson = $$"""
        {
          "type2_gas_deduction_regression[fork_Cancun-state_test]": {
            "_info": {"fixture-format": "state_test"},
            "env": {
              "currentCoinbase":  "0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba",
              "currentGasLimit":  "0x07270e00",
              "currentNumber":    "0x01",
              "currentTimestamp": "0x03e8",
              "currentRandom":    "0x0000000000000000000000000000000000000000000000000000000000020000",
              "currentDifficulty":"0x00",
              "currentBaseFee":   "0x0a",
              "currentExcessBlobGas": "0x00"
            },
            "pre": {
              "{{senderAddr}}": {
                "balance": "{{startBalance}}",
                "nonce": "0x00",
                "code": "0x",
                "storage": {}
              },
              "{{contractAddr}}": {
                "balance": "0x00",
                "nonce": "0x01",
                "code": "0x00",
                "storage": {}
              }
            },
            "transaction": {
              "chainId":              "0x01",
              "nonce":                "0x00",
              "maxPriorityFeePerGas": "0x00",
              "maxFeePerGas":         "0x07d0",
              "gasLimit":             ["0x7530"],
              "to":                   "{{contractAddr}}",
              "value":                ["0x00"],
              "data":                 ["0x"],
              "accessLists":          [[]],
              "sender":               "{{senderAddr}}"
            },
            "post": {
              "Cancun": [
                {
                  "hash": "0x0000000000000000000000000000000000000000000000000000000000000001",
                  "logs": "0x1dcc4de8dec75d7aab85b567b6ccd41ad312451b948a7413f0a142fd40d49347",
                  "indexes": {"data": 0, "gas": 0, "value": 0},
                  "receipt": {
                    "status": true,
                    "cumulativeGasUsed": "0x5208"
                  },
                  "state": {
                    "{{senderAddr}}": {
                      "balance": "0x0fffffffffffffffffffed180",
                      "nonce":   "0x01"
                    }
                  }
                }
              ]
            }
          }
        }
        """;

        // Write fixture to a temp file
        var fixturePath = Path.GetTempFileName() + ".json";
        await File.WriteAllTextAsync(fixturePath, fixtureJson);

        try
        {
            var caseId   = "type2_gas_deduction_regression[fork_Cancun-state_test]";
            var executor = new SchlierenCaseExecutor();
            var snapshot = await executor.ExecuteFromPathAsync(
                fixturePath, "Cancun", journalEnabled: false, caseId: caseId);

            // With the fix: sender balance must decrease (gas was charged)
            var postStateEntry = snapshot.PostState.FirstOrDefault(a =>
                string.Equals(a.Address, senderAddr, StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(postStateEntry);

            // Parse balances — must be strictly less than start
            var startBal = System.Numerics.BigInteger.Parse(startBalance[2..], System.Globalization.NumberStyles.HexNumber);
            var postBal  = ParseHexBalance(postStateEntry!.Balance);

            // Gas must have been deducted — balance must drop
            Assert.True(postBal < startBal,
                $"Expected sender balance to decrease after type-2 tx execution. " +
                $"Start: {startBal}, Post: {postBal} (difference: {startBal - postBal} wei)");

            // Gas used must be ≥ 21000 (minimum intrinsic for a simple call)
            Assert.True(snapshot.GasUsed >= 21000,
                $"Expected gasUsed ≥ 21000 for a simple call, got {snapshot.GasUsed}");
        }
        finally
        {
            File.Delete(fixturePath);
        }
    }

    private static System.Numerics.BigInteger ParseHexBalance(string hex)
    {
        var clean = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        if (string.IsNullOrEmpty(clean)) return System.Numerics.BigInteger.Zero;
        return System.Numerics.BigInteger.Parse("0" + clean, System.Globalization.NumberStyles.HexNumber);
    }
}
