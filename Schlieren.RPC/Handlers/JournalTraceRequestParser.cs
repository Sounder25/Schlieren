using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using Schlieren.RPC.Models;

namespace Schlieren.RPC.Handlers;

internal sealed record JournalPreStateAccount(
    Address Address,
    ulong Nonce,
    BigInteger Balance,
    byte[] Code,
    IReadOnlyList<(BigInteger Key, BigInteger Value)> Storage);

internal sealed record JournalBlockOverrides(
    ulong? Number,
    ulong? Timestamp,
    Address? Coinbase,
    ulong? GasLimit,
    ulong? BaseFee,
    ulong? ChainId,
    BigInteger? PrevRandao,
    ulong? ExcessBlobGas);

internal sealed record JournalTraceRequest(
    Address From,
    Address? To,
    ulong Gas,
    BigInteger? GasPrice,
    BigInteger Value,
    byte[] Data,
    byte[]? Code,
    string Fork,
    ulong? Nonce,
    bool DisableStack,
    bool DisableMemory,
    bool DisableStorage,
    byte TxType,
    BigInteger? MaxFeePerGas,
    BigInteger? MaxPriorityFeePerGas,
    BigInteger? MaxFeePerBlobGas,
    IReadOnlyList<AccessListEntry> AccessList,
    IReadOnlyList<Eip7702Authorization> AuthorizationList,
    IReadOnlyList<byte[]> BlobVersionedHashes,
    IReadOnlyList<JournalPreStateAccount> PreState,
    JournalBlockOverrides? Block);

/// <summary>
/// Parses a normalized execution-context object for <c>schlieren_traceJournal</c>.
/// Does not parse EELS/state-test fixtures, recover 7702 signatures, or mutate chain state.
/// <para>
/// Flat vs nested <c>to</c> (do not "simplify"):
/// <list type="bullet">
/// <item>Flat legacy root: <c>to</c> is required. Missing <c>to</c> is invalid params.</item>
/// <item>Normalized <c>transaction</c> object present: omitted or JSON <c>null</c> <c>to</c> means CREATE.</item>
/// </list>
/// Nested mode is the presence of the <c>transaction</c> property, not a value-type identity check.
/// </para>
/// Optional quantity fields retain presence: omitted is <c>null</c>, explicit <c>0x0</c> is zero.
/// </summary>
internal static class JournalTraceRequestParser
{
    internal static readonly BigInteger Uint256Max = BigInteger.Pow(2, 256) - 1;

    public static JournalTraceRequest Parse(object[] parameters, ulong defaultGas)
    {
        if (parameters is null || parameters.Length != 1 ||
            parameters[0] is not JsonElement element ||
            element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("Expected exactly one journal trace request object");
        }

        var transaction = TryObject(element, "transaction");
        var nested = transaction.HasValue;
        var txSource = transaction ?? element;
        var options = TryObject(element, "options") ?? element;

        var code = ReadBytes(element, "code", optional: true)
                   ?? ReadBytes(txSource, "code", optional: true);
        var to = ReadTo(txSource, nested, code);

        var fromText = ReadString(txSource, "from", optional: true)
                       ?? ReadString(element, "from", optional: true)
                       ?? Address.Zero.ToString();
        var gas = ReadUlongQuantity(txSource, "gasLimit")
                  ?? ReadUlongQuantity(txSource, "gas")
                  ?? ReadUlongQuantity(element, "gas")
                  ?? defaultGas;
        var gasPrice = ReadUint256Quantity(txSource, "gasPrice")
                       ?? ReadUint256Quantity(element, "gasPrice");
        var maxFee = ReadUint256Quantity(txSource, "maxFeePerGas");
        var maxPriority = ReadUint256Quantity(txSource, "maxPriorityFeePerGas");
        var maxBlobFee = ReadUint256Quantity(txSource, "maxFeePerBlobGas");
        var value = ReadUint256Quantity(txSource, "value")
                    ?? ReadUint256Quantity(element, "value")
                    ?? BigInteger.Zero;
        var data = ReadBytes(txSource, "data", optional: true)
                   ?? ReadBytes(element, "data", optional: true)
                   ?? Array.Empty<byte>();
        var nonce = ReadUlongQuantity(txSource, "nonce") ?? ReadUlongQuantity(element, "nonce");
        var fork = ReadString(element, "fork", optional: true)
                   ?? ReadString(txSource, "fork", optional: true)
                   ?? "Osaka";
        var accessList = ReadAccessList(txSource);
        var auths = ReadAuthorizations(txSource);
        var blobs = ReadBlobHashes(txSource);
        var txType = ReadTxType(txSource, accessList.Count, auths.Count, blobs.Count);
        var preState = ReadPreState(element);
        var block = ReadBlock(element);

        return new JournalTraceRequest(
            ParseAddress(fromText, "from"),
            to,
            gas,
            gasPrice,
            value,
            data,
            code,
            fork,
            nonce,
            ReadBoolean(options, "disableStack") || ReadBoolean(element, "disableStack"),
            ReadBoolean(options, "disableMemory") || ReadBoolean(element, "disableMemory"),
            ReadBoolean(options, "disableStorage") || ReadBoolean(element, "disableStorage"),
            txType,
            maxFee,
            maxPriority,
            maxBlobFee,
            accessList,
            auths,
            blobs,
            preState,
            block);
    }

    /// <summary>
    /// Applies type-aware fee defaults. Absence is not treated as zero:
    /// type 2+ with omitted maxFeePerGas inherits gasPrice; explicit 0x0 stays 0.
    /// </summary>
    public static (BigInteger GasPrice, BigInteger MaxFee, BigInteger MaxPriority, BigInteger MaxBlobFee)
        ResolveFees(JournalTraceRequest request)
    {
        var gasPrice = request.GasPrice ?? BigInteger.Zero;
        var maxFee = request.MaxFeePerGas ?? (request.TxType >= 2 ? gasPrice : BigInteger.Zero);
        var maxPriority = request.MaxPriorityFeePerGas ?? BigInteger.Zero;
        var maxBlob = request.MaxFeePerBlobGas ?? BigInteger.Zero;
        return (gasPrice, maxFee, maxPriority, maxBlob);
    }

    /// <summary>
    /// Flat requests require <c>to</c>. Nested <c>transaction</c> treats missing/null <c>to</c> as CREATE.
    /// </summary>
    private static Address? ReadTo(JsonElement txSource, bool nested, byte[]? code)
    {
        if (!txSource.TryGetProperty("to", out var property) || property.ValueKind == JsonValueKind.Null)
        {
            if (nested)
                return null;
            if (code is not null)
                throw Invalid("'to' is required when 'code' is present");
            throw Invalid("Missing 'to' address");
        }

        if (property.ValueKind != JsonValueKind.String)
            throw Invalid("'to' must be a hex address string");

        var toText = property.GetString();
        if (string.IsNullOrWhiteSpace(toText) || toText is "0x" or "0x0")
        {
            if (nested)
                return null;
            throw Invalid("Invalid 'to' address");
        }

        return ParseAddress(toText, "to");
    }

    private static byte ReadTxType(JsonElement tx, int accessCount, int authCount, int blobCount)
    {
        if (HasProperty(tx, "type") || HasProperty(tx, "txType"))
        {
            var explicitType = ReadUlongQuantity(tx, "type") ?? ReadUlongQuantity(tx, "txType") ?? 0;
            if (explicitType > 4)
                throw Invalid("Invalid transaction type");
            return (byte)explicitType;
        }

        if (authCount > 0) return 4;
        if (blobCount > 0) return 3;
        if (HasProperty(tx, "maxFeePerGas") || HasProperty(tx, "maxPriorityFeePerGas")) return 2;
        if (accessCount > 0) return 1;
        return 0;
    }

    private static IReadOnlyList<JournalPreStateAccount> ReadPreState(JsonElement root)
    {
        if (!root.TryGetProperty("preState", out var arr) || arr.ValueKind == JsonValueKind.Null)
            return Array.Empty<JournalPreStateAccount>();
        if (arr.ValueKind != JsonValueKind.Array)
            throw Invalid("'preState' must be an array of account objects");

        var accounts = new List<JournalPreStateAccount>();
        var i = 0;
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                throw Invalid($"preState[{i}] must be an object");
            var address = ReadString(el, "address", optional: false)!;
            var storage = new List<(BigInteger, BigInteger)>();
            if (el.TryGetProperty("storage", out var st) && st.ValueKind != JsonValueKind.Null)
            {
                if (st.ValueKind != JsonValueKind.Object)
                    throw Invalid($"preState[{i}].storage must be an object of quantity pairs");
                foreach (var slot in st.EnumerateObject())
                {
                    if (slot.Value.ValueKind != JsonValueKind.String)
                        throw Invalid($"preState[{i}].storage value must be a quantity string");
                    storage.Add((
                        ParseUint256(slot.Name, "storage key"),
                        ParseUint256(slot.Value.GetString() ?? "0x0", "storage value")));
                }
            }

            accounts.Add(new JournalPreStateAccount(
                ParseAddress(address, "preState.address"),
                ReadUlongQuantity(el, "nonce") ?? 0,
                ReadUint256Quantity(el, "balance") ?? BigInteger.Zero,
                ReadBytes(el, "code", optional: true) ?? Array.Empty<byte>(),
                storage));
            i++;
        }

        return accounts;
    }

    private static JournalBlockOverrides? ReadBlock(JsonElement root)
    {
        var block = TryObject(root, "blockContext");
        if (block is null)
            return null;

        var coinbaseText = ReadString(block.Value, "coinbase", optional: true);
        return new JournalBlockOverrides(
            ReadUlongQuantity(block.Value, "number"),
            ReadUlongQuantity(block.Value, "timestamp"),
            string.IsNullOrWhiteSpace(coinbaseText) ? null : ParseAddress(coinbaseText, "blockContext.coinbase"),
            ReadUlongQuantity(block.Value, "gasLimit"),
            ReadUlongQuantity(block.Value, "baseFee") ?? ReadUlongQuantity(block.Value, "baseFeePerGas"),
            ReadUlongQuantity(block.Value, "chainId"),
            ReadUint256Quantity(block.Value, "prevRandao") ?? ReadUint256Quantity(block.Value, "difficulty"),
            ReadUlongQuantity(block.Value, "excessBlobGas"));
    }

    private static IReadOnlyList<AccessListEntry> ReadAccessList(JsonElement tx)
    {
        if (!tx.TryGetProperty("accessList", out var arr) || arr.ValueKind == JsonValueKind.Null)
            return Array.Empty<AccessListEntry>();
        if (arr.ValueKind != JsonValueKind.Array)
            throw Invalid("'accessList' must be an array");

        var list = new List<AccessListEntry>();
        foreach (var entry in arr.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                throw Invalid("accessList entry must be an object");
            var addr = ReadString(entry, "address", optional: false)!;
            var keys = new List<BigInteger>();
            if (entry.TryGetProperty("storageKeys", out var sk) && sk.ValueKind != JsonValueKind.Null)
            {
                if (sk.ValueKind != JsonValueKind.Array)
                    throw Invalid("accessList.storageKeys must be an array");
                foreach (var key in sk.EnumerateArray())
                {
                    if (key.ValueKind != JsonValueKind.String)
                        throw Invalid("accessList.storageKeys entries must be quantity strings");
                    keys.Add(ParseUint256(key.GetString() ?? "0x0", "storageKey"));
                }
            }

            list.Add(new AccessListEntry
            {
                Address = ParseAddress(addr, "accessList.address"),
                StorageKeys = keys
            });
        }

        return list;
    }

    /// <summary>
    /// Normalized EIP-7702 authorizations. This parser never recovers (yParity, r, s).
    /// <c>signer</c> is the already-recovered authority. <c>address</c>/<c>delegate</c> is
    /// the delegation target. <c>valid</c> is caller-supplied; if omitted it is true only
    /// when signer is a non-zero address.
    /// </summary>
    private static IReadOnlyList<Eip7702Authorization> ReadAuthorizations(JsonElement tx)
    {
        if (!tx.TryGetProperty("authorizationList", out var arr) || arr.ValueKind == JsonValueKind.Null)
            return Array.Empty<Eip7702Authorization>();
        if (arr.ValueKind != JsonValueKind.Array)
            throw Invalid("'authorizationList' must be an array");

        var list = new List<Eip7702Authorization>();
        var i = 0;
        foreach (var entry in arr.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                throw Invalid("authorizationList entry must be an object");

            var hasRawSignature = HasProperty(entry, "yParity") || HasProperty(entry, "r") || HasProperty(entry, "s");
            var signerText = ReadString(entry, "signer", optional: true);
            if (hasRawSignature && string.IsNullOrWhiteSpace(signerText))
            {
                throw Invalid(
                    "authorizationList requires a recovered 'signer'; schlieren_traceJournal does not decode 7702 signatures");
            }

            var delegateText = ReadString(entry, "address", optional: true)
                               ?? ReadString(entry, "delegate", optional: true);
            var signer = string.IsNullOrWhiteSpace(signerText)
                ? default
                : ParseAddress(signerText, "authorizationList.signer");
            var hasValidFlag = HasProperty(entry, "valid");
            var valid = hasValidFlag
                ? ReadBoolean(entry, "valid")
                : signer.Bytes is { Length: 20 } && signer != Address.Zero;

            if (valid && (signer.Bytes is not { Length: 20 } || signer == Address.Zero))
                throw Invalid($"authorizationList[{i}] is valid but missing a non-zero signer");

            if (valid && string.IsNullOrWhiteSpace(delegateText))
                throw Invalid($"authorizationList[{i}] is valid but missing delegate address");

            list.Add(new Eip7702Authorization
            {
                ChainId = ReadUlongQuantity(entry, "chainId") ?? 0,
                Nonce = ReadUlongQuantity(entry, "nonce") ?? 0,
                DelegateAddress = string.IsNullOrWhiteSpace(delegateText)
                    ? default
                    : ParseAddress(delegateText, "authorizationList.address"),
                Signer = signer,
                IsValid = valid
            });
            i++;
        }

        return list;
    }

    private static IReadOnlyList<byte[]> ReadBlobHashes(JsonElement tx)
    {
        if (!tx.TryGetProperty("blobVersionedHashes", out var arr) || arr.ValueKind == JsonValueKind.Null)
            return Array.Empty<byte[]>();
        if (arr.ValueKind != JsonValueKind.Array)
            throw Invalid("'blobVersionedHashes' must be an array");
        var hashes = new List<byte[]>();
        var i = 0;
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.String)
                throw Invalid($"blobVersionedHashes[{i}] must be a 32-byte hex string");
            var bytes = ParseBytes(el.GetString() ?? "", "blobVersionedHashes");
            if (bytes.Length != 32)
                throw Invalid($"blobVersionedHashes[{i}] must be 32 bytes");
            hashes.Add(bytes);
            i++;
        }

        return hashes;
    }

    private static JsonElement? TryObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;
        if (property.ValueKind != JsonValueKind.Object)
            throw Invalid($"'{name}' must be an object");
        return property;
    }

    private static bool HasProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null;

    private static Address ParseAddress(string value, string name)
    {
        try
        {
            if (!EthereumTypes.IsValidAddress(value))
                throw new FormatException();
            return Address.FromHex(value);
        }
        catch
        {
            throw Invalid($"Invalid '{name}' address");
        }
    }

    private static string? ReadString(JsonElement element, string name, bool optional)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
            return optional ? null : throw Invalid($"Missing '{name}'");
        if (property.ValueKind == JsonValueKind.Array)
            throw Invalid($"'{name}' must be a hex string, not an array");
        if (property.ValueKind == JsonValueKind.Number)
            throw Invalid($"'{name}' must be a string, not a JSON number");
        if (property.ValueKind != JsonValueKind.String)
            throw Invalid($"'{name}' must be a string");
        return property.GetString();
    }

    private static byte[]? ReadBytes(JsonElement element, string name, bool optional)
    {
        var value = ReadString(element, name, optional);
        return value is null ? null : ParseBytes(value, name);
    }

    private static byte[] ParseBytes(string value, string name)
    {
        var clean = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        if (clean.Length % 2 != 0)
            throw Invalid($"Invalid '{name}' hex");
        try
        {
            return clean.Length == 0 ? Array.Empty<byte>() : Convert.FromHexString(clean);
        }
        catch
        {
            throw Invalid($"Invalid '{name}' hex");
        }
    }

    private static ulong? ReadUlongQuantity(JsonElement element, string name)
    {
        var value = ReadQuantityString(element, name);
        if (value is null)
            return null;
        var big = ParseUint256(value, name);
        if (big > ulong.MaxValue)
            throw Invalid($"'{name}' exceeds uint64 max ({ulong.MaxValue})");
        return (ulong)big;
    }

    /// <summary>
    /// Shared uint256 path for value, gasPrice, fee fields, balances, and storage.
    /// Absence is null; explicit 0x0 is zero; negatives and values &gt; 2^256-1 are invalid.
    /// </summary>
    private static BigInteger? ReadUint256Quantity(JsonElement element, string name)
    {
        var value = ReadQuantityString(element, name);
        return value is null ? null : ParseUint256(value, name);
    }

    private static string? ReadQuantityString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;
        if (property.ValueKind == JsonValueKind.Number)
            throw Invalid($"'{name}' must be a quantity string, not a JSON number");
        if (property.ValueKind == JsonValueKind.Array)
            throw Invalid($"'{name}' must be a quantity string, not an array");
        if (property.ValueKind != JsonValueKind.String)
            throw Invalid($"'{name}' must be a quantity string");
        return property.GetString();
    }

    internal static BigInteger ParseUint256(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw Invalid($"Invalid '{name}' quantity");
        if (value.StartsWith("-", StringComparison.Ordinal))
            throw Invalid($"'{name}' must be an unsigned quantity");

        BigInteger parsed;
        try
        {
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                parsed = EthereumTypes.FromEthHexBigInteger(value);
            }
            else if (value.All(char.IsAsciiDigit))
            {
                parsed = BigInteger.Parse(value, CultureInfo.InvariantCulture);
            }
            else
            {
                throw Invalid($"Invalid '{name}' quantity");
            }
        }
        catch (RpcException)
        {
            throw;
        }
        catch
        {
            throw Invalid($"Invalid '{name}' quantity");
        }

        if (parsed < BigInteger.Zero)
            throw Invalid($"'{name}' must be an unsigned quantity");
        if (parsed > Uint256Max)
            throw Invalid($"'{name}' exceeds uint256 max");
        return parsed;
    }

    private static bool ReadBoolean(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
            return false;
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Invalid($"'{name}' must be a boolean")
        };
    }

    private static RpcException Invalid(string message) =>
        new(JsonRpcErrorCodes.InvalidParams, message);
}
