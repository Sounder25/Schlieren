using System.Threading;

namespace Schlieren.Core.Security;

/// <summary>
/// Process-wide OpSec lockout. RPC is the authority; UI may only request this mode.
/// Distinct from <see cref="OpSecLockout"/> (per-async-flow test isolation).
/// </summary>
public static class OpSecGate
{
    private static int _locked;

    public static bool IsLocked => Volatile.Read(ref _locked) != 0;

    public static void SetLocked(bool locked) =>
        Volatile.Write(ref _locked, locked ? 1 : 0);

    /// <summary>
    /// Loopback Schlieren RPC and local files are allowed during lockout.
    /// Public providers, remote eth_getCode, and any non-loopback HTTP are not.
    /// </summary>
    public static bool IsLoopbackEndpoint(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        return uri.IsLoopback
            || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host is "127.0.0.1" or "::1" or "[::1]";
    }

    public static void AssertRemoteAllowed(string operation, string? endpoint = null)
    {
        if (!IsLocked) return;
        if (endpoint is not null && IsLoopbackEndpoint(endpoint)) return;
        throw new OpSecViolationException(
            $"[OpSec Violation] '{operation}' blocked. Lockout is on; only loopback RPC and local artifacts are allowed.");
    }
}
