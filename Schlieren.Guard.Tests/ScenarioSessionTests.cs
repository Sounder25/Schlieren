using System.Numerics;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Guard.Tests;

public sealed class ScenarioSessionTests
{
    private static readonly Address Buyer = Address.FromHex("0x6700000000000000000000000000000000000001");
    private static readonly Address Contract = Address.FromHex("0x1000000000000000000000000000000000000001");

    [Fact]
    public async Task SecondTransaction_SeesFirstTransactionStorage()
    {
        var overlay = new GlobalState();
        overlay.SetCode(Contract, Hex("6000361460125760005460005260206000f35b602a60005500"));
        var session = ScenarioSession.OpenLocal(overlay, Pin(), Buyer);
        session.FundBuyer(TokenRiskChecker.WeiPerEth);

        var write = await session.ExecuteAsync("write", Contract, Array.Empty<byte>(), 0);
        var read = await session.ExecuteAsync("read", Contract, new byte[] { 0x01 }, 0);

        Assert.True(write.Succeeded);
        Assert.True(read.Succeeded);
        Assert.Equal(42, Abi.DecodeUint256(read.Result.ReturnData));
    }

    private static PinnedBase Pin() => new(
        1, 20_000_000, "0x" + new string('a', 64), 1_700_000_000, 30_000_000, 1_000_000_000,
        Address.Zero, "Prague", 1);

    private static byte[] Hex(string hex) => Convert.FromHexString(hex);
}
