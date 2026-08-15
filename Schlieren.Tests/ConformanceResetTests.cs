using Schlieren.UI.ViewModels;
using Xunit;

namespace Schlieren.Tests;

public sealed class ConformanceResetTests
{
    [Fact(DisplayName = "Reset results clears scores and failures but keeps fork")]
    public void ResetResults_ClearsRunState_KeepsForkAndPath()
    {
        using var vm = new ConformanceViewModel();
        vm.SelectedFork = "Osaka";
        vm.Passed = 14516;
        vm.Failed = 0;
        vm.Total = 14516;
        vm.HasResults = true;
        vm.ShowEmptyFailures = false;
        vm.ProgressText = "14,516 / 14,516";
        vm.PassRateText = "100.0%";
        vm.ElapsedText = "03:21";
        vm.CurrentCase = "Done.";
        vm.StatusMessage = "✅ Osaka — 100%";

        vm.ResetResultsCommand.Execute(null);

        Assert.Equal(0, vm.Passed);
        Assert.Equal(0, vm.Failed);
        Assert.Equal(0, vm.Total);
        Assert.False(vm.HasResults);
        Assert.Equal("0 / 0", vm.ProgressText);
        Assert.Equal("—", vm.PassRateText);
        Assert.Equal("00:00", vm.ElapsedText);
        Assert.Equal(string.Empty, vm.CurrentCase);
        Assert.Empty(vm.Failures);
        Assert.Empty(vm.Clusters);
        Assert.Equal("Osaka", vm.SelectedFork);
        Assert.Contains("Reset", vm.StatusMessage);
    }
}
