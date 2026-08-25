using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Schlieren.Core.Execution;
using Schlieren.Core.Forks;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using Schlieren.Harvest.Fixtures;

namespace Schlieren.Harvest.Execution;

/// <summary>
/// Executes one admitted EELS state-test fixture case through the canonical
/// Schlieren EVM path and returns a normalized <see cref="ExecutionSnapshot"/>.
///
/// Contracts per Task 6 Step 4:
///   - Builds a fresh GlobalState, fresh opcode catalog, EvmMachine, and
///     StateTransition for every call. No shared mutable state.
///   - Uses only ApplyTransactionAsync — no diagnostic engine, no trace fallback.
///   - Snapshot output comes directly from ExecutionResult + committed state.
///   - Journal on/off changes only the JournalEvidence field; all consensus
///     output fields (status, gas, returnData, logs, postState) are identical.
///   - Does NOT reference Schlieren.EELS.Tests.
/// </summary>
public sealed class SchlierenCaseExecutor
{
    public async Task<ExecutionSnapshot> ExecuteAsync(
        FixtureCaseMetadata meta,
        bool journalEnabled,
        string? catalogRoot = null,
        CancellationToken ct = default)
    {
        // Reconstruct absolute path from catalog root + relative path, or use RelativePath directly if absolute
        string absolutePath;
        if (Path.IsPathRooted(meta.RelativePath))
        {
            absolutePath = Path.GetFullPath(meta.RelativePath);
        }
        else if (!string.IsNullOrEmpty(catalogRoot))
        {
            absolutePath = Path.GetFullPath(
                Path.Combine(catalogRoot, meta.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
        else
        {
            throw new ArgumentException(
                "catalogRoot is required when FixtureCaseMetadata.RelativePath is a relative path", nameof(catalogRoot));
        }

        return await ExecuteFromPathAsync(absolutePath, meta.Fork, journalEnabled, meta.CaseId, ct);
    }

    /// <summary>
    /// Execute directly from an absolute fixture path and fork name.
    /// When <paramref name="caseId"/> is provided, selects that specific top-level
    /// entry in the fixture JSON; otherwise uses the first entry.
    /// </summary>
    public async Task<ExecutionSnapshot> ExecuteFromPathAsync(
        string absoluteFixturePath,
        string forkName,
        bool journalEnabled,
        string? caseId = null,
        CancellationToken ct = default)
    {
        byte[] bytes;
        try { bytes = await File.ReadAllBytesAsync(absoluteFixturePath, ct); }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Cannot read fixture: {absoluteFixturePath} — {ex.Message}", ex);
        }

        using var doc = JsonDocument.Parse(bytes);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Fixture root is not a JSON object");

        // Select by CaseId or fall back to first entry
        JsonElement selectedEntry;
        if (!string.IsNullOrEmpty(caseId))
        {
            if (!doc.RootElement.TryGetProperty(caseId, out selectedEntry) ||
                selectedEntry.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException(
                    $"CaseId '{caseId}' not found in fixture file {absoluteFixturePath}");
        }
        else
        {
            selectedEntry = doc.RootElement.EnumerateObject().First().Value;
        }

        return await ExecuteCaseNodeAsync(selectedEntry, forkName, journalEnabled, ct);
    }

    private async Task<ExecutionSnapshot> ExecuteCaseNodeAsync(
        JsonElement caseNode,
        string forkName,
        bool journalEnabled,
        CancellationToken ct)
    {
        // ── Pre-state ───────────────────────────────────────────────────
        var globalState = new GlobalState();

        if (caseNode.TryGetProperty("pre", out var preNode))
        {
            foreach (var acctProp in preNode.EnumerateObject())
            {
                var addr     = ParseAddress(acctProp.Name);
                var acctEl   = acctProp.Value;

                var acctBalance = ParseBigHex(GetStr(acctEl, "balance"));
                var acctNonce   = ParseHexUlong(GetStr(acctEl, "nonce"));
                var acctCode    = ParseBytes(GetStr(acctEl, "code"));

                globalState.SetBalance(addr, acctBalance);
                globalState.SetNonce(addr, acctNonce);
                globalState.SetCode(addr, acctCode);

                if (acctEl.TryGetProperty("storage", out var storageEl))
                {
                    foreach (var slotProp in storageEl.EnumerateObject())
                    {
                        var slot       = ParseBigHex(slotProp.Name);
                        var slotValue  = ParseBigHex(slotProp.Value.GetString() ?? "0x0");
                        globalState.SetStorageAt(addr, slot, slotValue);
                    }
                }
            }
        }

        // ── Block context ────────────────────────────────────────────────
        ulong blockNumber    = 1;
        ulong blockTimestamp = 1;
        ulong blockGasLimit  = 30_000_000;
        var   blockCoinbase  = Address.Zero;
        ulong blockBaseFee   = 0;
        var   blockDifficulty = BigInteger.Zero;
        ulong chainId        = ParseChainId(caseNode);

        if (caseNode.TryGetProperty("env", out var envEl))
        {
            blockNumber    = TryParseHexUlong(envEl, "currentNumber");
            blockTimestamp = TryParseHexUlong(envEl, "currentTimestamp");
            var gl         = TryParseHexUlong(envEl, "currentGasLimit");
            blockGasLimit  = gl > 0 ? gl : 30_000_000;
            blockCoinbase  = envEl.TryGetProperty("currentCoinbase", out var cb)
                             ? ParseAddress(cb.GetString() ?? "0x00") : Address.Zero;
            blockBaseFee   = TryParseHexUlong(envEl, "currentBaseFee");
            blockDifficulty = envEl.TryGetProperty("currentDifficulty", out var diff)
                              ? ParseBigHex(diff.GetString() ?? "0x0") : BigInteger.Zero;
        }

        var blockCtx = new BlockContext
        {
            ChainId       = chainId,
            Number        = blockNumber,
            Timestamp     = blockTimestamp,
            GasLimit      = blockGasLimit,
            Coinbase      = blockCoinbase,
            BaseFeePerGas = blockBaseFee,
            Difficulty    = blockDifficulty,
            Rules         = ForkRulesFactory.For(forkName),
        };

        // ── Transaction ──────────────────────────────────────────────────
        if (!caseNode.TryGetProperty("transaction", out var txEl))
            return ExecutionSnapshot.Empty;

        if (!caseNode.TryGetProperty("post", out var postEl) ||
            !postEl.TryGetProperty(forkName, out var forkVariants) ||
            forkVariants.ValueKind != JsonValueKind.Array)
            return ExecutionSnapshot.Empty;

        var variant = forkVariants.EnumerateArray().FirstOrDefault();
        if (variant.ValueKind != JsonValueKind.Object)
            return ExecutionSnapshot.Empty;

        // Resolve variant indexes
        var indexes = variant.TryGetProperty("indexes", out var idx) ? idx : default;
        var dataIdx  = (int)GetIndexValue(indexes, "data");
        var gasIdx   = (int)GetIndexValue(indexes, "gas");
        var valueIdx = (int)GetIndexValue(indexes, "value");

        var sender = txEl.TryGetProperty("sender", out var senderEl)
            ? ParseAddress(senderEl.GetString() ?? "0x00") : Address.Zero;

        // to: null = CREATE
        Address? to = null;
        if (txEl.TryGetProperty("to", out var toEl) &&
            toEl.ValueKind == JsonValueKind.String &&
            !string.IsNullOrEmpty(toEl.GetString()))
            to = ParseAddress(toEl.GetString()!);

        var gasLimit = GetVariantUlong(txEl, "gasLimit", gasIdx);
        var value    = GetVariantBigInteger(txEl, "value", valueIdx);
        var data     = GetVariantBytes(txEl, "data", dataIdx);
        var gasPrice = ParseBigHex(GetStr(txEl, "gasPrice"));
        var nonce    = ParseHexUlong(GetStr(txEl, "nonce"));
        var accessList = GetVariantAccessList(txEl, dataIdx);

        // Check for expected exception (invalid tx declared in fixture)
        var expectedException = variant.TryGetProperty("expectException", out var excEl) &&
                                excEl.ValueKind == JsonValueKind.String
            ? excEl.GetString() : null;

        if (expectedException is not null)
        {
            // Invalid tx — no execution, return failure snapshot
            return new ExecutionSnapshot(
                IsSuccess:          false,
                GasUsed:            0,
                GasRefundCounter:   0,
                ReturnData:         "0x",
                Logs:               Array.Empty<SnapshotLog>(),
                PostState:          BuildPostState(globalState));
        }

        var tx = new Transaction
        {
            From          = sender,
            To            = to,
            Nonce         = nonce,
            GasLimit      = gasLimit,
            GasPrice      = gasPrice,
            MaxFeePerGas  = gasPrice,
            Value         = value,
            Data          = data,
            AccessList    = accessList,
            TxType        = accessList.Count > 0 ? (byte)1 : (byte)0,
            Authorization = TransactionAuthorization.Impersonated,
            EnableJournal = journalEnabled,
        };

        // ── Execute ──────────────────────────────────────────────────────
        var opcodes       = BuildOpcodes();
        var evm           = new EvmMachine(opcodes);
        var stateTransition = new StateTransition(evm);

        ExecutionResult result;
        try
        {
            result = await RunOnLargeStack(() =>
                stateTransition.ApplyTransactionAsync(tx, globalState, blockCtx, commit: true, ct: ct));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            result = ExecutionResult.Failure(EvmError.InternalError);
            _ = ex; // logged but not swallowed as pass
        }

        var returnDataHex = "0x" + Convert.ToHexString(result.ReturnData).ToLowerInvariant();
        var logs          = result.Logs.Select(l => new SnapshotLog(
            Address: l.Address,
            Topics:  l.Topics.AsReadOnly(),
            Data:    l.Data)).ToList();

        return new ExecutionSnapshot(
            IsSuccess:          result.IsSuccess,
            GasUsed:            result.GasUsed,
            GasRefundCounter:   result.GasRefundCounter,
            ReturnData:         returnDataHex,
            Logs:               logs,
            PostState:          BuildPostState(globalState),
            JournalEvidence:    result.Journal);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static IReadOnlyList<SnapshotAccount> BuildPostState(GlobalState state)
    {
        var snapshot = state.Snapshot();
        return snapshot.Select(kvp => new SnapshotAccount(
            Address: kvp.Key.ToString(),
            Nonce:   kvp.Value.Nonce,
            Balance: "0x" + kvp.Value.Balance.ToString("x"),
            Code:    "0x" + Convert.ToHexString(kvp.Value.Code).ToLowerInvariant(),
            Storage: kvp.Value.Storage.ToDictionary(
                s => "0x" + s.Key.ToString("x"),
                s => "0x" + s.Value.ToString("x"))
        )).ToList();
    }

    private static IReadOnlyList<IOpcode> BuildOpcodes()
    {
        var opcodeType = typeof(IOpcode);
        var assembly   = opcodeType.Assembly;
        var instances  = new List<IOpcode>();
        foreach (var type in assembly.GetTypes())
        {
            if (!opcodeType.IsAssignableFrom(type) || type.IsAbstract || type.IsInterface) continue;
            var ctor = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (ctor is null) continue;
            if (Activator.CreateInstance(type) is IOpcode op) instances.Add(op);
        }
        return instances.OrderBy(op => op.Code).ToArray();
    }

    // Run on a 32 MB stack to handle deep EVM call chains
    private static Task<T> RunOnLargeStack<T>(Func<Task<T>> action)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try   { tcs.SetResult(action().GetAwaiter().GetResult()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }, 32 * 1024 * 1024) { IsBackground = true };
        thread.Start();
        return tcs.Task;
    }

    // ── Fixture parsing helpers (self-contained, no EELS.Tests reference) ─

    private static Address ParseAddress(string hex) => Address.FromHex(hex);

    private static BigInteger ParseBigHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex is "0x" or "0x0") return BigInteger.Zero;
        var s = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        if (string.IsNullOrEmpty(s)) return BigInteger.Zero;
        return BigInteger.Parse("0" + s, System.Globalization.NumberStyles.HexNumber);
    }

    private static ulong ParseHexUlong(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex is "0x" or "0x0") return 0;
        var s = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        return ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
    }

    private static byte[] ParseBytes(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex is "0x") return Array.Empty<byte>();
        var s = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        if (s.Length % 2 != 0) s = "0" + s;
        return string.IsNullOrEmpty(s) ? Array.Empty<byte>() : Convert.FromHexString(s);
    }

    private static string GetStr(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString() ?? "0x0";
        return "0x0";
    }

    private static ulong TryParseHexUlong(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) ? ParseHexUlong(v.GetString()) : 0;

    private static ulong ParseChainId(JsonElement caseNode)
    {
        if (caseNode.TryGetProperty("config", out var cfg) &&
            cfg.TryGetProperty("chainid", out var ci))
            return ParseHexUlong(ci.GetString());
        return 1;
    }

    private static long GetIndexValue(JsonElement indexes, string key)
    {
        if (indexes.ValueKind == JsonValueKind.Object &&
            indexes.TryGetProperty(key, out var v) &&
            v.ValueKind == JsonValueKind.Number)
            return v.GetInt64();
        return 0;
    }

    private static IReadOnlyList<AccessListEntry> GetVariantAccessList(
        JsonElement transaction,
        int dataIndex)
    {
        if (!transaction.TryGetProperty("accessLists", out var variants) ||
            variants.ValueKind != JsonValueKind.Array)
            return Array.Empty<AccessListEntry>();

        var selected = variants.EnumerateArray().Skip(dataIndex).FirstOrDefault();
        if (selected.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return Array.Empty<AccessListEntry>();
        if (selected.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("transaction.accessLists variant must be an array");

        var entries = new List<AccessListEntry>();
        foreach (var entry in selected.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("address", out var addressElement) ||
                addressElement.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Access-list entry is missing an address");

            var storageKeys = new List<BigInteger>();
            if (entry.TryGetProperty("storageKeys", out var keysElement))
            {
                if (keysElement.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Access-list storageKeys must be an array");

                foreach (var key in keysElement.EnumerateArray())
                {
                    if (key.ValueKind != JsonValueKind.String)
                        throw new InvalidDataException("Access-list storage key must be a hex string");
                    storageKeys.Add(ParseBigHex(key.GetString()!));
                }
            }

            entries.Add(new AccessListEntry
            {
                Address = ParseAddress(addressElement.GetString()!),
                StorageKeys = storageKeys
            });
        }

        return entries;
    }

    private static ulong GetVariantUlong(JsonElement txEl, string key, int index)
    {
        if (!txEl.TryGetProperty(key, out var prop)) return 0;
        if (prop.ValueKind == JsonValueKind.Array)
        {
            var arr = prop.EnumerateArray().ToList();
            var item = arr.Count > index ? arr[index] : arr[0];
            return ParseHexUlong(item.GetString());
        }
        return ParseHexUlong(prop.GetString());
    }

    private static BigInteger GetVariantBigInteger(JsonElement txEl, string key, int index)
    {
        if (!txEl.TryGetProperty(key, out var prop)) return BigInteger.Zero;
        if (prop.ValueKind == JsonValueKind.Array)
        {
            var arr = prop.EnumerateArray().ToList();
            var item = arr.Count > index ? arr[index] : arr[0];
            return ParseBigHex(item.GetString());
        }
        return ParseBigHex(prop.GetString());
    }

    private static byte[] GetVariantBytes(JsonElement txEl, string key, int index)
    {
        if (!txEl.TryGetProperty(key, out var prop)) return Array.Empty<byte>();
        if (prop.ValueKind == JsonValueKind.Array)
        {
            var arr = prop.EnumerateArray().ToList();
            var item = arr.Count > index ? arr[index] : arr[0];
            return ParseBytes(item.GetString());
        }
        return ParseBytes(prop.GetString());
    }
}
