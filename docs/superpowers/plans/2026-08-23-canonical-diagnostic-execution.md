# Canonical Diagnostic Execution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete Schlieren's duplicate diagnostic evaluator and simplified intrinsic calculators so every diagnostic gas view is projected from the journal produced by the single canonical transaction run.

**Architecture:** `StateTransition.ApplyTransactionAsync` remains the only evaluator and optionally emits `ExecutionResult.Journal`. `JournalGasTree` remains the only gas-tree builder; a no-arithmetic compatibility projection preserves Avalonia and `debug_inspect` DTOs. All prospective intrinsic calculations require the selected block's `IForkRules`, while retrospective consumers read the canonical intrinsic journal event.

**Tech Stack:** C# 12, .NET 8, xUnit, System.Text.Json, Avalonia

**Spec:** `docs/superpowers/specs/2026-08-23-canonical-diagnostic-execution-design.md`

## Global Constraints

- Execute all work inline; do not delegate to subagents.
- Do not change canonical EVM, transaction, refund, or settlement behavior.
- Preserve existing `debug_inspect` and `debug_traceCall` JSON shapes.
- Do not reconstruct gas from flat trace steps or insert balancing buckets.
- Every production change follows a witnessed red/green test cycle.
- Preserve the known repository-wide fixture/campaign failure baseline and report it separately.

---

### Task 1: Require explicit fork rules for intrinsic gas

**Files:**
- Modify: `Schlieren.Tests/Execution/IntrinsicGasScheduleTests.cs`
- Modify: `Schlieren.Tests/RPC/EstimateGasRpcTests.cs`
- Modify: `Schlieren.Core/Execution/IntrinsicGas.cs`
- Modify: `Schlieren.RPC/Handlers/EthHandlers.cs`
- Modify: `Schlieren.EELS.Tests/Conformance/CancunOpcodeGasConformanceTests.cs`

**Interfaces:**
- Consumes: `IntrinsicGas.Compute(Transaction, IForkRules)` and `TryCompute(Transaction, IForkRules, out ulong)`.
- Produces: no ruleless public intrinsic API; RPC calculations use `BlockContext.Rules`.

- [ ] **Step 1: Write the failing architecture and Frontier boundary tests**

Add reflection assertions that no public static `Compute` overload accepts only `Transaction` and no `TryCompute` overload omits `IForkRules`. Add or extend the RPC estimate test so a Frontier CREATE request uses the active Frontier schedule rather than the latest schedule.

```csharp
[Fact]
public void PublicApi_RequiresExplicitForkRules()
{
    var methods = typeof(IntrinsicGas).GetMethods(BindingFlags.Public | BindingFlags.Static);
    Assert.DoesNotContain(methods, method => method.Name == "Compute" &&
        method.GetParameters().Select(p => p.ParameterType).SequenceEqual([typeof(Transaction)]));
    Assert.DoesNotContain(methods, method => method.Name == "TryCompute" &&
        method.GetParameters().All(p => p.ParameterType != typeof(IForkRules)));
}
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter "FullyQualifiedName~IntrinsicGasScheduleTests|FullyQualifiedName~EstimateGasRpcTests" --no-restore
```

Expected: the architecture assertion fails because ruleless overloads exist; the Frontier estimate exposes the latest-fork/create discrepancy if the handler is exercised on Frontier.

- [ ] **Step 3: Delete simplified and ruleless calculators**

Delete `IntrinsicGas.Compute(Transaction)`, `IntrinsicGas.TryCompute(Transaction, out ulong)`, and `EthHandlers.ComputeIntrinsicGas`. Replace every production and test caller with an explicit schedule. In RPC use:

```csharp
var intrinsicGas = IntrinsicGas.Compute(tx, blockContext.Rules);
```

Update fixed schedule tests to use `ForkRulesFactory.Latest` only when latest-fork semantics are the stated subject of the test.

- [ ] **Step 4: Run the focused tests and verify GREEN**

Run the command from Step 2 plus:

```powershell
dotnet test Schlieren.EELS.Tests/Schlieren.EELS.Tests.csproj --filter "FullyQualifiedName~CancunOpcodeGasConformanceTests" --no-restore
```

Expected: all selected tests pass.

- [ ] **Step 5: Commit**

```powershell
git add Schlieren.Core/Execution/IntrinsicGas.cs Schlieren.RPC/Handlers/EthHandlers.cs Schlieren.Tests/Execution/IntrinsicGasScheduleTests.cs Schlieren.Tests/RPC/EstimateGasRpcTests.cs Schlieren.EELS.Tests/Conformance/CancunOpcodeGasConformanceTests.cs
git commit -m "refactor(gas): require explicit intrinsic schedule"
```

---

### Task 2: Project legacy gas-tree DTOs from the canonical journal

**Files:**
- Create: `Schlieren.Core/Execution/Journal/LegacyGasTreeProjection.cs`
- Modify: `Schlieren.Core/Execution/GasTree.cs`
- Modify: `Schlieren.Core/Execution/Inspect/InspectionAssembler.cs`
- Modify: `Schlieren.RPC/Handlers/EthHandlers.cs`
- Modify: `Schlieren.Tests/Execution/Inspect/InspectionAssemblerTests.cs`
- Modify: `Schlieren.Tests/RPC/DebugInspectRpcTests.cs`

**Interfaces:**
- Consumes: `JournalGasTree.Build(ExecutionJournal, ExecutionResult)`.
- Produces: `LegacyGasTreeProjection.FromCanonical(ExecutionResult) : GasTreeNode`; unchanged `InspectGasNode` serialization.

- [ ] **Step 1: Write failing journal-required inspection tests**

Add a test that `InspectionAssembler.FromCanonical` rejects an `ExecutionResult` without a journal, and convert the live inspection test to enable the journal and assert the legacy tree total equals the journal-derived total.

```csharp
[Fact]
public void MissingJournal_IsRejectedInsteadOfReconstructedFromTrace()
{
    var error = Assert.Throws<InvalidOperationException>(() =>
        InspectionAssembler.FromCanonical(FrontierRequest([]), ExecutionResult.Success(21_000)));
    Assert.Contains("journal", error.Message, StringComparison.OrdinalIgnoreCase);
}
```

Keep the existing serialized key assertions in `DebugInspectRpcTests` and add a conservation-backed total assertion without adding JSON properties.

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter "FullyQualifiedName~InspectionAssemblerTests|FullyQualifiedName~DebugInspectRpcTests" --no-restore
```

Expected: missing-journal test fails because the assembler currently reconstructs from trace; live/RPC calls do not yet capture a journal.

- [ ] **Step 3: Implement the no-arithmetic compatibility projection**

Create a recursive projection that copies journal values only:

```csharp
public static GasTreeNode FromCanonical(ExecutionResult result)
{
    var journal = result.Journal ?? throw new InvalidOperationException(
        "Canonical execution journal is required to build a diagnostic gas tree.");
    return Map(JournalGasTree.Build(journal, result).Root);
}

private static GasTreeNode Map(JournalGasNode source) => new()
{
    Label = source.Label,
    Gas = source.Amount,
    Children = source.Children.Select(Map).ToList()
};
```

Adjust `GasTreeNode` construction as needed so `TotalGas` can be copied or guaranteed to equal the journal total without recomputation from different semantics. Delete the heuristic `GasTreeBuilder` from `GasTree.cs`, retaining only the compatibility model and renderer.

Set `tx.EnableJournal = true` in `HandleDebugInspect`. Update all inspection unit fixtures that expect a gas tree to execute canonically with journaling or attach a deliberately constructed journal where execution is irrelevant.

- [ ] **Step 4: Run tests and verify GREEN**

Run the command from Step 2 and the existing journal gas-tree tests.

Expected: inspection/RPC shapes and journal totals pass; missing journal throws explicitly.

- [ ] **Step 5: Commit**

```powershell
git add Schlieren.Core/Execution/Journal/LegacyGasTreeProjection.cs Schlieren.Core/Execution/GasTree.cs Schlieren.Core/Execution/Inspect/InspectionAssembler.cs Schlieren.RPC/Handlers/EthHandlers.cs Schlieren.Tests/Execution/Inspect/InspectionAssemblerTests.cs Schlieren.Tests/RPC/DebugInspectRpcTests.cs
git commit -m "refactor(inspect): project gas tree from canonical journal"
```

---

### Task 3: Move Avalonia Workbench and audit totals to journal evidence

**Files:**
- Modify: `Schlieren.UI/Services/BytecodeExecutionService.cs`
- Modify: `Schlieren.UI/ViewModels/WorkbenchViewModel.cs`
- Modify: `Schlieren.Tests/WorkbenchCanonicalGasTreeTests.cs`
- Create: `Schlieren.Tests/WorkbenchCanonicalAuditTests.cs`

**Interfaces:**
- Consumes: `LegacyGasTreeProjection.FromCanonical`, `IntrinsicGasChargedEvent`, and `TransactionSettledEvent`.
- Produces: unchanged `WorkbenchRunResult.GasTree` and `IntrinsicGas`; audit report total from the last canonical result.

- [ ] **Step 1: Write failing Workbench journal and audit tests**

Strengthen `GasTree_UsesSameResultAsExecution_NotSecondPath`:

```csharp
Assert.NotNull(run.Result.Journal);
var intrinsic = Assert.Single(run.Result.Journal!.Events.OfType<IntrinsicGasChargedEvent>());
Assert.Equal(intrinsic.Amount, run.IntrinsicGas);
Assert.Equal(run.Result.GasUsed, run.GasTree!.TotalGas);
```

Add an audit integration test that runs bytecode, exports Markdown, and asserts `Total Gas Used` equals the canonical `ExecutionResult.GasUsed`, including a case whose calldata/fork would make the former manual formula diverge.

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter "FullyQualifiedName~WorkbenchCanonicalGasTreeTests|FullyQualifiedName~WorkbenchCanonicalAuditTests|FullyQualifiedName~AuditReportExporterTests" --no-restore
```

Expected: journal assertion fails because Workbench does not enable it; audit total fails because the ViewModel manually sums trace and intrinsic constants.

- [ ] **Step 3: Use the canonical journal in Workbench**

Set `EnableJournal = true` on the Workbench transaction. Build `WorkbenchRunResult.GasTree` through `LegacyGasTreeProjection`. Read `IntrinsicGas` from the single `IntrinsicGasChargedEvent`, defaulting to zero only when the canonical journal contains no external intrinsic charge.

Store the last canonical `result.GasUsed` or journal settlement total in `WorkbenchViewModel` during `PopulateFromResult`. Replace the manual calldata/depth-one calculation in `GenerateAuditReportAsync` with that stored canonical value.

- [ ] **Step 4: Run tests and verify GREEN**

Run the command from Step 2. Expected: all selected tests pass and the rendered tree still satisfies existing Avalonia assertions.

- [ ] **Step 5: Commit**

```powershell
git add Schlieren.UI/Services/BytecodeExecutionService.cs Schlieren.UI/ViewModels/WorkbenchViewModel.cs Schlieren.Tests/WorkbenchCanonicalGasTreeTests.cs Schlieren.Tests/WorkbenchCanonicalAuditTests.cs
git commit -m "refactor(ui): use canonical journal gas evidence"
```

---

### Task 4: Delete trace-derived and duplicate diagnostic execution paths

**Files:**
- Create: `Schlieren.Tests/Execution/CanonicalExecutionArchitectureTests.cs`
- Delete: `Schlieren.Core/Execution/GasTreeFromTrace.cs`
- Modify: `Schlieren.Core/Execution/StateTransition.cs`
- Modify: `Schlieren.Core/Execution/ExecutionContext.cs`
- Modify: `Schlieren.Core/Execution/GasTree.cs`
- Verify: `Schlieren.Tests/Execution/StateTransitionJournalTests.cs`
- Verify: `Schlieren.Tests/Execution/GasTraceInvariantTests.cs`
- Verify: `Schlieren.Tests/Execution/TransactionValueJournalingTests.cs`

**Interfaces:**
- Consumes: canonical `ApplyTransactionAsync` and journal frame events.
- Produces: no diagnostic evaluator, frame side-channel, or trace-derived tree implementation.

- [ ] **Step 1: Write failing architecture tests**

Add reflection assertions for the forbidden StateTransition methods and source assertions for forbidden production types:

```csharp
[Theory]
[InlineData("ApplyTransactionWithGasTreeAsync")]
[InlineData("ApplyTransactionWithFrameAsync")]
public void StateTransition_HasNoDiagnosticEvaluator(string methodName)
{
    Assert.Null(typeof(StateTransition).GetMethod(
        methodName,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
}
```

Add a repository-root source scan asserting production `.cs` files contain no `GasTreeFromTrace`, `GasFrameNode`, `parentGasFrame`, or `GasTreeBuilder`. The test locates the solution root deterministically from `AppContext.BaseDirectory`.

- [ ] **Step 2: Run the architecture tests and verify RED**

Run:

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter "FullyQualifiedName~CanonicalExecutionArchitectureTests" --no-restore
```

Expected: failures identify both duplicate methods and legacy gas-tree/frame symbols.

- [ ] **Step 3: Delete the duplicate implementation and plumbing**

Delete both diagnostic methods in `StateTransition`. Remove `parentGasFrame` from `ExecuteInternalAsync` and all recursive calls. Remove `ExecutionContext.GasFrame`, `GasFrameNode`, `GasTreeBuilder`, and the `GasTreeFromTrace.cs` file. Do not alter journal arguments, frame IDs, subcall behavior, or canonical transaction logic.

- [ ] **Step 4: Run architecture and semantic tests and verify GREEN**

Run:

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter "FullyQualifiedName~CanonicalExecutionArchitectureTests|FullyQualifiedName~StateTransitionJournalTests|FullyQualifiedName~GasTraceInvariantTests|FullyQualifiedName~TransactionValueJournalingTests|FullyQualifiedName~ExceptionalChildGasTests|FullyQualifiedName~ChildRefundJournalTests" --no-restore
```

Expected: architecture tests pass; nested frames, non-additive CALL movements, exceptional burns, refunds, and conservation remain green.

- [ ] **Step 5: Commit**

```powershell
git add Schlieren.Core/Execution/StateTransition.cs Schlieren.Core/Execution/ExecutionContext.cs Schlieren.Core/Execution/GasTree.cs Schlieren.Tests/Execution/CanonicalExecutionArchitectureTests.cs
git rm Schlieren.Core/Execution/GasTreeFromTrace.cs
git commit -m "refactor(execution): delete diagnostic re-execution path"
```

---

### Task 5: Documentation and final compatibility verification

**Files:**
- Modify: `README.md`
- Modify: `docs/gas/GAS_RULE_INVENTORY.md`
- Modify: `docs/rpc/schlieren_traceJournal.md` only if current wording implies a second path

**Interfaces:**
- Consumes: completed canonical journal architecture.
- Produces: current documentation and verified unchanged contracts.

- [ ] **Step 1: Update current documentation**

State that the duplicate evaluator, heuristic gas tree, and ruleless intrinsic overloads have been removed. Mark the corresponding inventory findings resolved with the implementing commits; retain historical audit context.

- [ ] **Step 2: Run parser/source/deletion scans**

Run:

```powershell
rg -n "ApplyTransactionWithGasTree|ApplyTransactionWithFrame|GasTreeFromTrace|GasFrameNode|parentGasFrame|GasTreeBuilder|private static ulong ComputeIntrinsicGas|IntrinsicGas\.Compute\(tx\)" Schlieren.Core Schlieren.RPC Schlieren.UI -g "*.cs"
```

Expected: no matches.

- [ ] **Step 3: Run focused compatibility suites**

Run:

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter "FullyQualifiedName~CanonicalExecutionArchitectureTests|FullyQualifiedName~IntrinsicGasScheduleTests|FullyQualifiedName~StateTransitionJournalTests|FullyQualifiedName~GasTraceInvariantTests|FullyQualifiedName~JournalGasTreeTests|FullyQualifiedName~JournalTraceAssemblerTests|FullyQualifiedName~InspectionAssemblerTests|FullyQualifiedName~DebugInspectRpcTests|FullyQualifiedName~EstimateGasRpcTests|FullyQualifiedName~WorkbenchCanonicalGasTreeTests|FullyQualifiedName~WorkbenchCanonicalAuditTests" --no-restore
dotnet test Schlieren.EELS.Tests/Schlieren.EELS.Tests.csproj --filter "FullyQualifiedName~CancunOpcodeGasConformanceTests|FullyQualifiedName~Layer1DiagnosisBridgeTests|FullyQualifiedName~TypedDiscrepancyTests" --no-restore
```

Expected: all focused tests pass.

- [ ] **Step 4: Run full repository verification**

Run:

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --no-restore
```

Record exact pass/fail/skip counts. Compare failures to the known fixture/campaign baseline; do not claim a full green suite if unrelated failures remain.

- [ ] **Step 5: Check and commit documentation**

```powershell
git diff --check
git add README.md docs/gas/GAS_RULE_INVENTORY.md docs/rpc/schlieren_traceJournal.md
git commit -m "docs: record canonical diagnostic execution"
```
