using Schlieren.Core.Execution.Journal;

namespace Schlieren.EELS.Tests.Conformance;

public sealed class JournalEelsAlignmentTests
{
    [Fact]
    public void JournalStep_ProjectsToEip3155Shape()
    {
        var projected = JournalEelsAlignment.Project([Step(pc: 4, op: "SSTORE", gas: 100, cost: 20)]);

        var step = Assert.Single(projected);
        Assert.Equal(4, step.Pc);
        Assert.Equal("SSTORE", step.Op);
        Assert.Equal(100UL, step.Gas);
        Assert.Equal(20UL, step.GasCost);
        Assert.Equal(1, step.Depth);
        Assert.Equal(["0x01"], step.Stack);
    }

    [Fact]
    public void Alignment_ReportsFirstSemanticDivergenceWithJournalContext()
    {
        var actual = JournalEelsAlignment.Project([
            Step(0, "PUSH1", 100, 3),
            Step(2, "SSTORE", 97, 20)
        ]);
        var reference = actual.Select(step => step with { }).ToArray();
        reference[1] = reference[1] with { GasCost = 21 };

        var result = JournalEelsAlignment.Compare(actual, reference);

        Assert.False(result.IsAligned);
        Assert.Equal(1, result.ComparedSteps);
        Assert.Equal("gasCost", result.FirstDivergence!.Field);
        Assert.Equal("21", result.FirstDivergence.Expected);
        Assert.Equal("20", result.FirstDivergence.Actual);
        Assert.Equal(7, result.FirstDivergence.FrameId);
        Assert.Equal(2, result.FirstDivergence.Pc);
    }

    private static JournalStepDto Step(int pc, string op, ulong gas, ulong cost) => new()
    {
        Sequence = pc + 1,
        FrameId = 7,
        ParentFrameId = 1,
        Depth = 1,
        Pc = pc,
        Opcode = "0x00",
        Op = op,
        GasBefore = gas,
        GasAfter = gas - cost,
        GasCost = cost,
        Semantics = "exclusiveCharge",
        Output = "0x",
        Stack = ["0x01"],
        Memory = ["00"],
        Storage = new Dictionary<string, string> { ["0x00"] = "0x01" }
    };
}
