using Schlieren.UI.Services;
using Schlieren.UI.ViewModels;
using Xunit;

namespace Schlieren.Tests;

public sealed class WorkbenchAaBbAcceptanceTests
{
    private static string SampleJson()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "muscle", "prestate-aa-calls-bb.json"));
        return File.ReadAllText(path);
    }

    [Fact(DisplayName = "aa CALLs bb: graph, child return 0x1234, bb slot0=0x42")]
    public async Task PrestateAaCallsBb_AllFourAgree()
    {
        var json = SampleJson();
        Assert.True(WorkbenchPrestateLoader.Parse(json).Ok);

        var run = await BytecodeExecutionService.RunAsync("", new BytecodeRunOptions
        {
            ForkLabel = "Osaka",
            ContractHex = "0x00000000000000000000000000000000000000aa",
            ExtraAccounts = WorkbenchPrestateLoader.Parse(json).Accounts
        });

        Assert.NotNull(run);
        Assert.True(run!.Result.IsSuccess, run.Result.Error.ToString());

        var graph = new CallTopologyViewModel();
        graph.LoadFromTrace(run.Result.TraceSteps.ToList());
        Assert.Contains(graph.Rows, r => r.IsEdge && r.Title.Contains("CALL"));
        Assert.Contains(graph.Rows, r =>
            !r.IsEdge &&
            r.Address.EndsWith("bb", StringComparison.OrdinalIgnoreCase));

        var call = run.Result.TraceSteps.First(s => s.Op == "CALL");
        Assert.NotNull(call.OutputData);
        Assert.Equal("0x1234", BytecodeExecutionService.ToHex(call.OutputData));

        var bb = "0x00000000000000000000000000000000000000bb";
        Assert.True(run.PostStorage.TryGetValue(bb, out var slots));
        Assert.Contains(slots, s => s.Contains("0x42", StringComparison.OrdinalIgnoreCase));

        using var vm = new WorkbenchViewModel();
        vm.LoadPrestateJson(json, "prestate-aa-calls-bb.json");
        vm.TxTo = "0x00000000000000000000000000000000000000aa";
        Assert.Contains("pre-state", vm.ExecutionTargetLine, StringComparison.OrdinalIgnoreCase);
        vm.AddCustomFile("package.json", @"C:\tmp\package.json", ["{ \"name\": \"nope\" }"]);
        Assert.Contains("editor only", vm.EditorFileLine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("package.json", vm.ExecutionTargetLine, StringComparison.OrdinalIgnoreCase);

        await vm.RunBytecodeCommand.ExecuteAsync(null);
        Assert.True(vm.LastRunSuccess);
        vm.ShowCallGraphCommand.Execute(null);
        var child = vm.CallTopology.Rows.First(r =>
            r.Address.EndsWith("bb", StringComparison.OrdinalIgnoreCase));
        vm.SelectCallGraphRowCommand.Execute(child);
        Assert.Contains("0x42", vm.StorageText);
    }
}
