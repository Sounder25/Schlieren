using System.Numerics;
using Schlieren.Core.State;
using Schlieren.Core.Execution;
using Schlieren.Core.Primitives;
using Xunit;

namespace Schlieren.Tests.State;

public class TxMempoolTests
{
    private readonly TxMempool _mempool;

    public TxMempoolTests()
    {
        _mempool = new TxMempool();
    }

    [Fact]
    public void PopBest_ReturnsHighestGasPrice_First()
    {
        // Arrange
        var lowGasTx = CreateTx(10, 1);
        var highGasTx = CreateTx(20, 2);
        var midGasTx = CreateTx(15, 3);

        // Act
        _mempool.Add(lowGasTx);
        _mempool.Add(highGasTx);
        _mempool.Add(midGasTx);

        // Assert
        var first = _mempool.PopBest();
        var second = _mempool.PopBest();
        var third = _mempool.PopBest();

        Assert.NotNull(first);
        Assert.Equal(highGasTx, first); // 20
        Assert.Equal(midGasTx, second); // 15
        Assert.Equal(lowGasTx, third);  // 10
    }

    [Fact]
    public void Add_DeduplicatesTransactions()
    {
        // Arrange
        var tx = CreateTx(10, 1);

        // Act
        _mempool.Add(tx);
        _mempool.Add(tx); // Duplicate add

        // Assert
        Assert.Equal(1, _mempool.Count);
    }

    private static Transaction CreateTx(int gasPrice, int nonce)
    {
        var random = new Random(nonce); 
        var hash = new byte[32];
        random.NextBytes(hash);

        return new Transaction
        {
            Hash = hash,
            From = Address.Zero,
            GasPrice = new BigInteger(gasPrice),
            Nonce = (ulong)nonce,
            Value = BigInteger.Zero
        };
    }
}