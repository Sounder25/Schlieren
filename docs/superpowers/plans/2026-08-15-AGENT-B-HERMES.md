# Hermes assignment — Agent B (RPC inspect backend)

You are **Agent B**. You own the **JSON-RPC server only**. You do **not** invent gas formulas or change `CausalDiagnosisEngine`.

Repo: `C:\projects\Schlieren`  
Parent plan: `docs/superpowers/plans/2026-08-15-inspect-rpc-backend.md`  
Wire contract (do not rename fields): `docs/superpowers/plans/2026-08-15-inspect-result.schema.md`  
Agent A brief (your dependency): `docs/superpowers/plans/2026-08-15-AGENT-A-CORE.md`

## Your goal (one sentence)

A frontend can call JSON-RPC `debug_inspect` and get **execution + structLogs (gas per opcode) + gas tree + human diagnosis**. Existing `debug_traceTransaction` / `debug_traceCall` keep working and gain extra structLog fields.

## What already exists (do not rebuild)

| Piece | Where | Status |
|---|---|---|
| Canonical EVM | `StateTransition.ApplyTransactionAsync` | Ready |
| Per-step gas | `ExecutionTraceStep.Gas` / `GasCost` (hex) | Ready |
| Causal diagnosis | `CausalDiagnosisEngine.Analyze` | Ready **in Core**, not on RPC |
| Gas tree | `GasTreeFromTrace.FromCanonical` | Ready **in Core**, not on RPC |
| Geth traces | `debug_traceTransaction`, `debug_traceCall` | Ready, missing extras |
| Pre-exec blockers | `debug_whyNot` | Ready — **do not replace** |
| Router | `Schlieren.RPC/Server/RpcRouter.cs` | Register methods here |
| Call parser | `EthHandlers.BuildCallTransaction` | Reuse |
| RPC test pattern | `Schlieren.Tests/RPC/DebugWhyNotRpcTests.cs` | Copy `BuildFixture` style |

## Hard rules

1. Do **not** edit `Schlieren.Core/Execution/Causal/*` formulas.
2. Do **not** call `ApplyTransactionWithFrameAsync`.
3. Do **not** edit Avalonia (`Schlieren.UI`) except if a project file will not compile (it should not).
4. Do **not** change `debug_whyNot` behavior.
5. `debug_trace*` envelope stays `{ gas, failed, returnValue, structLogs }`. New structLog keys are **additive**.
6. For `debug_inspect`, after Agent A lands, call **only** `InspectionAssembler.FromCanonical`. Do not re-implement diagnosis in the handler.
7. If `InspectionAssembler` does not exist yet, finish **B1 only** and stop. Do not stub a fake diagnosis.

## How to tell Agent A is ready

These files exist and tests pass:

```
Schlieren.Core/Execution/Inspect/InspectionAssembler.cs
Schlieren.Core/Execution/Inspect/InspectRequest.cs
Schlieren.Core/Execution/Inspect/InspectDtos.cs
Schlieren.Core/Execution/Inspect/InspectMapper.cs
```

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~InspectionAssemblerTests --nologo
```

Expected: PASS. If the filter matches nothing or the project does not compile, **wait / only do B1**.

---

# B1 — Enrich `debug_trace*` structLogs (start immediately)

## Goal

Every structLog in `debug_traceCall`, `debug_traceTransaction`, and stored-trace playback includes:

- `gasCostDec` (number) — parse `GasCost` hex
- `contract` — `ExecutionTraceStep.ContractAddress` (string or null)
- `caller` — `CallerAddress`
- `callType` — `CallType?.ToString()` or null
- `output` — hex of `OutputData`, or null if empty

Existing fields unchanged: `pc`, `op`, `gas`, `gasCost`, `depth`, `stack`, `memory`, `storage`.

## Files

- Modify: `Schlieren.RPC/Handlers/EthHandlers.cs`
  - `BuildTraceResponse` (~line 1345)
  - `BuildTraceResponseFromStored` (~line 1372)
- Test: add methods to `Schlieren.Tests/RPC/DebugTraceAdvancedRpcTests.cs` **or** create `Schlieren.Tests/RPC/DebugTraceStructLogEnrichmentTests.cs`

## Implementation (both builders, same shape)

Replace the anonymous structLog projection with:

```csharp
pc = s.Pc,
op = s.Op,
gas = s.Gas,
gasCost = s.GasCost,
gasCostDec = ParseHexGasDec(s.GasCost),
depth = s.Depth,
stack = options.DisableStack ? new List<string>() : s.Stack,
memory = options.DisableMemory ? new List<string>() : s.Memory,
storage = options.DisableStorage ? new Dictionary<string, string>() : s.Storage,
contract = s.ContractAddress,
caller = s.CallerAddress,
callType = s.CallType?.ToString(),
output = s.OutputData is { Length: > 0 } ? EthereumTypes.ToEthHex(s.OutputData) : null
```

Add a private helper in `EthHandlers`:

```csharp
private static int ParseHexGasDec(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return 0;
    var s = raw.Trim();
    if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        s = s[2..];
    return int.TryParse(s, System.Globalization.NumberStyles.HexNumber,
        System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
}
```

Do **not** change `returnValue` on stored traces unless you have a real value; leave `"0x"`.

## Tests you must add

Reuse `BuildFixture` from `DebugTraceAdvancedRpcTests` (STOP/PUSH bytecode). After `debug_traceCall`:

```csharp
var log0 = result.GetProperty("structLogs")[0];
Assert.True(log0.TryGetProperty("gasCostDec", out var dec));
Assert.Equal(JsonValueKind.Number, dec.ValueKind);
Assert.True(dec.GetInt32() >= 0);
Assert.True(log0.TryGetProperty("contract", out _) || log0.TryGetProperty("caller", out _));
```

Also re-run the **existing** tests in that file. They must still pass (`op == "PUSH1"`, block traces, nested depth).

## Commands

```powershell
cd C:\projects\Schlieren
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~DebugTrace --nologo
```

Expected: all PASS.

If MSBuild says DLL locked by `Schlieren.UI` or `testhost`, stop **testhost** only, retry. Do not kill a UI the user just opened unless the copy cannot complete.

## Commit

`feat(rpc): add gasCostDec and call metadata on debug traces`

## B1 done criteria

- [ ] Both `BuildTraceResponse` methods emit the five additive fields
- [ ] Old envelope fields still present
- [ ] `dotnet test --filter FullyQualifiedName~DebugTrace` green
- [ ] `CausalDiagnosisEngine.cs` untouched

---

# B2 — `debug_inspect` (only after Agent A Step A3)

## Goal

JSON-RPC method `debug_inspect` runs one canonical tx and returns `InspectResult` as specified in the schema file.

## Files

- Modify: `Schlieren.RPC/Server/RpcRouter.cs`
  - Add `"debug_inspect"` next to `"debug_whyNot"` in the registered method set (~line 68)
  - Add `"debug_inspect" => await _ethHandlers.HandleDebugInspect(parameters, ct),` in `RouteToHandler` (~line 179)
- Modify: `Schlieren.RPC/Handlers/EthHandlers.cs` — add `HandleDebugInspect`
- Optional (if `EthHandlers.cs` is too large): create `Schlieren.RPC/Handlers/DebugInspectHandler.cs` and call it from `EthHandlers` or the router. Prefer one new method on `EthHandlers` unless the file is unreviewable.
- Test: **create** `Schlieren.Tests/RPC/DebugInspectRpcTests.cs`

## Usings to add on the handler

```csharp
using Schlieren.Core.Execution.Inspect;
using Schlieren.Core.Forks;
```

## Handler behavior (exact)

1. If `parameters` is null/empty or `parameters[0]` is not a JSON object → `RpcException` `InvalidParams`, message `"Expected inspect object"`.
2. Parse `parameters[0]` as `JsonElement obj`.
3. Build `BlockContext`:
   - Start from existing `BuildCurrentBlockContext()`.
   - If `obj` has `"fork"` string (e.g. `"Frontier"`), construct a **new** `BlockContext` copying Number/Timestamp/GasLimit/Coinbase/ChainId/BaseFeePerGas and set `Rules = ForkRulesFactory.For(forkName)`.
   - `BlockContext` is a class with `init` properties — copy fields; you cannot `with` unless you add a helper.
4. `var tx = BuildCallTransaction(obj, block.GasLimit);`
   - `tx.EnableTracing = true;`
   - `tx.Authorization = TransactionAuthorization.Impersonated;`
   - If `from` is missing, `BuildCallTransaction` already has defaults — keep them.
5. `var result = await _stateTransition.ApplyTransactionAsync(tx, _globalState, block, commit: false, ct);`
6. Build `InspectRequest`:

```csharp
var request = new InspectRequest
{
    Tx = tx,
    Block = block,
    Mismatches = ReadStringArray(obj, "mismatches"),
    ExpectException = obj.TryGetProperty("expectException", out var ex) && ex.ValueKind == JsonValueKind.String
        ? ex.GetString() : null,
    ExpectedReceiptSuccess = obj.TryGetProperty("expectedReceiptSuccess", out var ers) &&
        (ers.ValueKind is JsonValueKind.True or JsonValueKind.False)
        ? ers.GetBoolean() : null,
    DisableStack = ReadBool(obj, "disableStack"),
    DisableMemory = ReadBool(obj, "disableMemory"),
    DisableStorage = ReadBool(obj, "disableStorage")
};
```

`ReadBool` missing → `false`. `ReadStringArray` missing → `Array.Empty<string>()`.

7. `return InspectionAssembler.FromCanonical(request, result);`

The router already uses `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`, so `InspectResult` will serialize as `ok`, `fork`, `gasCostDec`, `ruleId`, etc.

## State for the PROVEN test

Fund accounts **before** inspect, same as other RPC tests:

```csharp
var sender = Address.FromHex("0xf6c3a9edc1afa0ad5b720e4d42e1437c43d3b3ff");
var coin = Address.FromHex("0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba");
globalState.SetBalance(sender, 10_000_000);
globalState.SetNonce(sender, 0);
// coinbase is the block miner; set ChainState current block miner to coin if needed
```

`BuildCurrentBlockContext` uses `block.Miner`. For the fee-pair diagnosis to see coinbase, set the current block miner to `0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba` **or** pass mismatches only (diagnosis does not need a live coinbase balance if mismatches are supplied). **Mismatches alone are enough** for PROVEN — you do not need the EVM to actually overcharge. Use `ExecutionResult` from a simple STOP create or empty create; diagnosis reads the mismatch strings.

Request body (id 1):

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "debug_inspect",
  "params": [{
    "from": "0xf6c3a9edc1afa0ad5b720e4d42e1437c43d3b3ff",
    "data": "0x6000",
    "gas": "0x186a0",
    "gasPrice": "0xa",
    "fork": "Frontier",
    "mismatches": [
      "balance mismatch for 0xf6c3a9edc1afa0ad5b720e4d42e1437c43d3b3ff: expected=0xf4240, actual=0xa6040",
      "balance mismatch for 0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba: expected=0x0, actual=0x4e200"
    ]
  }]
}
```

Note: omit `"to"` or set `"to": null` so it is a CREATE (`BuildCallTransaction` — **read that method**. If it treats missing `to` as a default address, set the JSON `to` field the same way Agent A’s test sets `Tx.To = null`. If the parser cannot express null to, set `to` to `""` if that maps to create; otherwise add a one-line fix in `BuildCallTransaction` only: empty/`null` `to` → `tx.To = null`. That is allowed.

## Tests (write these first, then implement)

File: `Schlieren.Tests/RPC/DebugInspectRpcTests.cs`

Copy fixture construction from `DebugWhyNotRpcTests.BuildFixture`, but use a full opcode set (`OpcodeCatalog.CreateAll()` if it exists, else the same opcode list as `DebugTraceAdvancedRpcTests`).

**Test 1 — registered**

```csharp
var methods = router.GetRegisteredMethods();
Assert.Contains("debug_inspect", methods);
```

**Test 2 — invalid params**

```csharp
var response = await router.ProcessRequest(
    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"debug_inspect\",\"params\":[]}");
// result is error object, code InvalidParams
```

**Test 3 — Frontier CREATE mismatches**

Parse `result.diagnosis.root.ruleId` == `TX.CREATE_SURCHARGE`  
`result.diagnosis.root.grade` == `PROVEN`  
`result.diagnosis.fingerprint` contains `INTRINSIC`  
`result.trace.structLogs` is an array (length may be 0 if create fails immediately; still must be present)  
`result.gasTree` exists (`label` or `totalGas`)  
`result.execution.gasUsed` is a `0x` string  
`result.ok` is true

**Test 4 — no mismatches ⇒ not PROVEN**

Same call without `mismatches`. Assert `grade` is `STRONG` or `POSSIBLE`, never `PROVEN`.

## Commands

```powershell
cd C:\projects\Schlieren
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~DebugInspectRpcTests --nologo
```

Then:

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~Debug --nologo
```

All existing Debug* tests must stay green.

## Commit

`feat(rpc): add debug_inspect (trace + gasTree + diagnosis)`

## B2 done criteria

- [ ] `debug_inspect` in `GetRegisteredMethods()` and `RouteToHandler`
- [ ] Handler uses `InspectionAssembler.FromCanonical` only
- [ ] Four tests above pass
- [ ] No edits to `CausalDiagnosisEngine.cs`

---

# B3 — Protect `debug_whyNot`

## Goal

Pre-execution classifier stays as-is.

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~DebugWhyNot --nologo
```

Expected: PASS, same reasons (`insufficient_funds`, `nonce_too_high`, `no_blocker_detected`).

Do not merge diagnosis into this method.

## B3 done criteria

- [ ] Command green
- [ ] `HandleDebugWhyNot` still returns `{ source, success, error, gasUsed, intrinsicGas, reasons }`

---

# B4 — Agent B completion

```powershell
cd C:\projects\Schlieren
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter "FullyQualifiedName~DebugInspect|FullyQualifiedName~DebugTrace|FullyQualifiedName~DebugWhyNot" --nologo
```

Expected: all PASS.

If `Schlieren.RPC.http` or an RPC README lists methods, add one line for `debug_inspect`. Do not write a new design doc.

## Commit (if README/http updated)

`docs(rpc): list debug_inspect`

## B done — tell the parent

```
Agent B complete.
- debug_trace* structLogs now include gasCostDec, contract, caller, callType, output
- debug_inspect returns InspectResult (execution, trace, gasTree, diagnosis)
- debug_whyNot unchanged
- tests: DebugInspect + DebugTrace + DebugWhyNot green
```

---

# Order of work

```
NOW:     B1 (no dependency on Agent A)
WAIT:    InspectionAssembler exists + InspectionAssemblerTests pass
THEN:    B2, B3, B4
NEVER:   frontend, CausalDiagnosisEngine edits, ApplyTransactionWithFrameAsync
```

# If you get stuck

- Testhost lock: stop `testhost`, rebuild.
- `BuildCallTransaction` always sets a `To`: allow null/empty `to` for CREATE. That is in scope.
- `InspectionAssembler` missing: stop after B1 and report “blocked on Agent A A3”.
- Do not implement a second diagnosis engine to unblock B2.
