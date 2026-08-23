using System.Numerics;
using Schlieren.Core.Forks;
using Schlieren.Core.Gas;

namespace Schlieren.Tests.Gas;

public sealed class CopyOpcodeGasRuleTests
{
    [Theory]
    [InlineData("OP.CALLDATACOPY")]
    [InlineData("OP.CODECOPY")]
    [InlineData("OP.RETURNDATACOPY")]
    public void Calculate_ComposesBaseCopyWordsAndExpansion(string id)
    {
        var result = CopyOpcodeGasRule.For(new GasRuleId(id)).Calculate(
            new MemoryGasContext(0, 0, 33),
            Fork.Osaka);

        Assert.Equal(15UL, result.ChargedGas);
        Assert.Equal(3, (int)result.Components.Single(c => c.Id == "base").Amount);
        Assert.Equal(6, (int)result.Components.Single(c => c.Id == "copy-words").Amount);
        Assert.Equal(6, (int)result.Components.Single(c => c.Id == "memory-expansion").Amount);
    }

    [Fact]
    public void ZeroLength_HasBaseOnlyAndIgnoresHugeDestination()
    {
        var result = CopyOpcodeGasRule.For(new GasRuleId("OP.CODECOPY")).Calculate(
            new MemoryGasContext(0, BigInteger.One << 255, 0),
            Fork.Frontier);

        Assert.Equal(3UL, result.ChargedGas);
    }
}
