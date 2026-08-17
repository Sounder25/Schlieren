using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Forks;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Tests.Execution;

public sealed class BlockEpilogueTests
{
    [Fact]
    public void Withdrawals_CreditGweiAsWei()
    {
        var addr = Address.FromHex("0x00000000000000000000000000000000000000aa");
        var state = new GlobalState();
        state.SetBalance(addr, 1);

        BlockEpilogue.ApplyWithdrawals(state, new[] { (addr, 2UL) });

        Assert.Equal(1 + 2 * (BigInteger)BlockEpilogue.GweiToWei, state.GetBalanceAsync(addr).Result);
    }

    [Fact]
    public void OsakaRules_ExposeRequestAndWithdrawalFlags()
    {
        Assert.True(OsakaRules.Instance.HasEip4895Withdrawals);
        Assert.True(OsakaRules.Instance.HasEip7685Requests);
        Assert.False(CancunRules.Instance.HasEip7685Requests);
        Assert.True(CancunRules.Instance.HasEip4895Withdrawals);
        Assert.False(ParisRules.Instance.HasEip4895Withdrawals);
    }
}
