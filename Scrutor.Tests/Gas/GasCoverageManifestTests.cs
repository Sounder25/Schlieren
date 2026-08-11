using Scrutor.Core.Forks;
using Scrutor.Core.Gas;

namespace Scrutor.Tests.Gas;

public sealed class GasCoverageManifestTests
{
    [Fact]
    public void Constructor_CopiesAndSortsRequiredRules()
    {
        var source = new[]
        {
            new GasRuleId("OP.SLOAD"),
            new GasRuleId("OP.ADD")
        };

        var manifest = new GasCoverageManifest(source);
        source[0] = new GasRuleId("OP.CHANGED");

        Assert.Equal(new[] { "OP.ADD", "OP.SLOAD" },
            manifest.RequiredRuleIds.Select(id => id.Value));
    }

    [Fact]
    public void Constructor_RejectsDuplicateRules()
    {
        var id = new GasRuleId("OP.ADD");

        var ex = Assert.Throws<ArgumentException>(() =>
            new GasCoverageManifest(new[] { id, id }));

        Assert.Contains("OP.ADD", ex.Message);
    }

    [Fact]
    public void Build_ReportsEveryMissingRuleInOrdinalOrder()
    {
        var manifest = new GasCoverageManifest(new[]
        {
            new GasRuleId("OP.SLOAD"),
            new GasRuleId("OP.ADD"),
            new GasRuleId("OP.BALANCE")
        });

        var ex = Assert.Throws<GasScheduleException>(() =>
            ForkGasScheduleBuilder.Empty(Fork.Frontier)
                .Set(new ManifestRule(new GasRuleId("OP.ADD"), 3))
                .Build(manifest));

        Assert.Contains("OP.BALANCE, OP.SLOAD", ex.Message);
    }

    [Fact]
    public void Build_InheritedRuleSatisfiesChildManifest()
    {
        var add = new GasRuleId("OP.ADD");
        var manifest = new GasCoverageManifest(new[] { add });
        var frontier = ForkGasScheduleBuilder.Empty(Fork.Frontier)
            .Set(new ManifestRule(add, 3))
            .Build(manifest);

        var homestead = ForkGasScheduleBuilder.From(frontier, Fork.Homestead)
            .Build(manifest);

        Assert.Equal((ulong)3, homestead.Calculate(add, 0).ChargedGas);
        Assert.Equal(add, Assert.Single(homestead.Coverage.RequiredRuleIds));
    }

    private sealed class ManifestRule : IGasRule<int>
    {
        private readonly ulong _cost;

        public ManifestRule(GasRuleId id, ulong cost)
        {
            _cost = cost;
            Metadata = new GasRuleMetadata(id, "Test", Fork.Frontier, "test", "test");
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
