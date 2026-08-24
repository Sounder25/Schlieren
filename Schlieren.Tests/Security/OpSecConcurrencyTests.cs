using Schlieren.Core.Security;
using Xunit;

namespace Schlieren.Tests.Security;

/// <summary>
/// Proves OpSecLockout isolation per async execution flow.
/// </summary>
public class OpSecConcurrencyTests
{
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ConcurrentFlows_DoNotInterfere()
    {
        var insideScopeActive = new TaskCompletionSource();
        var outsideFinishedReading = new TaskCompletionSource();

        var insideTask = Task.Run(async () =>
        {
            try
            {
                await OpSecLockout.ExecuteIsolatedAsync(async () =>
                {
                    Assert.True(OpSecLockout.IsEnabled);
                    insideScopeActive.TrySetResult(); // Signal: OpSec is now active inside
                    await outsideFinishedReading.Task.WaitAsync(DeadlockGuard);
                });
            }
            catch
            {
                insideScopeActive.TrySetResult(); // Release dependent on failure
                throw;
            }
        });

        var outsideTask = Task.Run(async () =>
        {
            try
            {
                await insideScopeActive.Task.WaitAsync(DeadlockGuard);
                bool outsideSawEnabled = OpSecLockout.IsEnabled;
                Assert.False(outsideSawEnabled, "Outside flow observed OpSec enabled — state is leaking between flows");
            }
            finally
            {
                outsideFinishedReading.TrySetResult(); // Always release inside scope
            }
        });

        await Task.WhenAll(insideTask, outsideTask);
    }

    [Fact]
    public async Task NestedScopes_RestoreCorrectly()
    {
        Assert.False(OpSecLockout.IsEnabled);

        await OpSecLockout.ExecuteIsolatedAsync(async () =>
        {
            Assert.True(OpSecLockout.IsEnabled);

            await OpSecLockout.ExecuteIsolatedAsync(async () =>
            {
                Assert.True(OpSecLockout.IsEnabled);
                await Task.CompletedTask;
            });

            // After inner scope exits, outer scope is still active
            Assert.True(OpSecLockout.IsEnabled);
        });

        // After all scopes exit, caller sees disabled
        Assert.False(OpSecLockout.IsEnabled);
    }

    [Fact]
    public async Task ExceptionInScope_RestoresCallerState()
    {
        Assert.False(OpSecLockout.IsEnabled);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await OpSecLockout.ExecuteIsolatedAsync(async () =>
            {
                Assert.True(OpSecLockout.IsEnabled);
                await Task.CompletedTask;
                throw new InvalidOperationException("deliberate");
            });
        });

        // Must be restored even after exception
        Assert.False(OpSecLockout.IsEnabled);
    }

    [Fact]
    public void EnterScope_ProvidesDisposableIsolation()
    {
        Assert.False(OpSecLockout.IsEnabled);

        using (var scope1 = OpSecLockout.EnterScope())
        {
            Assert.True(OpSecLockout.IsEnabled);

            using (var scope2 = OpSecLockout.EnterScope())
            {
                Assert.True(OpSecLockout.IsEnabled);
                // Nested scope: depth = 2
            }

            // Still active after inner dispose (depth = 1)
            Assert.True(OpSecLockout.IsEnabled);
        }

        Assert.False(OpSecLockout.IsEnabled);
    }

    [Fact]
    public void EnterScope_IdempotentDispose()
    {
        Assert.False(OpSecLockout.IsEnabled);

        var scope = OpSecLockout.EnterScope();
        Assert.True(OpSecLockout.IsEnabled);

        scope.Dispose();
        Assert.False(OpSecLockout.IsEnabled);

        // Double-dispose should not throw or go negative
        scope.Dispose();
        Assert.False(OpSecLockout.IsEnabled);
    }
}
