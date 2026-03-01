using System.Numerics;
using Nethereum.RLP;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using Xunit;

namespace Scrutor.Tests.State;

public class TransactionRawDecodingTests
{
    [Fact]
    public void FromRaw_DecodesType1TransactionFields()
    {
        var to = Address.FromHex("0x1111111111111111111111111111111111111111");
        var r = Enumerable.Repeat((byte)0x11, 32).ToArray();
        var s = Enumerable.Repeat((byte)0x22, 32).ToArray();

        var payload = RLP.EncodeList(
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(1)), // chainId
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(7)), // nonce
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(50_000_000_000)), // gasPrice
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(21_000)), // gasLimit
            RLP.EncodeElement(to.Bytes),
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(5)), // value
            RLP.EncodeElement(new byte[] { 0x12, 0x34 }), // data
            RLP.EncodeList(), // accessList
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(1)), // yParity
            RLP.EncodeElement(r),
            RLP.EncodeElement(s)
        );

        var raw = new byte[1 + payload.Length];
        raw[0] = 0x01;
        Array.Copy(payload, 0, raw, 1, payload.Length);

        var tx = Transaction.FromRaw(raw);

        Assert.Equal<ulong>(7, tx.Nonce);
        Assert.Equal(new BigInteger(50_000_000_000), tx.GasPrice);
        Assert.Equal<ulong>(21_000, tx.GasLimit);
        Assert.Equal(to, tx.To);
        Assert.Equal(new BigInteger(5), tx.Value);
        Assert.Equal(new byte[] { 0x12, 0x34 }, tx.Data);
        Assert.Equal(1, tx.V);
        Assert.Equal(r, tx.R);
        Assert.Equal(s, tx.S);
        Assert.Equal(CryptoUtils.Keccak256(raw), tx.Hash);
    }

    [Fact]
    public void FromRaw_DecodesType2TransactionFields()
    {
        var to = Address.FromHex("0x2222222222222222222222222222222222222222");
        var r = Enumerable.Repeat((byte)0x33, 32).ToArray();
        var s = Enumerable.Repeat((byte)0x44, 32).ToArray();

        var payload = RLP.EncodeList(
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(1)), // chainId
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(9)), // nonce
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(2_000_000_000)), // maxPriorityFeePerGas
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(30_000_000_000)), // maxFeePerGas
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(70_000)), // gasLimit
            RLP.EncodeElement(to.Bytes),
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(8)), // value
            RLP.EncodeElement(Array.Empty<byte>()), // data
            RLP.EncodeList(), // accessList
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(0)), // yParity
            RLP.EncodeElement(r),
            RLP.EncodeElement(s)
        );

        var raw = new byte[1 + payload.Length];
        raw[0] = 0x02;
        Array.Copy(payload, 0, raw, 1, payload.Length);

        var tx = Transaction.FromRaw(raw);

        Assert.Equal<ulong>(9, tx.Nonce);
        // Scrutor maps effective gas cap into GasPrice for execution path compatibility.
        Assert.Equal(new BigInteger(30_000_000_000), tx.GasPrice);
        Assert.Equal<ulong>(70_000, tx.GasLimit);
        Assert.Equal(to, tx.To);
        Assert.Equal(new BigInteger(8), tx.Value);
        Assert.Equal(Array.Empty<byte>(), tx.Data);
        Assert.Equal(0, tx.V);
        Assert.Equal(r, tx.R);
        Assert.Equal(s, tx.S);
        Assert.Equal(CryptoUtils.Keccak256(raw), tx.Hash);
    }

    [Fact]
    public void FromRaw_DecodesType3TransactionFields()
    {
        var to = Address.FromHex("0x3333333333333333333333333333333333333333");
        var r = Enumerable.Repeat((byte)0x55, 32).ToArray();
        var s = Enumerable.Repeat((byte)0x66, 32).ToArray();

        var payload = RLP.EncodeList(
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(1)), // chainId
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(2)), // nonce
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(1_000_000_000)), // maxPriorityFeePerGas
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(40_000_000_000)), // maxFeePerGas
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(100_000)), // gasLimit
            RLP.EncodeElement(to.Bytes),
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(0)), // value
            RLP.EncodeElement(Array.Empty<byte>()), // data
            RLP.EncodeList(), // accessList
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(1_000_000_000)), // maxFeePerBlobGas
            RLP.EncodeList( // blobVersionedHashes
                RLP.EncodeElement(Enumerable.Repeat((byte)0x99, 32).ToArray())
            ),
            RLP.EncodeElement(CryptoUtils.ToBytesForRLP(1)), // yParity
            RLP.EncodeElement(r),
            RLP.EncodeElement(s)
        );

        var raw = new byte[1 + payload.Length];
        raw[0] = 0x03;
        Array.Copy(payload, 0, raw, 1, payload.Length);

        var tx = Transaction.FromRaw(raw);

        Assert.Equal<ulong>(2, tx.Nonce);
        Assert.Equal(new BigInteger(40_000_000_000), tx.GasPrice);
        Assert.Equal<ulong>(100_000, tx.GasLimit);
        Assert.Equal(to, tx.To);
        Assert.Equal(new BigInteger(0), tx.Value);
        Assert.Equal(Array.Empty<byte>(), tx.Data);
        Assert.Equal(1, tx.V);
        Assert.Equal(r, tx.R);
        Assert.Equal(s, tx.S);
        Assert.Equal(CryptoUtils.Keccak256(raw), tx.Hash);
    }
}
