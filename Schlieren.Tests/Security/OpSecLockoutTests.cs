using Schlieren.Core.Security;
using Xunit;

namespace Schlieren.Tests.Security;

public class OpSecLockoutTests
{
    public OpSecLockoutTests()
    {
        // Ensure OpSec is disabled before each test
        OpSecLockout.IsEnabled = false;
    }
    
    [Fact]
    public void AssertOffline_WhenDisabled_DoesNotThrow()
    {
        // Arrange
        OpSecLockout.IsEnabled = false;
        
        // Act - should not throw
        OpSecLockout.AssertOffline("eth_getBalance");
        
        // Assert - no exception means pass
    }
    
    [Fact]
    public void AssertOffline_WhenEnabled_ThrowsOpSecViolationException()
    {
        // Arrange
        OpSecLockout.IsEnabled = true;
        
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
        OpSecLockout.IsEnabled = true;
        
        // Act & Assert
        var ex = Assert.Throws<OpSecViolationException>(
            () => OpSecLockout.AssertOffline("debug_traceTransaction"));
        
        Assert.Contains("debug_traceTransaction", ex.Message);
    }
    
    [Fact]
    public void IsEnabled_CanBeSetAndGet()
    {
        // Arrange
        OpSecLockout.IsEnabled = false;
        Assert.False(OpSecLockout.IsEnabled);
        
        // Act
        OpSecLockout.IsEnabled = true;
        
        // Assert
        Assert.True(OpSecLockout.IsEnabled);
        
        // Cleanup
        OpSecLockout.IsEnabled = false;
    }
    
    [Fact]
    public void ExecuteIsolated_EnablesOpSecDuringExecution()
    {
        // Arrange
        OpSecLockout.IsEnabled = false;
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
        OpSecLockout.IsEnabled = false;
        
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
        // Arrange
        OpSecLockout.IsEnabled = true;
        
        // Act - ExecuteIsolated should restore to true after
        OpSecLockout.ExecuteIsolated(() =>
        {
            // execution
        });
        
        // Assert - restored to true (previous state)
        Assert.True(OpSecLockout.IsEnabled);
        
        // Cleanup
        OpSecLockout.IsEnabled = false;
    }
    
    [Fact]
    public void ExecuteIsolated_RestoresStateOnException()
    {
        // Arrange
        OpSecLockout.IsEnabled = false;
        
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
        // Arrange & Act - ExecuteIsolated runs with OpSec enabled
        // Don't call AssertOffline inside since it would throw
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
        OpSecLockout.IsEnabled = false;
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
        OpSecLockout.IsEnabled = false;
        
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
