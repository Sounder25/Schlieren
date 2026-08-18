using System;
using Microsoft.Extensions.Logging;

namespace Schlieren.RPC.Logging;

/// <summary>
/// Observable logger that raises events for GUI integration
/// Allows external components to subscribe to log events
/// </summary>
public class ObservableLogger : ILogger
{
    private readonly string _categoryName;
    public static event EventHandler<LogEventArgs>? LogEmitted;

    public ObservableLogger(string categoryName)
    {
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var logEntry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = logLevel,
            Category = _categoryName,
            Message = message,
            Exception = exception
        };

        LogEmitted?.Invoke(this, new LogEventArgs(logEntry));
        
        // Also write to console
        var color = GetColorForLevel(logLevel);
        Console.ForegroundColor = color;
        Console.WriteLine($"[{logLevel}] {_categoryName}: {message}");
        Console.ResetColor();
    }

    private static ConsoleColor GetColorForLevel(LogLevel level) => level switch
    {
        LogLevel.Critical => ConsoleColor.Red,
        LogLevel.Error => ConsoleColor.DarkRed,
        LogLevel.Warning => ConsoleColor.Yellow,
        LogLevel.Information => ConsoleColor.White,
        _ => ConsoleColor.Gray
    };
}

public class LogEntry
{
    public DateTime Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
}

public class LogEventArgs : EventArgs
{
    public LogEntry Entry { get; }
    public LogEventArgs(LogEntry entry) => Entry = entry;
}
