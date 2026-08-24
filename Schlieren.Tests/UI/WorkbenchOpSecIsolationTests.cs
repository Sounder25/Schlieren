using Schlieren.Core.Security;
using Schlieren.UI.ViewModels;
using Xunit;

namespace Schlieren.Tests.UI;

/// <summary>
/// Proves WorkbenchViewModel does not pollute another execution flow's OpSec state.
/// </summary>
public class WorkbenchOpSecIsolationTests
{
    [Fact]
    public void ConstructingAndDisposingWorkbench_DoesNotAffectOtherFlowOpSec()
    {
        // Ensure clean starting state for this flow
        Assert.False(OpSecLockout.IsEnabled);

        // Construct and dispose a Workbench with OpSec enabled
        using (var vm = new WorkbenchViewModel())
        {
            vm.OpSecEnabled = true;
            // ApplyOpSec() updates only local UI label now, not global state
        }

        // After disposal, this flow still sees disabled
        Assert.False(
            OpSecLockout.IsEnabled,
            "Workbench OpSec state leaked across execution flows");
    }

    [Fact]
    public async Task WorkbenchOpSec_DoesNotInterfereWithIsolatedExecution()
    {
        Assert.False(OpSecLockout.IsEnabled);

        var barrier = new Barrier(2);
        var workbenchSawEnabled = false;
        var isolatedSawDisabled = false;

        var workbenchTask = Task.Run(() =>
        {
            using var vm = new WorkbenchViewModel();
            vm.OpSecEnabled = true;
            barrier.SignalAndWait();
            // Workbench's OpSecEnabled only affects UI label, not global state
            workbenchSawEnabled = OpSecLockout.IsEnabled;
            // vm disposed here
        });

        var isolatedTask = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            await Task.Delay(50);
            isolatedSawDisabled = !OpSecLockout.IsEnabled;
        });

        await Task.WhenAll(workbenchTask, isolatedTask);

        // Workbench does NOT pollute async-local state
        Assert.False(workbenchSawEnabled, "Workbench incorrectly saw OpSec enabled");
        Assert.True(isolatedSawDisabled, "Isolated flow saw Workbench OpSec state — flows are not isolated");
    }
}
