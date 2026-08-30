using System.Numerics;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Guard.Tests;

public sealed class QualificationTests
{
    private static readonly Address Buyer = Address.FromHex("0x67000000000000000000000000000000000000aa");
    private static readonly Address Token = Address.FromHex("0x2200000000000000000000000000000000000002");
    private static readonly BigInteger Spend = BigInteger.Parse("100000000000000000"); // 0.1 ETH

    [Fact]
    public async Task A_NormalToken_BuyAndSellPass()
    {
        var report = await RunAsync(TokenFixtures.Normal);
        Assert.Equal(GuardOutcomeKind.SellSuccessful, report.Verdict.Kind);
        Assert.False(report.Verdict.LooksLikeHoneypot);
        Assert.True(report.Steps.All(s => s.Succeeded));
    }

    [Fact]
    public async Task B_Honeypot_SellBlocked_CausalFrameIdentified()
    {
        var report = await RunAsync(TokenFixtures.Honeypot);
        Assert.Equal(GuardOutcomeKind.SellBlocked, report.Verdict.Kind);
        Assert.False(report.Verdict.LooksLikeHoneypot);
        Assert.NotNull(report.Verdict.CausalFrame);
        Assert.Equal(Token, report.Verdict.CausalFrame!.Contract);
        var bundle = WorkbenchEvidence.WriteBundle(report);
        Assert.Contains("Show execution", bundle);
        Assert.Contains("\"kind\": \"schlieren-guard-evidence\"", bundle);
        Assert.Contains("\"method\": \"schlieren_traceJournal\"", bundle);
        Assert.Contains("\"preState\"", bundle);
        Assert.Contains(Token.ToString(), bundle);
    }

    [Fact]
    public async Task C_HighTax_SellPasses_LossMeasured()
    {
        var report = await RunAsync(TokenFixtures.Tax);
        Assert.Equal(GuardOutcomeKind.SellSuccessful, report.Verdict.Kind);
        Assert.True(report.Verdict.EffectiveLossPercent is > 40 and < 70, $"loss={report.Verdict.EffectiveLossPercent}");
    }

    [Fact]
    public async Task D_Cooldown_ImmediateSellFails_DelayedSellPasses_NotHoneypot()
    {
        var report = await RunAsync(TokenFixtures.Cooldown, includeDelayed: true);
        Assert.Equal(GuardOutcomeKind.SellDelayed, report.Verdict.Kind);
        Assert.False(report.Verdict.LooksLikeHoneypot);
        Assert.Equal("SELL DELAYED — same-block restriction", report.Verdict.Headline);
    }

    private static async Task<GuardReport> RunAsync(byte[] code, bool includeDelayed = false)
    {
        var overlay = new GlobalState();
        overlay.SetCode(Token, code);
        var session = ScenarioSession.OpenLocal(overlay, Pin(), Buyer, Token);
        session.FundBuyer(2 * TokenRiskChecker.WeiPerEth);

        var buy = await session.ExecuteAsync("buy", Token, Array.Empty<byte>(), Spend);
        var sell = await session.ExecuteAsync("sell", Token, new byte[] { 0x01 }, BigInteger.Zero);
        ScenarioStep? delayed = null;
        if (includeDelayed && !sell.Succeeded)
            delayed = await session.ExecuteAsync("sell-delayed", Token, new byte[] { 0x01 }, 0, extraSeconds: 12);

        return new GuardReport
        {
            Pin = session.Pinned,
            Token = Token.ToString(),
            Router = "direct",
            Buyer = Buyer.ToString(),
            Verdict = GuardAdjudicator.Adjudicate(buy, null, sell, delayed),
            Steps = session.Steps
        };
    }

    private static PinnedBase Pin() => new(
        1, 20_000_000, "0x" + new string('1', 64), 1_700_000_000, 30_000_000, 1_000_000_000,
        Address.Zero, "Prague", 1);
}

internal static class TokenFixtures
{
    // buy if msg.value>0: slot0=value, slot1=timestamp
    // sell otherwise: CALL remaining ETH to caller (or variant)
    public static readonly byte[] Normal = Convert.FromHexString(
        "3415600d57345f5542600155005b5f545f5f5f5f84335af15000");

    public static readonly byte[] Honeypot = Convert.FromHexString(
        "3415600d57345f5542600155005b60006000fd");

    // sell sends balance/2
    public static readonly byte[] Tax = Convert.FromHexString(
        "3415600d57345f5542600155005b60025f54045f5f5f5f84335af15000");

    // sell reverts when timestamp == slot1
    public static readonly byte[] Cooldown = Convert.FromHexString(
        "3415600d57345f5542600155005b42600154146022575f545f5f5f5f84335af150005b5f5ffd");
}
