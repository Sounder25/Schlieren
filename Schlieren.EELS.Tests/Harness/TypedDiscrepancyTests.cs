using System.Numerics;
using Schlieren.Core.Execution.Causal;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.EELS.Tests.Harness;

public sealed class TypedDiscrepancyTests
{
    [Fact]
    public void StateComparison_EmitsTypedFactsAndRendersLegacyMessage()
    {
        var address = Address.FromHex("0x00000000000000000000000000000000000000aa");
        var testCase = new EelsStateCase(
            "fixture.json", "case", "Osaka", new BlockContext(), Address.Zero,
            new Transaction { From = Address.Zero, To = address, GasLimit = 100_000 },
            new Dictionary<Address, EelsFixtureAccount>(),
            new Dictionary<Address, EelsFixtureAccount>
            {
                [address] = new(0, new BigInteger(7), [], new Dictionary<BigInteger, BigInteger>())
            },
            true);
        var discrepancies = new List<StateDiscrepancy>();

        var matches = EelsStateFixtureExecutor.CompareExpectedState(testCase, new GlobalState(), discrepancies);

        Assert.False(matches);
        var discrepancy = Assert.Single(discrepancies);
        Assert.Equal(DiscrepancyKind.MissingAccount, discrepancy.Kind);
        Assert.Equal(address, discrepancy.Address);
        Assert.Equal($"missing account in actual state: {address}", discrepancy.Render());
    }
}
