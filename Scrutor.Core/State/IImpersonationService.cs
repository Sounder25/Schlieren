using System.Collections.Concurrent;
using Scrutor.Core.Primitives;

namespace Scrutor.Core.State;

public interface IImpersonationService
{
    void Impersonate(Address address);
    void StopImpersonating(Address address);
    bool IsImpersonated(Address address);
    void Clear();
}

public sealed class ImpersonationService : IImpersonationService
{
    private readonly ConcurrentDictionary<string, byte> _impersonated = new();

    private static string Canon(Address address)
    {
        // Ensure 20-byte canonical hex string (40 chars)
        // We do NOT rely on Address.ToString() to ensure stability
        return "0x" + Convert.ToHexString(address.Bytes).ToLowerInvariant();
    }

    public void Impersonate(Address address) => _impersonated.TryAdd(Canon(address), 0);
    public void StopImpersonating(Address address) => _impersonated.TryRemove(Canon(address), out _);
    public bool IsImpersonated(Address address) => _impersonated.ContainsKey(Canon(address));
    public void Clear() => _impersonated.Clear();
}