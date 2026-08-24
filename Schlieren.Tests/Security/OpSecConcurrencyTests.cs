using Schlieren.Core.Security;
using Xunit;

namespace Schlieren.Tests.Security;

/// <summary>
/// Proves OpSecLockout isolation per async execution flow.
/// These tests fail against the current process-global implementation.
/// </summary>
public class OpSecConcurrencyTests
{
    [Fact]
    public async Task ConcurrentFlows_DoNotInterfere()
    {
        // Two concurrent tasks: one enters OpSec, the other stays outside.
        // The outside task must never see IsEnabled = true.
        var barrier = new Barrier(2);
        var outsideObservedTrue = false;

        var insideTask = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            await OpSecLockout.ExecuteIsolatedAsync(async () =>
            {
                Assert.True(OpSecLockout.IsEnabled);
                await Task.Delay(100); // Hold OpSec active
            });
        });

        var outsideTask = Task.Run(() =>
        {
            barrier.SignalAndWait();
            Thread.Sleep(50); // Let the inside task establish its scope
            if (OpSecLockout.IsEnabled)
                outsideObservedTrue = true;
            return Task.CompletedTask;
        });

        await Task.WhenAll(insideTask, outsideTask);
        Assert.False(outsideObservedTrue, "Outside flow observed OpSec enabled — state is leaking between flows");
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

    // [Fact] — uncomment after EnterScope is implemented
    public void EnterScope_ProvidesDisposableIsolation()
    {
        // The new contract: EnterScope returns IDisposable that enables OpSec
        // for the current async flow only, nesting correctly.
        // This test will compile once EnterScope is implemented.
        // Assert.False(OpSecLockout.IsEnabled);

        // using (OpSecLockout.EnterScope())
        // {
        //     Assert.True(OpSecLockout.IsEnabled);
        //
        //     using (OpSecLockout.EnterScope())
        //     {
        //         Assert.True(OpSecLockout.IsEnabled);
        //     }
        //
        //     // Still active after inner dispose
        //     Assert.True(OpSecLockout.IsEnabled);
        // }
        //
        // Assert.False(OpSecLockout.IsEnabled);
    }
}
