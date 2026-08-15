using System.Numerics;
using System.Text.Json;
using Nethereum.Signer;
using Schlieren.Core.Forks;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.EELS.Tests.Harness;

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

    public IReadOnlyList<EelsStateCase> LoadCases(
        EelsHarnessOptions options,
        IProgress<EelsLoadProgress>? progress = null)
    {
        if (!Directory.Exists(options.FixturesRoot))
        {
            throw new DirectoryNotFoundException($"EELS fixtures root not found: {options.FixturesRoot}");
        }

        var searchOption = options.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var fixtureFiles = Directory.EnumerateFiles(options.FixturesRoot, "*.json", searchOption)
            .Where(path => string.IsNullOrWhiteSpace(options.ExcludeFolder) ||
                           !path.Replace('\\', '/').Contains($"/{options.ExcludeFolder}/",
                               StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        progress?.Report(new EelsLoadProgress(0, fixtureFiles.Length, 0, string.Empty));

        var cases = new List<EelsStateCase>(Math.Min(options.MaxCases, 256));
        for (var i = 0; i < fixtureFiles.Length; i++)
        {
            var file = fixtureFiles[i];
            LoadCasesFromFile(file, options, cases);
            var done = i + 1;
            if (progress != null && (done == fixtureFiles.Length || done % 10 == 0 || done == 1))
            {
                progress.Report(new EelsLoadProgress(
                    done,
                    fixtureFiles.Length,
                    cases.Count,
                    Path.GetFileName(file)));
            }

            if (cases.Count >= options.MaxCases)
            {
                progress?.Report(new EelsLoadProgress(
                    done, fixtureFiles.Length, cases.Count, Path.GetFileName(file)));
                break;
            }
        }

        // Deduplicate by CaseId — the same fixture can appear under multiple fixture subdirectories
        // (e.g. a cancun fixture also listed under a fork-specific sub-tree), causing duplicate keys downstream.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var deduped = new List<EelsStateCase>(cases.Count);
        foreach (var c in cases)
            if (seen.Add(c.CaseId))
                deduped.Add(c);
        return deduped;
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
        var accessList = ParseVariantAccessList(txNode, dataIndex);
        var blobVersionedHashes = ParseBlobVersionedHashes(txNode);

        var blockContext = new BlockContext
        {
            ChainId = ParseChainId(caseNode),
            Number = EelsHex.ParseUlong(envNode.GetProperty("currentNumber").GetString()!),
            Timestamp = EelsHex.ParseUlong(envNode.GetProperty("currentTimestamp").GetString()!),
            GasLimit = EelsHex.ParseUlong(envNode.GetProperty("currentGasLimit").GetString()!),
            Coinbase = Address.FromHex(envNode.GetProperty("currentCoinbase").GetString()!),
            Difficulty = envNode.TryGetProperty("currentRandom", out var randomNode)
                ? EelsHex.ParseQuantity(GetJsonText(randomNode))  // Post-Paris: PREVRANDAO replaces DIFFICULTY
                : EelsHex.ParseQuantity(envNode.GetProperty("currentDifficulty").GetString()!),
            BaseFeePerGas = envNode.TryGetProperty("currentBaseFee", out var baseFeeNode)
                ? EelsHex.ParseUlong(GetJsonText(baseFeeNode))
                : 0UL,
            Rules = ForkRulesFactory.For(forkName),
            ExcessBlobGas = envNode.TryGetProperty("currentExcessBlobGas", out var excessBlobGasNode)
                ? EelsHex.ParseUlong(GetJsonText(excessBlobGasNode))
                : 0,
            BlockHashes = ParseBlockHashes(envNode),
        };


        var priorityFee = ResolvePriorityFee(txNode);
        var txType = DetectTxType(txNode, accessList);
        var authorizationList = ParseAuthorizationList(txNode);
        var tx = new Transaction
        {
            From = sender,
            To = to,
            Nonce = nonce,
            GasPrice = gasPrice,
            MaxFeePerGas = gasPrice,
            MaxPriorityFeePerGas = priorityFee,
            TxType = txType,
            GasLimit = gasLimit,
            Value = value,
            Data = data,
            AccessList = accessList,
            BlobVersionedHashes = blobVersionedHashes,
            MaxFeePerBlobGas = ResolveMaxFeePerBlobGas(txNode),
            AuthorizationList = authorizationList,
            // Type-4 (EIP-7702) uses a different signing hash format; use Impersonated
            // when the fixture provides 'sender' directly to avoid wrong address recovery.
            Authorization = (txNode.TryGetProperty("secretKey", out _) && txType != 4)
                ? TransactionAuthorization.Signed
                : TransactionAuthorization.Impersonated
        };

        // Generate valid signature for Signed transactions using Nethereum
        // StateTransition will recover address from signature to verify sender
        if (tx.Authorization == TransactionAuthorization.Signed)
        {
            if (txNode.TryGetProperty("secretKey", out var secretKeyNode))
            {
                var skText = GetJsonText(secretKeyNode);
                var skBytes = EelsHex.ParseBytes(skText);
                var key = new EthECKey(skBytes, true);
                
                // Build EIP-155 signing hash: keccak256(rlp([nonce, gasPrice, gasLimit, to, value, data, chainId, 0, 0]))
                var signingHash = BuildLegacyEip155SigningHash(tx, blockContext.ChainId);
                tx.SigningHash = signingHash;
                
                // Sign with Nethereum - EthECDSASignature has R and S as byte[][]
                var signature = key.SignAndCalculateV(signingHash);
                
                // signature.V from Nethereum is a byte array - extract the integer value
                tx.R = PadTo32Bytes(signature.R);
                tx.S = PadTo32Bytes(signature.S);
                // V is stored as a single byte or minimal big-endian bytes, convert to int
                if (signature.V.Length == 1)
                    tx.V = signature.V[0];
                else if (signature.V.Length > 1)
                {
                    // Convert from big-endian
                    tx.V = 0;
                    for (int i = 0; i < signature.V.Length; i++)
                        tx.V = (tx.V << 8) | signature.V[i];
                }
                else
                    tx.V = 0;
            }
        }

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

        // EELS modern format: when `expectException` is present the transaction is declared
        // invalid — it should be rejected without any state change.  Override the receipt
        // status to false so the executor treats this as a "must-reject" case.
        string? expectedException = null;
        if (firstPostVariant.TryGetProperty("expectException", out var excNode) &&
            excNode.ValueKind == JsonValueKind.String)
        {
            var excText = excNode.GetString();
            if (!string.IsNullOrEmpty(excText))
            {
                expectedException  = excText;
                expectedReceiptStatus = false; // tx should be rejected — no success receipt
            }
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
            expectedReceiptStatus,
            expectedException);

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
            Difficulty = envNode.TryGetProperty("currentRandom", out var randomNode2)
                ? EelsHex.ParseQuantity(GetJsonText(randomNode2))
                : EelsHex.ParseQuantity(GetPropertyText(envNode, "currentDifficulty")),
            BaseFeePerGas = envNode.TryGetProperty("currentBaseFee", out var baseFeeNode)
                ? EelsHex.ParseUlong(GetJsonText(baseFeeNode))
                : 0,
            Rules = ForkRulesFactory.For(forkName),
            ExcessBlobGas = envNode.TryGetProperty("currentExcessBlobGas", out var excessBlobGasNode)
                ? EelsHex.ParseUlong(GetJsonText(excessBlobGasNode))
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

            // Derive a deterministic private key from sender address for legacy tx signing
            var legacySenderKey = DeriveKeyFromAddress(sender);
            var legacyChainId = ParseChainId(txNode);

            foreach (var dataIndex in dataIndexes)
            foreach (var gasIndex in gasIndexes)
            foreach (var valueIndex in valueIndexes)
            {
                var legacyPriorityFee = ResolvePriorityFee(txNode);
                var legacyAccessList = ParseVariantAccessList(txNode, dataIndex);
                var legacyTxType = DetectTxType(txNode, legacyAccessList);
                var tx = new Transaction
                {
                    From = sender,
                    To = to,
                    Nonce = nonce,
                    GasPrice = gasPrice,
                    MaxFeePerGas = gasPrice,
                    MaxPriorityFeePerGas = legacyPriorityFee,
                    TxType = legacyTxType,
                    GasLimit = ParseVariantUlong(txNode, "gasLimit", gasIndex),
                    Value = ParseVariantBigInteger(txNode, "value", valueIndex),
                    Data = ParseVariantBytes(txNode, "data", dataIndex),
                    AccessList = legacyAccessList,
                    BlobVersionedHashes = ParseBlobVersionedHashes(txNode),
                    MaxFeePerBlobGas = ResolveMaxFeePerBlobGas(txNode),
                    Authorization = TransactionAuthorization.Signed
                };

                // Sign the legacy transaction for proper gas accounting
                SignTransaction(tx, legacySenderKey, legacyChainId);

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

        // A present "to" field with address 0x0 is a valid CALL target (not a CREATE).
        // Only return null for absent/null JSON values (handled above).
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

    /// <summary>
    /// Parses the access list for a given data variant index.
    /// EELS fixtures use either "accessLists" (array-of-arrays, one per data variant) or
    /// "accessList" (a single flat array). Returns an empty list if neither is present.
    /// </summary>
    private static IReadOnlyList<AccessListEntry> ParseVariantAccessList(JsonElement txNode, int dataIndex)
    {
        // "accessLists": [ [entry, entry], [entry, entry] ]  — one list per data variant
        if (txNode.TryGetProperty("accessLists", out var listsNode) && listsNode.ValueKind == JsonValueKind.Array)
        {
            var listsArr = listsNode.EnumerateArray().ToArray();
            if (listsArr.Length == 0) return Array.Empty<AccessListEntry>();
            var safeIndex = Math.Clamp(dataIndex, 0, listsArr.Length - 1);
            return ParseFlatAccessList(listsArr[safeIndex]);
        }

        // "accessList": [entry, entry]  — single list shared across all variants
        if (txNode.TryGetProperty("accessList", out var listNode) && listNode.ValueKind == JsonValueKind.Array)
        {
            return ParseFlatAccessList(listNode);
        }

        return Array.Empty<AccessListEntry>();
    }

    private static IReadOnlyList<byte[]> ParseBlobVersionedHashes(
        JsonElement txNode)
    {
        if (!txNode.TryGetProperty(
                "blobVersionedHashes",
                out var hashesNode) ||
            hashesNode.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<byte[]>();
        }

        return hashesNode
            .EnumerateArray()
            .Select(hash => EelsHex.ParseBytes(GetJsonText(hash)))
            .ToArray();
    }

    private static IReadOnlyList<AccessListEntry> ParseFlatAccessList(JsonElement listNode)
    {
        if (listNode.ValueKind != JsonValueKind.Array) return Array.Empty<AccessListEntry>();
        var result = new List<AccessListEntry>();
        foreach (var entryNode in listNode.EnumerateArray())
        {
            if (entryNode.ValueKind != JsonValueKind.Object) continue;
            if (!entryNode.TryGetProperty("address", out var addrNode)) continue;
            var address = Address.FromHex(GetJsonText(addrNode));
            var keys = new List<BigInteger>();
            if (entryNode.TryGetProperty("storageKeys", out var keysNode) && keysNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var keyNode in keysNode.EnumerateArray())
                {
                    keys.Add(EelsHex.ParseQuantity(GetJsonText(keyNode)));
                }
            }

            result.Add(new AccessListEntry { Address = address, StorageKeys = keys });
        }

        return result;
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
            var hasExplicitStorage =
                accountNode.TryGetProperty("storage", out var storageNode) &&
                storageNode.ValueKind == JsonValueKind.Object;
            if (hasBaseline && !hasExplicitStorage)
            {
                foreach (var (k, v) in baselineAccount!.Storage)
                {
                    storage[k] = v;
                }
            }

            if (hasExplicitStorage)
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

        // [AI-EDIT 2026-01-10] For type-2/3 fixtures, the effective gas price is
        // min(maxFeePerGas, baseFeePerGas + maxPriorityFeePerGas) per EIP-1559.
        // We store maxFeePerGas here as a cap; StateTransition resolves the real
        // effective price using block.BaseFeePerGas + MaxPriorityFeePerGas.
        if (txNode.TryGetProperty("maxFeePerGas", out var maxFeeNode))
        {
            return EelsHex.ParseQuantity(GetJsonText(maxFeeNode));
        }

        return BigInteger.Zero;
    }

    /// <summary>
    /// Extracts EIP-1559 maxPriorityFeePerGas from a transaction JSON node.
    /// Returns zero for legacy and EIP-2930 transactions.
    /// </summary>
    private static BigInteger ResolvePriorityFee(JsonElement txNode)
    {
        if (txNode.TryGetProperty("maxPriorityFeePerGas", out var pfNode))
            return EelsHex.ParseQuantity(GetJsonText(pfNode));
        return BigInteger.Zero;
    }

    private static BigInteger ResolveMaxFeePerBlobGas(JsonElement txNode)
    {
        if (txNode.TryGetProperty("maxFeePerBlobGas", out var feeNode))
            return EelsHex.ParseQuantity(GetJsonText(feeNode));
        return BigInteger.Zero;
    }

    /// <summary>
    /// [AI-EDIT 2026-01-10] Infers the EIP-2718 transaction type from fixture JSON fields.
    /// - type 0: legacy (has gasPrice, no maxFeePerGas)
    /// - type 1: EIP-2930 (has gasPrice + non-empty accessList)
    /// - type 2: EIP-1559 (has maxFeePerGas field, no gasPrice or an empty gasPrice)
    /// - type 3: EIP-4844 (has maxFeePerGas + blobVersionedHashes)
    /// </summary>
    private static byte DetectTxType(JsonElement txNode, IReadOnlyList<AccessListEntry> accessList)
    {
        bool hasMaxFeePerGas = txNode.TryGetProperty("maxFeePerGas", out _);
        bool hasGasPrice = txNode.TryGetProperty("gasPrice", out _);
        bool hasBlobs = txNode.TryGetProperty("blobVersionedHashes", out _);
        bool hasAuthList = txNode.TryGetProperty("authorizationList", out _);
        // Type-1 (EIP-2930) is signalled by the presence of the accessLists/accessList field,
        // even when the list is empty — the txbytes encoding is 0x01-prefixed.
        bool hasAccessListField = txNode.TryGetProperty("accessLists", out _)
                               || txNode.TryGetProperty("accessList", out _);

        if (hasAuthList) return 4;
        if (hasBlobs) return 3;
        if (hasMaxFeePerGas && !hasGasPrice) return 2;
        if (hasGasPrice && hasAccessListField) return 1;
        return 0;
    }

    /// <summary>
    /// Parses EIP-7702 authorizationList from a transaction node.
    /// Each entry has chainId, address, nonce, yParity, r, s, signer (pre-recovered).
    /// </summary>
    private static IReadOnlyList<Eip7702Authorization> ParseAuthorizationList(JsonElement txNode)
    {
        if (!txNode.TryGetProperty("authorizationList", out var authListNode) ||
            authListNode.ValueKind != JsonValueKind.Array)
            return Array.Empty<Eip7702Authorization>();

        // SECP256K1 constants for EELS recover_authority validation
        // r must be in (0, N), s must be in (0, N/2], y_parity must be 0 or 1
        var SECP256K1N = System.Numerics.BigInteger.Parse(
            "115792089237316195423570985008687907852837564279074904382605163141518161494337");
        var SECP256K1N_OVER_2 = SECP256K1N / 2;

        var list = new List<Eip7702Authorization>();
        foreach (var authNode in authListNode.EnumerateArray())
        {
            // chainId — 0 = any chain; may be u256 in invalid-auth test fixtures
            ulong chainId = 0;
            if (authNode.TryGetProperty("chainId", out var chainIdNode))
            {
                try { chainId = EelsHex.ParseUlong(GetJsonText(chainIdNode)); }
                catch { chainId = ulong.MaxValue; } // non-matching chain → skip auth
            }

            // delegate address
            if (!authNode.TryGetProperty("address", out var addrNode)) continue;
            var delegateAddress = Address.FromHex(GetJsonText(addrNode));

            // nonce — may be u256 in fixture; values >= 2^64 are treated as invalid
            ulong nonce = 0;
            if (authNode.TryGetProperty("nonce", out var nonceNode))
            {
                var nonceText = GetJsonText(nonceNode);
                // If the nonce doesn't fit in ulong, flag as invalid (EIP-7702 §validity)
                try { nonce = EelsHex.ParseUlong(nonceText); }
                catch { nonce = ulong.MaxValue; } // ulong.MaxValue → always invalid
            }

            // EELS recover_authority signature validation: check r, s, yParity ranges.
            // Entries with invalid signatures are included as IsValid=false (the tx still
            // executes; EELS warms the authority from the pre-recovered address if present,
            // but does NOT write delegation code or bump nonce).
            bool sigValid = true;
            int yParity = -1;
            if (authNode.TryGetProperty("yParity", out var ypNode) ||
                authNode.TryGetProperty("v", out ypNode))
            {
                try
                {
                    var ypVal = EelsHex.ParseUlong(GetJsonText(ypNode));
                    if (ypVal != 0 && ypVal != 1) sigValid = false;
                    yParity = (int)ypVal;
                }
                catch { sigValid = false; }
            }
            if (authNode.TryGetProperty("r", out var rNode))
            {
                try
                {
                    var rBytes = EelsHex.ParseBytes(GetJsonText(rNode));
                    var rBig = new System.Numerics.BigInteger(rBytes, isUnsigned: true, isBigEndian: true);
                    if (rBig <= 0 || rBig >= SECP256K1N) sigValid = false;
                }
                catch { sigValid = false; }
            }
            if (authNode.TryGetProperty("s", out var sNode))
            {
                try
                {
                    var sBytes = EelsHex.ParseBytes(GetJsonText(sNode));
                    var sBig = new System.Numerics.BigInteger(sBytes, isUnsigned: true, isBigEndian: true);
                    if (sBig <= 0 || sBig > SECP256K1N_OVER_2) sigValid = false;
                }
                catch { sigValid = false; }
            }

            // signer (pre-recovered from the fixture); missing means signature is invalid
            Address signer = Address.Zero;
            if (authNode.TryGetProperty("signer", out var signerNode))
                signer = Address.FromHex(GetJsonText(signerNode));
            else
                sigValid = false;

            list.Add(new Eip7702Authorization
            {
                ChainId         = chainId,
                DelegateAddress = delegateAddress,
                Nonce           = nonce,
                Signer          = signer,
                IsValid         = sigValid,
            });
        }
        return list;
    }

    /// <summary>
    /// Builds the EIP-155 signing hash for legacy transactions.
    /// Format: keccak256(rlp([nonce, gasPrice, gasLimit, to, value, data, chainId, 0, 0]))
    /// </summary>
    private static byte[] BuildLegacyEip155SigningHash(Transaction tx, ulong chainId)
    {
        var items = new List<byte[]>();
        items.Add(EncodeUint(tx.Nonce));
        items.Add(BigIntegerToBytes(tx.GasPrice));
        items.Add(EncodeUint(tx.GasLimit));
        items.Add(tx.To?.Bytes ?? Array.Empty<byte>());
        items.Add(BigIntegerToBytes(tx.Value));
        items.Add(tx.Data);
        items.Add(EncodeUint(chainId));
        items.Add(Array.Empty<byte>()); // 0 for r
        items.Add(Array.Empty<byte>()); // 0 for s
        
        var encoded = RlpEncodeList(items);
        return Schlieren.Core.Primitives.CryptoUtils.Keccak256(encoded);
    }

    /// <summary>
    /// Converts BigInteger to minimal big-endian bytes (no leading zeros).
    /// </summary>
    private static byte[] BigIntegerToBytes(BigInteger value)
    {
        if (value == 0) return Array.Empty<byte>();
        var bytes = value.ToByteArray();
        // Convert from little-endian to big-endian
        Array.Reverse(bytes);
        // Trim leading zeros
        int start = 0;
        while (start < bytes.Length && bytes[start] == 0) start++;
        if (start == 0) return bytes;
        var result = new byte[bytes.Length - start];
        Buffer.BlockCopy(bytes, start, result, 0, result.Length);
        return result;
    }

    /// <summary>
    /// Encodes an unsigned integer as big-endian minimal bytes.
    /// </summary>
    private static byte[] EncodeUint(ulong value)
    {
        if (value == 0) return Array.Empty<byte>();
        var bytes = BitConverter.GetBytes(value);
        // Convert from little-endian to big-endian
        Array.Reverse(bytes);
        // Trim leading zeros
        int start = 0;
        while (start < bytes.Length - 1 && bytes[start] == 0) start++;
        if (start == 0) return bytes;
        var result = new byte[bytes.Length - start];
        Buffer.BlockCopy(bytes, start, result, 0, result.Length);
        return result;
    }

    /// <summary>
    /// RLP encodes a list of byte arrays.
    /// </summary>
    private static byte[] RlpEncodeList(List<byte[]> items)
    {
        if (items.Count == 0) return new byte[] { 0xc0 };
        
        var encodedItems = items.Select(item => RlpEncodeItem(item)).ToArray();
        var totalLen = encodedItems.Sum(i => i.Length);
        
        if (totalLen <= 55)
        {
            var result = new byte[1 + totalLen];
            result[0] = (byte)(0xc0 + totalLen);
            int pos = 1;
            foreach (var item in encodedItems)
            {
                Buffer.BlockCopy(item, 0, result, pos, item.Length);
                pos += item.Length;
            }
            return result;
        }
        else
        {
            var lenBytes = GetLenBytes(totalLen);
            var result = new byte[1 + lenBytes.Length + totalLen];
            result[0] = (byte)(0xf7 + lenBytes.Length);
            Buffer.BlockCopy(lenBytes, 0, result, 1, lenBytes.Length);
            int pos = 1 + lenBytes.Length;
            foreach (var item in encodedItems)
            {
                Buffer.BlockCopy(item, 0, result, pos, item.Length);
                pos += item.Length;
            }
            return result;
        }
    }

    /// <summary>
    /// RLP encodes a single item.
    /// </summary>
    private static byte[] RlpEncodeItem(byte[] item)
    {
        if (item == null || item.Length == 0)
            return new byte[] { 0x80 };
        if (item.Length == 1 && item[0] < 0x80)
            return item;
        
        if (item.Length <= 55)
        {
            var result = new byte[1 + item.Length];
            result[0] = (byte)(0x80 + item.Length);
            Buffer.BlockCopy(item, 0, result, 1, item.Length);
            return result;
        }
        else
        {
            var lenBytes = GetLenBytes(item.Length);
            var result = new byte[1 + lenBytes.Length + item.Length];
            result[0] = (byte)(0xb7 + lenBytes.Length);
            Buffer.BlockCopy(lenBytes, 0, result, 1, lenBytes.Length);
            Buffer.BlockCopy(item, 0, result, 1 + lenBytes.Length, item.Length);
            return result;
        }
    }

    /// <summary>
    /// Gets minimal big-endian bytes for a length value.
    /// </summary>
    private static byte[] GetLenBytes(int len)
    {
        if (len == 0) return Array.Empty<byte>();
        var bytes = new List<byte>();
        while (len > 0)
        {
            bytes.Insert(0, (byte)(len & 0xff));
            len >>= 8;
        }
        return bytes.ToArray();
    }

    /// <summary>
    /// Pads a byte array to 32 bytes (big-endian).
    /// </summary>
    private static byte[] PadTo32Bytes(byte[] bytes)
    {
        if (bytes.Length >= 32) return bytes;
        var result = new byte[32];
        Buffer.BlockCopy(bytes, 0, result, 32 - bytes.Length, bytes.Length);
        return result;
    }

    /// <summary>
    /// Calculates the EIP-155 V value from raw V and chainId.
    /// </summary>
    private static int CalculateEip155V(int rawV, ulong chainId)
    {
        // For EIP-155, V = chainId * 2 + 35 or chainId * 2 + 36
        // rawV is typically 27 or 28
        if (rawV < 27)
            return rawV + (int)chainId * 2 + 8; // Adjust for recovery ID
        return rawV + (int)chainId * 2 + 8;
    }

    /// <summary>
    /// Derives a deterministic private key from an address for test signing.
    /// This allows signing legacy transactions that don't have a secretKey in the fixture.
    /// </summary>
    private static EthECKey DeriveKeyFromAddress(Address sender)
    {
        // Hash the address bytes to create a deterministic private key
        var addressBytes = sender.Bytes;
        var hash = Nethereum.Util.Sha3Keccack.Current.CalculateHash(addressBytes);
        return new EthECKey(hash, true);
    }

    /// <summary>
    /// Signs a transaction with the given key and chainId.
    /// </summary>
    private static void SignTransaction(Transaction tx, EthECKey key, ulong chainId)
    {
        // Build EIP-155 signing hash: keccak256(rlp([nonce, gasPrice, gasLimit, to, value, data, chainId, 0, 0]))
        var signingHash = BuildLegacyEip155SigningHash(tx, chainId);
        tx.SigningHash = signingHash;

        // Sign with Nethereum - EthECDSASignature has R and S as byte[][]
        var signature = key.SignAndCalculateV(signingHash);

        // Convert signature components
        tx.R = PadTo32Bytes(signature.R);
        tx.S = PadTo32Bytes(signature.S);

        // V is stored as a single byte or minimal big-endian bytes, convert to int
        if (signature.V.Length == 1)
            tx.V = signature.V[0];
        else if (signature.V.Length > 1)
        {
            // Convert from big-endian
            tx.V = 0;
            for (int i = 0; i < signature.V.Length; i++)
                tx.V = (tx.V << 8) | signature.V[i];
        }
        else
            tx.V = 0;
    }

    /// <summary>
    /// Builds the block-hash lookup table from the fixture env.
    ///
    /// The v20 state-test format uses the EELS convention:
    ///   blockHash(n) = keccak256(str(n).encode('ascii'))
    /// The fixture env may carry an explicit "blockHashes" object (decimal string
    /// keys → hex hash values), or leave it absent — in the absent case we
    /// synthesise all 256 hashes on demand so BLOCKHASH always returns a
    /// deterministic non-zero value for in-window block numbers.
    /// </summary>
    private static IReadOnlyDictionary<ulong, byte[]> ParseBlockHashes(JsonElement envNode)
    {
        var map = new Dictionary<ulong, byte[]>();

        // Explicit map in fixture (older format: "blockHashes": {"1": "0x..."})
        if (envNode.TryGetProperty("blockHashes", out var bhNode) &&
            bhNode.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in bhNode.EnumerateObject())
            {
                if (ulong.TryParse(prop.Name, out var num))
                {
                    var hex = prop.Value.GetString() ?? "0x0";
                    map[num] = Convert.FromHexString(hex.Replace("0x", "").PadLeft(64, '0'));
                }
            }
            return map;
        }

        // EELS convention: synthesise keccak256(str(n)) for each ancestor.
        // Get current block number so we know the window.
        if (!envNode.TryGetProperty("currentNumber", out var numNode)) return map;
        var current = EelsHex.ParseUlong(numNode.GetString()!);
        var windowStart = current > 256 ? current - 256 : 0;

        for (var n = windowStart; n < current; n++)
        {
            var ascii = System.Text.Encoding.ASCII.GetBytes(n.ToString());
            var hash  = Nethereum.Util.Sha3Keccack.Current.CalculateHash(ascii);
            map[n] = hash;
        }

        return map;
    }
}
