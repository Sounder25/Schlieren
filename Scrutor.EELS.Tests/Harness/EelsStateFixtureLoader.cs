using System.Numerics;
using System.Text.Json;
using Nethereum.Signer;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;

namespace Scrutor.EELS.Tests.Harness;

public sealed class EelsStateFixtureLoader
{
    private static readonly IReadOnlyDictionary<string, int> ForkOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Frontier"] = 0,
        ["Homestead"] = 1,
        ["Byzantium"] = 2,
        ["Constantinople"] = 3,
        ["Istanbul"] = 4,
        ["Berlin"] = 5,
        ["London"] = 6,
        ["Paris"] = 7,
        ["Shanghai"] = 8,
        ["Cancun"] = 9,
        ["Prague"] = 10,
        ["Osaka"] = 11
    };

    public IReadOnlyList<EelsStateCase> LoadCases(EelsHarnessOptions options)
    {
        if (!Directory.Exists(options.FixturesRoot))
        {
            throw new DirectoryNotFoundException($"EELS fixtures root not found: {options.FixturesRoot}");
        }

        var searchOption = options.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var fixtureFiles = Directory.EnumerateFiles(options.FixturesRoot, "*.json", searchOption)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var cases = new List<EelsStateCase>(Math.Min(options.MaxCases, 256));
        foreach (var file in fixtureFiles)
        {
            LoadCasesFromFile(file, options, cases);
            if (cases.Count >= options.MaxCases)
            {
                break;
            }
        }

        return cases;
    }

    private static void LoadCasesFromFile(string filePath, EelsHarnessOptions options, List<EelsStateCase> output)
    {
        using var stream = File.OpenRead(filePath);
        using var doc = JsonDocument.Parse(stream);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var fixtureCase in doc.RootElement.EnumerateObject())
        {
            if (output.Count >= options.MaxCases)
            {
                return;
            }

            var parsedCases = TryBuildCases(filePath, fixtureCase.Name, fixtureCase.Value, options.ForkName);
            foreach (var parsedCase in parsedCases)
            {
                output.Add(parsedCase);

                if (output.Count >= options.MaxCases)
                {
                    return;
                }
            }
        }
    }

    private static IReadOnlyList<EelsStateCase> TryBuildCases(
        string fixturePath,
        string caseId,
        JsonElement caseNode,
        string forkName)
    {
        if (LooksLikePublishedStateTest(caseNode))
        {
            return TryBuildPublishedCases(fixturePath, caseId, caseNode, forkName);
        }

        if (LooksLikeLegacyStateTest(caseNode))
        {
            return TryBuildLegacyCases(fixturePath, caseId, caseNode, forkName);
        }

        return Array.Empty<EelsStateCase>();
    }

    private static bool LooksLikePublishedStateTest(JsonElement caseNode)
    {
        if (!caseNode.TryGetProperty("post", out _))
        {
            return false;
        }

        if (!caseNode.TryGetProperty("_info", out var info))
        {
            return true;
        }

        if (info.TryGetProperty("fixture_format", out var legacyFormatProp))
        {
            return string.Equals(GetJsonText(legacyFormatProp), "state_test", StringComparison.OrdinalIgnoreCase);
        }

        if (info.TryGetProperty("fixture-format", out var modernFormatProp))
        {
            return string.Equals(GetJsonText(modernFormatProp), "state_test", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static bool LooksLikeLegacyStateTest(JsonElement caseNode)
    {
        return caseNode.TryGetProperty("env", out _) &&
               caseNode.TryGetProperty("pre", out _) &&
               caseNode.TryGetProperty("transaction", out _) &&
               caseNode.TryGetProperty("expect", out var expectNode) &&
               expectNode.ValueKind == JsonValueKind.Array;
    }

    private static IReadOnlyList<EelsStateCase> TryBuildPublishedCases(
        string fixturePath,
        string caseId,
        JsonElement caseNode,
        string forkName)
    {
        if (!caseNode.TryGetProperty("env", out var envNode) ||
            !caseNode.TryGetProperty("pre", out var preNode) ||
            !caseNode.TryGetProperty("transaction", out var txNode) ||
            !caseNode.TryGetProperty("post", out var postNode))
        {
            return Array.Empty<EelsStateCase>();
        }

        if (!postNode.TryGetProperty(forkName, out var forkPostArray) || forkPostArray.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<EelsStateCase>();
        }

        var firstPostVariant = forkPostArray.EnumerateArray().FirstOrDefault();
        if (firstPostVariant.ValueKind != JsonValueKind.Object ||
            !firstPostVariant.TryGetProperty("indexes", out var indexesNode))
        {
            return Array.Empty<EelsStateCase>();
        }

        var preState = ParseAccountMap(preNode, baseState: null, allowPartial: false);

        var dataIndex = ParseScalarIndex(indexesNode, "data");
        var gasIndex = ParseScalarIndex(indexesNode, "gas");
        var valueIndex = ParseScalarIndex(indexesNode, "value");

        var sender = ResolveSender(txNode, preState);
        var nonce = ParseRequiredUlong(txNode, "nonce");
        var gasPrice = ResolveGasPrice(txNode);
        var gasLimit = ParseVariantUlong(txNode, "gasLimit", gasIndex);
        var value = ParseVariantBigInteger(txNode, "value", valueIndex);
        var data = ParseVariantBytes(txNode, "data", dataIndex);
        var to = txNode.TryGetProperty("to", out var toNode) ? ParseOptionalAddress(toNode) : null;

        var blockContext = new BlockContext
        {
            ChainId = ParseChainId(caseNode),
            Number = EelsHex.ParseUlong(envNode.GetProperty("currentNumber").GetString()!),
            Timestamp = EelsHex.ParseUlong(envNode.GetProperty("currentTimestamp").GetString()!),
            GasLimit = EelsHex.ParseUlong(envNode.GetProperty("currentGasLimit").GetString()!),
            Coinbase = Address.FromHex(envNode.GetProperty("currentCoinbase").GetString()!),
            Difficulty = EelsHex.ParseQuantity(envNode.GetProperty("currentDifficulty").GetString()!),
            BaseFeePerGas = EelsHex.ParseUlong(envNode.GetProperty("currentBaseFee").GetString()!)
        };

        var tx = new Transaction
        {
            From = sender,
            To = to,
            Nonce = nonce,
            GasPrice = gasPrice,
            GasLimit = gasLimit,
            Value = value,
            Data = data,
            Authorization = TransactionAuthorization.Impersonated
        };

        if (!firstPostVariant.TryGetProperty("state", out var expectedStateNode))
        {
            return Array.Empty<EelsStateCase>();
        }

        var expectedState = ParseAccountMap(expectedStateNode, preState, allowPartial: true);
        bool? expectedReceiptStatus = null;
        if (firstPostVariant.TryGetProperty("receipt", out var receiptNode) &&
            receiptNode.TryGetProperty("status", out var statusNode) &&
            (statusNode.ValueKind is JsonValueKind.True or JsonValueKind.False))
        {
            expectedReceiptStatus = statusNode.GetBoolean();
        }

        var parsedCase = new EelsStateCase(
            fixturePath,
            caseId,
            forkName,
            blockContext,
            sender,
            tx,
            preState,
            expectedState,
            expectedReceiptStatus);

        return new[] { parsedCase };
    }

    private static IReadOnlyList<EelsStateCase> TryBuildLegacyCases(
        string fixturePath,
        string caseId,
        JsonElement caseNode,
        string forkName)
    {
        var envNode = caseNode.GetProperty("env");
        var preNode = caseNode.GetProperty("pre");
        var txNode = caseNode.GetProperty("transaction");
        var expectNode = caseNode.GetProperty("expect");

        var preState = ParseAccountMap(preNode, baseState: null, allowPartial: false);
        var sender = ResolveSender(txNode, preState);
        var nonce = ParseRequiredUlong(txNode, "nonce");
        var gasPrice = ResolveGasPrice(txNode);
        var to = txNode.TryGetProperty("to", out var toNode) ? ParseOptionalAddress(toNode) : null;

        var blockContext = new BlockContext
        {
            ChainId = ParseChainId(caseNode),
            Number = EelsHex.ParseUlong(GetPropertyText(envNode, "currentNumber")),
            Timestamp = EelsHex.ParseUlong(GetPropertyText(envNode, "currentTimestamp")),
            GasLimit = EelsHex.ParseUlong(GetPropertyText(envNode, "currentGasLimit")),
            Coinbase = Address.FromHex(GetPropertyText(envNode, "currentCoinbase")),
            Difficulty = EelsHex.ParseQuantity(GetPropertyText(envNode, "currentDifficulty")),
            BaseFeePerGas = envNode.TryGetProperty("currentBaseFee", out var baseFeeNode)
                ? EelsHex.ParseUlong(GetJsonText(baseFeeNode))
                : 0
        };

        var cases = new List<EelsStateCase>();
        var variantNumber = 0;
        foreach (var expectVariant in expectNode.EnumerateArray())
        {
            if (!ShouldIncludeForFork(expectVariant, forkName))
            {
                continue;
            }

            if (!expectVariant.TryGetProperty("indexes", out var indexesNode) ||
                !expectVariant.TryGetProperty("result", out var resultNode))
            {
                continue;
            }

            var dataIndexes = ParseIndexVector(indexesNode, "data");
            var gasIndexes = ParseIndexVector(indexesNode, "gas");
            var valueIndexes = ParseIndexVector(indexesNode, "value");
            var expectedState = ParseAccountMap(resultNode, preState, allowPartial: true);

            foreach (var dataIndex in dataIndexes)
            foreach (var gasIndex in gasIndexes)
            foreach (var valueIndex in valueIndexes)
            {
                var tx = new Transaction
                {
                    From = sender,
                    To = to,
                    Nonce = nonce,
                    GasPrice = gasPrice,
                    GasLimit = ParseVariantUlong(txNode, "gasLimit", gasIndex),
                    Value = ParseVariantBigInteger(txNode, "value", valueIndex),
                    Data = ParseVariantBytes(txNode, "data", dataIndex),
                    Authorization = TransactionAuthorization.Impersonated
                };

                var variantSuffix = $"expect{variantNumber}_d{dataIndex}_g{gasIndex}_v{valueIndex}";
                cases.Add(new EelsStateCase(
                    fixturePath,
                    $"{caseId}/{variantSuffix}",
                    forkName,
                    blockContext,
                    sender,
                    tx,
                    preState,
                    expectedState,
                    null));
            }

            variantNumber++;
        }

        return cases;
    }

    private static ulong ParseChainId(JsonElement caseNode)
    {
        if (caseNode.TryGetProperty("config", out var configNode) &&
            configNode.TryGetProperty("chainid", out var chainIdNode))
        {
            return EelsHex.ParseUlong(chainIdNode.GetString()!);
        }

        return 1;
    }

    private static Address ResolveSender(
        JsonElement txNode,
        IReadOnlyDictionary<Address, EelsFixtureAccount> preState)
    {
        if (txNode.TryGetProperty("sender", out var senderNode))
        {
            return Address.FromHex(GetJsonText(senderNode));
        }

        if (txNode.TryGetProperty("secretKey", out var secretKeyNode))
        {
            var secretKeyText = GetJsonText(secretKeyNode);
            var secretKeyBytes = EelsHex.ParseBytes(secretKeyText);
            var key = new EthECKey(secretKeyBytes, true);
            return Address.FromHex(key.GetPublicAddress());
        }

        var txNonce = ParseRequiredUlong(txNode, "nonce");
        var nonceMatches = preState
            .Where(kvp => kvp.Value.Nonce == txNonce)
            .Select(kvp => kvp.Key)
            .ToArray();
        if (nonceMatches.Length == 1)
        {
            return nonceMatches[0];
        }

        throw new InvalidOperationException("Unable to resolve sender for EELS state fixture transaction.");
    }

    private static ulong ParseRequiredUlong(JsonElement node, string key) =>
        EelsHex.ParseUlong(GetPropertyText(node, key));

    private static Address? ParseOptionalAddress(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var raw = GetJsonText(node);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var normalized = EelsHex.Normalize(raw);
        if (normalized == "0x0")
        {
            return null;
        }

        return Address.FromHex(raw);
    }

    private static int ParseScalarIndex(JsonElement indexesNode, string key)
    {
        if (!indexesNode.TryGetProperty(key, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt32(),
            JsonValueKind.Array => value.EnumerateArray().Select(v => v.GetInt32()).FirstOrDefault(),
            _ => 0
        };
    }

    private static IReadOnlyList<int> ParseIndexVector(JsonElement indexesNode, string key)
    {
        if (!indexesNode.TryGetProperty(key, out var value))
        {
            return new[] { 0 };
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            return new[] { value.GetInt32() };
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            var list = value.EnumerateArray().Select(v => v.GetInt32()).Distinct().ToArray();
            return list.Length == 0 ? new[] { 0 } : list;
        }

        return new[] { 0 };
    }

    private static ulong ParseVariantUlong(JsonElement txNode, string key, int index)
    {
        if (!txNode.TryGetProperty(key, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            var arr = value.EnumerateArray().ToArray();
            if (arr.Length == 0)
            {
                return 0;
            }

            var safeIndex = Math.Clamp(index, 0, arr.Length - 1);
            return EelsHex.ParseUlong(GetJsonText(arr[safeIndex]));
        }

        return EelsHex.ParseUlong(GetJsonText(value));
    }

    private static BigInteger ParseVariantBigInteger(JsonElement txNode, string key, int index)
    {
        if (!txNode.TryGetProperty(key, out var value))
        {
            return BigInteger.Zero;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            var arr = value.EnumerateArray().ToArray();
            if (arr.Length == 0)
            {
                return BigInteger.Zero;
            }

            var safeIndex = Math.Clamp(index, 0, arr.Length - 1);
            return EelsHex.ParseQuantity(GetJsonText(arr[safeIndex]));
        }

        return EelsHex.ParseQuantity(GetJsonText(value));
    }

    private static byte[] ParseVariantBytes(JsonElement txNode, string key, int index)
    {
        if (!txNode.TryGetProperty(key, out var value))
        {
            return Array.Empty<byte>();
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            var arr = value.EnumerateArray().ToArray();
            if (arr.Length == 0)
            {
                return Array.Empty<byte>();
            }

            var safeIndex = Math.Clamp(index, 0, arr.Length - 1);
            return EelsHex.ParseBytes(GetJsonText(arr[safeIndex]));
        }

        return EelsHex.ParseBytes(GetJsonText(value));
    }

    private static Dictionary<Address, EelsFixtureAccount> ParseAccountMap(
        JsonElement accountsNode,
        IReadOnlyDictionary<Address, EelsFixtureAccount>? baseState,
        bool allowPartial)
    {
        var map = new Dictionary<Address, EelsFixtureAccount>();
        foreach (var accountProp in accountsNode.EnumerateObject())
        {
            var accountAddress = Address.FromHex(accountProp.Name);
            var accountNode = accountProp.Value;

            EelsFixtureAccount? baselineAccount = null;
            var hasBaseline = baseState != null && baseState.TryGetValue(accountAddress, out baselineAccount);

            var nonce = accountNode.TryGetProperty("nonce", out var nonceNode)
                ? EelsHex.ParseUlong(GetJsonText(nonceNode))
                : (hasBaseline ? baselineAccount!.Nonce : 0UL);
            var balance = accountNode.TryGetProperty("balance", out var balanceNode)
                ? EelsHex.ParseQuantity(GetJsonText(balanceNode))
                : (hasBaseline ? baselineAccount!.Balance : BigInteger.Zero);
            var code = accountNode.TryGetProperty("code", out var codeNode)
                ? EelsHex.ParseBytes(GetJsonText(codeNode))
                : (hasBaseline ? baselineAccount!.Code : Array.Empty<byte>());
            var storage = new Dictionary<BigInteger, BigInteger>();
            if (hasBaseline)
            {
                foreach (var (k, v) in baselineAccount!.Storage)
                {
                    storage[k] = v;
                }
            }

            if (accountNode.TryGetProperty("storage", out var storageNode) && storageNode.ValueKind == JsonValueKind.Object)
            {
                foreach (var slotProp in storageNode.EnumerateObject())
                {
                    storage[EelsHex.ParseQuantity(slotProp.Name)] = EelsHex.ParseQuantity(GetJsonText(slotProp.Value));
                }
            }

            if (!allowPartial &&
                (!accountNode.TryGetProperty("nonce", out _) || !accountNode.TryGetProperty("balance", out _) || !accountNode.TryGetProperty("code", out _)))
            {
                throw new InvalidOperationException($"Pre-state account {accountAddress} is missing required fields.");
            }

            map[accountAddress] = new EelsFixtureAccount(nonce, balance, code, storage);
        }

        return map;
    }

    private static bool ShouldIncludeForFork(JsonElement expectVariant, string targetFork)
    {
        if (!expectVariant.TryGetProperty("network", out var networkNode) || networkNode.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        var targetRank = ForkRank(targetFork);
        foreach (var selectorNode in networkNode.EnumerateArray())
        {
            var selector = GetJsonText(selectorNode).Trim();
            if (selector.Length == 0)
            {
                continue;
            }

            if (selector.StartsWith(">=", StringComparison.Ordinal))
            {
                var fork = selector[2..].Trim();
                if (targetRank >= ForkRank(fork))
                {
                    return true;
                }
            }
            else if (selector.StartsWith("<=", StringComparison.Ordinal))
            {
                var fork = selector[2..].Trim();
                if (targetRank <= ForkRank(fork))
                {
                    return true;
                }
            }
            else if (selector.StartsWith(">", StringComparison.Ordinal))
            {
                var fork = selector[1..].Trim();
                if (targetRank > ForkRank(fork))
                {
                    return true;
                }
            }
            else if (selector.StartsWith("<", StringComparison.Ordinal))
            {
                var fork = selector[1..].Trim();
                if (targetRank < ForkRank(fork))
                {
                    return true;
                }
            }
            else if (string.Equals(selector, targetFork, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int ForkRank(string forkName) =>
        ForkOrder.TryGetValue(forkName, out var rank) ? rank : int.MinValue;

    private static string GetPropertyText(JsonElement node, string propertyName)
    {
        if (!node.TryGetProperty(propertyName, out var property))
        {
            throw new InvalidOperationException($"Required property '{propertyName}' not found in fixture node.");
        }

        return GetJsonText(property);
    }

    private static string GetJsonText(JsonElement node) =>
        node.ValueKind switch
        {
            JsonValueKind.String => node.GetString() ?? string.Empty,
            JsonValueKind.Number => node.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => node.GetRawText()
        };

    private static BigInteger ResolveGasPrice(JsonElement txNode)
    {
        if (txNode.TryGetProperty("gasPrice", out var gasPriceNode))
        {
            return EelsHex.ParseQuantity(GetJsonText(gasPriceNode));
        }

        // [AI-EDIT 2026-01-10] For type-2/3 fixtures, use maxFeePerGas as
        // effective execution gas price for baseline conformance comparison.
        if (txNode.TryGetProperty("maxFeePerGas", out var maxFeeNode))
        {
            return EelsHex.ParseQuantity(GetJsonText(maxFeeNode));
        }

        return BigInteger.Zero;
    }
}
