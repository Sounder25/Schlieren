# Typed State-Effect Journal and Frame-Aware Security Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Record authoritative state effects in the canonical execution journal and replace depth-based reentrancy and storage-collision heuristics with proof-linked frame-aware analysis.

**Architecture:** `StateTransition.ApplyTransactionAsync` remains the only evaluator. Existing frame overlays and opcode boundaries append immutable lifecycle and state-effect events; a single `JournalAnalysis` projection validates ancestry and derives execution/persistence dispositions; one `JournalSecurityAnalyzer` produces findings consumed by RPC, React, regression tests, and the remaining compatibility UI.

**Tech Stack:** C# 12, .NET 8, xUnit, System.Text.Json, TypeScript 6, React 19, Zustand, Vitest

**Spec:** `docs/superpowers/specs/2026-08-23-state-effect-journal-security-design.md`

## Global Constraints

- `StateTransition.ApplyTransactionAsync` remains the only transaction evaluator.
- Journal instrumentation must not write state, replay execution, or change gas, output, logs, receipts, or commit behavior.
- Every state effect is immutable and linked to its frame; opcode-caused effects also share an exact `InstructionId` with the corresponding `OpcodeGasEvent`.
- Execution disposition is derived from the full ancestor chain: `Survived` or `Reverted` with `RevertedByFrameId`.
- Persistence is independent: `CommittedToState`, `SimulationDiscarded`, or `NotApplicable` for reverted effects.
- Reverted attack attempts remain visible but cannot become high-severity committed-vulnerability findings.
- `debug_inspect` and `debug_traceCall` JSON shapes must remain identical.
- `schlieren_traceJournal` changes are additive and state/security evidence is returned by default.
- No global `IGlobalState` observer may reinterpret low-level setter calls or duplicate overlay propagation.
- No flat-trace fallback security analyzer may remain in active use at completion.
- Do not touch the user's main checkout; execute in `C:\projects\Schlieren\.worktrees\journal-gas-tree-rpc-react`.

## File and responsibility map

- `Schlieren.Core/Execution/Journal/ExecutionJournal.cs`: sequence, frame, instruction, and effect ID allocation.
- `Schlieren.Core/Execution/Journal/StateEffectEvents.cs`: typed lifecycle and state-effect event records and enums.
- `Schlieren.Core/Execution/Journal/JournalAnalysis.cs`: one-pass validation, indexes, ancestry, and final dispositions.
- `Schlieren.Core/Execution/Journal/JournalAnalysisException.cs`: typed malformed-journal failure.
- `Schlieren.Core/Execution/Journal/JournalSecurityAnalyzer.cs`: sole reentrancy and storage-collision rule engine.
- `Schlieren.Core/Execution/Journal/SecurityFinding.cs`: finding, severity, grade, and evidence models.
- `Schlieren.Core/Execution/ExecutionContext.cs`: active instruction correlation and semantic event helpers.
- `Schlieren.Core/Execution/EvmMachine.cs`: allocate one instruction ID before each opcode.
- `Schlieren.Core/Execution/StateTransition.cs`: explicit call type, frame lifecycle, transaction persistence, transfers, nonce, and code effects.
- `Schlieren.Core/Opcodes/StorageOpcodes.cs`: persistent and transient storage observations.
- `Schlieren.Core/Opcodes/LoggingOpcodes.cs`: typed log observations.
- `Schlieren.Core/Opcodes/SystemOpcodes.cs`: explicit subcall type, CREATE/code lifecycle, and SELFDESTRUCT effects.
- `Schlieren.Core/Execution/Journal/JournalTraceDtos.cs`: additive analyzed-effect and security DTOs.
- `Schlieren.Core/Execution/Journal/JournalTraceAssembler.cs`: event/analysis/DTO projection.
- `schlieren-ui/src/engine/store.ts`: React DTO types.
- `schlieren-ui/src/engine/journal.ts`: rolling-upgrade-safe response validation/defaults.
- `schlieren-ui/src/engine/journal-view.ts`: pure state/security presentation helpers.
- `schlieren-ui/src/views/Workbench/SecurityEvidence.tsx`: proof-linked React evidence view.
- `Schlieren.UI/Services/BytecodeExecutionService.cs`: expose canonical analysis to remaining Avalonia consumers.
- `Schlieren.UI/ViewModels/CallTopologyViewModel.cs`: consume explicit frames instead of depth reconstruction.
- `Schlieren.UI/ViewModels/WorkbenchViewModel.cs`: consume canonical security findings and remove synthetic security execution.
- `Schlieren.Tests/Execution/*`: engine and journal behavior tests.
- `Schlieren.Tests/Security/*`: journal-native detector tests and legacy-removal gates.
- `Schlieren.Tests/RPC/*`: additive/new endpoint and immutable legacy contract tests.

---

### Task 1: Make call identity explicit at the canonical recursion boundary

**Files:**
- Modify: `Schlieren.Core/Execution/ExecutionContext.cs`
- Modify: `Schlieren.Core/Execution/StateTransition.cs`
- Modify: `Schlieren.Core/Opcodes/SystemOpcodes.cs`
- Create: `Schlieren.Tests/Execution/ExplicitCallTypeJournalTests.cs`

**Interfaces:**
- Produces: `ExecutionContext.SubCall` with signature `Func<Transaction, bool, Address?, Address?, CallType, Task<ExecutionResult>>?`.
- Produces: `StateTransition.ExecuteInternalAsync(..., CallType callType, ...)` with no `DetermineCallType` inference.
- Consumed by: frame events, storage-collision analysis, and all CALL-family opcodes.

- [ ] **Step 1: Write failing call-identity tests**

Create integration cases that execute a tiny child (`STOP`) through CALL, STATICCALL, CALLCODE, DELEGATECALL, CREATE, and CREATE2, enable the journal, and assert the child `FrameEnteredEvent.CallType`.

For CALLCODE and DELEGATECALL, assert equal storage-owner address but distinct call types and the expected external `CodeAddress`.

- [ ] **Step 2: Run the tests and verify the current conflation**

Run:

```powershell
dotnet test .\Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~ExplicitCallTypeJournalTests" --nologo -v minimal
```

Expected: FAIL because CALLCODE is journaled as DELEGATECALL.

- [ ] **Step 3: Pass call type through the recursive interface**

Change the callback contract to:

```csharp
public Func<Transaction, bool, Address?, Address?, CallType, Task<ExecutionResult>>? SubCall { get; set; }
```

Add a required `CallType callType` argument to `ExecuteInternalAsync`. Pass `CallType.Root` from the top-level call. At each opcode call site pass the exact value:

```csharp
await context.SubCall(tx, context.IsStatic, null, null, CallType.Call);
await context.SubCall(tx, true, null, null, CallType.StaticCall);
await context.SubCall(tx, context.IsStatic, null, codeAddress, CallType.CallCode);
await context.SubCall(tx, context.IsStatic, null, codeAddress, CallType.DelegateCall);
await context.SubCall(tx, false, newAddress, null, CallType.Create);
await context.SubCall(tx, false, newAddress, null, CallType.Create2);
```

Use `callType` for `FrameEnteredEvent` and `SetCallContext`, then delete `DetermineCallType`.

- [ ] **Step 4: Verify behavior and existing nested frames**

Run:

```powershell
dotnet test .\Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~ExplicitCallTypeJournalTests|FullyQualifiedName~GasTraceInvariantTests|FullyQualifiedName~StateTransitionJournalTests" --nologo -v minimal
```

Expected: PASS; gas conservation and nested ownership remain green.

- [ ] **Step 5: Commit the explicit metadata correction**

```powershell
git add Schlieren.Core/Execution/ExecutionContext.cs Schlieren.Core/Execution/StateTransition.cs Schlieren.Core/Opcodes/SystemOpcodes.cs Schlieren.Tests/Execution/ExplicitCallTypeJournalTests.cs
git commit -m "fix(journal): preserve explicit call identity"
```

### Task 2: Add instruction correlation and typed event contracts

**Files:**
- Modify: `Schlieren.Core/Execution/Journal/ExecutionJournal.cs`
- Create: `Schlieren.Core/Execution/Journal/StateEffectEvents.cs`
- Modify: `Schlieren.Core/Execution/ExecutionContext.cs`
- Modify: `Schlieren.Core/Execution/EvmMachine.cs`
- Create: `Schlieren.Tests/Execution/JournalInstructionCorrelationTests.cs`
- Create: `Schlieren.Tests/Execution/StateEffectEventModelTests.cs`

**Interfaces:**
- Produces: nullable `ExecutionJournalEvent.InstructionId` and journal-assigned `StateEffectEvent.EffectId`.
- Produces: `ExecutionJournal.BeginInstruction()` and `ExecutionContext.CurrentInstructionId`.
- Produces: enums and records used by every later task.

- [ ] **Step 1: Write failing immutable-model tests**

Test monotonic IDs and read-only event storage:

```csharp
[Fact]
public void Journal_AssignsStableSequenceInstructionAndEffectIdentity()
{
    var journal = new ExecutionJournal();
    var firstInstruction = journal.BeginInstruction();
    var secondInstruction = journal.BeginInstruction();
    Assert.True(secondInstruction > firstInstruction);

    journal.Record(new TestStateEffectEvent
    {
        Scope = StateEffectScope.Frame,
        FrameId = 1,
        InstructionId = firstInstruction,
        Pc = 0,
        Opcode = 0x54
    });

    var effect = Assert.IsType<TestStateEffectEvent>(Assert.Single(journal.Events));
    Assert.Equal(0, effect.Sequence);
    Assert.Equal(1, effect.EffectId);
    Assert.Equal(firstInstruction, effect.InstructionId);
}

private sealed record TestStateEffectEvent : StateEffectEvent;
```

Add a real `PUSH1; POP; STOP` execution test asserting every `OpcodeGasEvent` has a non-null, distinct, monotonic `InstructionId`. Task 4 adds the state-effect-to-opcode correlation assertion when concrete storage events exist.

- [ ] **Step 2: Verify the contracts do not exist**

Run:

```powershell
dotnet test .\Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~JournalInstructionCorrelationTests|FullyQualifiedName~StateEffectEventModelTests" --nologo -v minimal
```

Expected: build/test failure for missing types and members.

- [ ] **Step 3: Define the typed vocabulary**

Create these exact foundations in `StateEffectEvents.cs`:

```csharp
public enum FrameStateResolution { Commit, Rollback }
public enum TransactionPersistenceOutcome { CommittedToState, SimulationDiscarded }
public enum ExecutionDisposition { Survived, Reverted }
public enum PersistenceDisposition { CommittedToState, SimulationDiscarded, NotApplicable }
public enum StateEffectScope { Transaction, Frame }
public enum BalanceTransferReason { TransactionValue, CallValue, SelfDestruct, GasRefund, MinerFee, ProtocolReward }
public enum CodeChangeAction { Created, Installed, Cleared, Deleted, DelegationDesignated }

public abstract record StateEffectEvent : ExecutionJournalEvent
{
    public long EffectId { get; internal init; }
    public required StateEffectScope Scope { get; init; }
    public int? Pc { get; init; }
    public byte? Opcode { get; init; }
}
```

Add the lifecycle records `FrameStateCheckpointEvent`, `FrameStateResolvedEvent`, and `TransactionPersistenceEvent`. Concrete storage and account-effect records are introduced in Tasks 4 and 5. Use `BigInteger` and `Address` in Core; do not pre-render hex strings.

- [ ] **Step 4: Allocate instruction and effect IDs once**

Add monotonic counters:

```csharp
private long _nextInstructionId = 1;
private long _nextEffectId = 1;

internal long BeginInstruction() => _nextInstructionId++;
```

In `Record`, assign `EffectId` when `entry is StateEffectEvent`. Add `long? InstructionId` to `ExecutionJournalEvent`. In `EvmMachine`, allocate before `IOpcode.ExecuteAsync`, set `ExecutionContext.CurrentInstructionId`, use it on `OpcodeGasEvent`, and clear it in `finally`.

- [ ] **Step 5: Run focused and parity tests**

```powershell
dotnet test .\Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~JournalInstructionCorrelationTests|FullyQualifiedName~StateEffectEventModelTests|FullyQualifiedName~EvmMachineJournalTests|FullyQualifiedName~StateTransitionJournalTests" --nologo -v minimal
```

Expected: PASS and journal-on/off trace parity remains intact.

- [ ] **Step 6: Commit the journal contracts**

```powershell
git add Schlieren.Core/Execution/Journal/ExecutionJournal.cs Schlieren.Core/Execution/Journal/StateEffectEvents.cs Schlieren.Core/Execution/ExecutionContext.cs Schlieren.Core/Execution/EvmMachine.cs Schlieren.Tests/Execution/JournalInstructionCorrelationTests.cs Schlieren.Tests/Execution/StateEffectEventModelTests.cs
git commit -m "feat(journal): add state effect identity contracts"
```

### Task 3: Record frame lifecycle and derive final dispositions

**Files:**
- Modify: `Schlieren.Core/Execution/StateTransition.cs`
- Modify: `Schlieren.Core/Execution/ExecutionResult.cs`
- Modify: `Schlieren.Core/Opcodes/SystemOpcodes.cs`
- Create: `Schlieren.Core/Execution/Journal/JournalAnalysisException.cs`
- Create: `Schlieren.Core/Execution/Journal/JournalAnalysis.cs`
- Create: `Schlieren.Tests/Execution/JournalDispositionTests.cs`
- Create: `Schlieren.Tests/Execution/JournalAnalysisInvariantTests.cs`

**Interfaces:**
- Consumes: lifecycle records and `StateEffectEvent` from Task 2.
- Produces: `JournalAnalysis.Build(ExecutionJournal)`.
- Produces: `AnalyzedStateEffect` with execution/persistence dispositions and `RevertedByFrameId`.
- Produces: internal `ExecutionResult.JournalFrameId` so CREATE/CREATE2 post-initcode validation resolves the correct checkpoint exactly once.

- [ ] **Step 1: Write failing nested rollback tests**

Build journals directly with a test-only `TestStateEffectEvent : StateEffectEvent` for invariant unit tests and use real nested calls for integration. Frame-scoped effects require a valid frame; transaction-scoped effects require `FrameId = null`. Cover these assertions:

```csharp
Assert.Equal(ExecutionDisposition.Reverted, childWrite.ExecutionDisposition);
Assert.Equal(parentFrameId, childWrite.RevertedByFrameId);
Assert.Equal(PersistenceDisposition.NotApplicable, childWrite.PersistenceDisposition);

Assert.Equal(ExecutionDisposition.Survived, simulatedWrite.ExecutionDisposition);
Assert.Equal(PersistenceDisposition.SimulationDiscarded, simulatedWrite.PersistenceDisposition);
```

Also test committed top-level execution, child rollback with successful parent, successful child followed by parent rollback, successful CREATE code deposit, and successful initcode followed by CREATE code-deposit failure. The failed-deposit CREATE checkpoint must resolve once as `Rollback`, never first as `Commit`.

- [ ] **Step 2: Write malformed-journal tests**

Require `JournalAnalysisException` for an effect with an unknown frame, duplicate checkpoint, missing resolution, resolution without checkpoint, and multiple persistence events. Assert the exception exposes a stable `Code`, such as `UnknownEffectFrame`.

- [ ] **Step 3: Run tests to verify lifecycle evidence is absent**

```powershell
dotnet test .\Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~JournalDispositionTests|FullyQualifiedName~JournalAnalysisInvariantTests" --nologo -v minimal
```

Expected: FAIL for missing analysis and lifecycle events.

- [ ] **Step 4: Emit checkpoint, resolution, and persistence events**

Immediately after each `StateOverlay` is created, record `FrameStateCheckpointEvent`. Add internal `ExecutionResult.JournalFrameId` and set it in the existing `CompleteFrame` closure before returning the result.

For CALL, CALLCODE, DELEGATECALL, STATICCALL, precompiles, and failed initcode, record one semantic resolution from the final child outcome:

```csharp
journal.Record(new FrameStateResolvedEvent
{
    FrameId = frameId,
    ParentFrameId = parentFrameId,
    Resolution = outcome.IsSuccess
        ? FrameStateResolution.Commit
        : FrameStateResolution.Rollback
});
```

For successful CREATE/CREATE2 initcode, do not resolve the checkpoint inside `CompleteFrame`: code-size, EF-prefix, and code-deposit-gas validation can still invalidate it. Use `result.JournalFrameId` at the existing CREATE/CREATE2 post-initcode branches to append exactly one `Commit` after code installation or `Rollback` before creation cleanup. Apply the same deferral to top-level contract creation in `ApplyTransactionAsync`.

Add a journal guard that rejects a second resolution for the same frame. At top-level completion append one `TransactionPersistenceEvent` using the original `commit` argument. Do this through the existing `Finish` path so validation and early execution failures cannot emit duplicates.

- [ ] **Step 5: Implement one-pass validated analysis**

Expose these models:

```csharp
public sealed record AnalyzedStateEffect(
    StateEffectEvent Effect,
    ExecutionDisposition ExecutionDisposition,
    PersistenceDisposition PersistenceDisposition,
    long? RevertedByFrameId);

public sealed record JournalFrameAnalysis(
    long Id,
    long? ParentId,
    int Depth,
    CallType CallType,
    Address ContractAddress,
    Address? CodeAddress,
    FrameStateResolution Resolution,
    IReadOnlyList<long> AncestorIds);

public sealed class JournalAnalysis
{
    public IReadOnlyDictionary<long, JournalFrameAnalysis> Frames { get; }
    public IReadOnlyList<AnalyzedStateEffect> StateEffects { get; }
    public static JournalAnalysis Build(ExecutionJournal journal);
}
```

Index frames and resolutions in one pass, validate after indexing, then walk cached ancestor paths. The nearest failed frame on the path becomes `RevertedByFrameId`. Transaction-scoped protocol effects are `Survived` and receive persistence from `TransactionPersistenceEvent`; they do not acquire a synthetic frame. Do not mutate journal events.

- [ ] **Step 6: Verify lifecycle, gas, and state parity**

```powershell
dotnet test .\Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~JournalDispositionTests|FullyQualifiedName~JournalAnalysisInvariantTests|FullyQualifiedName~GasTraceInvariantTests|FullyQualifiedName~StateTransitionJournalTests" --nologo -v minimal
```

Expected: PASS.

- [ ] **Step 7: Commit lifecycle analysis**

```powershell
git add Schlieren.Core/Execution/StateTransition.cs Schlieren.Core/Execution/ExecutionResult.cs Schlieren.Core/Opcodes/SystemOpcodes.cs Schlieren.Core/Execution/Journal/JournalAnalysisException.cs Schlieren.Core/Execution/Journal/JournalAnalysis.cs Schlieren.Tests/Execution/JournalDispositionTests.cs Schlieren.Tests/Execution/JournalAnalysisInvariantTests.cs
git commit -m "feat(journal): derive state effect dispositions"
```

### Task 4: Capture persistent and transient storage evidence

**Files:**
- Modify: `Schlieren.Core/Execution/Journal/StateEffectEvents.cs`
- Modify: `Schlieren.Core/Execution/ExecutionContext.cs`
- Modify: `Schlieren.Core/Opcodes/StorageOpcodes.cs`
- Create: `Schlieren.Tests/Execution/StorageEffectJournalTests.cs`
- Create: `Schlieren.Tests/Execution/TransientStorageEffectJournalTests.cs`

**Interfaces:**
- Produces: `StorageReadEvent`, `StorageWriteEvent`, `TransientStorageReadEvent`, and `TransientStorageWriteEvent`.
- Consumes: `ExecutionContext.CurrentInstructionId`, frame IDs, and `JournalAnalysis`.

- [ ] **Step 1: Write failing persistent-storage tests**

Execute SLOAD/SSTORE programs and assert exact address, slot, values, warm status, PC, opcode, frame ID, effect ID, and shared instruction ID:

```csharp
var write = Assert.Single(result.Journal!.Events.OfType<StorageWriteEvent>());
Assert.Equal(storageAddress, write.StorageAddress);
Assert.Equal(original, write.OriginalValue);
Assert.Equal(current, write.PreviousValue);
Assert.Equal(requested, write.Value);
Assert.Equal(0x55, write.Opcode);
Assert.Contains(result.Journal.Events.OfType<OpcodeGasEvent>(),
    op => op.InstructionId == write.InstructionId && op.Name == "SSTORE");
```

Include a no-op write and a nested write reverted by its parent.

- [ ] **Step 2: Write failing transient-storage tests**

Cover TSTORE/TLOAD in one frame, a child commit into its parent transient overlay, and child/CREATE rollback. Assert previous/current values and final disposition.

- [ ] **Step 3: Run the tests and verify no typed events exist**

```powershell
dotnet test .\Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~StorageEffectJournalTests|FullyQualifiedName~TransientStorageEffectJournalTests" --nologo -v minimal
```

Expected: FAIL because the opcodes do not record state-effect events.

- [ ] **Step 4: Add semantic recording helpers**

Add the four concrete records to `StateEffectEvents.cs` with these payloads:

```csharp
public sealed record StorageReadEvent : StateEffectEvent
{
    public required Address StorageAddress { get; init; }
    public required BigInteger Slot { get; init; }
    public required BigInteger Value { get; init; }
    public required bool IsWarm { get; init; }
}

public sealed record StorageWriteEvent : StateEffectEvent
{
    public required Address StorageAddress { get; init; }
    public required BigInteger Slot { get; init; }
    public required BigInteger OriginalValue { get; init; }
    public required BigInteger PreviousValue { get; init; }
    public required BigInteger Value { get; init; }
    public required bool IsWarm { get; init; }
}

public sealed record TransientStorageReadEvent : StateEffectEvent
{
    public required Address StorageAddress { get; init; }
    public required BigInteger Slot { get; init; }
    public required BigInteger Value { get; init; }
}

public sealed record TransientStorageWriteEvent : StateEffectEvent
{
    public required Address StorageAddress { get; init; }
    public required BigInteger Slot { get; init; }
    public required BigInteger PreviousValue { get; init; }
    public required BigInteger Value { get; init; }
}
```

Add helpers that no-op when `Journal` or `JournalFrameId` is absent:

```csharp
internal void RecordStorageWrite(
    BigInteger slot,
    BigInteger original,
    BigInteger previous,
    BigInteger value,
    bool isWarm);

internal void RecordTransientStorageWrite(
    BigInteger slot,
    BigInteger previous,
    BigInteger value);
```

Each helper copies the active frame, instruction, PC, and opcode metadata into one immutable event.

- [ ] **Step 5: Instrument storage opcodes at existing authoritative reads**

For SLOAD, record after the existing load and warm/cold determination. For SSTORE, record after gas validation and immediately before/after the existing `Store` call using values already loaded for EIP-2200. For TSTORE, load the prior transient value before the existing store; this is local memory access, not remote state IO.

- [ ] **Step 6: Verify storage behavior and fork suites**

```powershell
dotnet test .\Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~StorageEffectJournalTests|FullyQualifiedName~TransientStorageEffectJournalTests|FullyQualifiedName~Sstore|FullyQualifiedName~Tstore|FullyQualifiedName~StateTransitionJournalTests" --nologo -v minimal
dotnet test .\Schlieren.EELS.Tests\Schlieren.EELS.Tests.csproj --filter "FullyQualifiedName~Storage|FullyQualifiedName~Transient" --nologo -v minimal
```

Expected: PASS with journal-on/off gas and post-state parity.

- [ ] **Step 7: Commit storage evidence**

```powershell
git add Schlieren.Core/Execution/Journal/StateEffectEvents.cs Schlieren.Core/Execution/ExecutionContext.cs Schlieren.Core/Opcodes/StorageOpcodes.cs Schlieren.Tests/Execution/StorageEffectJournalTests.cs Schlieren.Tests/Execution/TransientStorageEffectJournalTests.cs
git commit -m "feat(journal): record typed storage effects"
```

### Task 5: Capture transfers, nonce, code, logs, and self-destruct

**Files:**
- Modify: `Schlieren.Core/Execution/Journal/StateEffectEvents.cs`
- Modify: `Schlieren.Core/Execution/ExecutionContext.cs`
- Modify: `Schlieren.Core/Execution/StateTransition.cs`
- Modify: `Schlieren.Core/Opcodes/LoggingOpcodes.cs`
- Modify: `Schlieren.Core/Opcodes/SystemOpcodes.cs`
- Create: `Schlieren.Tests/Execution/AccountEffectJournalTests.cs`
- Create: `Schlieren.Tests/Execution/LogAndSelfDestructJournalTests.cs`
- Create: `Schlieren.Tests/Execution/JournalBehaviorParityTests.cs`

**Interfaces:**
- Produces: `BalanceTransferEvent`, `NonceChangedEvent`, `CodeChangedEvent`, `LogEmittedEvent`, and `SelfDestructEvent`.
- Consumes: the event ID/instruction/frame contracts and disposition analysis.

- [ ] **Step 1: Write failing effect-coverage tests**

Add focused executions for transaction value, internal CALL value, gas refund, miner fee, CREATE/CREATE2 success and failure, EIP-7702 designation, LOG0/LOG4, and SELFDESTRUCT on both sides of EIP-6780. Assert typed reasons rather than rendered descriptions.

```csharp
var transfer = Assert.Single(events.OfType<BalanceTransferEvent>(),
    e => e.Reason == BalanceTransferReason.CallValue);
Assert.Equal(caller, transfer.From);
Assert.Equal(callee, transfer.To);
Assert.Equal(value, transfer.Amount);
```

For failed parent frames, assert log/transfer/code effects are retained in the journal but analyzed as reverted.

- [ ] **Step 2: Write failing behavior-parity theories**

For every scenario, execute with journaling off and on from identical seeded state and compare:

```csharp
Assert.Equal(without.Result.IsSuccess, withJournal.Result.IsSuccess);
Assert.Equal(without.Result.Error, withJournal.Result.Error);
Assert.Equal(without.Result.GasUsed, withJournal.Result.GasUsed);
Assert.Equal(without.Result.GasRefundCounter, withJournal.Result.GasRefundCounter);
Assert.Equal(without.Result.ReturnData, withJournal.Result.ReturnData);
Assert.Equal(
    JsonSerializer.Serialize(without.Result.Logs),
    JsonSerializer.Serialize(withJournal.Result.Logs));
Assert.Equal(
    NormalizeState(without.State.Snapshot()),
    NormalizeState(withJournal.State.Snapshot()));
```

Define `NormalizeState` in the test file to order accounts by address and storage by slot, then serialize balance, nonce, code, storage, creation marker, and deletion marker. `RunScenario(bool enableJournal)` returns a test-local record containing the `ExecutionResult` and `GlobalState State` so both snapshots are read through the same helper.

Add a `CountingGlobalState : IGlobalState` test double around the same seeded state and assert journaling does not increase balance, nonce, code, or storage read counts for the covered scenarios. This guards the requirement that instrumentation never adds remote fork-provider reads.

- [ ] **Step 3: Run focused tests to establish red state**

```powershell
dotnet test .\Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~AccountEffectJournalTests|FullyQualifiedName~LogAndSelfDestructJournalTests|FullyQualifiedName~JournalBehaviorParityTests" --nologo -v minimal
```

Expected: FAIL for missing typed events; parity cases that compile remain green.

- [ ] **Step 4: Emit semantic effects at authoritative boundaries**

Add these concrete records with typed payloads:

```csharp
public sealed record BalanceTransferEvent : StateEffectEvent
{
    public Address? From { get; init; }
    public Address? To { get; init; }
    public required BigInteger Amount { get; init; }
    public required BalanceTransferReason Reason { get; init; }
}

public sealed record NonceChangedEvent : StateEffectEvent
{
    public required Address Address { get; init; }
    public required ulong Previous { get; init; }
    public required ulong Current { get; init; }
    public required string Reason { get; init; }
}

public sealed record CodeChangedEvent : StateEffectEvent
{
    public required Address Address { get; init; }
    public required CodeChangeAction Action { get; init; }
    public required IReadOnlyList<byte> PreviousCodeHash { get; init; }
    public required IReadOnlyList<byte> NewCodeHash { get; init; }
    public required int PreviousSize { get; init; }
    public required int NewSize { get; init; }
}

public sealed record LogEmittedEvent : StateEffectEvent
{
    public required Address Address { get; init; }
    public required IReadOnlyList<BigInteger> Topics { get; init; }
    public required IReadOnlyList<byte> Data { get; init; }
}

public sealed record SelfDestructEvent : StateEffectEvent
{
    public required Address Contract { get; init; }
    public required Address Beneficiary { get; init; }
    public required BigInteger TransferredBalance { get; init; }
    public required bool DeletionEligible { get; init; }
    public required bool DeletionScheduled { get; init; }
}
```

Use small recorder methods that accept fully known semantic values. Emit one transfer event per logical transfer, not one event per `SetBalance`. Hash code with `CryptoUtils.Keccak256` and store previous/new sizes. Emit the log event where `TransactionLog` is created. Emit SELFDESTRUCT before balance mutation with deletion eligibility already computed from fork rules.

Extend `ExecutionJournal.Record` defensive copying to code hashes, log topics, and log data so callers cannot mutate recorded evidence.

Do not add diagnostic reads. When a previous value is not already available, use the value fetched by the existing execution branch; if no value is needed by execution, omit the optional previous field.

- [ ] **Step 5: Verify coverage and parity**

```powershell
dotnet test .\Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~AccountEffectJournalTests|FullyQualifiedName~LogAndSelfDestructJournalTests|FullyQualifiedName~JournalBehaviorParityTests|FullyQualifiedName~StateTransitionJournalTests" --nologo -v minimal
dotnet test .\Schlieren.EELS.Tests\Schlieren.EELS.Tests.csproj --filter "FullyQualifiedName~Create|FullyQualifiedName~SelfDestruct|FullyQualifiedName~Log|FullyQualifiedName~Eip7702" --nologo -v minimal
```

Expected: PASS with no gas or post-state difference.

- [ ] **Step 6: Commit complete state-effect capture**

```powershell
git add Schlieren.Core/Execution/Journal/StateEffectEvents.cs Schlieren.Core/Execution/ExecutionContext.cs Schlieren.Core/Execution/StateTransition.cs Schlieren.Core/Opcodes/LoggingOpcodes.cs Schlieren.Core/Opcodes/SystemOpcodes.cs Schlieren.Tests/Execution/AccountEffectJournalTests.cs Schlieren.Tests/Execution/LogAndSelfDestructJournalTests.cs Schlieren.Tests/Execution/JournalBehaviorParityTests.cs
git commit -m "feat(journal): capture canonical account effects"
```

### Task 6: Replace security heuristics with one journal-native analyzer

**Files:**
- Create: `Schlieren.Core/Execution/Journal/SecurityFinding.cs`
- Create: `Schlieren.Core/Execution/Journal/JournalSecurityAnalyzer.cs`
- Create: `Schlieren.Tests/Security/JournalReentrancyAnalyzerTests.cs`
- Create: `Schlieren.Tests/Security/JournalStorageCollisionAnalyzerTests.cs`
- Create: `Schlieren.Tests/Security/SecurityFindingGradeTests.cs`

**Interfaces:**
- Consumes: `JournalAnalysis` only.
- Produces: `JournalSecurityAnalyzer.Analyze(JournalAnalysis)` returning `IReadOnlyList<SecurityFinding>`.
- Produces: stable rule IDs `SEC.REENTRANCY.REENTRY`, `SEC.REENTRANCY.POST_WRITE`, and `SEC.STORAGE.DELEGATE_COLLISION`.

- [ ] **Step 1: Write failing reentrancy proof tests**

Cover true same-storage-context re-entry, normal nested calls, ordinary proxy DELEGATECALL, a post-interaction write, reverted child, and successful child later reverted by parent. Assert evidence links:

```csharp
var finding = Assert.Single(findings, f => f.RuleId == "SEC.REENTRANCY.REENTRY");
Assert.Equal(reentryFrameId, finding.PrimaryFrameId);
Assert.Contains(storageWriteSequence, finding.SupportingEventSequences);
Assert.Equal(ExecutionDisposition.Reverted, finding.ExecutionDisposition);
Assert.Equal(SecuritySeverity.Info, finding.Severity);
```

- [ ] **Step 2: Write failing storage-collision proof tests**

Cover EIP-1967 implementation/admin slots, slot zero, a non-reserved slot, CALLCODE versus DELEGATECALL, repeated depths in sibling frames, and parent rollback. Assert code owner and storage owner come from explicit frame metadata, not neighboring trace steps.

- [ ] **Step 3: Run security tests to verify analyzer is absent**

```powershell
dotnet test .\Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~JournalReentrancyAnalyzerTests|FullyQualifiedName~JournalStorageCollisionAnalyzerTests|FullyQualifiedName~SecurityFindingGradeTests" --nologo -v minimal
```

Expected: build/test failure for missing analyzer and finding types.

- [ ] **Step 4: Implement proof-bounded findings**

Define `SecurityCategory { Reentrancy, StorageCollision }` and `SecuritySeverity { Info, Medium, Critical }`, then define:

```csharp
public sealed record SecurityFinding(
    string Id,
    string RuleId,
    SecurityCategory Category,
    SecuritySeverity Severity,
    DiagnosisGrade FactGrade,
    long PrimaryFrameId,
    long? InstructionId,
    IReadOnlyList<long> SupportingEventSequences,
    IReadOnlyList<long> FrameAncestry,
    ExecutionDisposition ExecutionDisposition,
    PersistenceDisposition PersistenceDisposition,
    IReadOnlyList<Address> Addresses,
    IReadOnlyList<BigInteger> StorageSlots,
    string Summary,
    string Limitation);
```

Generate `Id` deterministically from rule ID and primary evidence sequence. `FactGrade` reflects execution evidence only. Clamp all reverted findings to `Info`. Include a limitation explaining that an observed path does not prove exploitability for all inputs.

- [ ] **Step 5: Implement exact frame-aware rules**

Reentrancy uses ancestor paths and repeated storage owner, excluding delegate execution that does not create external re-entry. Checks-effects-interactions uses instruction/event ordering and the child frame's exit boundary. Storage collision selects typed writes in CALLCODE/DELEGATECALL frames and compares explicit code owner to storage owner before applying reserved-slot rules.

- [ ] **Step 6: Verify deterministic findings**

```powershell
dotnet test .\Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~JournalReentrancyAnalyzerTests|FullyQualifiedName~JournalStorageCollisionAnalyzerTests|FullyQualifiedName~SecurityFindingGradeTests|FullyQualifiedName~JournalDispositionTests" --nologo -v minimal
```

Expected: PASS and findings are identical across repeated runs.

- [ ] **Step 7: Commit the canonical security analyzer**

```powershell
git add Schlieren.Core/Execution/Journal/SecurityFinding.cs Schlieren.Core/Execution/Journal/JournalSecurityAnalyzer.cs Schlieren.Tests/Security/JournalReentrancyAnalyzerTests.cs Schlieren.Tests/Security/JournalStorageCollisionAnalyzerTests.cs Schlieren.Tests/Security/SecurityFindingGradeTests.cs
git commit -m "feat(security): analyze journal frame evidence"
```

### Task 7: Expose additive state and security DTOs to React

**Files:**
- Modify: `Schlieren.Core/Execution/Journal/JournalTraceDtos.cs`
- Modify: `Schlieren.Core/Execution/Journal/JournalTraceAssembler.cs`
- Modify: `Schlieren.Tests/Execution/JournalTraceAssemblerTests.cs`
- Modify: `Schlieren.Tests/RPC/JournalTraceRpcTests.cs`
- Modify: `Schlieren.Tests/RPC/DebugInspectRpcTests.cs`
- Modify: `schlieren-ui/src/engine/store.ts`
- Modify: `schlieren-ui/src/engine/journal.ts`
- Modify: `schlieren-ui/src/engine/journal-view.ts`
- Create: `schlieren-ui/src/engine/security-view.test.ts`
- Create: `schlieren-ui/src/views/Workbench/SecurityEvidence.tsx`
- Modify: `schlieren-ui/src/views/Workbench/Workbench.tsx`

**Interfaces:**
- Consumes: `JournalAnalysis.Build` and `JournalSecurityAnalyzer.Analyze`.
- Produces: additive `stateEffects` and `securityFindings` arrays in `JournalTraceDto`.
- Produces: React `StateEffect` and `SecurityFinding` interfaces with empty-array defaults during rolling upgrades.

- [ ] **Step 1: Write failing assembler and RPC tests**

Assert a storage-write response includes effect/frame/instruction IDs, both dispositions, and rollback frame. Assert a finding links its evidence sequences. Keep the existing golden legacy RPC tests unchanged and run them in the same filter.

```csharp
Assert.Equal("reverted", dto.StateEffects[0].ExecutionDisposition);
Assert.Equal("notApplicable", dto.StateEffects[0].PersistenceDisposition);
Assert.Equal(parentId, dto.StateEffects[0].RevertedByFrameId);
Assert.Contains(dto.SecurityFindings[0].SupportingEventSequences,
    sequence => sequence == dto.StateEffects[0].Sequence);
```

- [ ] **Step 2: Write failing React parser/view tests**

Test full new responses and old journal responses without the additive arrays:

```ts
expect(parseJournalTrace(oldResponse).stateEffects).toEqual([]);
expect(parseJournalTrace(oldResponse).securityFindings).toEqual([]);
expect(securityTone({ severity: 'info', executionDisposition: 'reverted' })).toBe('reverted');
```

- [ ] **Step 3: Run red tests**

```powershell
dotnet test .\Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~JournalTraceAssemblerTests|FullyQualifiedName~JournalTraceRpcTests|FullyQualifiedName~DebugInspectRpcTests" --nologo -v minimal
Push-Location .\schlieren-ui; npm test -- --run src/engine/security-view.test.ts; Pop-Location
```

Expected: FAIL because the additive DTOs and React types are missing; legacy golden assertions remain green.

- [ ] **Step 4: Map typed events and analyzed evidence**

Add nullable `InstructionId` to raw event DTOs. Add `JournalStateEffectDto` and `JournalSecurityFindingDto`, rendering numeric protocol values consistently with the endpoint's existing conventions. Build `JournalAnalysis` once inside `JournalTraceAssembler`, pass it to the security analyzer, and reuse it for both arrays.

Malformed analysis must throw `JournalAnalysisException`; the RPC handler maps that to a structured internal RPC error and never falls back to flat traces.

- [ ] **Step 5: Add the React evidence view**

Normalize missing additive arrays to `[]` in `parseJournalTrace`. Render findings with severity, proof grade, frame, opcode/instruction link, execution/persistence badges, supporting effects, and the limitation. Reverted evidence must use informational styling and cannot display a committed-vulnerability badge.

- [ ] **Step 6: Verify RPC compatibility and React**

```powershell
dotnet test .\Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~JournalTraceAssemblerTests|FullyQualifiedName~JournalTraceRpcTests|FullyQualifiedName~DebugInspectRpcTests|FullyQualifiedName~InspectDtoJsonTests" --nologo -v minimal
Push-Location .\schlieren-ui; npm test; npm run build; npm run lint; Pop-Location
```

Expected: PASS; existing `debug_inspect` and `debug_traceCall` golden JSON is unchanged.

- [ ] **Step 7: Commit RPC and React evidence**

```powershell
git add Schlieren.Core/Execution/Journal/JournalTraceDtos.cs Schlieren.Core/Execution/Journal/JournalTraceAssembler.cs Schlieren.Tests/Execution/JournalTraceAssemblerTests.cs Schlieren.Tests/RPC/JournalTraceRpcTests.cs Schlieren.Tests/RPC/DebugInspectRpcTests.cs schlieren-ui/src/engine/store.ts schlieren-ui/src/engine/journal.ts schlieren-ui/src/engine/journal-view.ts schlieren-ui/src/engine/security-view.test.ts schlieren-ui/src/views/Workbench/SecurityEvidence.tsx schlieren-ui/src/views/Workbench/Workbench.tsx
git commit -m "feat(rpc): expose journal security evidence"
```

### Task 8: Migrate consumers and delete heuristic execution analysis

**Files:**
- Modify: `Schlieren.UI/Services/BytecodeExecutionService.cs`
- Modify: `Schlieren.UI/ViewModels/CallTopologyViewModel.cs`
- Modify: `Schlieren.UI/ViewModels/WorkbenchViewModel.cs`
- Modify: `Schlieren.Tests/Regression/DifferentialRegressionRunner.cs`
- Delete: `Schlieren.Core/Security/ReentrancyDetector.cs`
- Delete: `Schlieren.Core/Security/StorageCollisionDetector.cs`
- Delete: `Schlieren.Core/Security/LiveReentrancyDetector.cs`
- Delete: `Schlieren.Core/Security/LiveStorageCollisionDetector.cs`
- Delete: `Schlieren.UI/Services/WorkbenchExecutionService.cs`
- Rewrite: `Schlieren.Tests/Security/ReentrancyDetectorTests.cs`
- Rewrite: `Schlieren.Tests/Security/StorageCollisionDetectorTests.cs`
- Rewrite: `Schlieren.Tests/Security/SecurityDetectorIntegrationTests.cs`
- Modify: `Schlieren.Tests/WorkbenchAaBbAcceptanceTests.cs`
- Create: `Schlieren.Tests/WorkbenchCanonicalSecurityTests.cs`

**Interfaces:**
- Consumes: `JournalAnalysis` and `JournalSecurityAnalyzer` only.
- Removes: active analyzers accepting `IReadOnlyList<ExecutionTraceStep>` and the fabricated Workbench transaction.
- Produces: explicit-frame topology and canonical security findings for remaining Avalonia views/tests.

- [ ] **Step 1: Write failing consumer-cutover tests**

Add a real Workbench execution containing nested DELEGATECALL storage evidence. Assert its topology rows use journal frame IDs/parents and its displayed finding carries the same supporting event sequence as `JournalSecurityAnalyzer`. Add an Osaka EIP-7623 calldata-floor regression case and assert the runner's reported audit gas equals journal conservation rather than `21_000 + calldata + depth-one opcodes`.

```csharp
Assert.Equal(run.Analysis.Frames.Count, vm.CallTopology.Rows.Count);
Assert.Equal(
    run.SecurityFindings.Single().SupportingEventSequences,
    vm.SecurityFindings.Single().SupportingEventSequences);
Assert.Equal(
    JournalGasTree.Build(run.Result.Journal!, run.Result).Conservation.SettledGas,
    regression.ActualGas);
```

- [ ] **Step 2: Run tests to prove legacy consumers produce the wrong outcomes**

```powershell
dotnet test .\Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~WorkbenchCanonicalSecurityTests|FullyQualifiedName~WorkbenchCanonicalAuditTests" --nologo -v minimal
```

Expected: FAIL because Workbench topology/security and regression gas still consume flat-trace heuristics.

- [ ] **Step 3: Migrate Workbench result and topology**

Add required canonical properties to `WorkbenchRunResult`:

```csharp
public required JournalAnalysis Analysis { get; init; }
public required IReadOnlyList<SecurityFinding> SecurityFindings { get; init; }
```

Build both once after canonical execution. Change `CallTopologyViewModel.LoadFromTrace` to `LoadFromAnalysis(JournalAnalysis analysis)` and construct rows from explicit frames and parents. Update Workbench severity counts and evidence text from `SecurityFinding`.

- [ ] **Step 4: Remove synthetic and flat-trace security code**

Delete the four heuristic detector files and synthetic Workbench service. Remove the synthetic command/UI path from `WorkbenchViewModel`. Rewrite old detector tests as canonical journal executions or direct validated-journal fixtures so their security scenarios remain covered by the sole analyzer.

- [ ] **Step 5: Remove simplified regression gas/security inference**

In `DifferentialRegressionRunner`, obtain gas from `JournalGasTree.Build(result.Journal!, result).Conservation.SettledGas`, nested ownership from `JournalAnalysis.Frames`, and security findings from `JournalSecurityAnalyzer`. Delete `ComputeAuditGas`, `ParseGasCost`, `DetectNestedGasDoubleCount`, and the flat-trace reentrancy helper.

- [ ] **Step 6: Verify canonical consumer behavior**

```powershell
dotnet test .\Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~WorkbenchCanonicalSecurityTests|FullyQualifiedName~JournalReentrancyAnalyzerTests|FullyQualifiedName~JournalStorageCollisionAnalyzerTests|FullyQualifiedName~Workbench|FullyQualifiedName~GoldenCorpusTests" --nologo -v minimal
```

Expected: migrated consumer behavior passes. Record any still-failing golden baseline case by exact name; do not relabel it fixed without a green result.

- [ ] **Step 7: Commit consumer cutover and deletion**

```powershell
git add -A Schlieren.Core/Security Schlieren.UI/Services Schlieren.UI/ViewModels Schlieren.Tests/Security Schlieren.Tests/Regression Schlieren.Tests/WorkbenchAaBbAcceptanceTests.cs Schlieren.Tests/WorkbenchCanonicalSecurityTests.cs
git commit -m "refactor(security): delete flat trace analyzers"
```

### Task 9: Document, audit, and verify the complete slice

**Files:**
- Modify: `README.md`
- Modify: `docs/rpc/schlieren_traceJournal.md`
- Modify: `docs/gas/GAS_RULE_INVENTORY.md`
- Create: `docs/security/JOURNAL_SECURITY_EVIDENCE.md`

**Interfaces:**
- Documents: exact event semantics, disposition meanings, finding proof boundary, endpoint fields, and compatibility guarantees.
- Verifies: source architecture, focused suites, React, EELS, and the full baseline.

- [ ] **Step 1: Update documentation with exact semantics**

Document:

```text
Execution disposition answers whether an observation occurred on the surviving EVM path.
Persistence disposition answers whether a surviving result was written to backing state.
A reverted observation remains forensic evidence but is never reported as a committed vulnerability.
```

Include one nested example: child write, child success, parent revert, `RevertedByFrameId=parent`. State that findings prove the observed path, not universal exploitability.

- [ ] **Step 2: Run source scans for forbidden paths**

```powershell
rg -n --glob '*.cs' "DetermineCallType|class ReentrancyDetector|class StorageCollisionDetector|class LiveReentrancyDetector|class LiveStorageCollisionDetector" Schlieren.Core Schlieren.UI
rg -n --glob '*.cs' "ComputeAuditGas|DetectNestedGasDoubleCount|RunFullTransaction" Schlieren.Core Schlieren.UI Schlieren.Tests/Regression
```

Expected: no live implementation matches; documentation/history references are acceptable only when clearly marked removed.

- [ ] **Step 3: Run the focused deterministic matrix**

```powershell
dotnet test .\Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~Journal|FullyQualifiedName~Security|FullyQualifiedName~GasTraceInvariantTests|FullyQualifiedName~DebugInspectRpcTests|FullyQualifiedName~Workbench" --nologo -v minimal
dotnet test .\Schlieren.EELS.Tests\Schlieren.EELS.Tests.csproj --filter "FullyQualifiedName~Journal|FullyQualifiedName~Storage|FullyQualifiedName~Create|FullyQualifiedName~SelfDestruct|FullyQualifiedName~Eip7702" --nologo -v minimal
Push-Location .\schlieren-ui; npm test; npm run build; npm run lint; Pop-Location
```

Expected: all focused tests, build, and lint pass.

- [ ] **Step 4: Run the complete .NET suites and compare baseline**

```powershell
dotnet test .\Schlieren.Tests\Schlieren.Tests.csproj --nologo -v minimal
dotnet test .\Schlieren.EELS.Tests\Schlieren.EELS.Tests.csproj --nologo -v minimal
```

Expected: no new failures compared with the recorded clean-main baseline. Report exact pass/fail/skip totals and list every failure. Do not call the branch fully green while any suite fails.

- [ ] **Step 5: Verify clean diff and commit documentation**

```powershell
git diff --check
git status --short
git add README.md docs/rpc/schlieren_traceJournal.md docs/gas/GAS_RULE_INVENTORY.md docs/security/JOURNAL_SECURITY_EVIDENCE.md
git commit -m "docs: explain journal security evidence"
git status --short --branch
```

Expected: clean worktree after the commit.

## Completion report requirements

The implementation handoff must state:

- which commits implement each task;
- exact focused and full-suite totals;
- whether any failures match the verified baseline;
- whether `debug_inspect` and `debug_traceCall` golden contracts remained unchanged;
- which heuristic and synthetic files were deleted;
- one demonstrated surviving effect, one ancestor-reverted effect, and one simulation-discarded effect;
- one reentrancy and one storage-collision finding with supporting frame/event evidence;
- any performance or payload limitation not addressed by this slice.
