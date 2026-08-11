using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using Scrutor.EELS.Tests.Harness;

namespace Scrutor.EELS.Tests.Conformance;

/// <summary>
/// Phase 2 unit coverage: Layer 1 bridge + taxonomy markdown emission.
/// Does not run the full fixture suite.
/// </summary>
public sealed class Layer1DiagnosisBridgeTests
{
    [Fact(DisplayName = "Layer1 — balance delta matches known gas constant")]
    public void DiagnoseCase_BalanceDelta_MatchesGasConstant()
    {
        var sender = Address.FromHex("0xa94f5374fce5edbc8e2a8697c15331677e6ebf0b");
        var testCase = MakeCase(sender, gasPrice: 10);
        // actual = expected + 3000 * gasPrice  → +3000 gas (ECRECOVER constant)
        var expected = BigInteger.Parse("1000000");
        var actual = expected + (3000 * 10);
        var report = new EelsCaseExecutionReport(
            CaseId: "case-ecrecover-gas",
            ExecutionSucceeded: true,
            GasUsed: 21_000,
            GasRefundCounter: 0,
            StateMatches: false,
            ReceiptStatusMatches: true,
            Mismatches: new[]
            {
                $"balance mismatch for {sender}: expected=0x{expected:x}, actual=0x{actual:x}"
            });

        var diagnoses = Layer1DiagnosisBridge.DiagnoseCase(testCase, report);

        Assert.NotEmpty(diagnoses);
        Assert.Contains(diagnoses, d =>
            d.Category is "gas_constant_match" or "gas_multiple_match" &&
            d.Summary.Contains("ECRECOVER", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "Layer1 — sender nonce +1 ⇒ tx applied when should reject")]
    public void DiagnoseCase_SenderNoncePlusOne_TxShouldReject()
    {
        var sender = Address.FromHex("0xa94f5374fce5edbc8e2a8697c15331677e6ebf0b");
        var testCase = MakeCase(sender, gasPrice: 1);
        var report = new EelsCaseExecutionReport(
            CaseId: "case-nonce",
            ExecutionSucceeded: true,
            GasUsed: 21_000,
            GasRefundCounter: 0,
            StateMatches: false,
            ReceiptStatusMatches: true,
            Mismatches: new[]
            {
                $"nonce mismatch for {sender}: expected=0, actual=1"
            });

        var diagnoses = Layer1DiagnosisBridge.DiagnoseCase(testCase, report);

        Assert.Contains(diagnoses, d => d.Category == "tx_applied_when_should_reject");
    }

    [Fact(DisplayName = "Layer1 — CREATE lifecycle cluster (≥2 signals)")]
    public void DiagnoseCase_CreateLifecycle_FiresOnCluster()
    {
        var sender = Address.FromHex("0xa94f5374fce5edbc8e2a8697c15331677e6ebf0b");
        var contract = Address.FromHex("0x00000000000000000000000000000000000000aa");
        var testCase = MakeCase(sender, gasPrice: 1);
        var report = new EelsCaseExecutionReport(
            CaseId: "case-create",
            ExecutionSucceeded: true,
            GasUsed: 50_000,
            GasRefundCounter: 0,
            StateMatches: false,
            ReceiptStatusMatches: true,
            Mismatches: new[]
            {
                $"missing account in actual state: {contract}",
                $"nonce mismatch for {contract}: expected=1, actual=0",
                $"code mismatch for {contract}"
            });

        var diagnoses = Layer1DiagnosisBridge.DiagnoseCase(testCase, report);

        Assert.Contains(diagnoses, d => d.Category == "create_lifecycle");
        Assert.Contains(diagnoses, d => d.Category == "create_not_executed");
    }

    [Fact(DisplayName = "Layer1 — aggregate + RenderMarkdown includes Diagnosis section")]
    public void AggregateAndRender_IncludesLayer1Section()
    {
        var dx = new DivergenceDiagnostics.Diagnosis(
            Category: "gas_constant_match",
            Summary: "Exactly undercharged by ECRECOVER (3000 gas) — EIP-1",
            ProtocolRule: "EIP-1",
            CodeBoundary: "Precompiles.cs → EcRecover()",
            Confidence: DivergenceDiagnostics.Confidence.High,
            Evidence: "delta=-3000 gas = ECRECOVER");

        var buckets = Layer1DiagnosisBridge.Aggregate(new[]
        {
            ("case-a", dx),
            ("case-b", dx),
            ("case-c", dx),
        });

        Assert.Single(buckets);
        Assert.Equal(3, buckets[0].Occurrences);

        var report = new TaxonomyReport(
            FixturesRoot: "/tmp/fixtures",
            Fork: "Osaka",
            TotalCases: 10,
            PassedCases: 7,
            FailedCases: 3,
            CategoryBuckets: new Dictionary<string, (int count, List<string> examples)>
            {
                ["balance"] = (3, new List<string> { "balance mismatch …" })
            },
            TopDeltaBuckets: Array.Empty<KeyValuePair<BigInteger, int>>(),
            AddressHotSpots: new Dictionary<string, int>(),
            MaxCases: 10,
            Layer1Diagnoses: buckets);

        var md = EelsTaxonomyAnalyzer.RenderMarkdown(report);

        Assert.Contains("## Layer 1 Diagnoses", md, StringComparison.Ordinal);
        Assert.Contains("gas_constant_match", md, StringComparison.Ordinal);
        Assert.Contains("ECRECOVER", md, StringComparison.Ordinal);
        Assert.Contains("Layer 1 top hit", md, StringComparison.Ordinal);
        Assert.Contains("case-a", md, StringComparison.Ordinal);
    }

    private static EelsStateCase MakeCase(Address sender, int gasPrice)
    {
        var tx = new Transaction
        {
            From = sender,
            To = Address.FromHex("0x0000000000000000000000000000000000000001"),
            Value = BigInteger.Zero,
            Nonce = 0,
            GasPrice = gasPrice,
            GasLimit = 100_000,
            Data = Array.Empty<byte>(),
            TxType = 0,
        };

        return new EelsStateCase(
            FixturePath: @"C:\fixtures\state_tests\osaka\eip7951_p256verify\test.json",
            CaseId: "synthetic",
            ForkName: "Osaka",
            BlockContext: new BlockContext
            {
                Number = 1,
                BaseFeePerGas = 0,
                GasLimit = 30_000_000,
            },
            Sender: sender,
            Transaction: tx,
            PreState: new Dictionary<Address, EelsFixtureAccount>(),
            ExpectedPostState: new Dictionary<Address, EelsFixtureAccount>(),
            ExpectedReceiptStatus: true);
    }
}
