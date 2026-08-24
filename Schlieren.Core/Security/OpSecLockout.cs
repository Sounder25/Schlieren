using System.Threading;

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
/// **Isolation guarantee:** OpSec state is isolated per async execution flow.
/// Concurrent flows do not interfere with each other. Use <see cref="EnterScope"/>
/// or <see cref="ExecuteIsolatedAsync"/> to enable OpSec for a single flow.
/// </summary>
public sealed class OpSecLockout
{
    private static readonly AsyncLocal<int> _scopeDepth = new();

    /// <summary>
    /// Gets whether OpSec mode is active for the current async flow.
    /// </summary>
    public static bool IsEnabled => _scopeDepth.Value > 0;

    /// <summary>
    /// Enters an OpSec scope for the current async flow.
    /// Returns an <see cref="IDisposable"/> that exits the scope on dispose.
    /// Nested scopes are supported; the state is only disabled when the outermost
    /// scope exits.
    /// </summary>
    public static IDisposable EnterScope()
    {
        _scopeDepth.Value++;
        return new OpSecScope(() => _scopeDepth.Value--);
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
    /// Runs an action inside an isolated OpSec sandbox for the current async flow.
    /// Network operations will throw during execution.
    /// </summary>
    /// <param name="action">The action to execute in OpSec mode</param>
    public static void ExecuteIsolated(Action action)
    {
        using var scope = EnterScope();
        action();
    }

    /// <summary>
    /// Runs a function inside an isolated OpSec sandbox for the current async flow and returns the result.
    /// </summary>
    /// <typeparam name="T">The return type</typeparam>
    /// <param name="func">The function to execute in OpSec mode</param>
    /// <returns>The result of the function</returns>
    public static T ExecuteIsolated<T>(Func<T> func)
    {
        using var scope = EnterScope();
        return func();
    }

    /// <summary>
    /// Runs an async function inside an isolated OpSec sandbox for the current async flow.
    /// Network operations will throw during execution.
    /// </summary>
    /// <param name="action">The async function to execute in OpSec mode</param>
    public static async Task ExecuteIsolatedAsync(Func<Task> action)
    {
        using var scope = EnterScope();
        await action();
    }

    /// <summary>
    /// Runs an async function inside an isolated OpSec sandbox for the current async flow and returns the result.
    /// </summary>
    /// <typeparam name="T">The return type</typeparam>
    /// <param name="func">The async function to execute in OpSec mode</param>
    /// <returns>The result of the function</returns>
    public static async Task<T> ExecuteIsolatedAsync<T>(Func<Task<T>> func)
    {
        using var scope = EnterScope();
        return await func();
    }

    private sealed class OpSecScope : IDisposable
    {
        private Action? _exit;

        public OpSecScope(Action exit) => _exit = exit;

        public void Dispose()
        {
            _exit?.Invoke();
            _exit = null;
        }
    }
}
