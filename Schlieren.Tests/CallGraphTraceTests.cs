using Schlieren.Core.Execution;
using Schlieren.UI.ViewModels;
using Xunit;

namespace Schlieren.Tests;

public sealed class CallGraphTraceTests
{
    [Fact]
    public void ExtractsP256VerifyFromCallStack()
    {
        var step = new ExecutionTraceStep
        {
            Op = "CALL",
            Stack =
            [
                "0x1af4",
                "0x100",
                "0x0",
                "0x0",
                "0xa0",
                "0x0",
                "0x20"
            ]
        };

        var target = CallTopologyViewModel.ExtractCallTarget(step);
        Assert.Equal("0x0000000000000000000000000000000000000100", target);
        Assert.Equal("P256VERIFY (0x0100)", CallTopologyViewModel.PrecompileLabel(target));
    }

    [Fact]
    public void LoadFromTrace_ShowsRootAndP256()
    {
        var steps = new List<ExecutionTraceStep>
        {
            new() { Op = "PUSH2", Depth = 1, ContractAddress = "0x00000000000000000000000000000000000000aa" },
            new()
            {
                Op = "CALL",
                Depth = 1,
                ContractAddress = "0x00000000000000000000000000000000000000aa",
                Stack = ["0x1af4", "0x100", "0x0", "0x0", "0xa0", "0x0", "0x20"]
            }
        };

        var g = new CallTopologyViewModel();
        g.LoadFromTrace(steps);
        Assert.True(g.HasRows);
        Assert.Contains(g.Rows, r => r.Title.Contains("P256VERIFY"));
        Assert.Contains(g.Rows, r => r.Title.Contains("CALL"));
        var edge = g.Rows.First(r => r.IsEdge);
        Assert.Contains("gas forwarded: 6,900", edge.Detail);
        Assert.Contains("input: 160 B", edge.Detail);
        var child = g.Rows.First(r => r.Title.Contains("P256VERIFY"));
        Assert.Equal("Precompile", child.Kind);
        Assert.Contains("0x0000000000000000000000000000000000000100", child.Detail);
    }
}
