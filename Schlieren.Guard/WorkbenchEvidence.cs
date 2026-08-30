using System.Text.Json;
using System.Text.Json.Serialization;
using Schlieren.Core.Execution.Journal;

namespace Schlieren.Guard;

public sealed class GuardReport
{
    public required PinnedBase Pin { get; init; }
    public required string Token { get; init; }
    public required string Router { get; init; }
    public required string Buyer { get; init; }
    public required GuardVerdict Verdict { get; init; }
    public required IReadOnlyList<ScenarioStep> Steps { get; init; }

    public string ToPlainLanguage() => Verdict.ToPlainLanguage();
}

public static class WorkbenchEvidence
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string WriteBundle(GuardReport report)
    {
        var causal = report.Verdict.CausalFrame;
        var dto = new
        {
            kind = "schlieren-guard-evidence",
            version = PinnedBase.CurrentScenarioVersion,
            pin = new
            {
                chainId = report.Pin.ChainId,
                blockNumber = report.Pin.BlockNumber,
                blockHash = report.Pin.BlockHash,
                timestamp = report.Pin.Timestamp,
                fork = report.Pin.ForkName,
                scenarioVersion = report.Pin.ScenarioVersion
            },
            token = report.Token,
            router = report.Router,
            buyer = report.Buyer,
            verdict = new
            {
                kind = report.Verdict.Kind.ToString(),
                headline = report.Verdict.Headline,
                detail = report.Verdict.Detail,
                effectiveLossPercent = report.Verdict.EffectiveLossPercent,
                looksLikeHoneypot = report.Verdict.LooksLikeHoneypot,
                causalFrameId = causal?.FrameId,
                causalContract = causal?.Contract.ToString(),
                causalDepth = causal?.Depth
            },
            showExecution = causal is null
                ? "Open the matching step journal in Workbench."
                : $"Show execution → Workbench exact causal frame {causal.FrameId} at {causal.Contract}.",
            steps = report.Steps.Select(step => new
            {
                name = step.Name,
                success = step.Succeeded,
                error = step.Result.Error.ToString(),
                gasUsed = step.Result.GasUsed,
                to = step.Transaction.To?.ToString(),
                value = Abi.Qty(step.Transaction.Value),
                data = Abi.ToHex(step.Transaction.Data),
                tokenBefore = Abi.Qty(step.TokenBalanceBefore),
                tokenAfter = Abi.Qty(step.TokenBalanceAfter),
                ethBefore = Abi.Qty(step.BuyerEthBefore),
                ethAfter = Abi.Qty(step.BuyerEthAfter),
                journal = step.Journal?.Events.Select(DescribeEvent).ToArray()
            }).ToArray()
        };
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    private static object DescribeEvent(ExecutionJournalEvent entry) => entry switch
    {
        FrameEnteredEvent e => new
        {
            type = "frame-entered",
            sequence = e.Sequence,
            frameId = e.FrameId,
            parent = e.ParentFrameId,
            depth = e.Depth,
            callType = e.CallType.ToString(),
            contract = e.ContractAddress.ToString()
        },
        FrameStateResolvedEvent e => new
        {
            type = "frame-resolved",
            sequence = e.Sequence,
            frameId = e.FrameId,
            resolution = e.Resolution.ToString()
        },
        FrameExitedEvent e => new
        {
            type = "frame-exited",
            sequence = e.Sequence,
            frameId = e.FrameId,
            success = e.Success,
            error = e.Error.ToString()
        },
        _ => new { type = entry.GetType().Name, sequence = entry.Sequence, frameId = entry.FrameId }
    };
}
