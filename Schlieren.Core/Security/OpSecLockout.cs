namespace Schlieren.Core.Security;

/// <summary>
/// Exception thrown when a network operation is attempted during OpSec mode.
/// </summary>
public sealed class OpSecViolationException : Exception
{
    public OpSecViolationException(string message) : base(message) { }
}

/// <summary>
/// Enforces 100% offline mode for private PoC / zero-day exploit testing.
/// 
/// OpSec (Operational Security) mode prevents any network traffic from
/// occurring during execution, ensuring that:
/// 
/// 1. Zero-day exploits cannot be leaked to RPC providers
/// 2. MEV bots cannot front-run private test transactions
/// 3. Bug hunters can test PoCs in complete privacy
/// 
/// Usage:
/// <code>
/// // Enable for entire session
/// OpSecLockout.IsEnabled = true;
/// 
/// // Or run isolated operation
/// OpSecLockout.ExecuteIsolated(() => {
///     var result = engine.Execute(privateExploitTx);
/// });
/// 
/// // Network operations check before executing
/// OpSecLockout.AssertOffline("eth_getBalance");
/// </code>
/// </summary>
public static class OpSecLockout
{
    private static bool _isEnabled;
    private static readonly object _lock = new();

    /// <summary>
    /// Gets or sets whether OpSec mode is active.
    /// When true, any network operation will throw OpSecViolationException.
    /// </summary>
    public static bool IsEnabled
    {
        get { lock (_lock) return _isEnabled; }
        set { lock (_lock) _isEnabled = value; }
    }

    /// <summary>
    /// Throws an <see cref="OpSecViolationException"/> if network traffic is attempted while OpSec mode is enabled.
    /// Call this before any outbound RPC, HTTP, or remote state fetch.
    /// </summary>
    /// <param name="operation">The name of the blocked operation (e.g., "eth_getBalance")</param>
    /// <exception cref="OpSecViolationException">Thrown if OpSec mode is active</exception>
    public static void AssertOffline(string operation)
    {
        if (IsEnabled)
        {
            throw new OpSecViolationException(
                $"[OpSec Violation] Network operation '{operation}' blocked. " +
                $"OpSecMode is active to prevent zero-day exploit payload leakage.");
        }
    }

    /// <summary>
    /// Runs an action inside a thread-safe, isolated OpSec sandbox.
    /// Network operations will throw during execution.
    /// </summary>
    /// <param name="action">The action to execute in OpSec mode</param>
    public static void ExecuteIsolated(Action action)
    {
        lock (_lock)
        {
            bool previous = _isEnabled;
            _isEnabled = true;
            try
            {
                action();
            }
            finally
            {
                _isEnabled = previous;
            }
        }
    }

    /// <summary>
    /// Runs an async function inside a thread-safe, isolated OpSec sandbox.
    /// Network operations will throw during execution.
    /// </summary>
    /// <param name="action">The async function to execute in OpSec mode</param>
    public static async Task ExecuteIsolatedAsync(Func<Task> action)
    {
        // Enable OpSec before starting
        lock (_lock)
        {
            _isEnabled = true;
        }
        
        try
        {
            await action();
        }
        finally
        {
            // Disable after completion
            lock (_lock)
            {
                _isEnabled = false;
            }
        }
    }

    /// <summary>
    /// Runs a function inside a thread-safe, isolated OpSec sandbox and returns the result.
    /// </summary>
    /// <typeparam name="T">The return type</typeparam>
    /// <param name="func">The function to execute in OpSec mode</param>
    /// <returns>The result of the function</returns>
    public static T ExecuteIsolated<T>(Func<T> func)
    {
        lock (_lock)
        {
            bool previous = _isEnabled;
            _isEnabled = true;
            try
            {
                return func();
            }
            finally
            {
                _isEnabled = previous;
            }
        }
    }

    /// <summary>
    /// Runs an async function inside a thread-safe, isolated OpSec sandbox and returns the result.
    /// </summary>
    /// <typeparam name="T">The return type</typeparam>
    /// <param name="func">The async function to execute in OpSec mode</param>
    /// <returns>The result of the function</returns>
    public static async Task<T> ExecuteIsolatedAsync<T>(Func<Task<T>> func)
    {
        lock (_lock)
        {
            _isEnabled = true;
        }
        
        try
        {
            return await func();
        }
        finally
        {
            lock (_lock)
            {
                _isEnabled = false;
            }
        }
    }
}
