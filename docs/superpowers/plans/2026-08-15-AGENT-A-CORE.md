# Agent A brief — Core inspect assembler

You own **Schlieren.Core only** (plus `Schlieren.Tests` unit tests that do not start RPC). You do **not** edit `Schlieren.RPC` or `Schlieren.UI`.

**Parent plan:** `docs/superpowers/plans/2026-08-15-inspect-rpc-backend.md`  
**Wire contract:** `docs/superpowers/plans/2026-08-15-inspect-result.schema.md`

## Goal

When Agent B has an `ExecutionResult` from `ApplyTransactionAsync`, it can call **one** function and get a complete, JSON-serializable inspect payload:

- execution summary
- structLogs with decimal gas cost + contract/caller
- canonical gas tree
- human-readable causal diagnosis (inventory Rule IDs)

That function is `InspectionAssembler.FromCanonical`.

## Why this exists

`CausalDiagnosisEngine`, `GasTreeFromTrace`, and `ExecutionTraceStep` already work **in process**. They are not a DTO and not on the wire. You turn them into one object that System.Text.Json can emit as the schema.

## Hard rules

1. Execution path is `StateTransition.ApplyTransactionAsync` results only. Never `ApplyTransactionWithFrameAsync`.
2. Grades: `PROVEN` / `STRONG` / `POSSIBLE` only (map from `DiagnosisGrade`).
3. Rule IDs come from `docs/gas/GAS_RULE_INVENTORY.md` via the existing engine (`TX.CREATE_SURCHARGE`, etc.). Do not invent new IDs.
4. If `InspectRequest.Mismatches` is empty, you must **downgrade** a `PROVEN` root to `STRONG` (no expected-post ledger).
5. JSON names are camelCase (`ruleId`, `gasCostDec`, `firstPhase`).
6. Do not touch RPC or Avalonia.

## Existing types you must reuse

| Type | Path | Use |
|---|---|---|
| `CausalDiagnosisEngine` | `Schlieren.Core/Execution/Causal/CausalDiagnosisEngine.cs` | `Analyze(FailureEvidence)` |
| `FailureEvidenceFactory` | `Schlieren.Core/Execution/Causal/FailureEvidenceFactory.cs` | `From(...)` |
| `GasTreeFromTrace` | `Schlieren.Core/Execution/GasTreeFromTrace.cs` | `FromCanonical(tx, rules, result)` |
| `GasTreeNode` | `Schlieren.Core/Execution/GasTree.cs` | tree nodes |
| `ExecutionTraceStep` | `Schlieren.Core/Execution/ExecutionTrace.cs` | `Pc`, `Op`, `Gas`, `GasCost`, `Depth`, `Stack`, `Memory`, `Storage`, `ContractAddress`, `CallerAddress`, `CallType`, `OutputData` |
| `ExecutionResult` | `Schlieren.Core/Execution/ExecutionResult.cs` | `IsSuccess`, `Error`, `GasUsed`, `GasRefundCounter`, `ReturnData`, `TraceSteps` |
| `ForkRulesFactory` | `Schlieren.Core/Forks/ForkRules.cs` | `For("Frontier")` |
| `BlockContext.Rules` | `Schlieren.Core/Primitives/BlockContext.cs` | fork rules on the block |

Proven fixture (copy into tests):

- Fork: `Frontier`
- Sender: `0xf6c3a9edc1afa0ad5b720e4d42e1437c43d3b3ff`
- Coinbase: `0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba`
- Tx: `To = null`, `GasPrice = 10`, `GasLimit = 100000`, `Data` at least 32 bytes
- Mismatches:
  - `balance mismatch for 0xf6c3a9edc1afa0ad5b720e4d42e1437c43d3b3ff: expected=0xf4240, actual=0xa6040`
  - `balance mismatch for 0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba: expected=0x0, actual=0x4e200`
- Expect: `diagnosis.root.ruleId == "TX.CREATE_SURCHARGE"`, grade `PROVEN`, fingerprint contains `INTRINSIC`

See `Schlieren.Tests/CausalDiagnosisEngineTests.cs` (`FrontierFeePair_32000_IsProvenCreateSurcharge`).

---

## Step A1 — DTOs (goal: schema-shaped C# types)

**Create:** `Schlieren.Core/Execution/Inspect/InspectDtos.cs`  
**Test:** `Schlieren.Tests/Execution/Inspect/InspectDtoJsonTests.cs`

Define exactly:

```csharp
namespace Schlieren.Core.Execution.Inspect;

public sealed class InspectResult
{
    public bool Ok { get; init; } = true;
    public string Fork { get; init; } = "";
    public InspectExecution Execution { get; init; } = new();
    public InspectTrace Trace { get; init; } = new();
    public InspectGasNode? GasTree { get; init; }
    public InspectDiagnosis? Diagnosis { get; init; }
}

public sealed class InspectExecution
{
    public bool Success { get; init; }
    public string Error { get; init; } = "None";
    public string GasUsed { get; init; } = "0x0";
    public string GasLimit { get; init; } = "0x0";
    public string RefundCounter { get; init; } = "0x0";
    public string ReturnValue { get; init; } = "0x";
}

public sealed class InspectTrace
{
    public List<InspectStructLog> StructLogs { get; init; } = new();
}

public sealed class InspectStructLog
{
    public int Pc { get; init; }
    public string Op { get; init; } = "";
    public string Gas { get; init; } = "0x0";
    public string GasCost { get; init; } = "0x0";
    public int GasCostDec { get; init; }
    public int Depth { get; init; } = 1;
    public List<string> Stack { get; init; } = new();
    public List<string> Memory { get; init; } = new();
    public Dictionary<string, string> Storage { get; init; } = new();
    public string? Contract { get; init; }
    public string? Caller { get; init; }
    public string? CallType { get; init; }
    public string? Output { get; init; }
}

public sealed class InspectGasNode
{
    public string Label { get; init; } = "";
    public ulong Gas { get; init; }
    public ulong TotalGas { get; init; }
    public List<InspectGasNode> Children { get; init; } = new();
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
    public string Grade { get; init; } = "POSSIBLE";
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

public static class InspectJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
```

**Done when:** serialize a filled `InspectResult` and the JSON contains keys `ok`, `fork`, `execution`, `trace`, `structLogs`, `gasCostDec`, `gasTree`, `diagnosis`, `ruleId`, `grade`.

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~InspectDtoJsonTests --nologo
```

Commit: `feat(inspect): add InspectResult DTOs`

---

## Step A2 — Mapper (goal: no logic, only field mapping)

**Create:** `Schlieren.Core/Execution/Inspect/InspectMapper.cs`  
**Test:** `Schlieren.Tests/Execution/Inspect/InspectMapperTests.cs`

```csharp
public static class InspectMapper
{
    public static int ParseGasDec(string? hexOrDec);
    public static InspectStructLog FromStep(ExecutionTraceStep s);
    public static InspectGasNode FromTree(GasTreeNode n);
    public static InspectDiagnosisHit FromHit(ScoredDiagnosis d);
    public static InspectDiagnosis FromReport(CausalDiagnosisEngine.Report r);
    public static string ToHex(ulong value);      // 0x-prefixed lowercase
    public static string ToHex(byte[]? data);     // 0x or 0xAABB
}
```

Rules:

- `ParseGasDec("0x3") == 3`, `ParseGasDec("3") == 3`, null/empty → 0.
- `FromStep`: `ContractAddress` → `Contract`, `CallerAddress` → `Caller`, `CallType?.ToString()` → `CallType`, `OutputData` → `0x` hex or null if empty.
- `FromHit`: `DiagnosisGrade.Proven` → `"PROVEN"`, `Strong` → `"STRONG"`, else `"POSSIBLE"`. `Phase` = `d.Phase.ToLabel()` (`CausalFingerprint.ToLabel`).
- `FromReport`: `Root = FromHit(r.Root)`, `Candidates = r.Ranked.Skip(1).Select(FromHit)`, `Fingerprint = r.Fingerprint`, `FirstPhase = r.FirstPhase.ToLabel()`.
- `FromTree`: `Label`, `Gas`, `TotalGas`, recurse `Children`.

**Done when:** mapper tests pass.

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~InspectMapperTests --nologo
```

Commit: `feat(inspect): map trace, gas tree, and diagnosis to DTOs`

---

## Step A3 — Assembler (goal: Agent B’s only dependency)

**Create:** `Schlieren.Core/Execution/Inspect/InspectRequest.cs`  
**Create:** `Schlieren.Core/Execution/Inspect/InspectionAssembler.cs`  
**Test:** `Schlieren.Tests/Execution/Inspect/InspectionAssemblerTests.cs`

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
    public static InspectResult FromCanonical(InspectRequest request, ExecutionResult result)
}
```

Algorithm (do not skip steps):

1. `fork = request.Block.Rules.Fork.ToString()`.
2. Last trace step = `result.TraceSteps.Count > 0 ? result.TraceSteps[^1] : null`.
3. `FailureEvidenceFactory.From(caseId: "inspect", forkName: fork, fixturePath: "", tx: request.Tx, sender: request.Tx.From, coinbase: request.Block.Coinbase, mismatches: request.Mismatches, gasUsed: result.GasUsed, refundCounter: result.GasRefundCounter, executionSucceeded: result.IsSuccess, error: result.Error, lastOpcode: last?.Op, lastPc: last?.Pc ?? 0, expectException: request.ExpectException, expectedReceiptSuccess: request.ExpectedReceiptSuccess)`.
4. `var report = CausalDiagnosisEngine.Analyze(ev)`.
5. Map diagnosis; if `request.Mismatches.Count == 0` and root grade is `PROVEN`, set displayed grade to `STRONG`.
6. Build structLogs from `result.TraceSteps` via `FromStep`. If disable flags, replace stack/memory/storage with empty.
7. `gasTree = InspectMapper.FromTree(GasTreeFromTrace.FromCanonical(request.Tx, request.Block.Rules, result))`.
8. Hex-encode `result.GasUsed`, `request.Tx.GasLimit`, refund (treat negative refund as `0x0` or two’s complement — prefer decimal string of the long if you must, but schema wants hex; use `0x` + abs hex and document if negative).

**Tests (required):**

1. Frontier CREATE + the two mismatch lines above + `ExecutionResult.Success(53000)` (or a real apply) → `RuleId == "TX.CREATE_SURCHARGE"`, `Grade == "PROVEN"`, fingerprint contains `INTRINSIC`, `GasTree` not null.
2. Same but `Mismatches = []` → grade is not `PROVEN`.
3. `DisableStack = true` → every log `Stack.Count == 0`.

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~InspectionAssemblerTests --nologo
```

Commit: `feat(inspect): assemble InspectResult from canonical execution`

---

## Step A4 — Done / handoff to Hermes (Agent B)

Run:

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~Inspect --nologo
```

**Handoff note Agent B needs (paste into Hermes):**

```
InspectionAssembler is ready.

Call:
  Schlieren.Core.Execution.Inspect.InspectionAssembler.FromCanonical(InspectRequest, ExecutionResult)

InspectRequest lives in Schlieren.Core/Execution/Inspect/InspectRequest.cs
InspectResult serializes with InspectJson.Options (camelCase).

I did not change Schlieren.RPC.
debug_inspect should: parse call object → Transaction + BlockContext (set Rules from "fork") → ApplyTransactionAsync(..., commit: false, EnableTracing: true) → FromCanonical → return InspectResult.
```

**A is done when:** that command is green, and `git diff --name-only` has no `Schlieren.RPC/` or `Schlieren.UI/` files.
