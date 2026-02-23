using Scrutor.RPC.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Scrutor.Tests.RPC;

public class ErrorHandlingTests
{
    [Fact]
    public void ObservableLogger_RaisesEvents()
    {
        // Arrange
        var eventRaised = false;
        LogEntry? capturedEntry = null;
        
        ObservableLogger.LogEmitted += (sender, args) =>
        {
            eventRaised = true;
            capturedEntry = args.Entry;
        };

        var logger = new ObservableLogger("Test");

        // Act
        logger.LogInformation("Test message");

        // Assert
        Assert.True(eventRaised);
        Assert.NotNull(capturedEntry);
        Assert.Equal("Test message", capturedEntry.Message);
        Assert.Equal(LogLevel.Information, capturedEntry.Level);
    }

    [Fact]
    public void ObservableLogger_HandlesExceptions()
    {
        // Arrange
        var exception = new InvalidOperationException("Test error");
        LogEntry? capturedEntry = null;
        
        ObservableLogger.LogEmitted += (sender, args) =>
        {
            capturedEntry = args.Entry;
        };

        var logger = new ObservableLogger("Test");

        // Act
        logger.LogError(exception, "Error occurred");

        // Assert
        Assert.NotNull(capturedEntry);
        Assert.Equal(LogLevel.Error, capturedEntry.Level);
        Assert.NotNull(capturedEntry.Exception);
        Assert.Equal("Test error", capturedEntry.Exception.Message);
    }
}
