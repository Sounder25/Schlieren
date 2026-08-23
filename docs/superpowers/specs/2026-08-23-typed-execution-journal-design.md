# Typed Execution Journal Design

Date: 2026-08-23  
Status: Proposed for implementation  
Scope: typed journal definition and additive instrumentation of canonical `StateTransition` and `EvmMachine` execution

## Purpose

Schlieren currently derives gas attribution and call-frame ownership from a flat post-execution trace. That trace is useful for display, but its ordering and gas-cost fields are not an additive ledger: child steps are emitted before the parent CALL step, and the parent CALL delta includes child execution.

This change introduces a typed, opt-in execution journal that records frame identity and gas semantics at the point where they are known. It does not replace the current trace, alter EVM behavior, rebuild the gas tree, or change causal diagnosis in this stage.

## Goals

1. Give every executed frame a stable `FrameId` and explicit `ParentFrameId`.
2. Record transaction, frame, opcode, exceptional-burn, refund, and settlement observations as immutable typed events.
3. Mark every gas quantity with semantics that say whether it is additive, inclusive, allocative, returned, or credited.
4. Share one ordered journal across the entire root transaction and all recursive child frames.
5. Preserve execution results, state changes, existing trace steps, RPC shapes, and gas accounting when journaling is enabled.
6. Avoid journal allocation and event-recording overhead when journaling is disabled.

## Non-goals

- Rebuilding `GasTreeFromTrace` from journal events.
- Making the two existing red gas-tree invariant tests pass.
- Changing EIP-3155 struct-log order or contents.
- Changing opcode gas calculations, CALL forwarding, stipend handling, refunds, or settlement.
- Instrumenting individual CALL-family branches inside `SystemOpcodes` with protocol-component charges.
- Exposing the journal through RPC or UI DTOs.
- Changing `CausalDiagnosisEngine` or its confidence grades.
- Removing the duplicate `ApplyTransactionWithFrameAsync` path.

Those are follow-up stages that consume the journal established here.

## Chosen approach

Journal collection is controlled by a new `Transaction.EnableJournal` Boolean, independent of `EnableTracing`.

Alternatives rejected:

- **Always on:** simplest API, but adds allocations and event traffic to every transaction.
- **Reuse `EnableTracing`:** avoids a flag, but couples a compatibility/display trace to a semantic diagnostic ledger. Consumers often need one without the other.

The journal is additive. A null journal means collection is disabled. Execution code checks for null before constructing or recording an event.

## Public model

Create `Schlieren.Core/Execution/Journal/ExecutionJournal.cs` containing the recorder and immutable event model.

### Recorder

```csharp
public sealed class ExecutionJournal
{
    public IReadOnlyList<ExecutionJournalEvent> Events { get; }

    internal long OpenFrame(long? parentFrameId);
    internal void Record(ExecutionJournalEvent entry);
}
```

`OpenFrame` returns monotonically increasing positive IDs. `Record` stamps a monotonically increasing zero-based sequence number. Consumers can rely on sequence order; they cannot mutate the collection.

The EVM executes frames synchronously today, but ID and sequence generation will use simple checked increments owned by the journal. Thread safety is not part of this stage because a single transaction does not execute frames concurrently.

### Gas semantics

```csharp
public enum GasSemantics
{
    ExclusiveCharge,
    InclusiveFrameDelta,
    Allocation,
    Return,
    RefundCounter,
    Credit,
    ExceptionalBurn,
    Observation
}
```

Definitions:

- `ExclusiveCharge`: consumed exactly once by the identified scope and safe to sum with other exclusive charges.
- `InclusiveFrameDelta`: a net frame delta that may include nested execution and is not safe to sum with child charges.
- `Allocation`: gas made available to a frame; not consumption.
- `Return`: unused allocated gas returned to the parent or sender; not consumption.
- `RefundCounter`: a protocol refund-counter change before cap application; not a charged-gas credit.
- `Credit`: an effective refund or settlement credit; subtracts from gross consumption when computing charged gas.
- `ExceptionalBurn`: remaining frame gas consumed by an exceptional halt; safe to sum once within its frame.
- `Observation`: a gas value recorded for context but not a movement.

No consumer may derive additivity from event name or sign. It must inspect `GasSemantics`.

### Event hierarchy

```csharp
public abstract record ExecutionJournalEvent
{
    public long Sequence { get; internal init; }
    public long? FrameId { get; init; }
    public long? ParentFrameId { get; init; }
}
```

Concrete events:

```csharp
public sealed record TransactionStartedEvent : ExecutionJournalEvent
{
    public required ulong GasLimit { get; init; }
    public required bool IsInternal { get; init; }
}

public sealed record IntrinsicGasChargedEvent : ExecutionJournalEvent
{
    public required ulong Amount { get; init; }
    public GasSemantics Semantics => GasSemantics.ExclusiveCharge;
}

public sealed record FrameEnteredEvent : ExecutionJournalEvent
{
    public required int Depth { get; init; }
    public required CallType CallType { get; init; }
    public required Address ContractAddress { get; init; }
    public Address? CodeAddress { get; init; }
    public required ulong GasLimit { get; init; }
    public GasSemantics Semantics => GasSemantics.Allocation;
}

public sealed record OpcodeGasEvent : ExecutionJournalEvent
{
    public required int Pc { get; init; }
    public required byte Opcode { get; init; }
    public required string Name { get; init; }
    public required ulong GasBefore { get; init; }
    public required ulong GasAfter { get; init; }
    public required ulong Amount { get; init; }
    public required GasSemantics Semantics { get; init; }
}

public sealed record ExceptionalGasBurnedEvent : ExecutionJournalEvent
{
    public required int Pc { get; init; }
    public required string Opcode { get; init; }
    public required ulong Amount { get; init; }
    public required EvmError Error { get; init; }
    public GasSemantics Semantics => GasSemantics.ExceptionalBurn;
}

public sealed record RefundCounterChangedEvent : ExecutionJournalEvent
{
    public required long Previous { get; init; }
    public required long Current { get; init; }
    public long Delta => Current - Previous;
    public GasSemantics Semantics => GasSemantics.RefundCounter;
}

public sealed record FrameExitedEvent : ExecutionJournalEvent
{
    public required int Depth { get; init; }
    public required bool Success { get; init; }
    public required EvmError Error { get; init; }
    public required ulong GasUsed { get; init; }
    public required ulong GasRemaining { get; init; }
    public GasSemantics Semantics => GasSemantics.Return;
}

public sealed record EffectiveGasRefundedEvent : ExecutionJournalEvent
{
    public required ulong GrossGasUsed { get; init; }
    public required ulong RefundCap { get; init; }
    public required ulong Amount { get; init; }
    public GasSemantics Semantics => GasSemantics.Credit;
}

public sealed record TransactionSettledEvent : ExecutionJournalEvent
{
    public required ulong ChargedGas { get; init; }
    public required ulong UnusedGasReturned { get; init; }
}
```

The initial intrinsic event records the total fork-aware intrinsic amount. Component-level intrinsic events are deferred until the intrinsic calculator returns a typed breakdown.

## ExecutionResult and transaction integration

`Transaction` gains:

```csharp
public bool EnableJournal { get; set; }
```

`ExecutionResult` gains:

```csharp
public ExecutionJournal? Journal { get; init; }
```

The existing factory methods continue to work without a journal. The top-level canonical state transition attaches the shared journal to every returned result, including validation failure, exceptional halt, REVERT, and success.

Child `Transaction` objects created by CALL-family opcodes already inherit tracing explicitly. In this stage, journal propagation does not depend on those clone fields: the canonical `StateTransition` recursion passes the shared journal and parent frame ID directly through its private execution method. This avoids requiring edits to every CALL-family transaction initializer.

## StateTransition instrumentation

Only canonical `ApplyTransactionAsync` and its recursive `ExecuteInternalAsync` path are instrumented. The duplicate diagnostic frame path is unchanged.

### Transaction lifecycle

When `tx.EnableJournal` is true at the canonical entry point:

1. Create one `ExecutionJournal`.
2. Record `TransactionStartedEvent` before validation.
3. Record `IntrinsicGasChargedEvent` after the fork-aware intrinsic amount is computed and accepted. Internal transactions do not record intrinsic gas.
4. Pass the journal and null parent frame ID into the first `ExecuteInternalAsync` call.
5. Record `EffectiveGasRefundedEvent` where the capped refund is already calculated. A zero refund may be omitted.
6. Record `TransactionSettledEvent` where final charged gas and unused gas are already known.
7. Attach the journal to the returned `ExecutionResult` on every exit path.

Early validation failures retain transaction-level events and have no frame events.

### Frame lifecycle

`ExecuteInternalAsync` receives two new private parameters:

```csharp
ExecutionJournal? journal = null,
long? parentFrameId = null
```

At entry it:

1. obtains a new frame ID when the journal is enabled;
2. determines call type using the existing call-context logic;
3. records `FrameEnteredEvent` with depth, addresses, and execution gas limit;
4. places the journal and frame ID on `ExecutionContext`;
5. passes the same journal and current frame ID into recursive child execution.

Before returning, it records exactly one `FrameExitedEvent` with the actual result. A `try/finally` is not used to invent an EVM result for cancellation or unexpected CLR exceptions; those exceptions continue to propagate. Journal event absence in such a case honestly indicates an incomplete frame.

The child frame's `GasLimit`, `GasUsed`, and `GasRemaining` describe its allocation and return. They do not assert how much of that allocation was parent-funded versus stipend-funded. That component-level distinction requires later CALL-family instrumentation.

### Refund counter

`StateTransition` records refund-counter changes at frame exit by comparing the frame's initial counter with its final counter. This captures the net counter effect without modifying opcode implementations. Later opcode-level instrumentation may add causal source events, but this stage does not infer them.

## EvmMachine instrumentation

`ExecutionContext` gains internal journal references:

```csharp
public ExecutionJournal? Journal { get; init; }
public long? JournalFrameId { get; init; }
public long? JournalParentFrameId { get; init; }
```

After an opcode completes and the existing `actualGasUsed` value is calculated, `EvmMachine` records `OpcodeGasEvent`.

Semantics are explicit:

- CALL, CALLCODE, DELEGATECALL, STATICCALL, CREATE, and CREATE2 use `InclusiveFrameDelta`, because the observed before/after interval contains nested execution.
- All other successfully completed opcodes use `ExclusiveCharge`.
- A zero amount is still recorded because the event preserves execution order and frame ownership.

Exceptional paths:

- Unknown opcode: record `ExceptionalGasBurnedEvent` for all gas remaining before returning `InvalidOpcode`.
- `EvmOutOfGasException`: record the opcode observation plus an `ExceptionalGasBurnedEvent` for the gas remaining at opcode start, matching the existing full-frame burn result.
- An opcode-returned non-REVERT failure that promotes `GasUsed` to the frame limit records the unrepresented remaining gas as an exceptional burn.
- REVERT records the opcode event but no exceptional burn because unused gas is returned.
- Cancellation and unexpected CLR exceptions keep their current propagation behavior.

The existing `ExecutionTraceStep.GasCost`, ordering, stack snapshots, and gas calculations are untouched.

## Ordering guarantees

The journal guarantees:

1. `TransactionStartedEvent` is first when journaling is enabled.
2. Parent `FrameEnteredEvent` precedes every child event.
3. Child `FrameEnteredEvent` precedes its opcode events.
4. Child `FrameExitedEvent` precedes the parent's inclusive CALL `OpcodeGasEvent`, reflecting when the parent opcode actually completes.
5. Every completed frame has exactly one enter and one exit event.
6. Sequence values are unique and strictly increasing.

The parent CALL event remains after child execution, but explicit frame IDs make ownership unambiguous. A future presentation adapter can reorder or nest events without guessing.

## Execution-behavior preservation

Instrumentation must not change:

- `IsSuccess`, `Error`, `GasUsed`, `GasRefundCounter`, `ReturnData`, logs, or trace steps;
- account balances, nonce, code, storage, transient storage, or deletion marks;
- gas forwarded, gas returned, stipend, refund cap, or fee settlement;
- exception and cancellation behavior;
- existing RPC JSON.

Tests compare journaling-off and journaling-on executions using fresh equivalent state and assert equality for all observable execution and state outputs except the new journal field.

## Testing strategy

### Model tests

- frame IDs are positive and unique;
- sequence values are zero-based, unique, and strictly increasing;
- exposed events cannot be mutated through the public interface.

### Canonical integration tests

- journaling disabled returns `Journal == null`;
- a successful root execution records transaction start, intrinsic charge, frame enter, opcode events, frame exit, and settlement in order;
- nested CALL execution assigns depth-2 SSTORE to the child frame ID and records an explicit parent ID;
- CALL's opcode gas event is marked `InclusiveFrameDelta`;
- ordinary SSTORE is marked `ExclusiveCharge`;
- invalid opcode and OOG record exceptional burns;
- REVERT does not record exceptional burn;
- journaling enabled and disabled produce identical execution results, trace steps, and post-state.

### Existing red tests

`GasTraceInvariantTests.CanonicalGasTree_TotalGasEqualsChargedGas` and `NestedOpcodes_AreOwnedByChildFrame` remain red in this stage because the gas tree still consumes the legacy flat trace. Their eventual transition to green is the acceptance gate for the journal-to-gas-tree migration stage.

## Files

Create:

- `Schlieren.Core/Execution/Journal/ExecutionJournal.cs`
- `Schlieren.Tests/Execution/ExecutionJournalTests.cs`

Modify:

- `Schlieren.Core/State/Models.cs`
- `Schlieren.Core/Execution/ExecutionResult.cs`
- `Schlieren.Core/Execution/ExecutionContext.cs`
- `Schlieren.Core/Execution/EvmMachine.cs`
- `Schlieren.Core/Execution/StateTransition.cs`

No RPC, UI, opcode, gas-tree, or diagnosis files are modified in this stage.

## Acceptance criteria

1. Journal tests pass.
2. Existing tests other than the two intentionally red gas-tree invariant tests retain their prior status.
3. Journal-enabled and journal-disabled canonical executions are behaviorally identical outside the journal.
4. A real nested CALL produces explicit parent/child frame IDs and assigns child opcodes to the child ID.
5. CALL-family opcode events are explicitly non-additive; ordinary opcode charges are additive.
6. Exceptional frame burns are represented explicitly.
7. No existing JSON contract changes.
