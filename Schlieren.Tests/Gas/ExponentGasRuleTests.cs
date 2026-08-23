using System.Numerics;
using Schlieren.Core.Forks;
using Schlieren.Core.Gas;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using EvmExecutionContext = Schlieren.Core.Execution.ExecutionContext;

namespace Schlieren.Tests.Gas;

public sealed class ExponentGasRuleTests
{
    private readonly ExponentGasRule _rule = new();

    [Theory]
    [InlineData(Fork.Frontier, 1, 20)]
    [InlineData(Fork.TangerineWhistle, 1, 20)]
    [InlineData(Fork.SpuriousDragon, 1, 60)]
    [InlineData(Fork.Osaka, 2, 110)]
    public void Calculate_AppliesForkSpecificPerBytePrice(Fork fork, int bytes, ulong expected)
    {
        var exponent = BigInteger.One << ((bytes - 1) * 8);

        var result = _rule.Calculate(new ExponentGasContext(exponent), fork);

        Assert.Equal(expected, result.ChargedGas);
        Assert.Equal(new GasRuleId("OP.EXP"), result.RuleId);
        Assert.Equal(bytes, (int)result.Components.Single(c => c.Id == "exponent-bytes").Amount);
    }

    [Fact]
    public void ZeroExponent_ChargesOnlyBase()
    {
        Assert.Equal(10UL, _rule.Calculate(
            new ExponentGasContext(BigInteger.Zero),
            Fork.Osaka).ChargedGas);
    }

    [Fact]
    public async Task OpcodeExecution_ConsumesTheForkResolvedCalculation()
    {
        var context = new EvmExecutionContext
        {
            Block = new BlockContext { Rules = FrontierRules.Instance }
        };
        context.Stack.Push(BigInteger.One);
        context.Stack.Push(new BigInteger(2));

        var (result, _) = await new OpcodeExp().ExecuteAsync(context);

        Assert.Equal(20UL, result.GasUsed);
    }
}
