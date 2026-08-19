using System.Numerics;
using System.Text.Json;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.UI.Services;

/// <summary>
/// Official EELS <c>state_test</c> JSON → workbench fields (pre, tx, fork, expected post).
/// Impersonated sender — no secret-key signing required to run.
/// </summary>
public static class WorkbenchFixtureLoader
{
    public sealed class ExpectedAccount
    {
        public string AddressHex { get; init; } = "";
        public string BalanceWei { get; init; } = "0";
        public ulong Nonce { get; init; }
        public string CodeHex { get; init; } = "";
        public Dictionary<string, string> StorageHex { get; init; } = new();
    }

    public sealed class LoadedFixture
    {
        public string CaseName { get; set; } = "";
        public string Fork { get; set; } = "Osaka";
        public string SenderHex { get; set; } = "";
        public string? ToHex { get; set; }
        public string CallDataHex { get; set; } = "0x";
        public string ValueWei { get; set; } = "0";
        public ulong GasLimit { get; set; }
        public ulong Nonce { get; set; }
        public ulong ChainId { get; set; } = 1;
        public string CoinbaseHex { get; set; } = "0x0000000000000000000000000000000000000000";
        public string BaseFeeWei { get; set; } = "0";
        public string GasPriceWei { get; set; } = "0";
        public string MaxFeeWei { get; set; } = "0";
        public string MaxPriorityWei { get; set; } = "0";
        public byte TxType { get; set; }
        public List<WorkbenchAccountSeed> PreAccounts { get; } = new();
        public List<ExpectedAccount> ExpectedPost { get; } = new();
        public List<AccessListEntry> AccessList { get; } = new();
        public List<Eip7702Authorization> Authorizations { get; } = new();
        public bool ExpectSuccess { get; set; } = true;
        public string? ExpectException { get; set; }
    }

    public sealed record ParseResult(LoadedFixture? Fixture, string Error)
    {
        public bool Ok => Fixture != null && string.IsNullOrEmpty(Error);
    }

    public static bool LooksLikeStateTest(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.TrimStart();
        if (t.Length == 0 || t[0] != '{') return false;
        return t.Contains("\"pre\"", StringComparison.Ordinal)
               && t.Contains("\"transaction\"", StringComparison.Ordinal)
               && (t.Contains("\"post\"", StringComparison.Ordinal)
                   || t.Contains("\"expect\"", StringComparison.Ordinal));
    }

    public static ParseResult Parse(string? json, string preferredFork, string? caseId = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new(null, "Fixture JSON is empty.");

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new(null, "Expected a JSON object.");

            if (IsCaseNode(root))
                return ParseCase("fixture", root, preferredFork);

            JsonProperty? fallback = null;
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object || !IsCaseNode(prop.Value))
                    continue;
                if (!string.IsNullOrWhiteSpace(caseId) &&
                    (prop.Name.Equals(caseId, StringComparison.OrdinalIgnoreCase) ||
                     prop.Name.Contains(caseId, StringComparison.OrdinalIgnoreCase)))
                    return ParseCase(prop.Name, prop.Value, preferredFork);
                fallback ??= prop;
            }

            if (fallback is { } hit)
                return ParseCase(hit.Name, hit.Value, preferredFork);

            return new(null, "No state_test case found (need env + pre + transaction + post).");
        }
        catch (JsonException ex)
        {
            return new(null, $"Invalid JSON: {ex.Message}");
        }
    }

    private static bool IsCaseNode(JsonElement el) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty("pre", out _)
        && el.TryGetProperty("transaction", out _)
        && (el.TryGetProperty("post", out _) || el.TryGetProperty("expect", out _));

    private static ParseResult ParseCase(string name, JsonElement caseNode, string preferredFork)
    {
        if (!caseNode.TryGetProperty("pre", out var preNode) ||
            !caseNode.TryGetProperty("transaction", out var txNode))
            return new(null, "Case missing pre or transaction.");

        var fork = preferredFork;
        JsonElement postState = default;
        var havePost = false;
        var expectSuccess = true;
        string? expectExc = null;

        if (caseNode.TryGetProperty("post", out var postNode) && postNode.ValueKind == JsonValueKind.Object)
        {
            if (!TryForkPost(postNode, preferredFork, out fork, out var variant))
                return new(null, "No post[] variant for any known fork.");
            if (variant.TryGetProperty("state", out postState))
                havePost = postState.ValueKind == JsonValueKind.Object;
            if (variant.TryGetProperty("expectException", out var exc) &&
                exc.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(exc.GetString()))
            {
                expectExc = exc.GetString();
                expectSuccess = false;
            }
            var dataIndex = IndexOf(variant, "data");
            var gasIndex = IndexOf(variant, "gas");
            var valueIndex = IndexOf(variant, "value");
            return Finish(name, caseNode, preNode, txNode, fork, havePost ? postState : default, havePost,
                expectSuccess, expectExc, dataIndex, gasIndex, valueIndex);
        }

        return Finish(name, caseNode, preNode, txNode, fork, default, false,
            expectSuccess, expectExc, 0, 0, 0);
    }

    private static ParseResult Finish(
        string name, JsonElement caseNode, JsonElement preNode, JsonElement txNode,
        string fork, JsonElement postState, bool havePost,
        bool expectSuccess, string? expectExc,
        int dataIndex, int gasIndex, int valueIndex)
    {
        var loaded = new LoadedFixture
        {
            CaseName = name,
            Fork = fork,
            ExpectSuccess = expectSuccess,
            ExpectException = expectExc
        };

        foreach (var acc in preNode.EnumerateObject())
        {
            var seed = ReadAccount(acc.Name, acc.Value);
            if (seed != null)
                loaded.PreAccounts.Add(seed);
        }

        if (havePost)
        {
            foreach (var acc in postState.EnumerateObject())
            {
                var seed = ReadAccount(acc.Name, acc.Value);
                if (seed is null) continue;
                loaded.ExpectedPost.Add(new ExpectedAccount
                {
                    AddressHex = seed.AddressHex,
                    BalanceWei = seed.BalanceWei,
                    Nonce = seed.Nonce,
                    CodeHex = seed.CodeHex,
                    StorageHex = seed.StorageHex is null
                        ? new Dictionary<string, string>()
                        : new Dictionary<string, string>(seed.StorageHex)
                });
            }
        }

        // Standard EELS state-test fixtures carry "secretKey" (a 32-byte private key), not
        // "sender" — that must never be used as the sender address. When "sender" is absent,
        // fall through to the PreAccounts[0] address, matching this loader's stated intent
        // that no secret-key signing is required to run a fixture.
        var sender = Text(txNode, "sender") ?? "";
        if (string.IsNullOrWhiteSpace(sender) && loaded.PreAccounts.Count > 0)
            sender = loaded.PreAccounts[0].AddressHex;

        loaded.SenderHex = sender;
        var toText = Text(txNode, "to");
        loaded.ToHex = string.IsNullOrWhiteSpace(toText) ? null : toText;

        var data = VariantBytes(txNode, "data", dataIndex);
        loaded.CallDataHex = "0x" + Convert.ToHexString(data).ToLowerInvariant();

        var value = VariantBig(txNode, "value", valueIndex);
        loaded.ValueWei = WorkbenchQuantity.ToDecimalString(value);
        loaded.GasLimit = VariantUlong(txNode, "gasLimit", gasIndex);
        if (loaded.GasLimit == 0) loaded.GasLimit = 10_000_000;
        WorkbenchQuantity.TryUlong(Text(txNode, "nonce"), out var nonce);
        loaded.Nonce = nonce;

        if (caseNode.TryGetProperty("env", out var env))
        {
            loaded.CoinbaseHex = Text(env, "currentCoinbase") ?? loaded.CoinbaseHex;
            if (WorkbenchQuantity.TryUlong(Text(env, "currentGasLimit"), out var bgl) && bgl > 0)
            { /* block gas used only as context */ }
            WorkbenchQuantity.TryBigInteger(Text(env, "currentBaseFee"), out var bf);
            loaded.BaseFeeWei = WorkbenchQuantity.ToDecimalString(bf);
        }

        if (caseNode.TryGetProperty("config", out var cfg) &&
            WorkbenchQuantity.TryUlong(Text(cfg, "chainid") ?? Text(cfg, "chainId"), out var cid) && cid > 0)
            loaded.ChainId = cid;
        else
            loaded.ChainId = 1;

        var gasPrice = Text(txNode, "gasPrice") ?? Text(txNode, "maxFeePerGas") ?? "0";
        WorkbenchQuantity.TryBigInteger(gasPrice, out var gp);
        loaded.GasPriceWei = WorkbenchQuantity.ToDecimalString(gp);
        WorkbenchQuantity.TryBigInteger(Text(txNode, "maxFeePerGas") ?? gasPrice, out var mf);
        loaded.MaxFeeWei = WorkbenchQuantity.ToDecimalString(mf);
        WorkbenchQuantity.TryBigInteger(Text(txNode, "maxPriorityFeePerGas") ?? "0", out var mp);
        loaded.MaxPriorityWei = WorkbenchQuantity.ToDecimalString(mp);

        if (txNode.TryGetProperty("maxFeePerGas", out _)) loaded.TxType = 2;
        if (txNode.TryGetProperty("blobVersionedHashes", out _)) loaded.TxType = 3;
        if (txNode.TryGetProperty("authorizationList", out _)) loaded.TxType = 4;

        ParseAccessList(txNode, dataIndex, loaded.AccessList);
        ParseAuths(txNode, loaded.Authorizations);

        return new(loaded, "");
    }

    private static void ParseAccessList(JsonElement txNode, int dataIndex, List<AccessListEntry> dest)
    {
        if (!txNode.TryGetProperty("accessLists", out var lists) &&
            !txNode.TryGetProperty("accessList", out lists))
            return;

        JsonElement list = lists;
        if (lists.ValueKind == JsonValueKind.Array && lists.GetArrayLength() > 0)
        {
            var first = lists[0];
            if (first.ValueKind == JsonValueKind.Array)
            {
                var idx = Math.Clamp(dataIndex, 0, lists.GetArrayLength() - 1);
                list = lists[idx];
            }
        }

        if (list.ValueKind != JsonValueKind.Array) return;
        foreach (var entry in list.EnumerateArray())
        {
            var addrText = Text(entry, "address");
            if (string.IsNullOrWhiteSpace(addrText)) continue;
            try
            {
                var keys = new List<BigInteger>();
                if (entry.TryGetProperty("storageKeys", out var sk) && sk.ValueKind == JsonValueKind.Array)
                {
                    foreach (var k in sk.EnumerateArray())
                    {
                        if (WorkbenchQuantity.TryBigInteger(k.GetString(), out var slot))
                            keys.Add(slot);
                    }
                }
                dest.Add(new AccessListEntry
                {
                    Address = Address.FromHex(addrText),
                    StorageKeys = keys
                });
            }
            catch { /* skip bad address */ }
        }
    }

    private static void ParseAuths(JsonElement txNode, List<Eip7702Authorization> dest)
    {
        if (!txNode.TryGetProperty("authorizationList", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;
        foreach (var a in arr.EnumerateArray())
        {
            try
            {
                WorkbenchQuantity.TryUlong(Text(a, "chainId"), out var chain);
                WorkbenchQuantity.TryUlong(Text(a, "nonce"), out var nonce);
                var delegateText = Text(a, "address") ?? "";
                var signerText = Text(a, "signer") ?? "";
                dest.Add(new Eip7702Authorization
                {
                    ChainId = chain,
                    Nonce = nonce,
                    DelegateAddress = string.IsNullOrWhiteSpace(delegateText)
                        ? default
                        : Address.FromHex(delegateText),
                    Signer = string.IsNullOrWhiteSpace(signerText)
                        ? default
                        : Address.FromHex(signerText),
                    IsValid = !string.IsNullOrWhiteSpace(signerText)
                });
            }
            catch { /* skip */ }
        }
    }

    private static WorkbenchAccountSeed? ReadAccount(string address, JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        var balRaw = Text(el, "balance") ?? "0";
        WorkbenchQuantity.TryBigInteger(balRaw, out var bal);
        WorkbenchQuantity.TryUlong(Text(el, "nonce"), out var nonce);
        var storage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (el.TryGetProperty("storage", out var st) && st.ValueKind == JsonValueKind.Object)
        {
            foreach (var slot in st.EnumerateObject())
                storage[slot.Name] = slot.Value.ValueKind == JsonValueKind.String
                    ? slot.Value.GetString() ?? "0x0"
                    : slot.Value.ToString();
        }

        return new WorkbenchAccountSeed
        {
            AddressHex = address,
            BalanceWei = WorkbenchQuantity.ToDecimalString(bal),
            Nonce = nonce,
            CodeHex = Text(el, "code") ?? "",
            StorageHex = storage.Count == 0 ? null : storage
        };
    }

    private static bool TryForkPost(
        JsonElement postNode, string preferred, out string fork, out JsonElement variant)
    {
        fork = preferred;
        variant = default;
        // Prefer the requested fork only. Falling through to Osaka is why a Frontier
        // failure can "pass" in the workbench.
        string[] order = string.IsNullOrWhiteSpace(preferred)
            ? ["Osaka", "Prague", "Cancun", "Shanghai", "Paris", "London", "Berlin"]
            : [preferred];
        foreach (var name in order.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (postNode.TryGetProperty(name, out var arr) &&
                arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
            {
                fork = name;
                variant = arr[0];
                return variant.ValueKind == JsonValueKind.Object;
            }
        }

        foreach (var p in postNode.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.Array && p.Value.GetArrayLength() > 0)
            {
                fork = p.Name;
                variant = p.Value[0];
                return variant.ValueKind == JsonValueKind.Object;
            }
        }

        return false;
    }

    private static int IndexOf(JsonElement variant, string key)
    {
        if (!variant.TryGetProperty("indexes", out var idx) || idx.ValueKind != JsonValueKind.Object)
            return 0;
        if (!idx.TryGetProperty(key, out var n)) return 0;
        return n.ValueKind == JsonValueKind.Number ? n.GetInt32() : 0;
    }

    private static byte[] VariantBytes(JsonElement tx, string name, int index)
    {
        if (!tx.TryGetProperty(name, out var el)) return Array.Empty<byte>();
        if (el.ValueKind == JsonValueKind.Array)
        {
            if (el.GetArrayLength() == 0) return Array.Empty<byte>();
            index = Math.Clamp(index, 0, el.GetArrayLength() - 1);
            el = el[index];
        }
        var s = el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";
        BytecodeExecutionService.TryParseHexBytes(s, out var bytes);
        return bytes;
    }

    private static BigInteger VariantBig(JsonElement tx, string name, int index)
    {
        if (!tx.TryGetProperty(name, out var el)) return BigInteger.Zero;
        if (el.ValueKind == JsonValueKind.Array)
        {
            if (el.GetArrayLength() == 0) return BigInteger.Zero;
            index = Math.Clamp(index, 0, el.GetArrayLength() - 1);
            el = el[index];
        }
        WorkbenchQuantity.TryBigInteger(el.GetString(), out var v);
        return v;
    }

    private static ulong VariantUlong(JsonElement tx, string name, int index)
    {
        if (!tx.TryGetProperty(name, out var el)) return 0;
        if (el.ValueKind == JsonValueKind.Array)
        {
            if (el.GetArrayLength() == 0) return 0;
            index = Math.Clamp(index, 0, el.GetArrayLength() - 1);
            el = el[index];
        }
        WorkbenchQuantity.TryUlong(el.GetString(), out var v);
        return v;
    }

    private static string? Text(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p)) return null;
        return p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString();
    }
}
