using RichardSzalay.MockHttp;
using Moq;
using Schlieren.Core.Forking;
using Schlieren.Core.Models; // Canonical
using Schlieren.Core.Primitives;
using Schlieren.Core.Security;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Schlieren.Tests.Forking;

public class ForkProviderTests
{
    private readonly MockHttpMessageHandler _mockHttp;
    private readonly Mock<IBlockCache> _mockCache;
    private readonly ForkProvider _provider;

    public ForkProviderTests()
    {
        OpSecGate.SetLocked(false);
        _mockHttp = new MockHttpMessageHandler();
        var client = _mockHttp.ToHttpClient();
        client.BaseAddress = new Uri("http://localhost:8545");
        _mockCache = new Mock<IBlockCache>();
        _provider = new ForkProvider(client, _mockCache.Object);
    }

    [Fact]
    public async Task GetBlockByNumber_RpcSuccess_ReturnsBlock()
    {
        // Arrange
        // DTO response from RPC (Hex)
        var dto = new ForkBlockDto 
        { 
            Number = "0x1", 
            Hash = "0xabc",
            Difficulty = "0xa" 
        };
        var response = new RpcResponse<ForkBlockDto> { Result = dto };
        
        _mockHttp.Expect(HttpMethod.Post, "http://localhost:8545/")
            .Respond("application/json", JsonSerializer.Serialize(response));
        
        Block? outBlock = null;
        _mockCache.Setup(x => x.TryGetBlock(It.IsAny<ulong>(), out outBlock)).Returns(false);

        // Act
        var block = await _provider.GetBlockByNumberAsync(1);

        // Assert
        Assert.NotNull(block);
        Assert.Equal(1UL, block!.Number); // Canonical is ulong
        Assert.Equal("0xabc", block.Hash);
        _mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetCode_WhenOpSecLocked_PublicProvider_Throws()
    {
        var mockHttp = new MockHttpMessageHandler();
        var client = mockHttp.ToHttpClient();
        client.BaseAddress = new Uri("https://eth.llamarpc.com");
        var provider = new ForkProvider(client, _mockCache.Object);
        OpSecGate.SetLocked(true);
        try
        {
            await Assert.ThrowsAsync<OpSecViolationException>(
                () => provider.GetCodeAsync(Address.Zero));
        }
        finally
        {
            OpSecGate.SetLocked(false);
        }
    }

    [Fact]
    public async Task GetCode_WhenOpSecLocked_LoopbackProvider_IsAllowed()
    {
        OpSecGate.SetLocked(true);
        try
        {
            var response = new RpcResponse<string> { Result = "0x6000" };
            _mockHttp.Expect(HttpMethod.Post, "http://localhost:8545/")
                .Respond("application/json", JsonSerializer.Serialize(response));
            var code = await _provider.GetCodeAsync(Address.Zero);
            Assert.Equal(new byte[] { 0x60, 0x00 }, code);
            _mockHttp.VerifyNoOutstandingExpectation();
        }
        finally
        {
            OpSecGate.SetLocked(false);
        }
    }
}
