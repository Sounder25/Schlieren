using Schlieren.Core.Detection;
using Schlieren.Core.Execution;
using UiLibraryGuard = Schlieren.UI.Services.LibraryGuardDetector;

namespace Schlieren.Tests.Detection;

public class LibraryGuardDetectorTests
{
    private const string LibraryAddress =
        "0x0000000000000000000000004ebf522b5e9b8c3e1a2d3e4f5a6b7c8d9e00254a";
    private const string LeftoverTop = "0xdeadbeef";

    [Fact]
    public void Analyze_Push32FirstOpcode_ReadsPushedValueFromNextStep()
    {
        var trace = BuildLibraryGuardTrace(
            push32PreStack: new List<string>(),
            postPushStack: new List<string> { LibraryAddress });

        var result = LibraryGuardDetector.Analyze(trace);

        Assert.NotNull(result);
        Assert.Equal(LibraryAddress, result!.EmbeddedConstant);
    }

    [Fact]
    public void Analyze_Push32WithLeftoverStack_DoesNotReportPreExecutionTop()
    {
        var trace = BuildLibraryGuardTrace(
            prefix: new ExecutionTraceStep { Pc = 0, Op = "JUMPDEST", Stack = new() { LeftoverTop } },
            push32PreStack: new List<string> { LeftoverTop },
            postPushStack: new List<string> { LibraryAddress, LeftoverTop });

        var result = LibraryGuardDetector.Analyze(trace);

        Assert.NotNull(result);
        Assert.Equal(LibraryAddress, result!.EmbeddedConstant);
        Assert.NotEqual(LeftoverTop, result.EmbeddedConstant);
    }

    [Fact]
    public void UiAnalyze_Push32FirstOpcode_StillDetectsGuard()
    {
        var trace = BuildLibraryGuardTrace(
            push32PreStack: new List<string>(),
            postPushStack: new List<string> { LibraryAddress });

        var finding = UiLibraryGuard.Analyze(trace);

        Assert.NotNull(finding);
        Assert.Contains("library", finding!.Title, StringComparison.OrdinalIgnoreCase);
    }

    private static List<ExecutionTraceStep> BuildLibraryGuardTrace(
        List<string> push32PreStack,
        List<string> postPushStack,
        ExecutionTraceStep? prefix = null)
    {
        var steps = new List<ExecutionTraceStep>();
        if (prefix != null)
            steps.Add(prefix);

        steps.Add(new ExecutionTraceStep { Pc = 1, Op = "PUSH32", Stack = push32PreStack });
        steps.Add(new ExecutionTraceStep { Pc = 34, Op = "CALLER", Stack = postPushStack });
        steps.Add(new ExecutionTraceStep { Pc = 35, Op = "XOR", Stack = new() { "0x1" } });
        steps.Add(new ExecutionTraceStep { Pc = 36, Op = "PUSH1", Stack = new() { "0x1" } });
        steps.Add(new ExecutionTraceStep { Pc = 38, Op = "JUMPI", Stack = new() { "0x0", "0x40" } });

        while (steps.Count < 12)
        {
            steps.Add(new ExecutionTraceStep { Pc = 40 + steps.Count, Op = "POP" });
        }

        return steps;
    }
}
