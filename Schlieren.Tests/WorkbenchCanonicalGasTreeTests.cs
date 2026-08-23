using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
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
        Assert.NotNull(run.Result.Journal);
        var intrinsic = Assert.Single(run.Result.Journal!.Events.OfType<IntrinsicGasChargedEvent>());
        Assert.Equal(intrinsic.Amount, run.IntrinsicGas);
        Assert.NotNull(run.GasTree);
        Assert.Equal(run.Result.GasUsed, run.GasTree!.TotalGas);
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
            n.DisplayText.Contains("Transaction", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("gas", vm.ResultBanner, StringComparison.OrdinalIgnoreCase);
    }
}
