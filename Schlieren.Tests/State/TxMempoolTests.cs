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
    public void PopBest_ReturnsHighestGasPrice_First_AcrossSenders()
    {
        // Price ordering applies across senders' respective front-of-queue transactions.
        var lowGasTx = CreateTx(10, 0, sender: 1);
        var highGasTx = CreateTx(20, 0, sender: 2);
        var midGasTx = CreateTx(15, 0, sender: 3);

        _mempool.Add(lowGasTx);
        _mempool.Add(highGasTx);
        _mempool.Add(midGasTx);

        var first = _mempool.PopBest();
        var second = _mempool.PopBest();
        var third = _mempool.PopBest();

        Assert.NotNull(first);
        Assert.Equal(highGasTx, first); // 20
        Assert.Equal(midGasTx, second); // 15
        Assert.Equal(lowGasTx, third);  // 10
    }

    [Fact]
    public void PopBest_RespectsNonceOrder_WithinSameSender()
    {
        // Same sender, ascending nonces, prices deliberately NOT in nonce order.
        // A higher-nonce transaction must never be offered while a lower one from the
        // same sender is still pending — offering it causes NonceTooHigh on apply and,
        // since it keeps winning the price race on every retry, a persistent livelock
        // that starves the eligible lower-nonce transaction.
        var nonce0 = CreateTx(10, 0);
        var nonce1 = CreateTx(20, 1);
        var nonce2 = CreateTx(15, 2);

        _mempool.Add(nonce2);
        _mempool.Add(nonce0);
        _mempool.Add(nonce1);

        var first = _mempool.PopBest();
        var second = _mempool.PopBest();
        var third = _mempool.PopBest();

        Assert.Equal(nonce0, first);
        Assert.Equal(nonce1, second);
        Assert.Equal(nonce2, third);
    }

    [Fact]
    public void Add_DeduplicatesTransactions()
    {
        // Arrange
        var tx = CreateTx(10, 1);

        // Act
        var firstAdd = _mempool.Add(tx);
        var secondAdd = _mempool.Add(tx); // Duplicate add

        // Assert
        Assert.True(firstAdd);
        Assert.False(secondAdd);
        Assert.Equal(1, _mempool.Count);
    }

    [Fact]
    public void Add_SameNonceHigherPrice_ReplacesAndEvictsOldHash()
    {
        var original = CreateTx(10, 0);
        var replacement = CreateTx(20, 0); // same sender/nonce, higher price, different hash

        Assert.True(_mempool.Add(original));
        Assert.True(_mempool.Add(replacement));

        // Only the replacement remains reachable — the old hash must not linger orphaned.
        Assert.Equal(1, _mempool.Count);
        Assert.Equal(replacement, _mempool.PopBest());
    }

    [Fact]
    public void Add_SameNonceLowerOrEqualPrice_IsRejected()
    {
        var original = CreateTx(20, 0);
        var underpriced = CreateTx(10, 0);
        // Same nonce/sender/price as original but a distinct hash (differing salt models two
        // real transactions that happen to share price+nonce but differ elsewhere, e.g. calldata)
        // — otherwise this would collide with the duplicate-hash check instead of exercising
        // the "equal price does not replace" comparison.
        var samePriced = CreateTx(20, 0, salt: 1);

        Assert.True(_mempool.Add(original));
        Assert.False(_mempool.Add(underpriced));
        Assert.False(_mempool.Add(samePriced));

        Assert.Equal(1, _mempool.Count);
        Assert.Equal(original, _mempool.PopBest());
    }

    private static Transaction CreateTx(int gasPrice, int nonce, int sender = 1, int salt = 0)
    {
        // Seed must vary with gasPrice too — real transactions with different gas prices
        // have different hashes, and a replacement test needs a distinct hash from the
        // transaction it's replacing or it collides with the duplicate-hash check instead
        // of exercising the same-nonce price-comparison path.
        var random = new Random(unchecked(nonce * 1000 + sender * 7919 + gasPrice * 104_729 + salt * 15_485_863));
        var hash = new byte[32];
        random.NextBytes(hash);

        var senderBytes = new byte[20];
        senderBytes[19] = (byte)sender;

        return new Transaction
        {
            Hash = hash,
            From = new Address(senderBytes),
            GasPrice = new BigInteger(gasPrice),
            Nonce = (ulong)nonce,
            Value = BigInteger.Zero
        };
    }
}