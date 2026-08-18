# Inspect A/B Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove that Agent A (Core assembler) and Agent B (RPC `debug_inspect`) return the same inspect payload for one golden case, then point the workbench fixture-diff at that same assembler so UI and RPC cannot drift.

**Architecture:** Do not rebuild diagnosis or traces. Share one golden case (Frontier CREATE + fee-pair mismatch strings). Agent A owns the constants and a live `ApplyTransactionAsync` → `InspectionAssembler.FromCanonical` test. Agent B owns the JSON-RPC test that asserts `diagnosis.root.ruleId == "TX.CREATE_SURCHARGE"` and `grade == "PROVEN"` (the parent-plan joint gate Hermes skipped). Workbench then emits mismatch lines in the engine format and calls `FromCanonical` on the run it already has.

**Tech Stack:** .NET 8, xUnit, `Schlieren.Core` inspect DTOs, `Schlieren.RPC` `RpcRouter` in-process, Avalonia workbench (no new chrome).

## Global Constraints

- Repo: `C:\projects\Schlieren`, branch `codex/gas-rule-inventory`.
- Frozen JSON contract: `docs/superpowers/plans/2026-08-15-inspect-result.schema.md`.
- Canonical execute path is `StateTransition.ApplyTransactionAsync`. Never `ApplyTransactionWithFrameAsync`.
- RPC `debug_inspect` may call only `InspectionAssembler.FromCanonical`. Do not re-implement diagnosis in `EthHandlers`.
- Do not edit `Schlieren.Core/Execution/Causal/*` formulas.
- Do not change `debug_whyNot` behavior.
- `debug_trace*` envelope stays `{ gas, failed, returnValue, structLogs }`. Additive fields only.
- `debug_inspect` `gasCostDec` is a **number** (`InspectStructLog.GasCostDec` is `int`). `debug_trace*` `gasCostDec` stays a **decimal string**. Do not “fix” the trace path in this plan.
- Diagnosis grades are `PROVEN` | `STRONG` | `POSSIBLE`. Without `mismatches`, grade is at most `STRONG`.
- Inventory rule IDs only (`TX.CREATE_SURCHARGE`, not folder×balance theater).
- Do not auto-restart or restyle the Avalonia UI. J5 adds one diagnosis line from the assembler; no new panels.
- Testhost / `Schlieren.UI.exe` can lock DLLs. If copy fails, stop testhost only.

---

## Who does what

| Agent | Owns | Does not touch |
|---|---|---|
| **Agent A (Core)** | Golden fixture constants, live assembler test, mismatch line formatter, workbench attach | `EthHandlers`, `RpcRouter`, `debug_trace*` |
| **Agent B (Hermes / RPC)** | RPC contract tests, request `coinbase` field, invalid-params + registered-methods | `CausalDiagnosisEngine`, Avalonia, assembler formulas |
| **Either (last)** | Joint gate command + method-list one-liner if an RPC README exists | New features |

**Order:** J1 → then J2 and J3 in parallel → J4 can run with J3 → J5 after J1 (and ideally J2) → J6 last.

---

## File map

| File | Responsibility |
|---|---|
| Create: `Schlieren.Tests/Inspect/InspectGoldenCase.cs` | Shared addresses, hex, mismatch strings, JSON-RPC body |
| Create: `Schlieren.Core/Execution/Inspect/InspectMismatchFormat.cs` | One function that emits engine-parseable mismatch lines |
| Create: `Schlieren.Tests/Execution/Inspect/InspectMismatchFormatTests.cs` | Formatter unit test |
| Modify: `Schlieren.Tests/Execution/Inspect/InspectionAssemblerTests.cs` | Use golden constants; add live `ApplyTransactionAsync` test |
| Modify: `Schlieren.Tests/RPC/DebugInspectRpcTests.cs` | PROVEN RPC test + invalid params + registered methods |
| Modify: `Schlieren.RPC/Handlers/EthHandlers.cs` `HandleDebugInspect` | Honor optional request `coinbase` so the golden fee-pair does not depend on chain-head miner |
| Modify: `Schlieren.UI/Services/BytecodeExecutionService.cs` | Put `Tx` and `Block` on `WorkbenchRunResult` (always) |
| Modify: `Schlieren.UI/ViewModels/WorkbenchViewModel.cs` `AppendExpectedDiff` | Build engine-format mismatch strings from expected vs post; call `FromCanonical`; append one diagnosis line |

---

### Task J1: Shared golden case (Agent A)

**Files:**
- Create: `Schlieren.Tests/Inspect/InspectGoldenCase.cs`
- Modify: `Schlieren.Tests/Execution/Inspect/InspectionAssemblerTests.cs` (switch existing strings to the shared constants)

**Interfaces:**
- Consumes: none
- Produces: `InspectGoldenCase.SenderHex`, `CoinbaseHex`, `Fork`, `GasPriceHex`, `GasHex`, `InitcodeHex`, `SenderMismatch`, `CoinbaseMismatch`, `Mismatches`, `DebugInspectJsonRpc`

These numbers are the proven Frontier CREATE fee-pair already locked in `InspectionAssemblerTests` and `CausalDiagnosisEngineTests`:

- sender `0xf6c3a9edc1afa0ad5b720e4d42e1437c43d3b3ff` expected `0xf4240` actual `0xa6040`
- coinbase `0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba` expected `0x0` actual `0x4e200`
- `gasPrice = 10`, create tx, fork `Frontier`
- residual gas = `32000` = `TX.CREATE_SURCHARGE`

- [ ] **Step 1: Add the golden case class**

```csharp
namespace Schlieren.Tests.Inspect;

public static class InspectGoldenCase
{
    public const string SenderHex = "0xf6c3a9edc1afa0ad5b720e4d42e1437c43d3b3ff";
    public const string CoinbaseHex = "0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba";
    public const string Fork = "Frontier";
    public const string GasPriceHex = "0xa";
    public const string GasHex = "0x186a0";
    public const string InitcodeHex = "0x6000"; // PUSH1 0x00 — valid tiny initcode
    public const string SenderExpected = "0xf4240";
    public const string SenderActual = "0xa6040";
    public const string CoinbaseExpected = "0x0";
    public const string CoinbaseActual = "0x4e200";

    public static string SenderMismatch =>
        $"balance mismatch for {SenderHex}: expected={SenderExpected}, actual={SenderActual}";

    public static string CoinbaseMismatch =>
        $"balance mismatch for {CoinbaseHex}: expected={CoinbaseExpected}, actual={CoinbaseActual}";

    public static string[] Mismatches => [SenderMismatch, CoinbaseMismatch];

    public static string DebugInspectJsonRpc(int id = 1) =>
        $$"""
        {"jsonrpc":"2.0","id":{{id}},"method":"debug_inspect","params":[{
          "from":"{{SenderHex}}",
          "to":null,
          "data":"{{InitcodeHex}}",
          "gas":"{{GasHex}}",
          "value":"0x0",
          "gasPrice":"{{GasPriceHex}}",
          "fork":"{{Fork}}",
          "coinbase":"{{CoinbaseHex}}",
          "mismatches":[
            "{{SenderMismatch}}",
            "{{CoinbaseMismatch}}"
          ]
        }]}
        """;
}
```

- [ ] **Step 2: Point the existing assembler test at the constants**

In `FrontierCreateMismatches_AreProvenSurcharge`, replace the two hand-built mismatch strings and the hardcoded sender/coin addresses with `InspectGoldenCase.*`. Keep the rest of the assertions.

- [ ] **Step 3: Run assembler tests**

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~InspectionAssemblerTests --nologo
```

Expected: PASS (same 3 tests; constants only).

- [ ] **Step 4: Commit**

```powershell
git add Schlieren.Tests/Inspect/InspectGoldenCase.cs Schlieren.Tests/Execution/Inspect/InspectionAssemblerTests.cs
git commit -m "test(inspect): share Frontier CREATE golden case for A/B gate"
```

---

### Task J2: Live assembler path (Agent A)

**Files:**
- Modify: `Schlieren.Tests/Execution/Inspect/InspectionAssemblerTests.cs`

**Interfaces:**
- Consumes: `InspectGoldenCase`, `StateTransition.ApplyTransactionAsync`, `InspectionAssembler.FromCanonical`
- Produces: proof that a **real** `ExecutionResult` (not `ExecutionResult.Success(53_000)`) still diagnoses `TX.CREATE_SURCHARGE` / `PROVEN`

Today `InspectionAssemblerTests` injects a fake success result. RPC always runs the EVM first. This task closes that hole on the Core side.

- [ ] **Step 1: Write the failing live test** (add to `InspectionAssemblerTests`)

```csharp
[Fact]
public async Task LiveFrontierCreate_FromCanonical_IsProvenSurcharge()
{
    var sender = Address.FromHex(InspectGoldenCase.SenderHex);
    var coin = Address.FromHex(InspectGoldenCase.CoinbaseHex);
    var state = new GlobalState();
    state.SetBalance(sender, 10_000_000_000);

    var opcodes = new List<IOpcode> { new OpcodeStop(), new OpcodePush1() };
    var st = new StateTransition(new EvmMachine(opcodes));
    var tx = new Transaction
    {
        From = sender,
        To = null,
        GasPrice = 10,
        GasLimit = 100_000,
        Data = Convert.FromHexString(InspectGoldenCase.InitcodeHex[2..]),
        EnableTracing = true
    };
    var block = new BlockContext
    {
        Coinbase = coin,
        Rules = ForkRulesFactory.For(InspectGoldenCase.Fork),
        GasLimit = 30_000_000
    };

    var result = await st.ApplyTransactionAsync(tx, state, block, commit: false);
    var inspect = InspectionAssembler.FromCanonical(
        new InspectRequest { Tx = tx, Block = block, Mismatches = InspectGoldenCase.Mismatches },
        result);

    Assert.True(inspect.Ok);
    Assert.Equal("Frontier", inspect.Fork);
    Assert.NotNull(inspect.Diagnosis?.Root);
    Assert.Equal("TX.CREATE_SURCHARGE", inspect.Diagnosis!.Root!.RuleId);
    Assert.Equal("PROVEN", inspect.Diagnosis.Root.Grade);
    Assert.True(inspect.Trace.StructLogs.Count > 0);
    Assert.True(inspect.Trace.StructLogs[0].GasCostDec >= 0);
    Assert.NotNull(inspect.GasTree);
}
```

Add usings: `Schlieren.Core.Opcodes`, `Schlieren.Tests.Inspect`.

- [ ] **Step 2: Run only this test**

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~LiveFrontierCreate_FromCanonical_IsProvenSurcharge --nologo
```

Expected: PASS if the machine has `PUSH1`/`STOP` and create-tx works. If it fails because initcode `6000` is invalid on Frontier, switch `InitcodeHex` in J1 to `0x00` (STOP only) **in a follow-up commit on J1** and keep both agents on the same hex. Do not invent a second golden case.

- [ ] **Step 3: Commit**

```powershell
git add Schlieren.Tests/Execution/Inspect/InspectionAssemblerTests.cs
git commit -m "test(inspect): prove assembler on live ApplyTransactionAsync"
```

---

### Task J3: RPC asserts the same diagnosis (Agent B)

**Files:**
- Modify: `Schlieren.Tests/RPC/DebugInspectRpcTests.cs`
- Modify: `Schlieren.RPC/Handlers/EthHandlers.cs` (`HandleDebugInspect` only)

**Interfaces:**
- Consumes: `InspectGoldenCase.DebugInspectJsonRpc()`, `InspectionAssembler.FromCanonical`
- Produces: `debug_inspect` JSON with `diagnosis.root.ruleId == "TX.CREATE_SURCHARGE"` and `grade == "PROVEN"`; optional request field `coinbase`

**Why a handler change:** `FailureEvidenceFactory` only records `CoinbaseWeiDelta` when the mismatch address equals `request.Block.Coinbase`. Today `HandleDebugInspect` sets coinbase from `_chainState.CurrentBlock.Miner`, which is empty in tests → `Address.Zero` → fee-pair never PROVEN. That is the integration bug this task exists to catch.

- [ ] **Step 1: Write the failing RPC test**

Add to `DebugInspectRpcTests` (reuse `BuildFixture`, but fund the golden sender and keep the existing opcode list; add `OpcodePush1` if missing — already present):

```csharp
[Fact]
public async Task DebugInspect_GoldenFrontierCreate_ReturnsProvenCreateSurcharge()
{
    var (globalState, handlers) = BuildFixture();
    globalState.SetBalance(
        Address.FromHex(InspectGoldenCase.SenderHex),
        10_000_000_000);
    var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

    var response = await router.ProcessRequest(InspectGoldenCase.DebugInspectJsonRpc());

    using var doc = JsonDocument.Parse(response);
    Assert.False(doc.RootElement.TryGetProperty("error", out _), response);
    var result = doc.RootElement.GetProperty("result");
    Assert.True(result.GetProperty("ok").GetBoolean());
    Assert.Equal(InspectGoldenCase.Fork, result.GetProperty("fork").GetString());

    var logs = result.GetProperty("trace").GetProperty("structLogs");
    Assert.True(logs.GetArrayLength() > 0);
    Assert.Equal(JsonValueKind.Number, logs[0].GetProperty("gasCostDec").ValueKind);

    Assert.True(result.TryGetProperty("gasTree", out var tree));
    Assert.False(string.IsNullOrEmpty(tree.GetProperty("label").GetString()));

    var root = result.GetProperty("diagnosis").GetProperty("root");
    Assert.Equal("TX.CREATE_SURCHARGE", root.GetProperty("ruleId").GetString());
    Assert.Equal("PROVEN", root.GetProperty("grade").GetString());
    Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("why").GetString()));
}
```

Add `using Schlieren.Tests.Inspect;`.

- [ ] **Step 2: Run it — expect FAIL** (coinbase is Zero, grade is not PROVEN)

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~DebugInspect_GoldenFrontierCreate_ReturnsProvenCreateSurcharge --nologo
```

Expected: FAIL on `ruleId` or `grade`. If it already PASSes, skip Step 3 and still add the `coinbase` parse so callers are not coupled to chain-head miner.

- [ ] **Step 3: Parse optional `coinbase` in `HandleDebugInspect`**

In `HandleDebugInspect`, after `fork` is resolved and before `new BlockContext`, read:

```csharp
var coinbase = requestObj.Value.TryGetProperty("coinbase", out var cbProp)
               && cbProp.ValueKind == JsonValueKind.String
               && !string.IsNullOrWhiteSpace(cbProp.GetString())
    ? Address.FromHex(cbProp.GetString()!)
    : (string.IsNullOrEmpty(currentBlock.Miner)
        ? Address.Zero
        : Address.FromHex(currentBlock.Miner));
```

Set `BlockContext.Coinbase = coinbase`. Do not change any other handler.

- [ ] **Step 4: Re-run the golden RPC test**

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~DebugInspect --nologo
```

Expected: PASS (existing 3 structural tests + new PROVEN test).

- [ ] **Step 5: Commit**

```powershell
git add Schlieren.Tests/RPC/DebugInspectRpcTests.cs Schlieren.RPC/Handlers/EthHandlers.cs
git commit -m "test(rpc): debug_inspect golden case is TX.CREATE_SURCHARGE PROVEN"
```

---

### Task J4: RPC plumbing leftovers (Agent B)

**Files:**
- Modify: `Schlieren.Tests/RPC/DebugInspectRpcTests.cs`

Parent plan B2 required these; they are still missing.

- [ ] **Step 1: Add the two tests**

```csharp
[Fact]
public async Task DebugInspect_IsRegistered()
{
    var (_, handlers) = BuildFixture();
    var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);
    Assert.Contains("debug_inspect", router.GetRegisteredMethods());
}

[Fact]
public async Task DebugInspect_MissingParams_IsInvalidParams()
{
    var (_, handlers) = BuildFixture();
    var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);
    var response = await router.ProcessRequest(
        """{"jsonrpc":"2.0","id":1,"method":"debug_inspect","params":[]}""");
    using var doc = JsonDocument.Parse(response);
    var error = doc.RootElement.GetProperty("error");
    Assert.Equal(-32602, error.GetProperty("code").GetInt32());
}
```

- [ ] **Step 2: Run**

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~DebugInspect --nologo
```

Expected: PASS (5 tests if J3 landed, else 5 including golden).

- [ ] **Step 3: Commit**

```powershell
git add Schlieren.Tests/RPC/DebugInspectRpcTests.cs
git commit -m "test(rpc): debug_inspect registered and rejects empty params"
```

---

### Task J5: Workbench uses the assembler (Agent A)

**Files:**
- Create: `Schlieren.Core/Execution/Inspect/InspectMismatchFormat.cs`
- Create: `Schlieren.Tests/Execution/Inspect/InspectMismatchFormatTests.cs`
- Modify: `Schlieren.UI/Services/BytecodeExecutionService.cs` (`WorkbenchRunResult`)
- Modify: `Schlieren.UI/ViewModels/WorkbenchViewModel.cs` method `AppendExpectedDiff` (~662)

**Interfaces:**
- Consumes: `InspectionAssembler.FromCanonical`, `InspectMismatchFormat.Balance`, `WorkbenchRunResult.Tx` / `.Block` / `.Result`
- Produces: engine-parseable `balance mismatch for 0x…: expected=0x…, actual=0x…` lines; one RESULTS line `DIAGNOSIS  TX.CREATE_SURCHARGE  PROVEN  …`

Workbench today prints `MISMATCH {short} balance expected {decimal} got {decimal}`. `FailureEvidenceFactory` only parses lines that start with `balance mismatch` and contain `expected=0x…, actual=0x…`. Until those strings match, the UI cannot get PROVEN even if RPC can.

No new panels, no restyle, do not launch the UI.

- [ ] **Step 1: Formatter + test**

```csharp
// Schlieren.Core/Execution/Inspect/InspectMismatchFormat.cs
namespace Schlieren.Core.Execution.Inspect;

public static class InspectMismatchFormat
{
    public static string Balance(string addressHex, string expectedHex, string actualHex)
        => $"balance mismatch for {addressHex}: expected={NormalizeHex(expectedHex)}, actual={NormalizeHex(actualHex)}";

    public static string Nonce(string addressHex, ulong expected, ulong actual)
        => $"nonce mismatch for {addressHex}: expected={expected}, actual={actual}";

    private static string NormalizeHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return "0x0";
        var s = hex.Trim();
        return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s : "0x" + s;
    }
}
```

```csharp
[Fact]
public void Balance_MatchesGoldenSenderLine()
{
    var line = InspectMismatchFormat.Balance(
        InspectGoldenCase.SenderHex,
        InspectGoldenCase.SenderExpected,
        InspectGoldenCase.SenderActual);
    Assert.Equal(InspectGoldenCase.SenderMismatch, line);
}
```

- [ ] **Step 2: Run formatter test**

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~InspectMismatchFormatTests --nologo
```

Expected: PASS.

- [ ] **Step 3: Return Tx + Block from the service; assemble in the VM after the diff**

One EVM run. Assembler is CPU-only. Do not call `ApplyTransactionAsync` again.

On `WorkbenchRunResult` add and always fill:

```csharp
public required Transaction Tx { get; init; }
public required BlockContext Block { get; init; }
```

In `AppendExpectedDiff`, keep the existing human-readable rows. Also collect `List<string> engineMismatches` with **full** addresses and hex quantities:

```csharp
engineMismatches.Add(InspectMismatchFormat.Balance(
    addr,
    InspectMapper.ToHex(expBal),
    InspectMapper.ToHex(actualBalParsed)));
```

After the MATCH/MISMATCH footer:

```csharp
if (engineMismatches.Count > 0)
{
    var inspect = InspectionAssembler.FromCanonical(
        new InspectRequest { Tx = run.Tx, Block = run.Block, Mismatches = engineMismatches },
        run.Result);
    var root = inspect.Diagnosis?.Root;
    if (root != null)
        AccountStateRows.Add($"  DIAGNOSIS  {root.RuleId}  {root.Grade}  {root.Why}");
}
```

- [ ] **Step 4: Build UI project (compile only)**

```powershell
dotnet build Schlieren.UI/Schlieren.UI.csproj --nologo
```

Expected: 0 errors. Do not start the UI.

- [ ] **Step 5: Commit**

```powershell
git add Schlieren.Core/Execution/Inspect/InspectMismatchFormat.cs Schlieren.Tests/Execution/Inspect/InspectMismatchFormatTests.cs Schlieren.UI/ViewModels/WorkbenchViewModel.cs
git commit -m "feat(workbench): diagnose fixture diffs via InspectionAssembler"
```

---

### Task J6: Joint gate (Either — last)

**Files:** none required. If `Schlieren.RPC.http` or an RPC README lists methods and `debug_inspect` is missing, add one line.

- [ ] **Step 1: Run the joint filter**

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter "FullyQualifiedName~Inspect|FullyQualifiedName~DebugInspect|FullyQualifiedName~DebugTrace|FullyQualifiedName~DebugWhyNot" --nologo
```

Expected: all PASS. After J1–J4 this is at least:

- Inspect / assembler / mapper / dto / formatter tests (Agent A)
- DebugInspect including golden PROVEN (Agent B)
- DebugTrace* still green
- DebugWhyNot 3/3 unchanged

- [ ] **Step 2: Run RPC namespace (regression)**

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~Schlieren.Tests.RPC --nologo
```

Expected: all PASS (was 56 before J3/J4; count may rise by 3).

- [ ] **Step 3: Do not claim done unless Step 1 and Step 2 were run in this session and printed PASS.**

- [ ] **Step 4: Commit docs only if a method list file was edited**

```powershell
git add docs/superpowers/plans/2026-08-15-inspect-ab-integration.md
# plus any RPC method-list file actually changed
git commit -m "docs(inspect): A/B integration gate"
```

---

## Done when

1. `debug_inspect` on `InspectGoldenCase` returns `diagnosis.root.ruleId = TX.CREATE_SURCHARGE` and `grade = PROVEN`.
2. The same constants feed a live `ApplyTransactionAsync` assembler test that asserts the same pair.
3. `gasCostDec` on `debug_inspect` is a JSON number.
4. `debug_whyNot` and `debug_trace*` tests still pass.
5. Workbench fixture diffs feed the assembler (J5) or J5 is explicitly deferred by the user.

## Out of scope

- Avalonia Case Inspector restyle or new tabs
- Changing `debug_trace*` `gasCostDec` from string to number
- `Schlieren.Core.Gas` typed schedule / `ForkGasSchedule` rewrite
- Calling EELS / `ethereum-spec-evm` from RPC
- Auto-starting the desktop UI
- Historical-fork conformance score chasing

## Risk notes

- `HandleDebugInspect` must pass **request** coinbase into `BlockContext` or the golden RPC test cannot PROVEN.
- Workbench human lines (`expected 1000000 got …`) must stay; engine lines are a second list with hex. Do not replace the human rows.
- If testhost locks `Schlieren.UI.dll`, build with `--no-dependencies` after killing testhost only.
