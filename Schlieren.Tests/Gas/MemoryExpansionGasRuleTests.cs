using System.Numerics;
using Schlieren.Core.Forks;
using Schlieren.Core.Gas;

namespace Schlieren.Tests.Gas;

public sealed class MemoryExpansionGasRuleTests
{
    private readonly MemoryExpansionGasRule _rule = new();

    [Theory]
    [InlineData(0, 0, 32, 3)]
    [InlineData(32, 32, 32, 3)]
    [InlineData(0, 0, 736, 70)]
    public void Calculate_ChargesDifferenceBetweenMemoryCostCurves(
        int currentSize, int offset, int length, ulong expected)
    {
        var result = _rule.Calculate(
            new MemoryGasContext(currentSize, offset, length),
            Fork.Osaka);

        Assert.Equal(new GasRuleId("MEMORY.EXPANSION"), result.RuleId);
        Assert.Equal(expected, result.ChargedGas);
        Assert.Equal("new-memory-cost", result.Components[0].Id);
        Assert.Equal("old-memory-cost", result.Components[1].Id);
    }

    [Fact]
    public void Calculate_ZeroLengthDoesNotExpandEvenWithHugeOffset()
    {
        var result = _rule.Calculate(
            new MemoryGasContext(64, BigInteger.Pow(2, 255), BigInteger.Zero),
            Fork.Frontier);

        Assert.Equal(0UL, result.ChargedGas);
        Assert.Equal("unchanged", result.Decisions.Single().SelectedBranch);
    }

    [Fact]
    public void Calculate_RejectsCurrentSizeThatIsNotWordAligned()
    {
        Assert.Throws<ArgumentException>(() => _rule.Calculate(
            new MemoryGasContext(1, 0, 32),
            Fork.Osaka));
    }

    [Fact]
    public void Calculate_UsesMathematicalOperandsBeforeHostAllocationChecks()
    {
        var result = _rule.Calculate(
            new MemoryGasContext(0, int.MaxValue, 1),
            Fork.Osaka);

        Assert.Equal(8_796_294_348_800UL, result.ChargedGas);
    }
}

public sealed class MemoryExpansionIntegrationTests
{
    [Fact]
    public void EvmMemory_CalculateGasCost_DoesNotOverflowAtSignedIntegerBoundary()
    {
        var memory = new Schlieren.Core.Execution.EvmMemory();

        Assert.Equal(8_796_294_348_800UL, memory.CalculateGasCost(int.MaxValue));
    }
}
