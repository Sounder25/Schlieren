using System.Numerics;
using Moq;
using Scrutor.Core.Forking;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using Xunit;

namespace Scrutor.Tests.Forking;

public class ForkingStateTests
{
    private readonly Mock<IForkProvider> _mockForkProvider;
    private readonly GlobalState _localState;
    private readonly ForkingGlobalState _forkingState;
    private readonly Address _testAddress = Address.FromHex("0x1234567890123456789012345678901234567890");

    public ForkingStateTests()
    {
        _mockForkProvider = new Mock<IForkProvider>();
        _localState = new GlobalState();
        _forkingState = new ForkingGlobalState(_localState, _mockForkProvider.Object, 12345);
    }

    [Fact]
    public async Task GetBalanceAsync_FetchesFromRemote_IfNotInLocal()
    {
        // Arrange
        var expectedBalance = new BigInteger(1000);
        _mockForkProvider.Setup(p => p.GetBalanceAsync(_testAddress, It.IsAny<ulong?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedBalance);
        _mockForkProvider.Setup(p => p.GetTransactionCountAsync(_testAddress, It.IsAny<ulong?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5UL);
        _mockForkProvider.Setup(p => p.GetCodeAsync(_testAddress, It.IsAny<ulong?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0x60, 0x01 });

        // Act
        var actualBalance = await _forkingState.GetBalanceAsync(_testAddress);

        // Assert
        Assert.Equal(expectedBalance, actualBalance);
        _mockForkProvider.Verify(p => p.GetBalanceAsync(_testAddress, 12345, It.IsAny<CancellationToken>()), Times.Once);
        
        var cachedBalance = await _forkingState.GetBalanceAsync(_testAddress);
        Assert.Equal(expectedBalance, cachedBalance);
        _mockForkProvider.Verify(p => p.GetBalanceAsync(_testAddress, 12345, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetStorageAtAsync_FetchesFromRemote_IfLocalIsZero()
    {
        // Arrange
        var key = new BigInteger(1);
        var expectedValue = new BigInteger(42);
        
        _mockForkProvider.Setup(p => p.GetBalanceAsync(_testAddress, It.IsAny<ulong?>(), It.IsAny<CancellationToken>())).ReturnsAsync(BigInteger.Zero);
        _mockForkProvider.Setup(p => p.GetTransactionCountAsync(_testAddress, It.IsAny<ulong?>(), It.IsAny<CancellationToken>())).ReturnsAsync(0UL);
        _mockForkProvider.Setup(p => p.GetCodeAsync(_testAddress, It.IsAny<ulong?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<byte>());
        
        _mockForkProvider.Setup(p => p.GetStorageAtAsync(_testAddress, key, It.IsAny<ulong?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedValue);

        // Act
        var actualValue = await _forkingState.GetStorageAtAsync(_testAddress, key);

        // Assert
        Assert.Equal(expectedValue, actualValue);
        _mockForkProvider.Verify(p => p.GetStorageAtAsync(_testAddress, key, 12345, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetBalance_PreventsRemoteFetch()
    {
        // Arrange
        _forkingState.SetBalance(_testAddress, 500);

        // Act
        var balance = await _forkingState.GetBalanceAsync(_testAddress);

        // Assert
        Assert.Equal(new BigInteger(500), balance);
        _mockForkProvider.Verify(p => p.GetBalanceAsync(It.IsAny<Address>(), It.IsAny<ulong?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
