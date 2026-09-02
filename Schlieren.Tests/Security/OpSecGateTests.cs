using Schlieren.Core.Security;

namespace Schlieren.Tests.Security;

public sealed class OpSecGateTests : IDisposable
{
    public OpSecGateTests() => OpSecGate.SetLocked(false);

    public void Dispose() => OpSecGate.SetLocked(false);

    [Fact]
    public void Loopback_IsAllowedDuringLockout()
    {
        Assert.True(OpSecGate.IsLoopbackEndpoint("http://127.0.0.1:8545"));
        Assert.True(OpSecGate.IsLoopbackEndpoint("http://localhost:8545"));
        Assert.True(OpSecGate.IsLoopbackEndpoint("http://[::1]:8545"));
        Assert.False(OpSecGate.IsLoopbackEndpoint("https://eth.llamarpc.com"));
        Assert.False(OpSecGate.IsLoopbackEndpoint("not-a-url"));
        Assert.False(OpSecGate.IsLoopbackEndpoint(""));
        Assert.False(OpSecGate.IsLoopbackEndpoint(null));
    }

    [Fact]
    public void AssertRemoteAllowed_BlocksMissingEndpointWhenLocked()
    {
        OpSecGate.SetLocked(true);
        Assert.Throws<OpSecViolationException>(() => OpSecGate.AssertRemoteAllowed("external_http"));
    }

    [Fact]
    public void AssertRemoteAllowed_BlocksPublicProviderWhenLocked()
    {
        OpSecGate.SetLocked(true);
        Assert.Throws<OpSecViolationException>(() =>
            OpSecGate.AssertRemoteAllowed("eth_getCode", "https://eth.llamarpc.com"));
    }

    [Fact]
    public void AssertRemoteAllowed_AllowsLoopbackWhenLocked()
    {
        OpSecGate.SetLocked(true);
        OpSecGate.AssertRemoteAllowed("eth_getCode", "http://127.0.0.1:8545");
    }

    [Fact]
    public void AssertRemoteAllowed_AllowsPublicWhenUnlocked()
    {
        OpSecGate.SetLocked(false);
        OpSecGate.AssertRemoteAllowed("eth_getCode", "https://eth.llamarpc.com");
    }
}
