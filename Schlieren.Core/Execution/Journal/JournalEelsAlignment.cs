using System.Text.Json;
using System.Text.Json.Serialization;

namespace Schlieren.Core.Execution.Journal;

/// <summary>EIP-3155 projection and deterministic first-divergence comparison.</summary>
public static class JournalEelsAlignment
{
    public static IReadOnlyList<Eip3155StepDto> Project(IEnumerable<JournalStepDto> steps) =>
        steps.Select(step => new Eip3155StepDto(
            step.Pc,
            step.Op,
            step.GasBefore,
            step.GasCost,
            step.Depth,
            step.Stack,
            step.Memory,
            step.Storage)
        {
            FrameId = step.FrameId,
            Sequence = step.Sequence
        }).ToArray();

    public static EelsAlignmentResult Compare(
        IReadOnlyList<Eip3155StepDto> actual,
        IReadOnlyList<Eip3155StepDto> reference)
    {
        var shared = Math.Min(actual.Count, reference.Count);
        for (var index = 0; index < shared; index++)
        {
            var a = actual[index];
            var e = reference[index];
            var mismatch = FirstMismatch(a, e);
            if (mismatch is not null)
                return new(false, index, new EelsDivergence(
                    index, mismatch.Value.Field, mismatch.Value.Expected, mismatch.Value.Actual,
                    a.FrameId, a.Sequence, a.Pc, a.Op));
        }

        if (actual.Count != reference.Count)
        {
            var context = actual.Count > shared ? actual[shared] : actual.LastOrDefault();
            return new(false, shared, new EelsDivergence(
                shared, "length", reference.Count.ToString(), actual.Count.ToString(),
                context?.FrameId, context?.Sequence, context?.Pc, context?.Op));
        }
        return new(true, shared, null);
    }

    private static (string Field, string Expected, string Actual)? FirstMismatch(Eip3155StepDto actual, Eip3155StepDto expected)
    {
        if (actual.Pc != expected.Pc) return ("pc", expected.Pc.ToString(), actual.Pc.ToString());
        if (!string.Equals(actual.Op, expected.Op, StringComparison.OrdinalIgnoreCase)) return ("op", expected.Op, actual.Op);
        if (actual.Gas != expected.Gas) return ("gas", expected.Gas.ToString(), actual.Gas.ToString());
        if (actual.GasCost != expected.GasCost) return ("gasCost", expected.GasCost.ToString(), actual.GasCost.ToString());
        if (actual.Depth != expected.Depth) return ("depth", expected.Depth.ToString(), actual.Depth.ToString());
        if (!Equivalent(actual.Stack, expected.Stack)) return ("stack", Json(expected.Stack), Json(actual.Stack));
        if (!Equivalent(actual.Memory, expected.Memory)) return ("memory", Json(expected.Memory), Json(actual.Memory));
        if (!Equivalent(actual.Storage, expected.Storage)) return ("storage", Json(expected.Storage), Json(actual.Storage));
        return null;
    }

    private static bool Equivalent<T>(T left, T right) => Json(left) == Json(right);
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
}

public sealed record Eip3155StepDto(
    int Pc,
    string Op,
    ulong Gas,
    ulong GasCost,
    int Depth,
    IReadOnlyList<string>? Stack,
    IReadOnlyList<string>? Memory,
    IReadOnlyDictionary<string, string>? Storage)
{
    [JsonIgnore] public long? FrameId { get; init; }
    [JsonIgnore] public long? Sequence { get; init; }
}

public sealed record EelsDivergence(
    int Index,
    string Field,
    string Expected,
    string Actual,
    long? FrameId,
    long? Sequence,
    int? Pc,
    string? Op);

public sealed record EelsAlignmentResult(
    bool IsAligned,
    int ComparedSteps,
    EelsDivergence? FirstDivergence);
