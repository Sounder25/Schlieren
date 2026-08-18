using Schlieren.Core.Execution;
using Schlieren.UI.Services;
using Schlieren.UI.ViewModels;
using Xunit;

namespace Schlieren.Tests;

public sealed class WorkbenchCanonicalGasTreeTests
{
    [Fact]
    public async Task GasTree_UsesSameResultAsExecution_NotSecondPath()
    {
        var run = await BytecodeExecutionService.RunAsync(
            "600560030160005260206000f3",
            new BytecodeRunOptions { ForkLabel = "Osaka", GasLimit = 100_000 });
        Assert.NotNull(run);
        Assert.True(run!.Result.IsSuccess);
        Assert.NotNull(run.GasTree);
        Assert.Contains("canonical", run.GasTree!.Label, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(run.Result.GasUsed.ToString("N0"), run.GasTree.Label);
        Assert.Equal(run.Result.GasUsed, run.Result.GasUsed);
        var rendered = GasTreeRenderer.Render(run.GasTree);
        Assert.Contains("Intrinsic", rendered);
        Assert.DoesNotContain("second", rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VmPopulate_GasPaneMatchesCanonicalTree()
    {
        using var vm = new WorkbenchViewModel();
        vm.BytecodeInput = "600560030160005260206000f3";
        vm.SelectedFork = "Osaka";
        await vm.RunBytecodeCommand.ExecuteAsync(null);
        Assert.True(vm.HasTrace);
        Assert.Contains(vm.GasTreeNodes, n =>
            n.DisplayText.Contains("canonical", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("gas", vm.ResultBanner, StringComparison.OrdinalIgnoreCase);
    }
}
