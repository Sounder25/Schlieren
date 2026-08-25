using System.Text.Json;
using Schlieren.Harvest.Domain;

namespace Schlieren.Harvest.Execution;

/// <summary>
/// Builds an ExecutionSnapshot from the EELS fixture's expected post-state data.
/// This is the "fixture post-state authority" for comparison — the independent
/// expected values declared in the fixture file, not Schlieren output.
///
/// Extracts from post[fork][variant]:
///   - receipt.status → IsSuccess
///   - receipt.cumulativeGasUsed → GasUsed
///   - state → PostState (accounts + storage)
///
/// These are the comparison fields declared by the storage-lifecycle campaign.
/// Logs and returnData are taken from the fixture when present; when absent they
/// remain empty (not guessed) and the comparator leaves them out of the delta set.
/// </summary>
public static class FixtureSnapshotBuilder
{
    /// <summary>
    /// Builds an expected ExecutionSnapshot from the first variant of the given fork
    /// in the fixture file. Returns null with a reason string if the fixture cannot
    /// supply the required fields (callers map this to FixtureInvalid or HarnessError).
    /// </summary>
    public static (ExecutionSnapshot? Snapshot, string? Error) Build(
        string absoluteFixturePath,
        string forkName)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(absoluteFixturePath); }
        catch (Exception ex)
        {
            return (null, $"Cannot read fixture: {ex.Message}");
        }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(bytes); }
        catch (JsonException ex)
        {
            return (null, $"Fixture is not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return (null, "Fixture root is not a JSON object");

            // Take first test entry
            var entry = doc.RootElement.EnumerateObject().FirstOrDefault();
            if (entry.Value.ValueKind != JsonValueKind.Object)
                return (null, "No test entries in fixture");

            var caseNode = entry.Value;
            if (!caseNode.TryGetProperty("post", out var postNode) ||
                !postNode.TryGetProperty(forkName, out var forkArray) ||
                forkArray.ValueKind != JsonValueKind.Array)
                return (null, $"Fixture has no post[{forkName}]");

            var variant = forkArray.EnumerateArray().FirstOrDefault();
            if (variant.ValueKind != JsonValueKind.Object)
                return (null, "post[fork] has no variants");

            // Status authority — required
            if (!variant.TryGetProperty("receipt", out var receipt) ||
                !receipt.TryGetProperty("status", out var statusProp))
                return (null, "Fixture missing receipt.status (MissingStatusAuthority)");

            bool isSuccess = statusProp.ValueKind == JsonValueKind.True ||
                             (statusProp.ValueKind == JsonValueKind.Number && statusProp.GetInt32() != 0) ||
                             (statusProp.ValueKind == JsonValueKind.String && statusProp.GetString() != "0x0");

            // Gas authority — required
            if (!receipt.TryGetProperty("cumulativeGasUsed", out var gasProp))
                return (null, "Fixture missing receipt.cumulativeGasUsed (MissingGasAuthority)");

            var gasStr = gasProp.GetString() ?? "0x0";
            var gasUsed = ParseHexUlong(gasStr);

            // Post-state (storage + account fields)
            var postAccounts = new List<SnapshotAccount>();
            if (variant.TryGetProperty("state", out var stateNode) &&
                stateNode.ValueKind == JsonValueKind.Object)
            {
                foreach (var acctProp in stateNode.EnumerateObject())
                {
                    var addr = acctProp.Name;
                    var acctEl = acctProp.Value;

                    var nonce   = ParseNonce(acctEl);
                    var balance = GetStr(acctEl, "balance") ?? "0x0";
                    var code    = GetStr(acctEl, "code") ?? "0x";
                    var storage = new Dictionary<string, string>();

                    if (acctEl.TryGetProperty("storage", out var storEl) &&
                        storEl.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var slotProp in storEl.EnumerateObject())
                            storage[slotProp.Name] = slotProp.Value.GetString() ?? "0x0";
                    }

                    postAccounts.Add(new SnapshotAccount(addr, nonce, balance, code, storage));
                }
            }

            // Return data — optional, empty when absent
            var returnData = "0x";

            // Logs — optional, empty when absent (logs hash only in fixture, not parsed entries)
            var logs = new List<SnapshotLog>();

            var snapshot = new ExecutionSnapshot(
                IsSuccess:          isSuccess,
                GasUsed:            gasUsed,
                GasRefundCounter:   0,   // refund counter not in fixture; comparator skips if both are 0
                ReturnData:         returnData,
                Logs:               logs,
                PostState:          postAccounts);

            return (snapshot, null);
        }
    }

    private static ulong ParseHexUlong(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex is "0x" or "0x0") return 0;
        var s = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        return ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
    }

    private static ulong ParseNonce(JsonElement el)
    {
        if (el.TryGetProperty("nonce", out var v))
            return ParseHexUlong(v.GetString());
        return 0;
    }

    private static string? GetStr(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString();
        return null;
    }
}
