# Inspect RPC Backend Implementation Plan

> **For agentic workers:** Two agents. Agent A owns Core assembly. Agent B owns RPC. Both implement against the frozen JSON contract in Task 0. Do not start a frontend.

**Goal:** One functional backend payload a UI can call: canonical execution + structLogs with gas per opcode + gas tree + causal diagnosis (inventory Rule IDs, PROVEN/STRONG/POSSIBLE, cause vs effects).

**Architecture:** Keep `CausalDiagnosisEngine`, `GasTreeFromTrace`, and `ApplyTransactionAsync` as the only execution path. Add a Core assembler that builds a serializable `InspectResult`. RPC exposes `debug_inspect` (new) and enriches existing `debug_trace*` structLogs. Do not call `ApplyTransactionWithFrameAsync`. Do not invent a second constant catalog.

**Tech Stack:** C# 12 / .NET 8, existing `Schlieren.RPC` JSON-RPC 2.0, xUnit, `docs/gas/GAS_FORMULAS.md` rule IDs.

## Global Constraints

- No runtime dependency on EELS, Geth, or another client.
- Canonical execution is `StateTransition.ApplyTransactionAsync` only.
- Diagnosis grades are `PROVEN` / `STRONG` / `POSSIBLE` only.
- Rule IDs must match `docs/gas/GAS_RULE_INVENTORY.md` (e.g. `TX.CREATE_SURCHARGE`). Do not create a parallel taxonomy.
- `debug_traceTransaction` Geth shape (`gas`, `failed`, `returnValue`, `structLogs`) must keep working. Extra fields are additive.
- Host policies (16 MiB memory, ModExp 10B clamp) are labeled host, never as protocol Rule IDs.
- Do not modify Avalonia UI in this workstream.

## Split

```
Task 0 (either agent, first, small)     Freeze InspectResult JSON contract
Agent A  (Core, no RPC)                 Assembler + DTOs + unit tests
Agent B  (RPC, after Task 0; inspect
          handler after Agent A lands)  Enrich traces + debug_inspect + RPC tests
Joint gate                              One curl/script proves the full payload
```

Agent B may start Task B1 (structLog enrichment) in parallel with Agent A. Task B2 (`debug_inspect`) waits for Agent A’s `InspectionAssembler`.

---

## Task 0: Freeze the wire contract (both agents, 20 minutes, one commit)

**Files:**

- Create: `docs/superpowers/plans/2026-08-15-inspect-result.schema.md`

The contract is the handoff. Neither agent changes field names after this commit without updating this file and both test suites.

Required JSON for `debug_inspect` result:

```json
{
  "ok": true,
  "fork": "Frontier",
  "execution": {
    "success": true,
    "error": "None",
    "gasUsed": "0xcf08",
    "gasLimit": "0x186a0",
    "refundCounter": "0x0",
    "returnValue": "0x"
  },
  "trace": {
    "structLogs": [
      {
        "pc": 0,
        "op": "PUSH1",
        "gas": "0x1869d",
        "gasCost": "0x3",
        "gasCostDec": 3,
        "depth": 1,
        "stack": ["0x1"],
        "memory": [],
        "storage": {},
        "contract": "0x00000000000000000000000000000000000000aa",
        "caller": "0x0000000000000000000000000000000000000001",
        "callType": null,
        "output": null
      }
    ]
  },
  "gasTree": {
    "label": "Transaction (canonical)  [53,000 gas used]",
    "gas": 0,
    "totalGas": 53000,
    "children": []
  },
  "diagnosis": {
    "fingerprint": "INTRINSIC / TX.CREATE_SURCHARGE / Frontier",
    "firstPhase": "INTRINSIC",
    "root": {
      "ruleId": "TX.CREATE_SURCHARGE",
      "title": "Frontier CREATE tx surcharge 32000 (should be 0)",
      "grade": "PROVEN",
      "score": 92,
      "phase": "INTRINSIC",
      "why": "...",
      "proof": "...",
      "consequences": "DIRECT EFFECT: ...\nDOWNSTREAM: ...\nFINAL SYMPTOM: ...",
      "likelyFix": "...",
      "codeBoundary": "Schlieren.Core/Execution/IntrinsicGas.cs:61-64",
      "protocolRule": "EIP-2: ...",
      "gasDelta": 32000
    },
    "candidates": []
  }
}
```

`debug_inspect` params (JSON-RPC array, one object):

```json
[{
  "from": "0x...",
  "to": "0x..." ,
  "data": "0x...",
  "gas": "0x186a0",
  "value": "0x0",
  "gasPrice": "0xa",
  "fork": "Frontier",
  "mismatches": [
    "balance mismatch for 0x...: expected=0xf4240, actual=0xa6040"
  ],
  "expectException": null,
  "expectedReceiptSuccess": true,
  "disableStack": false,
  "disableMemory": false,
  "disableStorage": false
}]
```

`mismatches` is optional. If omitted, `diagnosis.root.grade` is at most `STRONG` (no expected-post ledger). If provided, PROVEN is allowed.

- [ ] Write the schema file exactly as above plus one paragraph: “Geth `debug_traceTransaction` keeps its current envelope; structLogs may add `gasCostDec`, `contract`, `caller`, `callType`, `output`.”
- [ ] Commit: `docs(inspect): freeze debug_inspect JSON contract`

---

## Agent A — Core inspect assembler

**Owns:** `Schlieren.Core/Execution/Inspect/*`, unit tests in `Schlieren.Tests/Execution/Inspect/`  
**Must not:** edit `Schlieren.RPC`, Avalonia, or `ApplyTransactionWithFrameAsync`.

### Task A1: DTOs that serialize 1:1 with the schema

**Files:**

- Create: `Schlieren.Core/Execution/Inspect/InspectDtos.cs`
- Test: `Schlieren.Tests/Execution/Inspect/InspectDtoJsonTests.cs`

**Produces:**

```csharp
public sealed class InspectResult
{
    public bool Ok { get; init; } = true;
    public string Fork { get; init; } = "";
    public InspectExecution Execution { get; init; } = new();
    public InspectTrace Trace { get; init; } = new();
    public InspectGasNode? GasTree { get; init; }
    public InspectDiagnosis? Diagnosis { get; init; }
}

public sealed class InspectStructLog
{
    public int Pc { get; init; }
    public string Op { get; init; } = "";
    public string Gas { get; init; } = "0x0";
    public string GasCost { get; init; } = "0x0";
    public int GasCostDec { get; init; }
    public int Depth { get; init; }
    public List<string> Stack { get; init; } = new();
    public List<string> Memory { get; init; } = new();
    public Dictionary<string, string> Storage { get; init; } = new();
    public string? Contract { get; init; }
    public string? Caller { get; init; }
    public string? CallType { get; init; }
    public string? Output { get; init; }
}

public sealed class InspectDiagnosis
{
    public string Fingerprint { get; init; } = "";
    public string FirstPhase { get; init; } = "";
    public InspectDiagnosisHit? Root { get; init; }
    public List<InspectDiagnosisHit> Candidates { get; init; } = new();
}

public sealed class InspectDiagnosisHit
{
    public string RuleId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Grade { get; init; } = "POSSIBLE"; // PROVEN | STRONG | POSSIBLE
    public int Score { get; init; }
    public string Phase { get; init; } = "";
    public string Why { get; init; } = "";
    public string Proof { get; init; } = "";
    public string Consequences { get; init; } = "";
    public string LikelyFix { get; init; } = "";
    public string CodeBoundary { get; init; } = "";
    public string ProtocolRule { get; init; } = "";
    public long? GasDelta { get; init; }
}
```

Also define `InspectExecution`, `InspectTrace`, `InspectGasNode` to match the schema.

JSON property names: camelCase via `[JsonPropertyName]` or a shared `InspectJson` options object in the same file:

```csharp
public static class InspectJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
```

- [ ] Write `InspectDtoJsonTests.RoundTrip_MatchesSchemaKeys` that serializes a filled `InspectResult` and asserts keys `ok`, `fork`, `execution`, `trace`, `gasTree`, `diagnosis`, `structLogs`, `gasCostDec`, `ruleId`, `grade`.
- [ ] Run: `dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~InspectDtoJsonTests --nologo`  
  Expected: FAIL (types missing).
- [ ] Add the DTOs. Re-run. Expected: PASS.
- [ ] Commit: `feat(inspect): add InspectResult DTOs`

### Task A2: Map Core types → DTOs

**Files:**

- Create: `Schlieren.Core/Execution/Inspect/InspectMapper.cs`
- Test: `Schlieren.Tests/Execution/Inspect/InspectMapperTests.cs`

**Consumes:** `ExecutionTraceStep`, `GasTreeNode`, `ScoredDiagnosis`, `CausalDiagnosisEngine.Report`  
**Produces:**

```csharp
public static class InspectMapper
{
    public static InspectStructLog FromStep(ExecutionTraceStep s);
    public static InspectGasNode FromTree(GasTreeNode n);
    public static InspectDiagnosisHit FromHit(ScoredDiagnosis d);
    public static InspectDiagnosis FromReport(CausalDiagnosisEngine.Report r);
    public static int ParseGasDec(string hexOrDec);
}
```

`ParseGasDec("0x3") == 3`. `FromStep` copies `ContractAddress` → `Contract`, `CallerAddress` → `Caller`, `CallType?.ToString()`, `OutputData` as `0x` hex.

- [ ] Test: one `ExecutionTraceStep` with `GasCost = "0x3"`, `ContractAddress` set → DTO has `GasCostDec == 3` and `Contract` set.
- [ ] Test: `ScoredDiagnosis` with `Grade = Proven` → `"PROVEN"`.
- [ ] Implement mapper. Tests PASS.
- [ ] Commit: `feat(inspect): map trace, gas tree, and diagnosis to DTOs`

### Task A3: InspectionAssembler (the only Core entry the RPC may call)

**Files:**

- Create: `Schlieren.Core/Execution/Inspect/InspectRequest.cs`
- Create: `Schlieren.Core/Execution/Inspect/InspectionAssembler.cs`
- Test: `Schlieren.Tests/Execution/Inspect/InspectionAssemblerTests.cs`

```csharp
public sealed class InspectRequest
{
    public required Transaction Tx { get; init; }
    public required BlockContext Block { get; init; }
    public IReadOnlyList<string> Mismatches { get; init; } = Array.Empty<string>();
    public string? ExpectException { get; init; }
    public bool? ExpectedReceiptSuccess { get; init; }
    public bool DisableStack { get; init; }
    public bool DisableMemory { get; init; }
    public bool DisableStorage { get; init; }
}

public static class InspectionAssembler
{
    public static InspectResult FromCanonical(
        InspectRequest request,
        ExecutionResult result)
}
```

Behavior:

1. `execution` from `result` + `request.Tx.GasLimit` + `request.Block.Rules.Fork`.
2. `trace.structLogs` from `result.TraceSteps` via mapper; honor disable flags (empty arrays/dicts).
3. `gasTree` from `GasTreeFromTrace.FromCanonical(request.Tx, request.Block.Rules, result)` then mapper.
4. Build `FailureEvidence` with `FailureEvidenceFactory.From(...)` using `request.Tx`, `request.Tx.From`, `request.Block.Coinbase`, `request.Mismatches`, `result.GasUsed`, `result.GasRefundCounter`, `result.IsSuccess`, `result.Error`, last step op/pc, `request.ExpectException`, `request.ExpectedReceiptSuccess`.
5. `CausalDiagnosisEngine.Analyze(ev)` → `diagnosis`.
6. If `request.Mismatches` is empty and root grade is `PROVEN`, downgrade displayed grade to `STRONG` (no expected ledger).

- [ ] Test: Frontier CREATE tx, mismatches sender `0xf4240` vs `0xa6040` and coinbase `0x0` vs `0x4e200`, gasPrice 10, coinbase `0x2adc…`. After a dummy `ExecutionResult.Success(53000)` (or a real `ApplyTransactionAsync` if easier), `diagnosis.root.ruleId == "TX.CREATE_SURCHARGE"` and `grade == "PROVEN"` and fingerprint contains `INTRINSIC`.
- [ ] Test: no mismatches → diagnosis present, grade is not `PROVEN`.
- [ ] Test: `DisableStack: true` → every structLog `stack` is empty.
- [ ] Implement. Tests PASS.
- [ ] Commit: `feat(inspect): assemble InspectResult from canonical execution`

### Task A4: Agent A done checklist

- [ ] `dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~Inspect --nologo` green.
- [ ] No files under `Schlieren.RPC/` or `Schlieren.UI/` in the Agent A diff.
- [ ] Hand off: Agent B may call `InspectionAssembler.FromCanonical`.

---

## Agent B — RPC surface

**Owns:** `Schlieren.RPC/**`, `Schlieren.Tests/RPC/DebugInspectRpcTests.cs`, enrich `BuildTraceResponse*`.  
**Must not:** change diagnosis formulas. Call `InspectionAssembler` only.

### Task B1 (parallel with A): Enrich existing `debug_trace*` structLogs

**Files:**

- Modify: `Schlieren.RPC/Handlers/EthHandlers.cs` `BuildTraceResponse` and `BuildTraceResponseFromStored` (around 1345–1396)
- Test: `Schlieren.Tests/RPC/DebugTraceAdvancedRpcTests.cs` (extend) or create `Schlieren.Tests/RPC/DebugTraceStructLogEnrichmentTests.cs`

Add additive fields on each structLog:

```csharp
gasCostDec = ParseDec(s.GasCost),
contract = s.ContractAddress,
caller = s.CallerAddress,
callType = s.CallType?.ToString(),
output = s.OutputData is { Length: > 0 }
    ? EthereumTypes.ToEthHex(s.OutputData)
    : null
```

Keep `gas`, `failed`, `returnValue`, `structLogs`, `pc`, `op`, `gas`, `gasCost`, `depth`, `stack`, `memory`, `storage`.

Fix stored-trace `returnValue`: if receipt/trace has none, keep `"0x"` (do not invent).

- [ ] Test: existing `debug_traceCall` / `debug_traceTransaction` tests still pass.
- [ ] Test: a traced CALL includes `gasCostDec` as a number and `contract` when the step has `ContractAddress`.
- [ ] Implement. Tests PASS.
- [ ] Commit: `feat(rpc): add contract/caller/gasCostDec on structLogs`

### Task B2 (after A3): `debug_inspect`

**Files:**

- Modify: `Schlieren.RPC/Server/RpcRouter.cs` — add `"debug_inspect"` to the registered set and the switch.
- Modify: `Schlieren.RPC/Handlers/EthHandlers.cs` — add `HandleDebugInspect`.
- Test: `Schlieren.Tests/RPC/DebugInspectRpcTests.cs`

Handler sketch:

```csharp
public async Task<object> HandleDebugInspect(object[] parameters, CancellationToken ct)
{
    if (parameters is not { Length: > 0 } || parameters[0] is not JsonElement obj
        || obj.ValueKind != JsonValueKind.Object)
        throw new RpcException(JsonRpcErrorCodes.InvalidParams, "Expected inspect object");

    var forkName = obj.TryGetProperty("fork", out var f) ? f.GetString() : null;
    var block = BuildCurrentBlockContext();
    if (!string.IsNullOrWhiteSpace(forkName))
        block = block with { Rules = ForkRulesFactory.For(forkName) }; // or set Rules if BlockContext is not a record

    var tx = BuildCallTransaction(obj, block.GasLimit);
    tx.EnableTracing = true;
    tx.Authorization = TransactionAuthorization.Impersonated;

    var result = await _stateTransition.ApplyTransactionAsync(tx, _globalState, block, commit: false, ct);

    var mismatches = ReadStringArray(obj, "mismatches");
    var request = new InspectRequest
    {
        Tx = tx,
        Block = block,
        Mismatches = mismatches,
        ExpectException = ReadString(obj, "expectException"),
        ExpectedReceiptSuccess = ReadBool(obj, "expectedReceiptSuccess"),
        DisableStack = ReadBool(obj, "disableStack") ?? false,
        DisableMemory = ReadBool(obj, "disableMemory") ?? false,
        DisableStorage = ReadBool(obj, "disableStorage") ?? false
    };

    return InspectionAssembler.FromCanonical(request, result);
}
```

If `BlockContext` is not a `with` record, copy fields and set `Rules` like other handlers.

Use `InspectJson.Options` when the router serializes, **or** return `InspectResult` and rely on the router’s existing camelCase options. If the router uses different naming, add an explicit serialize test.

- [ ] Test: `debug_inspect` with Frontier CREATE + the two balance mismatch strings from Task A3 → `diagnosis.root.ruleId == "TX.CREATE_SURCHARGE"`, `grade == "PROVEN"`, `trace.structLogs` is a non-empty array, `gasTree` is non-null.
- [ ] Test: missing params object → JSON-RPC invalid params.
- [ ] Test: `debug_inspect` is in `GetRegisteredMethods()`.
- [ ] Implement route + handler. Tests PASS.
- [ ] Commit: `feat(rpc): add debug_inspect (trace + gasTree + diagnosis)`

### Task B3: Do not break `debug_whyNot`

**Files:**

- Test: existing `Schlieren.Tests/RPC/DebugWhyNotRpcTests.cs`

`debug_whyNot` stays pre-execution / validation reasons. Do not replace it with causal diagnosis.

- [ ] Run: `dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~DebugWhyNot --nologo`  
  Expected: PASS unchanged.

### Task B4: Agent B done checklist

- [ ] `dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~Debug --nologo` green.
- [ ] No diagnosis formula changes in `CausalDiagnosisEngine.cs`.

---

## Joint gate (either agent, last)

**Files:** none required (script optional).

```powershell
# From repo root, with `schlieren node` already running OR via in-process RPC tests.
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter "FullyQualifiedName~Inspect|FullyQualifiedName~DebugInspect|FullyQualifiedName~DebugTrace" --nologo
```

Expected: all PASS.

Manual check (only if a node is up):

```text
POST debug_inspect
→ body has execution, trace.structLogs[].gasCostDec, gasTree.label, diagnosis.root.ruleId, diagnosis.root.why
```

- [ ] Commit nothing unless a README RPC method list exists; if `Schlieren.RPC` README or `Schlieren.RPC.http` lists methods, add `debug_inspect` there.

---

## Out of scope (do not do)

- Avalonia Case Inspector restyle
- `Schlieren.Core.Gas` typed schedule
- Replacing `debug_traceTransaction` envelope
- Calling EELS / `ethereum-spec-evm` from RPC
- Auto-restarting the desktop UI

## Risk notes for both agents

- `EthHandlers.cs` is large. Agent B should add `HandleDebugInspect` near the other debug methods and keep mapping helpers private in that file or a new `Schlieren.RPC/Handlers/InspectRpc.cs` if the file becomes unreviewable.
- Testhost / `Schlieren.UI.exe` often locks DLLs. If copy fails, stop testhost only, not the user’s UI unless they asked.
- `ComputeIntrinsicGas` in `EthHandlers` is fork-blind (always +32000 on create). Agent B must not use it for `debug_inspect` diagnosis. Assembler uses `IntrinsicGas.Compute(tx, rules)` via `GasTreeFromTrace`.

## Execution

After this plan is approved:

1. Run Task 0 in the parent session (or Agent A).
2. Launch Agent A on A1–A4 and Agent B on B1 in parallel.
3. When A3 is merged, launch Agent B on B2–B4.
4. Parent runs the joint gate.
