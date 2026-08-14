using Schlieren.Core.Execution;

namespace Schlieren.Tests.Execution;

public sealed class StructuralPatternRulesTests
{
    [Fact]
    public void Eip2200_Stipend_FiresOn2300Delta()
    {
        var ctx = Base() with
        {
            HasBalanceMismatch = true,
            HasStorageMismatch = true,
            PrimaryBalanceDeltaGas = -2300
        };

        var hits = StructuralPatternRules.Evaluate(ctx);
        Assert.Contains(hits, d => d.Category == "struct_eip2200_stipend");
    }

    [Fact]
    public void Eip7610_CreateCollision_FiresOnLifecycleCluster()
    {
        var ctx = Base() with
        {
            EipFolder = "eip7610_create_collision",
            FixturePath = "/fixtures/state_tests/osaka/eip7610_create_collision/x.json",
            HasMissingAccount = true,
            HasNonceMismatch = true,
            HasCodeMismatch = true
        };

        var hits = StructuralPatternRules.Evaluate(ctx);
        Assert.Contains(hits, d => d.Category == "struct_eip7610_collision");
    }

    [Fact]
    public void Eip7825_FiresOnOsakaReceiptAcceptWhenShouldFail()
    {
        var ctx = Base() with
        {
            ForkName = "Osaka",
            IsOsakaOrLater = true,
            EipFolder = "eip7825_transaction_gas_limit",
            FixturePath = "/x/eip7825_transaction_gas_limit/y.json",
            ReceiptExpectedFailActualSuccess = true,
            SenderNoncePlusOne = true
        };

        var hits = StructuralPatternRules.Evaluate(ctx);
        Assert.Contains(hits, d => d.Category == "struct_eip7825_tx_gas_cap");
        Assert.Equal(DivergenceDiagnostics.Confidence.Certain,
            hits.First(d => d.Category == "struct_eip7825_tx_gas_cap").Confidence);
    }

    [Fact]
    public void Eip7883_ModExp_FiresOnFolderAndDelta()
    {
        var ctx = Base() with
        {
            EipFolder = "eip7883_modexp",
            FixturePath = "/osaka/eip7883_modexp/gas.json",
            HasBalanceMismatch = true,
            PrimaryBalanceDeltaGas = 500
        };

        var hits = StructuralPatternRules.Evaluate(ctx);
        Assert.Contains(hits, d => d.Category == "struct_eip7883_modexp");
    }

    [Fact]
    public void CoinbasePriorityFee_FiresWhenCoinbaseTouched()
    {
        var ctx = Base() with
        {
            HasBalanceMismatch = true,
            TouchesCoinbaseBalance = true,
            PrimaryBalanceDeltaGas = 1000
        };

        var hits = StructuralPatternRules.Evaluate(ctx);
        Assert.Contains(hits, d => d.Category == "struct_coinbase_priority_fee"
            && d.Confidence == DivergenceDiagnostics.Confidence.Medium);
    }

    [Fact]
    public void CoinbasePriorityFee_SuppressedWhenCreateSignalsPresent()
    {
        var ctx = Base() with
        {
            HasBalanceMismatch = true,
            TouchesCoinbaseBalance = true,
            HasCodeMismatch = true,
            HasMissingAccount = true,
            PrimaryBalanceDeltaGas = 1000
        };

        var hits = StructuralPatternRules.Evaluate(ctx);
        Assert.DoesNotContain(hits, d => d.Category == "struct_coinbase_priority_fee");
    }

    [Fact]
    public void Eip3529_RefundCap_FiresWhenDeltaMatchesCap()
    {
        var ctx = Base() with
        {
            HasBalanceMismatch = true,
            GasUsed = 100_000,
            GasRefundCounter = 50_000, // cap = 20000
            PrimaryBalanceDeltaGas = 20_000
        };

        var hits = StructuralPatternRules.Evaluate(ctx);
        Assert.Contains(hits, d => d.Category == "struct_eip3529_refund_cap");
    }

    [Fact]
    public void Evaluate_ReturnsOrderedByConfidence()
    {
        var ctx = Base() with
        {
            IsOsakaOrLater = true,
            ForkName = "Osaka",
            EipFolder = "eip7825_transaction_gas_limit",
            FixturePath = "/eip7825_transaction_gas_limit/a.json",
            ReceiptExpectedFailActualSuccess = true,
            SenderNoncePlusOne = true,
            HasBalanceMismatch = true,
            PrimaryBalanceDeltaGas = 999 // also triggers low residual
        };

        var hits = StructuralPatternRules.Evaluate(ctx);
        Assert.NotEmpty(hits);
        for (int i = 1; i < hits.Count; i++)
            Assert.True(hits[i - 1].Confidence >= hits[i].Confidence);
    }

    private static MismatchContext Base() => new(
        ForkName: "Cancun",
        FixturePath: "/fixtures/state_tests/cancun/other/x.json",
        EipFolder: "other",
        GasUsed: 21_000,
        GasRefundCounter: 0,
        HasBalanceMismatch: false,
        HasStorageMismatch: false,
        HasNonceMismatch: false,
        HasCodeMismatch: false,
        HasReceiptMismatch: false,
        HasMissingAccount: false,
        HasUnexpectedAccount: false,
        StorageWriteWhenExpectedEmpty: false,
        StorageEmptyWhenExpectedNonZero: false,
        BalanceActualBelowExpected: false,
        BalanceActualAboveExpected: false,
        PrimaryBalanceDeltaGas: null,
        ReceiptExpectedFailActualSuccess: false,
        ReceiptExpectedSuccessActualFail: false,
        SenderNoncePlusOne: false,
        ContractNonceZeroWhenExpectedOne: false,
        TouchesCoinbaseBalance: false,
        IsOsakaOrLater: false,
        IsPragueOrLater: false);
}
