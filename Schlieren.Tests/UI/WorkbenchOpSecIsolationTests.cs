using Schlieren.Core.Security;
using Schlieren.UI.ViewModels;
using Xunit;

namespace Schlieren.Tests.UI;

/// <summary>
/// Proves WorkbenchViewModel does not pollute another execution flow's OpSec state.
/// </summary>
public class WorkbenchOpSecIsolationTests
{
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(5);

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
        var isolatedScopeActive = new TaskCompletionSource();
        var workbenchHolding = new TaskCompletionSource();
        var isolatedCanRelease = new TaskCompletionSource();

        var isolatedTask = Task.Run(async () =>
        {
            using var scope = OpSecLockout.EnterScope();
            try
            {
                Assert.True(OpSecLockout.IsEnabled);
                isolatedScopeActive.TrySetResult();
                await workbenchHolding.Task.WaitAsync(DeadlockGuard);
                Assert.True(OpSecLockout.IsEnabled, "Isolated flow lost OpSec while Workbench held OpSecEnabled=false");
                isolatedCanRelease.TrySetResult();
            }
            catch
            {
                isolatedScopeActive.TrySetResult(); // Release dependent on failure
                isolatedCanRelease.TrySetResult();  // Release dependent on failure
                throw;
            }
        });

        var workbenchTask = Task.Run(async () =>
        {
            try
            {
                await isolatedScopeActive.Task.WaitAsync(DeadlockGuard);

                using var vm = new WorkbenchViewModel();
                vm.OpSecEnabled = true;
                vm.OpSecEnabled = false;
                workbenchHolding.TrySetResult();

                await isolatedCanRelease.Task.WaitAsync(DeadlockGuard);
            }
            catch
            {
                isolatedScopeActive.TrySetResult(); // Release dependent on failure
                workbenchHolding.TrySetResult();
                isolatedCanRelease.TrySetResult();
                throw;
            }
        });

        await Task.WhenAll(isolatedTask, workbenchTask);
    }

    [Fact]
    public async Task Workbench_TogglingDoesNotAffectIsolatedExecution()
    {
        var isolatedEntered = new TaskCompletionSource();
        var isolatedFinished = new TaskCompletionSource();

        var isolatedTask = Task.Run(async () =>
        {
            try
            {
                await OpSecLockout.ExecuteIsolatedAsync(async () =>
                {
                    Assert.True(OpSecLockout.IsEnabled);
                    isolatedEntered.TrySetResult();
                    await isolatedFinished.Task.WaitAsync(DeadlockGuard);
                    Assert.True(OpSecLockout.IsEnabled, "Isolated execution lost OpSec during Workbench toggle");
                });
            }
            catch
            {
                isolatedEntered.TrySetResult(); // Release dependent on failure
                isolatedFinished.TrySetResult();
                throw;
            }
        });

        var workbenchTask = Task.Run(async () =>
        {
            try
            {
                await isolatedEntered.Task.WaitAsync(DeadlockGuard);

                using var vm = new WorkbenchViewModel();
                vm.OpSecEnabled = false;
                Assert.False(OpSecLockout.IsEnabled); // Workbench's own flow sees disabled

                vm.OpSecEnabled = true;
                Assert.False(OpSecLockout.IsEnabled); // Still disabled; Workbench toggle is UI-only

                isolatedFinished.TrySetResult();
            }
            catch
            {
                isolatedEntered.TrySetResult(); // Release dependent on failure
                isolatedFinished.TrySetResult();
                throw;
            }
        });

        await Task.WhenAll(isolatedTask, workbenchTask);
    }
}
