using Schlieren.Core.Forks;
using Schlieren.Core.Gas;

namespace Schlieren.Tests.Gas;

public sealed class FixedOpcodeGasScheduleTests
{
    [Theory]
    [InlineData("OP.ADD", 3)]
    [InlineData("OP.MUL", 5)]
    [InlineData("OP.SUB", 3)]
    [InlineData("OP.DIV", 5)]
    [InlineData("OP.SDIV", 5)]
    [InlineData("OP.MOD", 5)]
    [InlineData("OP.SMOD", 5)]
    [InlineData("OP.ADDMOD", 8)]
    [InlineData("OP.MULMOD", 8)]
    [InlineData("OP.SIGNEXTEND", 5)]
    public void ArithmeticRule_ReturnsNamedCalculation(string ruleId, ulong expected)
    {
        var result = FixedOpcodeGasSchedule.Calculate(new GasRuleId(ruleId), Fork.Osaka);

        Assert.Equal(expected, result.ChargedGas);
        Assert.Equal(ruleId, result.RuleId.Value);
        Assert.Equal("fixed-opcode-cost", result.Components.Single().Id);
    }

    [Fact]
    public void UnknownRule_IsRejectedInsteadOfDefaultingToZero()
    {
        Assert.Throws<GasScheduleException>(() =>
            FixedOpcodeGasSchedule.Calculate(new GasRuleId("OP.UNKNOWN"), Fork.Osaka));
    }
}
