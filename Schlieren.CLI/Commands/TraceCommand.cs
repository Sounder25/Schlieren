using System.Text;
using System.Text.Json;

namespace Schlieren.CLI.Commands;

/// <summary>
/// Replays a transaction-shaped call through <c>schlieren_traceJournal</c> and renders
/// the server-derived exclusive gas tree. No frame or gas semantics are reconstructed
/// from legacy struct logs in the CLI.
/// </summary>
public static class TraceCommand
{
    public static async Task<int> RunAsync(string txHash, string? rpcUrl, string? workdir)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (!txHash.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            txHash = "0x" + txHash;

        var resolvedRpc = rpcUrl ?? "http://127.0.0.1:8545";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        JsonElement tx;
        try
        {
            tx = await RpcCallAsync(http, resolvedRpc, "eth_getTransactionByHash", txHash);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗  Cannot reach node at {resolvedRpc}: {ex.Message}");
            Console.Error.WriteLine("   Start the node first with: schlieren node");
            return 1;
        }

        if (tx.ValueKind == JsonValueKind.Null)
        {
            Console.Error.WriteLine($"✗  Transaction not found: {txHash}");
            return 1;
        }
        if (!TryString(tx, "to", out var to))
        {
            Console.Error.WriteLine("✗  Journal replay of contract-creation transactions is not yet supported by this command.");
            return 1;
        }

        var request = new Dictionary<string, object?>
        {
            ["to"] = to,
            ["disableStack"] = true,
            ["disableMemory"] = true,
            ["disableStorage"] = true
        };
        CopyString(tx, request, "from", "from");
        CopyString(tx, request, "gas", "gas");
        CopyString(tx, request, "gasPrice", "gasPrice");
        CopyString(tx, request, "value", "value");
        CopyString(tx, request, "input", "data");

        JsonElement journal;
        try
        {
            journal = await RpcCallAsync(http, resolvedRpc, "schlieren_traceJournal", request);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗  schlieren_traceJournal failed: {ex.Message}");
            return 1;
        }

        if (!journal.TryGetProperty("gasTree", out var gasTree))
        {
            Console.Error.WriteLine("✗  Journal response missing 'gasTree'.");
            return 1;
        }

        Console.WriteLine($"Journal replay for {txHash}");
        Console.WriteLine("Note: this is a non-persisting replay against the RPC server's current state, not a historical block-state proof.");
        Console.Write(RenderJournalGasTree(gasTree));
        if (journal.TryGetProperty("conservation", out var conservation))
        {
            var conserved = conservation.TryGetProperty("isConserved", out var flag) && flag.GetBoolean();
            var derived = Number(conservation, "derivedGas");
            var settled = Number(conservation, "settledGas");
            Console.WriteLine(conserved
                ? $"✓ conserved: {derived:N0} derived / {settled:N0} settled"
                : $"✗ conservation drift: {derived:N0} derived / {settled:N0} settled");
        }
        return 0;
    }

    internal static string RenderJournalGasTree(JsonElement root)
    {
        var output = new StringBuilder();
        RenderNode(root, output, "", true, isRoot: true);
        return output.ToString();
    }

    private static void RenderNode(JsonElement node, StringBuilder output, string prefix, bool isLast, bool isRoot)
    {
        var label = node.TryGetProperty("label", out var labelElement) ? labelElement.GetString() ?? "?" : "?";
        var total = Number(node, "totalGas");
        var amount = Number(node, "amount");
        var effect = node.TryGetProperty("effect", out var effectElement) ? effectElement.GetString() ?? "none" : "none";
        var marker = effect switch { "charge" => "+", "credit" => "−", _ => "≈" };
        output.Append(prefix);
        if (!isRoot) output.Append(isLast ? "└── " : "├── ");
        output.Append(label).Append(": ").Append(marker).Append(amount.ToString("N0"));
        if (total != amount) output.Append("  Σ ").Append(total.ToString("N0"));
        output.AppendLine();

        if (!node.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array)
            return;
        var childArray = children.EnumerateArray().ToArray();
        for (var index = 0; index < childArray.Length; index++)
            RenderNode(childArray[index], output, prefix + (isRoot ? "" : isLast ? "    " : "│   "), index == childArray.Length - 1, isRoot: false);
    }

    private static ulong Number(JsonElement value, string property) =>
        value.TryGetProperty(property, out var element) && element.TryGetUInt64(out var number) ? number : 0;

    private static bool TryString(JsonElement source, string property, out string value)
    {
        value = string.Empty;
        return source.TryGetProperty(property, out var element)
            && element.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value = element.GetString() ?? string.Empty);
    }

    private static void CopyString(JsonElement source, IDictionary<string, object?> target, string sourceName, string targetName)
    {
        if (TryString(source, sourceName, out var value)) target[targetName] = value;
    }

    private static async Task<JsonElement> RpcCallAsync(
        HttpClient http, string rpcUrl, string method, params object?[] rpcParams)
    {
        var payload = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = rpcParams });
        var response = await http.PostAsync(rpcUrl, new StringContent(payload, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error))
            throw new InvalidOperationException(error.TryGetProperty("message", out var message) ? message.GetString() : "RPC error");
        return root.TryGetProperty("result", out var result) ? result.Clone() : default;
    }
}
