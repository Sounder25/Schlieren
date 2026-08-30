using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Primitives;

namespace Schlieren.Guard;

public enum GuardOutcomeKind
{
    SellSuccessful,
    SellBlocked,
    SellDelayed,
    BuyFailed,
    Inconclusive
}

public sealed record CausalFrame(
    long FrameId,
    int Depth,
    Address Contract,
    CallType CallType,
    FrameStateResolution Resolution);

public sealed record GuardVerdict(
    GuardOutcomeKind Kind,
    string Headline,
    string Detail,
    decimal? EffectiveLossPercent,
    CausalFrame? CausalFrame,
    bool LooksLikeHoneypot)
{
    public string ToPlainLanguage()
    {
        var loss = EffectiveLossPercent is { } pct
            ? $" Effective loss {pct:0.##}%."
            : string.Empty;
        var frame = CausalFrame is { } c
            ? $" First causal frame {c.FrameId} at {c.Contract} (depth {c.Depth})."
            : string.Empty;
        return $"{Headline} {Detail}{loss}{frame}".Trim();
    }
}

public static class GuardAdjudicator
{
    public static GuardVerdict Adjudicate(
        ScenarioStep buy,
        ScenarioStep? approve,
        ScenarioStep sell,
        ScenarioStep? delayedSell = null)
    {
        if (!buy.Succeeded)
        {
            return new GuardVerdict(
                GuardOutcomeKind.BuyFailed,
                "BUY FAILED",
                DescribeFailure(buy),
                null,
                FirstCausalFrame(buy.Journal),
                LooksLikeHoneypot: false);
        }

        if (sell.Succeeded)
        {
            var loss = ComputeLossPercent(buy, sell);
            return new GuardVerdict(
                GuardOutcomeKind.SellSuccessful,
                "SELL SUCCESSFUL",
                loss is { } pct && pct > 0
                    ? $"Buy and sell both committed. Measured round-trip loss {pct:0.##}%."
                    : "Buy and sell both committed.",
                loss,
                null,
                LooksLikeHoneypot: false);
        }

        if (delayedSell is { Succeeded: true })
        {
            return new GuardVerdict(
                GuardOutcomeKind.SellDelayed,
                "SELL DELAYED — same-block restriction",
                "Immediate sell failed, but a timestamp/block-advanced sell committed. This is not classified as a honeypot.",
                ComputeLossPercent(buy, delayedSell),
                FirstCausalFrame(sell.Journal),
                LooksLikeHoneypot: false);
        }

        if (approve is { Succeeded: false })
        {
            return new GuardVerdict(
                GuardOutcomeKind.Inconclusive,
                "INCONCLUSIVE — allowance/scenario limitation",
                "Approve did not commit, so the sell failure is not attributed to token transfer restrictions.",
                null,
                FirstCausalFrame(approve.Journal),
                LooksLikeHoneypot: false);
        }

        var causal = FirstCausalFrame(sell.Journal);
        return new GuardVerdict(
            GuardOutcomeKind.SellBlocked,
            "SELL BLOCKED",
            "Buy committed and the same-wallet sell reverted. " + DescribeFailure(sell) +
            " This is not automatically a honeypot; it is the first observed sell restriction.",
            null,
            causal,
            LooksLikeHoneypot: false);
    }

    public static CausalFrame? FirstCausalFrame(ExecutionJournal? journal)
    {
        if (journal is null)
            return null;

        var entered = new Dictionary<long, FrameEnteredEvent>();
        foreach (var entry in journal.Events)
        {
            switch (entry)
            {
                case FrameEnteredEvent frame when frame.FrameId is { } id:
                    entered[id] = frame;
                    break;
                case FrameStateResolvedEvent resolved
                    when resolved.Resolution == FrameStateResolution.Rollback &&
                         resolved.FrameId is { } id &&
                         entered.TryGetValue(id, out var origin):
                    return new CausalFrame(
                        id,
                        origin.Depth,
                        origin.ContractAddress,
                        origin.CallType,
                        resolved.Resolution);
            }
        }

        foreach (var entry in journal.Events)
        {
            if (entry is FrameExitedEvent { Success: false, FrameId: { } id } &&
                entered.TryGetValue(id, out var origin))
            {
                return new CausalFrame(
                    id,
                    origin.Depth,
                    origin.ContractAddress,
                    origin.CallType,
                    FrameStateResolution.Rollback);
            }
        }

        return null;
    }

    public static decimal? ComputeLossPercent(ScenarioStep buy, ScenarioStep sell)
    {
        var spent = buy.BuyerEthBefore - buy.BuyerEthAfter;
        var recovered = sell.BuyerEthAfter - sell.BuyerEthBefore;
        if (spent <= 0)
            return null;
        var loss = (decimal)(spent - recovered) / (decimal)spent * 100m;
        return decimal.Round(loss, 2);
    }

    private static string DescribeFailure(ScenarioStep step)
    {
        if (step.Result.IsSuccess)
            return "Execution committed.";
        var error = step.Result.Error.ToString();
        var data = step.Result.ReturnData.Length == 0
            ? string.Empty
            : $" revert data {Abi.ToHex(step.Result.ReturnData)}";
        return $"{error}{data}.";
    }
}
