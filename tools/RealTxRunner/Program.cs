using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Schlieren.Core.Execution;
using Schlieren.Core.Forks;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

if (args.Length < 1) { Console.Error.WriteLine("usage: RealTxRunner <fixture.json> [--fork Osaka]"); return 1; }

var fixturePath = args[0];
var forkName = "Osaka"; // safe default; override with --fork
for (int i = 1; i < args.Length - 1; i++)
    if (args[i] == "--fork") forkName = args[i + 1];

// ── Load all IOpcode implementations by reflection (mirrors EELS.Tests OpcodeCatalog) ──
static IReadOnlyList<IOpcode> CreateAllOpcodes()
{
    var opcodeType = typeof(IOpcode);
    var assembly   = opcodeType.Assembly;
    var instances  = new List<IOpcode>();
    foreach (var type in assembly.GetTypes())
    {
        if (!opcodeType.IsAssignableFrom(type) || type.IsAbstract || type.IsInterface) continue;
        var ctor = type.GetConstructor(
            BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
        if (ctor is null) continue;
        if (Activator.CreateInstance(type) is IOpcode op) instances.Add(op);
    }
    return instances.OrderBy(op => op.Code).ToArray();
}

// ── Helpers ───────────────────────────────────────────────────────────────────
static BigInteger Big(string? h)
{
    if (string.IsNullOrEmpty(h) || h == "0x") return BigInteger.Zero;
    var hex = h.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? h[2..] : h;
    if (string.IsNullOrEmpty(hex)) return BigInteger.Zero;
    // Always prepend 0 to ensure BigInteger parses as unsigned positive
    return BigInteger.Parse("0" + hex, System.Globalization.NumberStyles.HexNumber);
}

static ulong U64(string? h)
{
    if (string.IsNullOrEmpty(h) || h == "0x") return 0UL;
    var hex = h.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? h[2..] : h;
    if (string.IsNullOrEmpty(hex)) return 0UL;
    return Convert.ToUInt64(hex, 16);
}
static byte[] Bytes(string? h) => h is null or "0x" ? Array.Empty<byte>() : Convert.FromHexString(h[2..]);

// ── Parse fixture ─────────────────────────────────────────────────────────────
var doc   = JsonDocument.Parse(File.ReadAllText(fixturePath));
var caseEl = doc.RootElement.EnumerateObject().First();
var root  = caseEl.Value;
var caseId = caseEl.Name;

Console.WriteLine($"Case: {caseId}");

var env = root.GetProperty("env");

// Determine fork from fixture block context (post-Cancun = Osaka for now)
// The fixture was captured at a real mainnet block — use the requested fork.
var rules = ForkRulesFactory.For(forkName);

var block = new BlockContext
{
    Number       = U64(env.GetProperty("currentNumber").GetString()),
    Timestamp    = U64(env.GetProperty("currentTimestamp").GetString()),
    GasLimit     = U64(env.GetProperty("currentGasLimit").GetString()),
    BaseFeePerGas = U64(env.GetProperty("currentBaseFee").GetString()),
    Coinbase     = Address.FromHex(env.GetProperty("currentCoinbase").GetString()!),
    ExcessBlobGas = U64(env.TryGetProperty("currentExcessBlobGas", out var ebg) ? ebg.GetString() : null),
    Rules        = rules,
};

// ── Load pre-state ────────────────────────────────────────────────────────────
var state = new GlobalState();
foreach (var acc in root.GetProperty("pre").EnumerateObject())
{
    var addr = Address.FromHex(acc.Name);
    var a    = acc.Value;
    state.SetNonce  (addr, U64 (a.GetProperty("nonce").GetString()));
    state.SetBalance(addr, Big (a.GetProperty("balance").GetString()));
    var code = a.GetProperty("code").GetString();
    if (!string.IsNullOrEmpty(code) && code != "0x")
        state.SetCode(addr, Bytes(code));

    if (a.TryGetProperty("storage", out var storageProp))
    {
        foreach (var slot in storageProp.EnumerateObject())
        {
            var key = Big(slot.Name);
            var val = Big(slot.Value.GetString());
            if (val != BigInteger.Zero)
                state.SetStorageAt(addr, key, val);
        }
    }
}

// ── Build transaction ─────────────────────────────────────────────────────────
var tx = root.GetProperty("transaction");

// value/data/gasLimit may be arrays (EELS post-state format allows multiple post-states)
static string? FirstOrStr(JsonElement el) =>
    el.ValueKind == JsonValueKind.Array ? el[0].GetString() : el.GetString();

var txn = new Transaction
{
    From      = Address.FromHex(tx.GetProperty("sender").GetString()!),
    To        = tx.TryGetProperty("to", out var toEl) && toEl.ValueKind == JsonValueKind.String
                ? Address.FromHex(toEl.GetString()!) : null,
    Value     = Big(FirstOrStr(tx.GetProperty("value"))),
    Nonce     = U64(tx.GetProperty("nonce").GetString()),
    GasLimit  = U64(FirstOrStr(tx.GetProperty("gasLimit"))),
    GasPrice  = Big(tx.TryGetProperty("gasPrice", out var gp) ? gp.GetString() : null),
    Data      = Bytes(FirstOrStr(tx.GetProperty("data"))),
    // Impersonated authorization runs the complete transaction lifecycle (intrinsic gas,
    // calldata floor, access lists, gas refunds, EVM execution) without needing raw ECDSA sig.
    Authorization = TransactionAuthorization.Impersonated,
    EnableTracing = true,
};

if (tx.TryGetProperty("maxFeePerGas", out var mf) && mf.ValueKind == JsonValueKind.String)
    txn.MaxFeePerGas = Big(mf.GetString());
if (tx.TryGetProperty("maxPriorityFeePerGas", out var mp) && mp.ValueKind == JsonValueKind.String)
    txn.MaxPriorityFeePerGas = Big(mp.GetString());
if (tx.TryGetProperty("type", out var txType))
    txn.TxType = txType.GetString() switch { "0x1" => 1, "0x2" => 2, "0x3" => 3, _ => 0 };

// ── Execute ───────────────────────────────────────────────────────────────────
var opcodes         = CreateAllOpcodes();
var stateTransition = new StateTransition(new EvmMachine(opcodes));
var result          = await stateTransition.ApplyTransactionAsync(txn, state, block, commit: false);

// ── Compare against real mainnet receipt ──────────────────────────────────────
var real       = root.GetProperty("realReceipt");
var realStatus = real.GetProperty("status").GetString();
var realGasUsed = U64(real.GetProperty("gasUsed").GetString());
var realLogs   = real.TryGetProperty("logCount", out var lc) ? lc.GetInt32() : -1;

bool statusMatch  = result.IsSuccess == (realStatus == "0x1");
long gasDelta     = (long)result.GasUsed - (long)realGasUsed;
bool gasMatch     = gasDelta == 0;
bool logsMatch    = realLogs < 0 || result.Logs.Count == realLogs;

Console.WriteLine();
Console.WriteLine("=== Schlieren replay vs real mainnet receipt ===");
Console.WriteLine($"  Fork       : {forkName}");
Console.WriteLine($"  To         : {txn.To}");
Console.WriteLine($"  Value      : {txn.Value}");
Console.ForegroundColor = statusMatch ? ConsoleColor.Green : ConsoleColor.Red;
Console.WriteLine($"  Status     : Schlieren={result.IsSuccess}  Mainnet={(realStatus == "0x1")}  {(statusMatch ? "✓ MATCH" : "✗ MISMATCH")}");
Console.ResetColor();
Console.ForegroundColor = gasMatch ? ConsoleColor.Green : ConsoleColor.Yellow;
Console.WriteLine($"  Gas used   : Schlieren={result.GasUsed}  Mainnet={realGasUsed}  delta={gasDelta:+#;-#;0}  {(gasMatch ? "✓ MATCH" : "≠ MISMATCH")}");
Console.ResetColor();
if (realLogs >= 0)
{
    Console.ForegroundColor = logsMatch ? ConsoleColor.Green : ConsoleColor.Red;
    Console.WriteLine($"  Logs       : Schlieren={result.Logs.Count}  Mainnet={realLogs}  {(logsMatch ? "✓ MATCH" : "✗ MISMATCH")}");
    Console.ResetColor();
}
if (!result.IsSuccess)
    Console.WriteLine($"  Error      : {result.Error}  data=0x{Convert.ToHexString(result.ReturnData)}");

Console.WriteLine();
bool allPass = statusMatch && gasMatch && logsMatch;
if (allPass)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("  ✓ All checks passed — Schlieren matches mainnet.");
}
else
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("  ✗ Replay diverged from mainnet.");
}
Console.ResetColor();

// Print trace summary (first + last 5 steps)
// Print trace summary (first + last 5 steps)
if (result.TraceSteps.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"  Trace: {result.TraceSteps.Count} steps");
    var show = result.TraceSteps.Count <= 10
        ? result.TraceSteps
        : result.TraceSteps.Take(5).Concat(result.TraceSteps.TakeLast(5)).ToList();
    foreach (var step in show)
        Console.WriteLine($"    [{step.Depth}] PC={step.Pc:X4} {step.Op,-14} gas={step.Gas}");
}

return allPass ? 0 : 1;
