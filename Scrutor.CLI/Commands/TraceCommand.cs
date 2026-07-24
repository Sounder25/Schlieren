using System.Numerics;
using System.Text.Json;
using Scrutor.Core.Execution;
using Scrutor.Core.Primitives;

namespace Scrutor.CLI.Commands;

/// <summary>
/// scrutor trace &lt;txhash&gt; [--rpc http://…]
///
/// Fetches the debug trace for a committed transaction and renders the gas
/// causality tree — a hierarchical breakdown showing exactly where every unit
/// of gas went, per call frame and per opcode group.
///
/// Example output:
///   Transaction 0xabc… : 48,321 gas
///   ├── Intrinsic transaction cost: 21,000
///   ├── Calldata: 320
///   ├── Contract 0x1000… execution: 26,901
///   │   ├── Cold SLOAD: 2,100
///   │   ├── SSTORE (cold slot): 20,000
///   │   ├── PUSH32 × 14: 42
///   │   └── ...
///   └── Unused gas returned: 100
/// </summary>
public static class TraceCommand
{
    public static async Task<int> RunAsync(string txHash, string? rpcUrl, string? workdir)
    {
        // Ensure box-drawing characters render correctly on Windows console
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Normalise tx hash
        if (!txHash.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            txHash = "0x" + txHash;

        // If no RPC supplied, find a running node or bail clearly
        var resolvedRpc = rpcUrl ?? "http://127.0.0.1:8545";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // ── 1. Fetch transaction metadata (gasLimit, gasPrice, input) ─────────
        JsonElement txObj;
        try
        {
            txObj = await RpcCallAsync(http, resolvedRpc, "eth_getTransactionByHash", txHash);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗  Cannot reach node at {resolvedRpc}: {ex.Message}");
            Console.Error.WriteLine("   Start the node first with: scrutor node");
            return 1;
        }

        if (txObj.ValueKind == JsonValueKind.Null)
        {
            Console.Error.WriteLine($"✗  Transaction not found: {txHash}");
            return 1;
        }

        var gasLimit  = ParseHexUlong(txObj.GetProperty("gas").GetString() ?? "0x0");
        var gasPrice  = ParseHexUlong(txObj.GetProperty("gasPrice").GetString() ?? "0x0");
        var inputHex  = txObj.GetProperty("input").GetString() ?? "0x";
        var toField   = txObj.TryGetProperty("to", out var toEl) && toEl.ValueKind != JsonValueKind.Null
                        ? toEl.GetString() ?? "" : "";

        // Compute calldata gas (4 per zero byte, 16 per non-zero byte)
        var inputBytes  = HexToBytes(inputHex);
        ulong calldataGas = 0;
        foreach (var b in inputBytes) calldataGas += b == 0 ? 4UL : 16UL;

        // Intrinsic: 21000 base + calldata (simplified; ignores access list / CREATE surcharge)
        ulong intrinsicGas = 21_000 + calldataGas;

        // ── 2. Fetch receipt (actual gas used) ────────────────────────────────
        JsonElement receipt;
        try { receipt = await RpcCallAsync(http, resolvedRpc, "eth_getTransactionReceipt", txHash); }
        catch { receipt = default; }

        ulong gasUsed = 0;
        if (receipt.ValueKind != JsonValueKind.Null && receipt.ValueKind != JsonValueKind.Undefined)
        {
            if (receipt.TryGetProperty("gasUsed", out var guEl))
                gasUsed = ParseHexUlong(guEl.GetString() ?? "0x0");
        }

        // ── 3. Fetch opcode trace ─────────────────────────────────────────────
        JsonElement traceResult;
        try
        {
            traceResult = await RpcCallAsync(http, resolvedRpc, "debug_traceTransaction",
                txHash, new { disableStack = true, disableMemory = true, disableStorage = true });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗  debug_traceTransaction failed: {ex.Message}");
            return 1;
        }

        if (!traceResult.TryGetProperty("structLogs", out var structLogs))
        {
            Console.Error.WriteLine("✗  Trace response missing 'structLogs'");
            return 1;
        }

        // ── 4. Build frame tree from flat structLogs (keyed on depth changes) ─
        var rootFrame = BuildFrameTree(structLogs, toField);

        // ── 5. Build and render gas tree ──────────────────────────────────────
        var builder = new GasTreeBuilder();
        var txLabel = $"Transaction {txHash[..Math.Min(18, txHash.Length)]}…";
        var tree = builder.Build(txLabel, gasLimit, intrinsicGas, calldataGas, rootFrame);

        // Override the root's displayed total with the actual gasUsed from receipt if available
        if (gasUsed > 0)
            tree.Label = $"Transaction {txHash[..Math.Min(18, txHash.Length)]}…  [{gasUsed:N0} gas used]";

        Console.WriteLine(GasTreeRenderer.Render(tree));
        return 0;
    }

    // ── Frame-tree builder ────────────────────────────────────────────────────

    /// <summary>
    /// Reconstructs a call-frame tree from a flat structLogs array.
    /// Each entry has a 'depth' field. When depth increases, we enter a sub-call;
    /// when it decreases, we return to the parent.
    /// </summary>
    private static GasFrameNode BuildFrameTree(JsonElement structLogs, string rootToAddress)
    {
        var root = new GasFrameNode { Label = string.IsNullOrEmpty(rootToAddress)
            ? "root" : $"Contract {rootToAddress} execution" };

        // Stack of active frames; frame at index 0 = root.
        var stack = new Stack<GasFrameNode>();
        stack.Push(root);

        int prevDepth = 1;

        foreach (var step in structLogs.EnumerateArray())
        {
            var op       = step.TryGetProperty("op", out var opEl)      ? opEl.GetString() ?? "" : "";
            ulong gasCost;
            if (step.TryGetProperty("gasCost", out var gcEl))
            {
                // gasCost may be a JSON number or a hex string
                gasCost = gcEl.ValueKind == JsonValueKind.Number
                    ? gcEl.GetUInt64()
                    : ParseHexUlong(gcEl.GetString() ?? "0x0");
            }
            else gasCost = 0UL;
            var depth    = step.TryGetProperty("depth", out var dEl)    ? dEl.GetInt32() : 1;

            // Depth increased → entered a sub-call on the PREVIOUS step's CALL opcode.
            // The sub-call frame was already pushed when we saw the CALL opcode.
            // Depth decreased → returned from sub-call; pop back to parent.
            while (depth < prevDepth && stack.Count > 1)
            {
                stack.Pop();
                prevDepth--;
            }

            var current = stack.Peek();

            // Is this step a CALL that will spawn a child frame?
            bool isCallOpcode = IsCallOpcode(op);
            if (isCallOpcode)
            {
                // Extract callee address from stack if present
                var calleeAddr = TryGetCalleeAddress(step);
                var childLabel = $"{op} → {(calleeAddr != null ? calleeAddr : "?")}";
                var child = new GasFrameNode { Label = childLabel };
                current.Children.Add(child);

                // The cold-address surcharge is charged in the CALL opcode itself;
                // record it against the parent frame (it IS the CALL opcode cost here).
                if (gasCost > 0)
                    current.OpcodeSteps.Add((op, gasCost));

                // Push child so subsequent steps at depth+1 land in it
                stack.Push(child);
                prevDepth = depth + 1;
            }
            else
            {
                if (gasCost > 0)
                    current.OpcodeSteps.Add((op, gasCost));

                prevDepth = depth;
            }
        }

        return root;
    }

    private static bool IsCallOpcode(string op) =>
        op is "CALL" or "DELEGATECALL" or "STATICCALL" or "CALLCODE" or "CREATE" or "CREATE2";

    private static string? TryGetCalleeAddress(JsonElement step)
    {
        // structLogs don't include the stack by default when disableStack=true.
        // Return null — labels will show the opcode type.
        return null;
    }

    // ── JSON-RPC helpers ──────────────────────────────────────────────────────

    private static async Task<JsonElement> RpcCallAsync(
        HttpClient http, string rpcUrl, string method, params object?[] rpcParams)
    {
        var payload = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id      = 1,
            method,
            @params = rpcParams
        });

        var response = await http.PostAsync(rpcUrl,
            new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var doc  = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var err))
            throw new Exception(err.TryGetProperty("message", out var m) ? m.GetString() : "RPC error");

        return root.TryGetProperty("result", out var res) ? res : default;
    }

    private static ulong ParseHexUlong(string hex)
    {
        hex = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        if (string.IsNullOrEmpty(hex)) return 0;
        return Convert.ToUInt64(hex, 16);
    }

    private static byte[] HexToBytes(string hex)
    {
        hex = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        if (string.IsNullOrEmpty(hex)) return Array.Empty<byte>();
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }
}
