using Schlieren.UI.ViewModels;
using Xunit;

namespace Schlieren.Tests;

public sealed class WorkbenchResetTests
{
    [Fact(DisplayName = "Reset workbench clears bytecode, calldata, files, and last-run banner")]
    public void ResetWorkbench_ClearsSessionButKeepsForkAndGas()
    {
        using var vm = new WorkbenchViewModel();
        vm.SelectedFork = "Osaka";
        vm.TxGasLimit = 1_000_000;
        vm.BytecodeInput = "600560030160005260206000f3";
        vm.CallDataHex = "0xb94d27b9";
        vm.AddCustomFile("sample.hex", @"C:\tmp\sample.hex", new[] { "0x6005600301" });
        vm.ResultBanner = "SUCCESS · 21,000 gas";
        vm.HasTrace = true;
        vm.TotalSteps = 4;

        vm.ResetWorkbenchCommand.Execute(null);

        Assert.Equal(string.Empty, vm.BytecodeInput);
        Assert.Equal(string.Empty, vm.CallDataHex);
        Assert.Empty(vm.ProjectFiles);
        Assert.False(vm.HasOpenFiles);
        Assert.False(vm.HasTrace);
        Assert.Equal(0, vm.TotalSteps);
        Assert.Equal("No run yet", vm.ResultBanner);
        Assert.Equal("Workbench reset", vm.StatusMessage);
        Assert.Equal("Osaka", vm.SelectedFork);
        Assert.Equal(1_000_000ul, vm.TxGasLimit);
    }
}
