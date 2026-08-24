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
    public async Task Workbench_DoesNotAffectActivelyIsolatedFlow()
    {
        // One flow is actively inside EnterScope, verifying it stays enabled
        // while another flow constructs, toggles OpSecEnabled, and disposes WorkbenchViewModel.

        var isolatedScopeActive = new TaskCompletionSource();
        var workbenchHolding = new TaskCompletionSource();
        var isolatedCanRelease = new TaskCompletionSource();

        var isolatedTask = Task.Run(async () =>
        {
            using var scope = OpSecLockout.EnterScope();
            Assert.True(OpSecLockout.IsEnabled);
            isolatedScopeActive.SetResult();
            await workbenchHolding.Task;
            // Must still be enabled while Workbench holds OpSecEnabled = false in another flow
            Assert.True(OpSecLockout.IsEnabled, "Isolated flow lost OpSec while Workbench held OpSecEnabled=false");
            isolatedCanRelease.SetResult();
        });

        var workbenchTask = Task.Run(async () =>
        {
            await isolatedScopeActive.Task; // Wait until isolated flow has active scope

            using var vm = new WorkbenchViewModel();
            vm.OpSecEnabled = true;  // Workbench enables its own toggle
            vm.OpSecEnabled = false; // Workbench disables its own toggle
            workbenchHolding.SetResult(); // Signal: Workbench is holding OpSecEnabled=false

            await isolatedCanRelease.Task; // Wait for isolated to verify
            // vm disposed here
        });

        await Task.WhenAll(isolatedTask, workbenchTask);
    }

    [Fact]
    public async Task Workbench_TogglingDoesNotAffectIsolatedExecution()
    {
        // Isolated execution with OpSec enabled should not be affected
        // by Workbench toggling OpSecEnabled in another flow.

        var isolatedEntered = new TaskCompletionSource();
        var isolatedFinished = new TaskCompletionSource();

        var isolatedTask = Task.Run(async () =>
        {
            await OpSecLockout.ExecuteIsolatedAsync(async () =>
            {
                Assert.True(OpSecLockout.IsEnabled);
                isolatedEntered.SetResult();
                await isolatedFinished.Task;
                Assert.True(OpSecLockout.IsEnabled, "Isolated execution lost OpSec during Workbench toggle");
            });
        });

        var workbenchTask = Task.Run(async () =>
        {
            await isolatedEntered.Task;

            using var vm = new WorkbenchViewModel();
            vm.OpSecEnabled = false;
            Assert.False(OpSecLockout.IsEnabled); // Workbench's own flow sees disabled

            vm.OpSecEnabled = true;
            Assert.False(OpSecLockout.IsEnabled); // Still disabled; Workbench toggle is UI-only

            isolatedFinished.SetResult();
        });

        await Task.WhenAll(isolatedTask, workbenchTask);
    }
}
