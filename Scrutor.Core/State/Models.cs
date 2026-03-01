using System.Numerics;
using Scrutor.Core.Primitives;

namespace Scrutor.Core.State;

/// <summary>
/// Ethereum Account State
/// </summary>
public sealed class Account
{
    public BigInteger Balance { get; set; } = BigInteger.Zero;
    public ulong Nonce { get; set; } = 0;
    public byte[] Code { get; set; } = Array.Empty<byte>();
    public Dictionary<BigInteger, BigInteger> Storage { get; set; } = new();

    // Helper for cloning state to avoid direct reference mutation
    public Account Clone()
    {
        return new Account
        {
            Balance = this.Balance,
            Nonce = this.Nonce,
            Code = (byte[])this.Code.Clone(),
            Storage = new Dictionary<BigInteger, BigInteger>(this.Storage)
        };
    }
}

public sealed class UnsupportedTransactionTypeException : Exception
{
    public byte TransactionType { get; }
    public UnsupportedTransactionTypeException(byte type) 
        : base($"Unsupported typed transaction type 0x{type:X2}.")
    {
        TransactionType = type;
    }
}

public enum TransactionAuthorization
{
    None,
    Signed,
    Impersonated,
    Internal
}

/// <summary>
/// Transaction Model for Mempool
/// </summary>
public sealed class Transaction : IComparable<Transaction>
{
    public byte[] Hash { get; set; } = Array.Empty<byte>();
    public Address From { get; set; }
    public Address? To { get; set; } // Null for contract creation
    public BigInteger Value { get; set; }
    public ulong Nonce { get; set; }
    public BigInteger GasPrice { get; set; } // Legacy or MaxFeePerGas
    public ulong GasLimit { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    
    /// <summary>
    /// The authorization method for this transaction.
    /// </summary>
    public TransactionAuthorization Authorization { get; set; } = TransactionAuthorization.Signed;

    /// <summary>
    /// Enables opcode-level tracing for debug RPC endpoints.
    /// </summary>
    public bool EnableTracing { get; set; } = false;

    // Signature data
    public int V { get; set; }
    public byte[] R { get; set; } = Array.Empty<byte>();
    public byte[] S { get; set; } = Array.Empty<byte>();

    public static Transaction FromRaw(byte[] rawTx)
    {
        if (rawTx == null || rawTx.Length == 0) 
            throw new ArgumentException("Transaction data cannot be empty");

        byte firstByte = rawTx[0];
        if (firstByte > 0 && firstByte <= 0x7f)
        {
            return firstByte switch
            {
                0x01 => DecodeTyped(rawTx, firstByte, nonceIndex: 1, gasPriceIndex: 2, gasLimitIndex: 3, toIndex: 4, valueIndex: 5, dataIndex: 6, vIndex: 8, rIndex: 9, sIndex: 10),
                0x02 => DecodeTyped(rawTx, firstByte, nonceIndex: 1, gasPriceIndex: 3, gasLimitIndex: 4, toIndex: 5, valueIndex: 6, dataIndex: 7, vIndex: 9, rIndex: 10, sIndex: 11),
                0x03 => DecodeTyped(rawTx, firstByte, nonceIndex: 1, gasPriceIndex: 3, gasLimitIndex: 4, toIndex: 5, valueIndex: 6, dataIndex: 7, vIndex: 11, rIndex: 12, sIndex: 13),
                _ => throw new UnsupportedTransactionTypeException(firstByte)
            };
        }

        if (firstByte < 0x80)
            throw new Exception($"Invalid transaction prefix: 0x{firstByte:X2}");

        var rlp = Scrutor.Core.Encoding.RlpDecoder.Decode(rawTx);
        if (!rlp.IsList) throw new Exception("Transaction RLP must be a list");

        var items = rlp.Items;
        if (items.Count < 6) throw new Exception("Invalid transaction RLP items count");

        var tx = new Transaction
        {
            Nonce = (ulong)items[0].ToBigInteger(),
            GasPrice = items[1].ToBigInteger(),
            GasLimit = (ulong)items[2].ToBigInteger(),
            To = items[3].Data.Length > 0 ? new Address(items[3].Data.ToArray()) : null,
            Value = items[4].ToBigInteger(),
            Data = items[5].Data.ToArray(),
            Hash = CryptoUtils.Keccak256(rawTx),
            Authorization = TransactionAuthorization.Signed
        };
        
        if (items.Count >= 9)
        {
            tx.V = (int)items[6].ToBigInteger();
            tx.R = items[7].Data.ToArray();
            tx.S = items[8].Data.ToArray();
            // We delay recovery until StateTransition or explicit request
        }
        
        return tx;
    }

    private static Transaction DecodeTyped(
        byte[] rawTx,
        byte txType,
        int nonceIndex,
        int gasPriceIndex,
        int gasLimitIndex,
        int toIndex,
        int valueIndex,
        int dataIndex,
        int vIndex,
        int rIndex,
        int sIndex)
    {
        if (rawTx.Length < 2)
            throw new Exception($"Invalid typed transaction type 0x{txType:X2}: missing payload");

        var payload = new byte[rawTx.Length - 1];
        Array.Copy(rawTx, 1, payload, 0, payload.Length);

        var rlp = Scrutor.Core.Encoding.RlpDecoder.Decode(payload);
        if (!rlp.IsList) throw new Exception($"Typed transaction payload for type 0x{txType:X2} must be an RLP list");

        var items = rlp.Items;
        if (items.Count <= sIndex)
            throw new Exception($"Invalid typed transaction type 0x{txType:X2}: insufficient RLP items");

        return new Transaction
        {
            Nonce = (ulong)items[nonceIndex].ToBigInteger(),
            GasPrice = items[gasPriceIndex].ToBigInteger(),
            GasLimit = (ulong)items[gasLimitIndex].ToBigInteger(),
            To = items[toIndex].Data.Length > 0 ? new Address(items[toIndex].Data.ToArray()) : null,
            Value = items[valueIndex].ToBigInteger(),
            Data = items[dataIndex].Data.ToArray(),
            V = (int)items[vIndex].ToBigInteger(),
            R = items[rIndex].Data.ToArray(),
            S = items[sIndex].Data.ToArray(),
            Hash = CryptoUtils.Keccak256(rawTx),
            Authorization = TransactionAuthorization.Signed
        };
    }

    public int CompareTo(Transaction? other)
    {
        if (other == null) return 1;
        if (GasPrice > other.GasPrice) return -1;
        if (GasPrice < other.GasPrice) return 1;
        return 0;
    }
}

public enum TransactionPriority
{
    Low,
    Normal,
    High,
    Critical
}
