using Schlieren.Core.Security;
using Xunit;

namespace Schlieren.Tests.Security;

public class OpSecLockoutTests
{
    public OpSecLockoutTests()
    {
        // Ensure OpSec is disabled before each test (scoped per async flow)
        // Since IsEnabled is now read-only, we rely on AsyncLocal isolation
        // to start fresh per test context.
    }

    [Fact]
    public void AssertOffline_WhenDisabled_DoesNotThrow()
    {
        // Arrange - default state is disabled for this flow
        Assert.False(OpSecLockout.IsEnabled);

        // Act - should not throw
        OpSecLockout.AssertOffline("eth_getBalance");

        // Assert - no exception means pass
    }

    [Fact]
    public void AssertOffline_WhenEnabled_ThrowsOpSecViolationException()
    {
        // Arrange
        using var scope = OpSecLockout.EnterScope();

        // Act & Assert
        var ex = Assert.Throws<OpSecViolationException>(
            () => OpSecLockout.AssertOffline("eth_getBalance"));

        Assert.Contains("eth_getBalance", ex.Message);
        Assert.Contains("OpSec Violation", ex.Message);
    }

    [Fact]
    public void AssertOffline_WhenEnabled_IncludesOperationName()
    {
        // Arrange
        using var scope = OpSecLockout.EnterScope();

        // Act & Assert
        var ex = Assert.Throws<OpSecViolationException>(
            () => OpSecLockout.AssertOffline("debug_traceTransaction"));

        Assert.Contains("debug_traceTransaction", ex.Message);
    }

    [Fact]
    public void IsEnabled_IsReadOnly()
    {
        // Arrange & Act - IsEnabled is read-only; EnterScope controls state
        Assert.False(OpSecLockout.IsEnabled);

        using (var scope = OpSecLockout.EnterScope())
        {
            Assert.True(OpSecLockout.IsEnabled);
        }

        Assert.False(OpSecLockout.IsEnabled);
    }

    [Fact]
    public void ExecuteIsolated_EnablesOpSecDuringExecution()
    {
        // Arrange
        bool opSecWasEnabledDuringExecution = false;

        // Act
        OpSecLockout.ExecuteIsolated(() =>
        {
            opSecWasEnabledDuringExecution = OpSecLockout.IsEnabled;
        });

        // Assert - OpSec was enabled inside the action
        Assert.True(opSecWasEnabledDuringExecution);
    }

    [Fact]
    public void ExecuteIsolated_RestoresPreviousStateAfterExecution()
    {
        // Arrange
        Assert.False(OpSecLockout.IsEnabled);

        // Act
        OpSecLockout.ExecuteIsolated(() =>
        {
            // OpSec is enabled during execution
        });

        // Assert - OpSec is restored to previous state (false)
        Assert.False(OpSecLockout.IsEnabled);
    }

    [Fact]
    public void ExecuteIsolated_RestoresTrueStateWhenPreviouslyEnabled()
    {
        // Arrange - start with a scope
        using var outerScope = OpSecLockout.EnterScope();
        Assert.True(OpSecLockout.IsEnabled);

        // Act - nested ExecuteIsolated should preserve outer scope
        OpSecLockout.ExecuteIsolated(() =>
        {
            // execution with nested scope
        });

        // Assert - still enabled from outer scope
        Assert.True(OpSecLockout.IsEnabled);
    }

    [Fact]
    public void ExecuteIsolated_RestoresStateOnException()
    {
        // Arrange
        Assert.False(OpSecLockout.IsEnabled);

        // Act
        Assert.Throws<InvalidOperationException>(() =>
        {
            OpSecLockout.ExecuteIsolated(() =>
            {
                throw new InvalidOperationException("Test exception");
            });
        });

        // Assert - OpSec state is still restored after exception
        Assert.False(OpSecLockout.IsEnabled);
    }

    [Fact]
    public void ExecuteIsolated_WithReturnValue_ReturnsValue()
    {
        // Arrange & Act
        var result = OpSecLockout.ExecuteIsolated(() =>
        {
            // OpSec is enabled here - can run secure code
            return 42;
        });

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public void ExecuteIsolated_BlockNetworkOperations()
    {
        // Arrange
        bool networkBlocked = false;

        // Act
        OpSecLockout.ExecuteIsolated(() =>
        {
            try
            {
                OpSecLockout.AssertOffline("eth_call");
            }
            catch (OpSecViolationException)
            {
                networkBlocked = true;
            }
        });

        // Assert - network operations should be blocked inside
        Assert.True(networkBlocked);
    }

    [Fact]
    public async Task ExecuteIsolatedAsync_EnablesOpSecDuringExecution()
    {
        // Arrange
        bool opSecWasEnabled = false;

        // Act
        await OpSecLockout.ExecuteIsolatedAsync(async () =>
        {
            await Task.Delay(10);
            opSecWasEnabled = OpSecLockout.IsEnabled;
        });

        // Assert
        Assert.True(opSecWasEnabled);
        Assert.False(OpSecLockout.IsEnabled); // Restored after
    }

    [Fact]
    public async Task ExecuteIsolatedAsync_RestoresStateOnException()
    {
        // Arrange
        Assert.False(OpSecLockout.IsEnabled);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await OpSecLockout.ExecuteIsolatedAsync(async () =>
            {
                await Task.Delay(10);
                throw new InvalidOperationException("Test");
            });
        });

        // Assert - state restored after async exception
        Assert.False(OpSecLockout.IsEnabled);
    }

    [Fact]
    public async Task ExecuteIsolatedAsync_WithReturnValue_ReturnsValue()
    {
        // Arrange & Act
        var result = await OpSecLockout.ExecuteIsolatedAsync(async () =>
        {
            await Task.Delay(10);
            return "secure_result";
        });

        // Assert
        Assert.Equal("secure_result", result);
    }

    [Fact]
    public void OpSecViolationException_IsException()
    {
        // Arrange
        var ex = new OpSecViolationException("test");

        // Assert
        Assert.IsAssignableFrom<Exception>(ex);
        Assert.Equal("test", ex.Message);
    }
}
