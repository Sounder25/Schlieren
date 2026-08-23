using Schlieren.Core.Execution;
using Schlieren.Core.Forks;
using Schlieren.Core.Gas;
using Schlieren.Core.State;

namespace Schlieren.Tests.Gas;

public sealed class TransactionIntrinsicGasScheduleTests
{
    [Fact]
    public void Osaka_ProducesAuditablePerRuleBreakdown()
    {
        var context = new TransactionGasContext(
            IsContractCreation: true,
            CalldataZeroBytes: 32,
            CalldataNonZeroBytes: 0,
            AccessListAddresses: 1,
            AccessListStorageKeys: 2,
            AuthorizationCount: 1,
            TransactionType: 4);

        var calculations = TransactionIntrinsicGasSchedule.Calculate(context, OsakaRules.Instance)
            .ToDictionary(item => item.RuleId.Value, item => item.ChargedGas);

        Assert.Equal(21_000UL, calculations["TX.BASE"]);
        Assert.Equal(32_000UL, calculations["TX.CREATE_SURCHARGE"]);
        Assert.Equal(128UL, calculations["TX.CALLDATA_ZERO"]);
        Assert.Equal(0UL, calculations["TX.CALLDATA_NONZERO"]);
        Assert.Equal(2_400UL, calculations["TX.ACCESS_LIST_ADDRESS"]);
        Assert.Equal(3_800UL, calculations["TX.ACCESS_LIST_STORAGE_KEY"]);
        Assert.Equal(2UL, calculations["TX.INITCODE_WORD"]);
        Assert.Equal(25_000UL, calculations["TX.AUTHORIZATION_COST"]);
        Assert.Equal(84_330UL, calculations.Values.Aggregate(0UL, GasMath.AddChecked));
    }

    [Fact]
    public void Frontier_DoesNotActivateHomesteadCreationSurcharge()
    {
        var context = new TransactionGasContext(true, 0, 0, 0, 0, 0, 0);

        var calculations = TransactionIntrinsicGasSchedule.Calculate(context, FrontierRules.Instance);

        Assert.DoesNotContain(calculations, item => item.RuleId.Value == "TX.CREATE_SURCHARGE");
        Assert.Equal(21_000UL, calculations.Aggregate(
            0UL,
            (total, item) => GasMath.AddChecked(total, item.ChargedGas)));
    }

    [Fact]
    public void IntrinsicGas_UsesExecutableScheduleForFrontierCreation()
    {
        var tx = new Transaction
        {
            To = null,
            Data = Array.Empty<byte>(),
            GasLimit = 100_000
        };

        Assert.Equal(21_000UL, IntrinsicGas.Compute(tx, FrontierRules.Instance));
        Assert.Equal(53_000UL, IntrinsicGas.Compute(tx, HomesteadRules.Instance));
    }
}
