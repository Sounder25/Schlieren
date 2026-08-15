using System.Text.Json;

namespace Schlieren.UI.Services;

/// <summary>
/// Parses workbench pre-state JSON into <see cref="WorkbenchAccountSeed"/> rows.
/// Accepts <c>{ "accounts": [ ... ] }</c> or a bare account array.
/// </summary>
public static class WorkbenchPrestateLoader
{
    public sealed record LoadResult(IReadOnlyList<WorkbenchAccountSeed> Accounts, string Error)
    {
        public bool Ok => string.IsNullOrEmpty(Error) && Accounts.Count > 0;
    }

    public static LoadResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new([], "Pre-state JSON is empty.");

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            JsonElement array;
            if (root.ValueKind == JsonValueKind.Array)
                array = root;
            else if (root.ValueKind == JsonValueKind.Object &&
                     root.TryGetProperty("accounts", out var acc) &&
                     acc.ValueKind == JsonValueKind.Array)
                array = acc;
            else
                return new([], "Expected { \"accounts\": [ ... ] } or a JSON array of accounts.");

            var seeds = new List<WorkbenchAccountSeed>();
            var i = 0;
            foreach (var el in array.EnumerateArray())
            {
                i++;
                if (el.ValueKind != JsonValueKind.Object)
                    return new([], $"Account #{i} is not an object.");

                var address = ReadString(el, "address", "Address", "addr");
                if (string.IsNullOrWhiteSpace(address))
                    return new([], $"Account #{i} is missing address.");

                var storage = ReadStorage(el);
                seeds.Add(new WorkbenchAccountSeed
                {
                    AddressHex = address.Trim(),
                    BalanceWei = ReadString(el, "balance", "Balance", "balanceWei") ?? "0",
                    CodeHex = ReadString(el, "code", "Code", "bytecode") ?? "",
                    Nonce = ReadUlong(el, "nonce", "Nonce"),
                    StorageHex = storage
                });
            }

            if (seeds.Count == 0)
                return new([], "No accounts in pre-state JSON.");

            return new(seeds, "");
        }
        catch (JsonException ex)
        {
            return new([], $"Invalid JSON: {ex.Message}");
        }
    }

    public static bool LooksLikePrestate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.TrimStart();
        if (t.Length == 0 || (t[0] != '{' && t[0] != '[')) return false;
        var probe = Parse(text);
        return probe.Ok;
    }

    private static string? ReadString(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (!el.TryGetProperty(n, out var p)) continue;
            return p.ValueKind switch
            {
                JsonValueKind.String => p.GetString(),
                JsonValueKind.Number => p.GetRawText(),
                _ => p.ToString()
            };
        }
        return null;
    }

    private static ulong ReadUlong(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (!el.TryGetProperty(n, out var p)) continue;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetUInt64(out var u)) return u;
            if (p.ValueKind == JsonValueKind.String &&
                ulong.TryParse(p.GetString(), out var s)) return s;
        }
        return 0;
    }

    private static IReadOnlyDictionary<string, string>? ReadStorage(JsonElement el)
    {
        if (!el.TryGetProperty("storage", out var st) &&
            !el.TryGetProperty("Storage", out st))
            return null;
        if (st.ValueKind != JsonValueKind.Object) return null;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in st.EnumerateObject())
        {
            var val = p.Value.ValueKind == JsonValueKind.String
                ? p.Value.GetString() ?? "0x0"
                : p.Value.GetRawText();
            map[p.Name] = val;
        }
        return map.Count == 0 ? null : map;
    }
}
