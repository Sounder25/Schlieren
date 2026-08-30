using System.Numerics;
using Moq;
using Schlieren.Core.Forking;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Guard.Tests;

public sealed class StorageZeroOverlayTests
{
    [Fact]
    public async Task LocalZeroWrite_DoesNotRefetchRemoteValue()
    {
        var address = Address.FromHex("0x1111111111111111111111111111111111111111");
        var key = BigInteger.One;
        var fork = new Mock<IForkProvider>();
        fork.Setup(p => p.GetBalanceAsync(address, It.IsAny<ulong?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BigInteger.Zero);
        fork.Setup(p => p.GetTransactionCountAsync(address, It.IsAny<ulong?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1UL);
        fork.Setup(p => p.GetCodeAsync(address, It.IsAny<ulong?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0x00 });
        fork.Setup(p => p.GetStorageAtAsync(address, key, It.IsAny<ulong?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BigInteger(42));

        var state = new ForkingGlobalState(new GlobalState(), fork.Object, 100);

        Assert.Equal(42, await state.GetStorageAtAsync(address, key));
        state.SetStorageAt(address, key, BigInteger.Zero);
        Assert.Equal(BigInteger.Zero, await state.GetStorageAtAsync(address, key));
        fork.Verify(
            p => p.GetStorageAtAsync(address, key, 100, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
