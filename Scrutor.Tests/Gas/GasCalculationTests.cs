using System.Numerics;
using Scrutor.Core.Forks;
using Scrutor.Core.Gas;

namespace Scrutor.Tests.Gas;

public sealed class GasCalculationTests
{
    private static readonly GasRuleId RuleId = new("CALL.VALUE_TRANSFER");

    [Fact]
    public void GasRuleId_RejectsBlankValue()
    {
        Assert.Throws<ArgumentException>(() => new GasRuleId("  "));
    }

    [Fact]
    public void Create_CopiesInputsAndValidatesComponentTotals()
    {
        var components = new[]
        {
            new GasComponent("base", "CALL base", GasComponentKind.Charge, 100, "warm_access"),
            new GasComponent("value", "Value transfer", GasComponentKind.Charge, 9_000, "value != 0"),
            new GasComponent("refund", "Refund delta", GasComponentKind.RefundCounter, -2_400, "clear slot")
        };
        var decisions = new[]
        {
            new GasDecision("warm", "Target is warm", "true", "warm", new[] { "cold" })
        };
        var metadata = new GasRuleMetadata(
            RuleId, "Calls", Fork.Berlin, "EIP-2929", "SystemOpcodes.cs");

        var calculation = GasCalculation.Create(
            metadata, Fork.Berlin, 9_100, -2_400,
            GasDisposition.Charge, components, decisions);

        components[0] = components[0] with { Amount = BigInteger.Zero };
        decisions[0] = decisions[0] with { SelectedBranch = "cold" };

        Assert.Equal((ulong)9_100, calculation.ChargedGas);
        Assert.Equal(-2_400, calculation.RefundCounterDelta);
        Assert.Equal(new BigInteger(100), calculation.Components[0].Amount);
        Assert.Equal("warm", calculation.Decisions[0].SelectedBranch);
    }

    [Fact]
    public void Create_RejectsChargeComponentMismatch()
    {
        var metadata = new GasRuleMetadata(
            RuleId, "Calls", Fork.Berlin, "EIP-2929", "SystemOpcodes.cs");

        var ex = Assert.Throws<ArgumentException>(() => GasCalculation.Create(
            metadata, Fork.Berlin, 2_600, 0, GasDisposition.Charge,
            new[] { new GasComponent("access", "Access", GasComponentKind.Charge, 100) },
            Array.Empty<GasDecision>()));

        Assert.Contains("charged gas", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_RejectsRefundComponentMismatch()
    {
        var metadata = new GasRuleMetadata(
            RuleId, "Storage", Fork.London, "EIP-3529", "StorageOpcodes.cs");

        var ex = Assert.Throws<ArgumentException>(() => GasCalculation.Create(
            metadata, Fork.London, 0, 4_800, GasDisposition.RefundCounterDelta,
            new[] { new GasComponent("refund", "Refund", GasComponentKind.RefundCounter, 15_000) },
            Array.Empty<GasDecision>()));

        Assert.Contains("refund", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
