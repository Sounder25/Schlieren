using Schlieren.Core.Forks;
using Schlieren.Core.Gas;

namespace Schlieren.Tests.Gas;

public sealed class ForkGasScheduleTests
{
    [Fact]
    public void Build_ProvidesTypedCalculation()
    {
        var id = new GasRuleId("OP.ADD");
        var schedule = ForkGasScheduleBuilder.Empty(Fork.Frontier)
            .Set(new ConstantRule(id, 3))
            .Build();

        var result = schedule.Calculate(id, 0);

        Assert.Equal((ulong)3, result.ChargedGas);
        Assert.Equal(Fork.Frontier, result.Fork);
    }

    [Fact]
    public void FromParent_ReplacesRuleWithoutMutatingParent()
    {
        var id = new GasRuleId("OP.SLOAD");
        var frontier = ForkGasScheduleBuilder.Empty(Fork.Frontier)
            .Set(new ConstantRule(id, 50))
            .Build();
        var tangerine = ForkGasScheduleBuilder.From(frontier, Fork.TangerineWhistle)
            .Set(new ConstantRule(id, 200, Fork.TangerineWhistle))
            .Build();

        Assert.Equal((ulong)50, frontier.Calculate(id, 0).ChargedGas);
        Assert.Equal((ulong)200, tangerine.Calculate(id, 0).ChargedGas);
    }

    [Fact]
    public void Calculate_RejectsWrongContextType()
    {
        var id = new GasRuleId("OP.ADD");
        var schedule = ForkGasScheduleBuilder.Empty(Fork.Frontier)
            .Set(new ConstantRule(id, 3))
            .Build();

        var ex = Assert.Throws<GasScheduleException>(() => schedule.Calculate(id, "wrong"));

        Assert.Contains("System.Int32", ex.Message);
        Assert.Contains("System.String", ex.Message);
        Assert.Contains("OP.ADD", ex.Message);
    }

    [Fact]
    public void Calculate_RejectsMissingRule()
    {
        var schedule = ForkGasScheduleBuilder.Empty(Fork.Frontier).Build();

        var ex = Assert.Throws<GasScheduleException>(() =>
            schedule.Calculate(new GasRuleId("OP.MISSING"), 0));

        Assert.Contains("OP.MISSING", ex.Message);
        Assert.Contains("Frontier", ex.Message);
    }

    [Fact]
    public void Build_RejectsForkBeforeParent()
    {
        var parent = ForkGasScheduleBuilder.Empty(Fork.Berlin).Build();

        Assert.Throws<GasScheduleException>(() =>
            ForkGasScheduleBuilder.From(parent, Fork.Istanbul));
    }

    [Fact]
    public void Set_RejectsRuleThatIsNotActiveYet()
    {
        var id = new GasRuleId("OP.PUSH0");

        var ex = Assert.Throws<GasScheduleException>(() =>
            ForkGasScheduleBuilder.Empty(Fork.London)
                .Set(new ConstantRule(id, 2, Fork.Shanghai)));

        Assert.Contains("Shanghai", ex.Message);
        Assert.Contains("London", ex.Message);
        Assert.Contains("OP.PUSH0", ex.Message);
    }

    [Fact]
    public void FromParent_CanDisableInheritedRuleWithoutMutatingParent()
    {
        var id = new GasRuleId("OP.REMOVED");
        var frontier = ForkGasScheduleBuilder.Empty(Fork.Frontier)
            .Set(new ConstantRule(id, 3))
            .Build();

        var homestead = ForkGasScheduleBuilder.From(frontier, Fork.Homestead)
            .Remove(id)
            .Build();

        Assert.Equal((ulong)3, frontier.Calculate(id, 0).ChargedGas);
        Assert.Throws<GasScheduleException>(() => homestead.Calculate(id, 0));
    }

    internal sealed class ConstantRule : IGasRule<int>
    {
        private readonly ulong _cost;

        public ConstantRule(GasRuleId id, ulong cost, Fork activationFork = Fork.Frontier)
        {
            _cost = cost;
            Metadata = new GasRuleMetadata(id, "Test", activationFork, "test", "test");
        }

        public GasRuleMetadata Metadata { get; }

        public GasCalculation Calculate(int context, Fork fork) => GasCalculation.Create(
            Metadata,
            fork,
            _cost,
            0,
            GasDisposition.Charge,
            new[] { new GasComponent("base", "Base", GasComponentKind.Charge, _cost) },
            Array.Empty<GasDecision>());
    }
}
