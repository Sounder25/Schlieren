using RichardSzalay.MockHttp;
using Moq;
using Schlieren.Core.Forking;
using Schlieren.Core.Models; // Canonical
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
}
