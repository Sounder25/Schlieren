using System.Text;
using Scrutor.Core.Primitives;
using Xunit;
using Nethereum.Signer;
using Nethereum.Util;

namespace Scrutor.Tests.Primitives;

public class CryptoUtilsTests
{
    [Fact]
    public void Keccak256_EmptyString_ReturnsCorrectHash()
    {
        // Keccak256("") = c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470
        var data = Array.Empty<byte>();
        var hash = CryptoUtils.Keccak256(data);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        
        Assert.Equal("c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470", hex);
    }

    [Fact]
    public void Keccak256_HelloWorld_ReturnsCorrectHash()
    {
        // Keccak256("hello world") = 47173285a8d7341e5e972fc677286384f802f8ef42a5ec5f03bbfa254cb01fad
        var data = Encoding.UTF8.GetBytes("hello world");
        var hash = CryptoUtils.Keccak256(data);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        
        Assert.Equal("47173285a8d7341e5e972fc677286384f802f8ef42a5ec5f03bbfa254cb01fad", hex);
    }

    [Fact]
    public void RecoverAddress_RoundTrip_Works()
    {
        // Deterministic key for testing
        var key = new EthECKey("4c0883a69102937d6231471b5dbb6204fe51296170827921417dd989738d11af");
        var hash = CryptoUtils.Keccak256(Encoding.UTF8.GetBytes("test"));
        var sig = key.SignAndCalculateV(hash);
        
        var recovered = CryptoUtils.RecoverAddress(hash, (int)sig.V[0], sig.R, sig.S);
        
        Assert.Equal(key.GetPublicAddress().ToLowerInvariant(), recovered.ToString().ToLowerInvariant());
    }

    [Fact]
    public void RecoverAddress_WithEip155V_Works()
    {
        var key = new EthECKey("4c0883a69102937d6231471b5dbb6204fe51296170827921417dd989738d11af");
        var hash = CryptoUtils.Keccak256(Encoding.UTF8.GetBytes("test"));
        var sig = key.SignAndCalculateV(hash);
        
        // Simulate EIP-155 V: v = CHAIN_ID * 2 + 35 + parity
        // For mainnet (1): 1 * 2 + 35 + (0 or 1) = 37 or 38
        int eip155V = 1 * 2 + 35 + (sig.V[0] - 27);
        
        var recovered = CryptoUtils.RecoverAddress(hash, eip155V, sig.R, sig.S);
        
        Assert.Equal(key.GetPublicAddress().ToLowerInvariant(), recovered.ToString().ToLowerInvariant());
    }
}