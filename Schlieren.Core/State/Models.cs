using System.Numerics;
using Schlieren.Core.Primitives;

namespace Schlieren.Core.State;

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
    Simulation,
    Internal,
    /// <summary>
    /// Block-level system call (EIP-4788, EIP-2935, etc.).
    /// Bypasses all sender validation, fee checks, and balance deduction.
    /// CALLER is set to SYSTEM_ADDRESS (0xffff...fffe) inside the contract.
    /// </summary>
    System
}

public enum StoragePresence
{
    Empty,
    NonEmpty,
    Unknown
}

/// <summary>
/// EIP-2930 access list entry: one address + zero or more storage keys.
/// </summary>
public sealed class AccessListEntry
{
    public Address Address { get; set; }
    public IReadOnlyList<BigInteger> StorageKeys { get; set; } = Array.Empty<BigInteger>();
}

/// <summary>
/// Transaction Model for Mempool
/// </summary>
public sealed class Transaction : IComparable<Transaction>
{
    /// <summary>Transaction hash: keccak256(raw signed payload). Used as eth tx id.</summary>
    public byte[] Hash { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// ECDSA recovery digest. For typed txs this is keccak256(type || rlp(unsigned fields)),
    /// which differs from <see cref="Hash"/>. When empty, recovery falls back to <see cref="Hash"/>.
    /// </summary>
    public byte[] SigningHash { get; set; } = Array.Empty<byte>();

    public Address From { get; set; }
    public Address? To { get; set; } // Null for contract creation
    public BigInteger Value { get; set; }
    public ulong Nonce { get; set; }
    public BigInteger GasPrice { get; set; } // Legacy or MaxFeePerGas
    public ulong GasLimit { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// EIP-2930/1559/4844 access list. Empty for legacy transactions.
    /// </summary>
    public IReadOnlyList<AccessListEntry> AccessList { get; set; } = Array.Empty<AccessListEntry>();

    /// <summary>
    /// Transaction type: 0 = legacy, 1 = EIP-2930, 2 = EIP-1559, 3 = EIP-4844, 4 = EIP-7702.
    /// </summary>
    public byte TxType { get; set; } = 0;

    /// <summary>
    /// EIP-7702 (type-4): authorization list. Empty for all other tx types.
    /// Each entry designates a signer EOA whose code will be set to a delegation pointer.
    /// </summary>
    public IReadOnlyList<Eip7702Authorization> AuthorizationList { get; set; } =
        Array.Empty<Eip7702Authorization>();

    /// <summary>
    /// EIP-4844 versioned blob hashes exposed to EVM execution by BLOBHASH.
    /// Empty for transactions without blobs.
    /// </summary>
    public IReadOnlyList<byte[]> BlobVersionedHashes { get; set; } =
        Array.Empty<byte[]>();

    /// <summary>
    /// EIP-4844 maximum fee per blob gas. Zero for non-blob transactions.
    /// </summary>
    public BigInteger MaxFeePerBlobGas { get; set; } = BigInteger.Zero;

    /// <summary>
    /// [AI-EDIT 2026-01-10] EIP-1559 max priority fee per gas (type-2/3 txs only).
    /// Zero for legacy and EIP-2930 transactions.
    /// </summary>
    public BigInteger MaxPriorityFeePerGas { get; set; } = BigInteger.Zero;

    /// <summary>
    /// [AI-EDIT 2026-01-10] EIP-1559 maximum fee per gas cap (type-2/3 txs).
    /// Zero for legacy transactions; equals GasPrice for type-0/1.
    /// </summary>
    public BigInteger MaxFeePerGas { get; set; } = BigInteger.Zero;
    
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

    /// <summary>Hash used for ECDSA sender recovery.</summary>
    public byte[] GetRecoveryHash() =>
        SigningHash.Length == 32 ? SigningHash : Hash;

    public static Transaction FromRaw(byte[] rawTx)
    {
        if (rawTx == null || rawTx.Length == 0) 
            throw new ArgumentException("Transaction data cannot be empty");

        byte firstByte = rawTx[0];
        if (firstByte > 0 && firstByte <= 0x7f)
        {
            // EIP-2718 typed envelopes.
            // Unsigned field counts (items before yParity,r,s):
            //   type 1: 8  [chainId,nonce,gasPrice,gas,to,value,data,accessList]
            //   type 2: 9  [chainId,nonce,maxPriority,maxFee,gas,to,value,data,accessList]
            //   type 3: 11 [+maxBlobFee,blobVersionedHashes]
            return firstByte switch
            {
                0x01 => DecodeTyped(rawTx, firstByte, unsignedFieldCount: 8,
                    nonceIndex: 1, maxFeeIndex: 2, priorityFeeIndex: -1, gasLimitIndex: 3,
                    toIndex: 4, valueIndex: 5, dataIndex: 6, accessListIndex: 7,
                    vIndex: 8, rIndex: 9, sIndex: 10),
                0x02 => DecodeTyped(rawTx, firstByte, unsignedFieldCount: 9,
                    nonceIndex: 1, maxFeeIndex: 3, priorityFeeIndex: 2, gasLimitIndex: 4,
                    toIndex: 5, valueIndex: 6, dataIndex: 7, accessListIndex: 8,
                    vIndex: 9, rIndex: 10, sIndex: 11),
                0x03 => DecodeTyped(rawTx, firstByte, unsignedFieldCount: 11,
                    nonceIndex: 1, maxFeeIndex: 3, priorityFeeIndex: 2, gasLimitIndex: 4,
                    toIndex: 5, valueIndex: 6, dataIndex: 7, accessListIndex: 8,
                    vIndex: 11, rIndex: 12, sIndex: 13),
                _ => throw new UnsupportedTransactionTypeException(firstByte)
            };
        }

        if (firstByte < 0x80)
            throw new Exception($"Invalid transaction prefix: 0x{firstByte:X2}");

        var rlp = Schlieren.Core.Encoding.RlpDecoder.Decode(rawTx);
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
            // EIP-155 signing hash: keccak(rlp([nonce,gasPrice,gas,to,value,data,chainId,0,0]))
            // when v encodes chainId (v >= 35). Pre-EIP-155 uses keccak of unsigned 6-field list.
            tx.SigningHash = ComputeLegacySigningHash(items, tx.V);
        }
        
        return tx;
    }

    private static Transaction DecodeTyped(
        byte[] rawTx,
        byte txType,
        int unsignedFieldCount,
        int nonceIndex,
        int maxFeeIndex,
        int priorityFeeIndex,
        int gasLimitIndex,
        int toIndex,
        int valueIndex,
        int dataIndex,
        int accessListIndex,
        int vIndex,
        int rIndex,
        int sIndex)
    {
        if (rawTx.Length < 2)
            throw new Exception($"Invalid typed transaction type 0x{txType:X2}: missing payload");

        var payload = new byte[rawTx.Length - 1];
        Array.Copy(rawTx, 1, payload, 0, payload.Length);

        var rlp = Schlieren.Core.Encoding.RlpDecoder.Decode(payload);
        if (!rlp.IsList) throw new Exception($"Typed transaction payload for type 0x{txType:X2} must be an RLP list");

        var items = rlp.Items;
        if (items.Count <= sIndex)
            throw new Exception($"Invalid typed transaction type 0x{txType:X2}: insufficient RLP items");

        var maxFee = items[maxFeeIndex].ToBigInteger();
        var priority = priorityFeeIndex >= 0 ? items[priorityFeeIndex].ToBigInteger() : BigInteger.Zero;

        // Signing hash: keccak256(0x{type} || rlp(unsigned fields)) — NOT keccak of the signed raw.
        var unsignedItems = items.GetRange(0, unsignedFieldCount);
        var unsignedRlp = Schlieren.Core.Encoding.RlpEncoder.EncodeList(unsignedItems);
        var signingPayload = new byte[1 + unsignedRlp.Length];
        signingPayload[0] = txType;
        Buffer.BlockCopy(unsignedRlp, 0, signingPayload, 1, unsignedRlp.Length);

        var accessList = ParseAccessList(items[accessListIndex]);
        var blobVersionedHashes = txType == 3
            ? ParseBlobVersionedHashes(items[10])
            : Array.Empty<byte[]>();
        var maxFeePerBlobGas = txType == 3
            ? items[9].ToBigInteger()
            : BigInteger.Zero;

        return new Transaction
        {
            TxType = txType,
            Nonce = (ulong)items[nonceIndex].ToBigInteger(),
            GasPrice = maxFee, // ranking / legacy fallback field
            MaxFeePerGas = maxFee,
            MaxPriorityFeePerGas = priority,
            GasLimit = (ulong)items[gasLimitIndex].ToBigInteger(),
            To = items[toIndex].Data.Length > 0 ? new Address(items[toIndex].Data.ToArray()) : null,
            Value = items[valueIndex].ToBigInteger(),
            Data = items[dataIndex].Data.ToArray(),
            AccessList = accessList,
            BlobVersionedHashes = blobVersionedHashes,
            MaxFeePerBlobGas = maxFeePerBlobGas,
            V = (int)items[vIndex].ToBigInteger(),
            R = items[rIndex].Data.ToArray(),
            S = items[sIndex].Data.ToArray(),
            Hash = CryptoUtils.Keccak256(rawTx),
            SigningHash = CryptoUtils.Keccak256(signingPayload),
            Authorization = TransactionAuthorization.Signed
        };
    }

    private static byte[][] ParseBlobVersionedHashes(
        Schlieren.Core.Encoding.RlpItem item)
    {
        if (!item.IsList)
        {
            return Array.Empty<byte[]>();
        }

        return item.Items
            .Select(hash => hash.Data.ToArray())
            .ToArray();
    }

    private static IReadOnlyList<AccessListEntry> ParseAccessList(Schlieren.Core.Encoding.RlpItem accessListItem)
    {
        if (!accessListItem.IsList || accessListItem.Items.Count == 0)
            return Array.Empty<AccessListEntry>();

        var result = new List<AccessListEntry>(accessListItem.Items.Count);
        foreach (var entry in accessListItem.Items)
        {
            if (!entry.IsList || entry.Items.Count < 1)
                continue;

            var addrItem = entry.Items[0];
            if (addrItem.Data.Length != 20)
                continue;

            var keys = new List<BigInteger>();
            if (entry.Items.Count > 1 && entry.Items[1].IsList)
            {
                foreach (var keyItem in entry.Items[1].Items)
                {
                    if (keyItem.Data.Length == 0) continue;
                    keys.Add(keyItem.ToBigInteger());
                }
            }

            result.Add(new AccessListEntry
            {
                Address = new Address(addrItem.Data.ToArray()),
                StorageKeys = keys
            });
        }

        return result;
    }

    private static byte[] ComputeLegacySigningHash(List<Schlieren.Core.Encoding.RlpItem> items, int v)
    {
        // items: [nonce, gasPrice, gas, to, value, data, v, r, s]
        if (v >= 35)
        {
            // EIP-155: chainId = (v - 35) / 2  (floor); append chainId, 0, 0
            var chainId = (v - 35) / 2;
            var unsigned = new List<Schlieren.Core.Encoding.RlpItem>(9);
            for (int i = 0; i < 6; i++) unsigned.Add(items[i]);
            unsigned.Add(EncodeUintRlp((ulong)chainId));
            unsigned.Add(new Schlieren.Core.Encoding.RlpItem { Data = ReadOnlyMemory<byte>.Empty });
            unsigned.Add(new Schlieren.Core.Encoding.RlpItem { Data = ReadOnlyMemory<byte>.Empty });
            return CryptoUtils.Keccak256(Schlieren.Core.Encoding.RlpEncoder.EncodeList(unsigned));
        }

        // Homestead: sign over first 6 fields only
        var six = items.GetRange(0, 6);
        return CryptoUtils.Keccak256(Schlieren.Core.Encoding.RlpEncoder.EncodeList(six));
    }

    private static Schlieren.Core.Encoding.RlpItem EncodeUintRlp(ulong value)
    {
        if (value == 0)
            return new Schlieren.Core.Encoding.RlpItem { Data = ReadOnlyMemory<byte>.Empty };

        // big-endian minimal
        Span<byte> buf = stackalloc byte[8];
        for (int i = 7; i >= 0; i--)
        {
            buf[i] = (byte)(value & 0xff);
            value >>= 8;
        }
        int start = 0;
        while (start < 7 && buf[start] == 0) start++;
        return new Schlieren.Core.Encoding.RlpItem { Data = buf.Slice(start).ToArray() };
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

/// <summary>
/// EIP-7702 authorization tuple: one entry per authority in the authorization list.
/// The fixture harness provides <see cref="Signer"/> directly (pre-recovered from signature).
/// </summary>
public sealed class Eip7702Authorization
{
    /// <summary>Chain ID that scopes this authorization (0 = any chain).</summary>
    public ulong ChainId { get; init; }

    /// <summary>Address of the contract that the signer delegates to.</summary>
    public Address DelegateAddress { get; init; }

    /// <summary>Expected nonce of the signer at the time of processing.</summary>
    public ulong Nonce { get; init; }

    /// <summary>Pre-recovered signer address (from fixture or ECDSA recovery).</summary>
    public Address Signer { get; init; }

    /// <summary>True when the authorization has been validated and should be applied.</summary>
    public bool IsValid { get; init; } = true;
}
