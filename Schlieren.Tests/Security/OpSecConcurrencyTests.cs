using Schlieren.Core.Security;
using Xunit;

namespace Schlieren.Tests.Security;

/// <summary>
/// Proves OpSecLockout isolation per async execution flow.
/// </summary>
public class OpSecConcurrencyTests
{
    [Fact]
    public async Task ConcurrentFlows_DoNotInterfere()
    {
        // Two concurrent tasks: one enters OpSec, the other stays outside.
        // The outside task must never see IsEnabled = true.
        // Uses TaskCompletionSource to coordinate without sleeps.

        var insideScopeActive = new TaskCompletionSource();
        var outsideFinishedReading = new TaskCompletionSource();

        var insideTask = Task.Run(async () =>
        {
            await OpSecLockout.ExecuteIsolatedAsync(async () =>
            {
                Assert.True(OpSecLockout.IsEnabled);
                insideScopeActive.SetResult(); // Signal: OpSec is now active inside
                await outsideFinishedReading.Task; // Wait for outside to finish reading
            });
        });

        var outsideTask = Task.Run(async () =>
        {
            await insideScopeActive.Task; // Wait until inside scope is active
            try
            {
                bool outsideSawEnabled = OpSecLockout.IsEnabled;
                Assert.False(outsideSawEnabled, "Outside flow observed OpSec enabled — state is leaking between flows");
            }
            finally
            {
                outsideFinishedReading.SetResult(); // Release inside scope
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
