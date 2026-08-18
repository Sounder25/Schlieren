using System.Numerics;
using System.Text.Json;
using Schlieren.Core.Forks;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.EELS.Tests.Harness;

public sealed record EelsBlockchainReceipt(bool? Status, ulong? CumulativeGasUsed);

public sealed record EelsWithdrawal(Address Address, ulong AmountGwei);

public sealed record EelsBlockchainBlock(
    BlockContext Context,
    IReadOnlyList<Transaction> Transactions,
    IReadOnlyList<EelsBlockchainReceipt> Receipts,
    IReadOnlyList<EelsWithdrawal> Withdrawals,
    string? ExpectException);

public sealed record EelsBlockchainCase(
    string FixturePath,
    string CaseId,
    string ForkName,
    IReadOnlyDictionary<Address, EelsFixtureAccount> PreState,
    IReadOnlyDictionary<Address, EelsFixtureAccount> ExpectedPostState,
    IReadOnlyList<EelsBlockchainBlock> Blocks);

/// <summary>
/// Loads EEST <c>blockchain_test</c> JSON (pre + blocks + postState).
/// Invalid blocks (<c>expectException</c>) are kept so the executor can skip applying them.
/// </summary>
public sealed class EelsBlockchainFixtureLoader
{
    public IReadOnlyList<EelsBlockchainCase> LoadCases(EelsHarnessOptions options)
    {
        if (!Directory.Exists(options.FixturesRoot))
            throw new DirectoryNotFoundException($"blockchain fixtures root not found: {options.FixturesRoot}");

        var search = options.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(options.FixturesRoot, "*.json", search)
            .Where(path => string.IsNullOrWhiteSpace(options.ExcludeFolder) ||
                           !path.Replace('\\', '/').Contains($"/{options.ExcludeFolder}/",
                               StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var cases = new List<EelsBlockchainCase>();
        foreach (var file in files)
        {
            LoadFile(file, options, cases);
            if (cases.Count >= options.MaxCases)
                break;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var deduped = new List<EelsBlockchainCase>(cases.Count);
        foreach (var c in cases)
        {
            if (seen.Add(c.CaseId))
                deduped.Add(c);
            if (deduped.Count >= options.MaxCases)
                break;
        }

        return deduped;
    }

    private static void LoadFile(string filePath, EelsHarnessOptions options, List<EelsBlockchainCase> output)
    {
        using var stream = File.OpenRead(filePath);
        using var doc = JsonDocument.Parse(stream);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return;

        foreach (var fixtureCase in doc.RootElement.EnumerateObject())
        {
            if (output.Count >= options.MaxCases)
                return;

            var parsed = TryBuild(filePath, fixtureCase.Name, fixtureCase.Value, options.ForkName);
            if (parsed != null)
                output.Add(parsed);
        }
    }

    private static EelsBlockchainCase? TryBuild(
        string fixturePath,
        string caseId,
        JsonElement node,
        string forkName)
    {
        if (!LooksLikeBlockchainTest(node))
            return null;

        var network = node.TryGetProperty("network", out var netNode)
            ? EelsStateFixtureLoader.GetJsonText(netNode)
            : "";
        if (!string.IsNullOrEmpty(network) &&
            !string.Equals(network, forkName, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!node.TryGetProperty("pre", out var preNode) ||
            !node.TryGetProperty("postState", out var postNode) ||
            !node.TryGetProperty("blocks", out var blocksNode) ||
            blocksNode.ValueKind != JsonValueKind.Array)
            return null;

        var pre = EelsStateFixtureLoader.ParseAccountMap(preNode, null, allowPartial: false);
        var post = EelsStateFixtureLoader.ParseAccountMap(postNode, pre, allowPartial: true);
        var chainId = ParseChainId(node);
        var rules = ForkRulesFactory.For(forkName);

        var blocks = new List<EelsBlockchainBlock>();
        foreach (var blockNode in blocksNode.EnumerateArray())
        {
            var parsed = ParseBlock(blockNode, chainId, rules);
            if (parsed != null)
                blocks.Add(parsed);
        }

        return new EelsBlockchainCase(fixturePath, caseId, forkName, pre, post, blocks);
    }

    private static bool LooksLikeBlockchainTest(JsonElement node)
    {
        if (node.TryGetProperty("_info", out var info))
        {
            if (info.TryGetProperty("fixture-format", out var fmt) ||
                info.TryGetProperty("fixture_format", out fmt))
            {
                var text = EelsStateFixtureLoader.GetJsonText(fmt);
                if (text.StartsWith("blockchain_test", StringComparison.OrdinalIgnoreCase) &&
                    !text.Contains("engine", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return node.TryGetProperty("blocks", out _) &&
               node.TryGetProperty("postState", out _) &&
               node.TryGetProperty("pre", out _);
    }

    private static EelsBlockchainBlock? ParseBlock(JsonElement blockNode, ulong chainId, IForkRules rules)
    {
        string? expectException = null;
        if (blockNode.TryGetProperty("expectException", out var excNode))
            expectException = EelsStateFixtureLoader.GetJsonText(excNode);

        JsonElement header;
        JsonElement txsNode = default;
        JsonElement receiptsNode = default;
        var haveTxs = false;
        var haveReceipts = false;

        JsonElement withdrawalsNode = default;
        var haveWithdrawals = false;

        if (blockNode.TryGetProperty("blockHeader", out header))
        {
            haveTxs = blockNode.TryGetProperty("transactions", out txsNode);
            haveReceipts = blockNode.TryGetProperty("receipts", out receiptsNode);
            haveWithdrawals = blockNode.TryGetProperty("withdrawals", out withdrawalsNode);
        }
        else if (blockNode.TryGetProperty("rlp_decoded", out var decoded) &&
                 decoded.ValueKind == JsonValueKind.Object &&
                 decoded.TryGetProperty("blockHeader", out header))
        {
            haveTxs = decoded.TryGetProperty("transactions", out txsNode);
            haveReceipts = decoded.TryGetProperty("receipts", out receiptsNode);
            haveWithdrawals = decoded.TryGetProperty("withdrawals", out withdrawalsNode);
        }
        else
        {
            return null;
        }

        var txs = new List<Transaction>();
        if (haveTxs && txsNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var txNode in txsNode.EnumerateArray())
            {
                if (txNode.ValueKind == JsonValueKind.Object)
                    txs.Add(ParseTransaction(txNode));
            }
        }

        var receipts = new List<EelsBlockchainReceipt>();
        if (haveReceipts && receiptsNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var rec in receiptsNode.EnumerateArray())
            {
                bool? status = null;
                ulong? gas = null;
                if (rec.TryGetProperty("status", out var st))
                {
                    if (st.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        status = st.GetBoolean();
                    else
                        status = EelsHex.ParseUlong(EelsStateFixtureLoader.GetJsonText(st)) != 0;
                }

                if (rec.TryGetProperty("cumulativeGasUsed", out var g))
                    gas = EelsHex.ParseUlong(EelsStateFixtureLoader.GetJsonText(g));
                receipts.Add(new EelsBlockchainReceipt(status, gas));
            }
        }

        var parentHash = HeaderBytes(header, "parentHash");
        var beacon = HeaderBytes(header, "parentBeaconBlockRoot");
        var number = HeaderUlong(header, "number", "blockNumber");
        var coinbase = HeaderAddress(header, "coinbase", "feeRecipient");
        var mixHash = HeaderBytes(header, "mixHash", "prevRandao");

        var ctx = new BlockContext
        {
            ChainId = chainId,
            Number = number,
            Timestamp = HeaderUlong(header, "timestamp"),
            GasLimit = HeaderUlong(header, "gasLimit"),
            Coinbase = coinbase,
            Difficulty = mixHash.Length > 0
                ? new BigInteger(mixHash, isUnsigned: true, isBigEndian: true)
                : HeaderQuantity(header, "difficulty"),
            BaseFeePerGas = HeaderUlong(header, "baseFeePerGas"),
            Hash = parentHash,
            ExcessBlobGas = HeaderUlong(header, "excessBlobGas"),
            ParentBeaconBlockRoot = beacon,
            Rules = rules,
        };

        var withdrawals = new List<EelsWithdrawal>();
        if (haveWithdrawals && withdrawalsNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var w in withdrawalsNode.EnumerateArray())
            {
                if (w.ValueKind != JsonValueKind.Object)
                    continue;
                if (!w.TryGetProperty("address", out var addrNode))
                    continue;
                var amount = w.TryGetProperty("amount", out var amtNode)
                    ? EelsHex.ParseUlong(EelsStateFixtureLoader.GetJsonText(amtNode))
                    : 0;
                withdrawals.Add(new EelsWithdrawal(
                    Address.FromHex(EelsStateFixtureLoader.GetJsonText(addrNode)),
                    amount));
            }
        }

        return new EelsBlockchainBlock(ctx, txs, receipts, withdrawals, expectException);
    }

    private static Transaction ParseTransaction(JsonElement txNode)
    {
        var accessList = txNode.TryGetProperty("accessList", out var al) && al.ValueKind == JsonValueKind.Array
            ? EelsStateFixtureLoader.ParseFlatAccessList(al)
            : Array.Empty<AccessListEntry>();

        var txType = (byte)0;
        if (txNode.TryGetProperty("type", out var typeNode))
        {
            try { txType = (byte)EelsHex.ParseUlong(EelsStateFixtureLoader.GetJsonText(typeNode)); }
            catch { txType = EelsStateFixtureLoader.DetectTxType(txNode, accessList); }
        }
        else
        {
            txType = EelsStateFixtureLoader.DetectTxType(txNode, accessList);
        }

        var sender = txNode.TryGetProperty("sender", out var senderNode)
            ? Address.FromHex(EelsStateFixtureLoader.GetJsonText(senderNode))
            : Address.Zero;

        Address? to = null;
        if (txNode.TryGetProperty("to", out var toNode))
        {
            var raw = EelsStateFixtureLoader.GetJsonText(toNode);
            if (!string.IsNullOrWhiteSpace(raw))
                to = Address.FromHex(raw);
        }

        BigInteger gasPrice = BigInteger.Zero;
        if (txNode.TryGetProperty("gasPrice", out var gp))
            gasPrice = EelsHex.ParseQuantity(EelsStateFixtureLoader.GetJsonText(gp));
        else if (txNode.TryGetProperty("maxFeePerGas", out var mf))
            gasPrice = EelsHex.ParseQuantity(EelsStateFixtureLoader.GetJsonText(mf));

        BigInteger priority = BigInteger.Zero;
        if (txNode.TryGetProperty("maxPriorityFeePerGas", out var pf))
            priority = EelsHex.ParseQuantity(EelsStateFixtureLoader.GetJsonText(pf));

        BigInteger blobFee = BigInteger.Zero;
        if (txNode.TryGetProperty("maxFeePerBlobGas", out var bf))
            blobFee = EelsHex.ParseQuantity(EelsStateFixtureLoader.GetJsonText(bf));

        var blobs = Array.Empty<byte[]>();
        if (txNode.TryGetProperty("blobVersionedHashes", out var hashes) && hashes.ValueKind == JsonValueKind.Array)
            blobs = hashes.EnumerateArray().Select(h => EelsHex.ParseBytes(EelsStateFixtureLoader.GetJsonText(h))).ToArray();

        return new Transaction
        {
            From = sender,
            To = to,
            Nonce = txNode.TryGetProperty("nonce", out var n)
                ? EelsHex.ParseUlong(EelsStateFixtureLoader.GetJsonText(n)) : 0,
            GasPrice = gasPrice,
            MaxFeePerGas = gasPrice,
            MaxPriorityFeePerGas = priority,
            TxType = txType,
            GasLimit = txNode.TryGetProperty("gasLimit", out var gl)
                ? EelsHex.ParseUlong(EelsStateFixtureLoader.GetJsonText(gl)) : 0,
            Value = txNode.TryGetProperty("value", out var v)
                ? EelsHex.ParseQuantity(EelsStateFixtureLoader.GetJsonText(v)) : BigInteger.Zero,
            Data = txNode.TryGetProperty("data", out var d)
                ? EelsHex.ParseBytes(EelsStateFixtureLoader.GetJsonText(d)) : Array.Empty<byte>(),
            AccessList = accessList,
            BlobVersionedHashes = blobs,
            MaxFeePerBlobGas = blobFee,
            AuthorizationList = EelsStateFixtureLoader.ParseAuthorizationList(txNode),
            Authorization = TransactionAuthorization.Impersonated,
        };
    }

    private static ulong ParseChainId(JsonElement caseNode)
    {
        if (caseNode.TryGetProperty("config", out var config) &&
            config.TryGetProperty("chainid", out var id))
            return EelsHex.ParseUlong(EelsStateFixtureLoader.GetJsonText(id));
        return 1;
    }

    private static ulong HeaderUlong(JsonElement header, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (header.TryGetProperty(key, out var n))
                return EelsHex.ParseUlong(EelsStateFixtureLoader.GetJsonText(n));
        }

        return 0;
    }

    private static Address HeaderAddress(JsonElement header, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (header.TryGetProperty(key, out var n))
            {
                var text = EelsStateFixtureLoader.GetJsonText(n);
                if (!string.IsNullOrWhiteSpace(text))
                    return Address.FromHex(text);
            }
        }

        return Address.Zero;
    }

    private static byte[] HeaderBytes(JsonElement header, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (header.TryGetProperty(key, out var n))
            {
                var text = EelsStateFixtureLoader.GetJsonText(n);
                if (!string.IsNullOrWhiteSpace(text) && text != "0x")
                    return EelsHex.ParseBytes(text);
            }
        }

        return Array.Empty<byte>();
    }

    private static BigInteger HeaderQuantity(JsonElement header, string key)
    {
        if (!header.TryGetProperty(key, out var n))
            return BigInteger.Zero;
        var text = EelsStateFixtureLoader.GetJsonText(n);
        return string.IsNullOrWhiteSpace(text) ? BigInteger.Zero : EelsHex.ParseQuantity(text);
    }
}
