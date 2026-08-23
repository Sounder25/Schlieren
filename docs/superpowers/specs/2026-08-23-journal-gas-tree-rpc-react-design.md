# Journal Gas Tree, RPC, and React Migration Design

Date: 2026-08-23  
Status: Approved architecture; pending implementation-plan approval  
Scope: rebuild gas attribution from the typed execution journal, expose it through `schlieren_traceJournal`, and make `schlieren-ui` the primary journal-native workbench

## Purpose

Schlieren now records frame identity and explicit gas semantics at execution time, but its canonical gas tree still guesses ownership from the legacy flat trace. That causes two known failures: the tree does not conserve charged gas, and nested opcodes can be assigned to the parent frame.

This stage makes the typed execution journal the source of truth for gas attribution and for the new React workbench contract. Existing Ethereum-compatible tracing and the Avalonia inspection path remain byte-for-byte compatible at the JSON boundary.

## Goals

1. Build the gas tree from explicit journal frame relationships instead of trace depth heuristics.
2. Sum only gas events whose semantics are additive, and subtract effective credits exactly once.
3. Record CALL-family local charges, child-gas allocation, and unused-gas return explicitly at their actual mutation sites.
4. Make every returned tree auditable against `TransactionSettledEvent.ChargedGas`.
5. Add `schlieren_traceJournal` as a new JSON-RPC method with frame-aware journal DTOs.
6. Support optional ephemeral bytecode so the React Workbench can run pasted code without mutating global state.
7. Return stack, memory, and storage snapshots by default, with explicit opt-out flags.
8. Migrate `schlieren-ui` from `debug_traceCall` plus `anvil_setCode` to the new atomic endpoint.
9. Preserve `debug_traceCall`, `debug_inspect`, their JSON shapes, and Avalonia behavior.
10. Turn the existing conservation and nested-frame gas-tree tests green.

## Non-goals

- Rewriting or removing `ExecutionTraceStep`.
- Changing EIP-3155 `structLogs` ordering or values.
- Migrating the Avalonia `Schlieren.UI` workbench to journal DTOs.
- Changing `debug_traceCall`, `debug_inspect`, or stored transaction trace contracts.
- Changing opcode gas arithmetic, forwarding rules, stipend behavior, refund caps, or fee settlement.
- Exposing a persistent state-write operation through `schlieren_traceJournal`.
- Reworking causal diagnosis rules in this stage.

## Considered approaches

### Explicit gas components at mutation sites — chosen

Record CALL-family local charges, allocation, and return where `ConsumeGas` and `RefundGas` already change the frame gas counter. Ordinary opcode deltas remain additive `OpcodeGasEvent` entries; CALL-family completion remains an inclusive, non-additive observation.

This produces an authoritative ledger and handles nested calls, failures, precompiles, and stipends without guessing from trace order.

### Derive CALL overhead as a residual — rejected

Subtract child-frame gas from the inclusive CALL delta. This is smaller but makes stipend, failed-call, and precompile attribution inferred rather than observed.

### Join journal events with `ExecutionTraceStep` — rejected

Use frame IDs from the journal and snapshots from the legacy trace. This retains two sources of truth and reintroduces ordering joins that the journal exists to eliminate.

## Canonical gas ledger

### Additive rules

The tree calculator uses event type and `GasSemantics`; it never infers additivity from an event name or sign.

Positive contributions:

- `IntrinsicGasChargedEvent` with `ExclusiveCharge`.
- Ordinary `OpcodeGasEvent` with `ExclusiveCharge`.
- CALL-family local `GasComponentEvent` with `ExclusiveCharge`.
- Precompile and transaction-level `GasComponentEvent` with `ExclusiveCharge`.
- `ExceptionalGasBurnedEvent` and exceptional `GasComponentEvent` with `ExceptionalBurn`.

Negative contributions:

- `EffectiveGasRefundedEvent` with `Credit`, applied once at transaction scope.

Non-contributing observations:

- CALL-family `OpcodeGasEvent` with `InclusiveFrameDelta`.
- `FrameEnteredEvent` allocations.
- `FrameExitedEvent` returns.
- `GasComponentEvent` entries marked `Allocation`, `Return`, or `Observation`.
- `RefundCounterChangedEvent`; it is the raw protocol counter, not the effective credit.
- `TransactionSettledEvent`; it is the expected total, not another charge.

### Gas component event

Add one general component event rather than a separate type per protocol edge:

```csharp
public enum GasComponentScope
{
    Transaction,
    Frame,
    Opcode
}

public sealed record GasComponentEvent : ExecutionJournalEvent
{
    public required GasComponentScope Scope { get; init; }
    public required string Component { get; init; }
    public required ulong Amount { get; init; }
    public required GasSemantics Semantics { get; init; }
    public int? Pc { get; init; }
    public byte? Opcode { get; init; }
    public string? OpcodeName { get; init; }
}
```

Components use stable machine-readable names, including:

- `call.local`
- `call.forwarded`
- `call.unused-return`
- `precompile.execution`
- `create.code-deposit`
- `create.exceptional-burn`
- `transaction.calldata-floor`
- `transaction.collision-burn`

Labels shown by the UI are derived by the DTO mapper, not embedded as protocol prose in the journal.

### CALL-family instrumentation

`EvmMachine` establishes the active opcode identity on `ExecutionContext` before invoking an opcode and clears it after completion or a handled EVM failure.

`ExecutionContext.ConsumeGas` and `RefundGas` retain their current arithmetic. They gain optional journal metadata that defaults to the current behavior:

```csharp
void ConsumeGas(
    ulong amount,
    GasSemantics semantics = GasSemantics.ExclusiveCharge,
    string component = "opcode.local");

void RefundGas(
    ulong amount,
    GasSemantics semantics = GasSemantics.Return,
    string component = "opcode.return");
```

Component events are emitted only when the active opcode is CALL, CALLCODE, DELEGATECALL, STATICCALL, CREATE, or CREATE2, or when a caller explicitly requests a non-default semantic. This avoids duplicating ordinary additive `OpcodeGasEvent` charges.

CALL-family implementations annotate forwarded child gas as `Allocation` and unused child gas as `Return`. Their access, value, memory, and other local protocol costs remain `ExclusiveCharge`. The existing post-op `OpcodeGasEvent` remains `InclusiveFrameDelta` and is displayed as an observation but excluded from totals.

### Non-opcode transaction gas

`StateTransition` records additive components that cannot be represented by an ordinary opcode event:

- successful precompile execution;
- precompile exceptional burn;
- top-level CREATE code deposit;
- top-level CREATE post-frame exceptional burn;
- top-level creation-collision burn;
- calldata-floor uplift after refund processing.

These events use the frame ID when a frame exists and transaction scope otherwise. This ensures conservation for success, REVERT, exceptional halt, precompile, CREATE, and calldata-floor paths.

## Journal opcode snapshots

`OpcodeGasEvent` becomes the complete journal-native step source. It retains gas fields and gains:

- depth;
- contract, caller, code address, and call type;
- pre-execution stack snapshot;
- post-op memory and storage snapshots, matching the existing trace convention;
- output/return data when available.

Snapshots are copied into read-only values before recording so later machine mutation cannot alter prior events.

When journaling is enabled, `EvmMachine` captures the pre-op stack even if legacy tracing is disabled. Legacy `ExecutionTraceStep` capture remains controlled only by `EnableTracing`.

The context's storage-snapshot mirror updates when either tracing or journaling is enabled. This preserves journal storage snapshots when `EnableTracing = false` without creating legacy trace steps.

## Journal gas tree

Create `GasTreeFromJournal` as the journal-native builder. It does not inspect `ExecutionTraceStep`, depth transitions, stack arguments, or CALL ordering heuristics.

### Hierarchy

1. Index every `FrameEnteredEvent` by `FrameId`.
2. Attach frames using their explicit `ParentFrameId`.
3. Assign opcode and component events directly by `FrameId`.
4. Preserve journal sequence within each frame.
5. Put transaction-scope intrinsic charges and credits under the transaction root.

### Tree node model

The journal-native tree DTO carries:

```csharp
public sealed class JournalGasNode
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public long? FrameId { get; init; }
    public GasSemantics? Semantics { get; init; }
    public ulong Amount { get; init; }
    public GasEffect Effect { get; init; }
    public ulong TotalGas { get; init; }
    public List<JournalGasNode> Children { get; init; } = new();
}

public enum GasEffect
{
    None,
    Charge,
    Credit
}
```

`Amount` is always unsigned. `Effect` controls whether it contributes positively, negatively, or not at all. `TotalGas` is computed with checked unsigned arithmetic and rejects a credit larger than accumulated charges.

Opcode buckets may group adjacent or same-name exclusive events for display, but every bucket retains source event sequence IDs so the UI can expand it back to evidence.

### Conservation

Every response includes:

```csharp
public sealed class JournalConservation
{
    public ulong DerivedGas { get; init; }
    public ulong SettledGas { get; init; }
    public string Delta { get; init; } = "0";
    public bool IsConserved { get; init; }
}
```

`Delta` is a signed decimal string so the contract can represent the full difference between two `ulong` values without overflow or JavaScript number truncation.

`SettledGas` comes from `TransactionSettledEvent.ChargedGas`. Internal or non-settled executions use `ExecutionResult.GasUsed` as the expected value and identify the source in the DTO.

The builder never inserts an unattributed bucket to force equality. A mismatch remains visible through `Delta` and `IsConserved = false`, and invariant tests fail.

## RPC method

### Registration

Add `schlieren_traceJournal` to the RPC router and handler capability list. Existing methods and handlers are not modified except for shared parsing extraction that is proven JSON-neutral by regression tests.

### Request

The method takes one object:

```json
{
  "from": "0x...",
  "to": "0x...",
  "gas": "0x989680",
  "gasPrice": "0x0",
  "value": "0x0",
  "data": "0x",
  "code": "0x600160005500",
  "fork": "Osaka",
  "nonce": "0x0",
  "disableStack": false,
  "disableMemory": false,
  "disableStorage": false
}
```

Rules:

- `to` is required when `code` is present.
- `code` is optional. Missing means execute code already stored at `to`.
- Hex fields accept the same quantity/data conventions as current call handlers.
- Snapshot disable flags default to `false`.
- Unknown forks, malformed hex, missing `to` for ephemeral code, and invalid option types return JSON-RPC invalid-params errors.

### Ephemeral code isolation

When `code` is present:

1. Create a `StateOverlay` over the current global state.
2. Set the supplied code at `to` on that overlay.
3. Execute canonical `StateTransition` with `commit: false` against the overlay.
4. Discard the overlay after the response.

The handler never calls `anvil_setCode`, never mutates `GlobalState`, and produces the same pre/post global state on success, REVERT, exceptional halt, and cancellation.

### Execution flags

The RPC transaction uses:

```csharp
EnableJournal = true;
EnableTracing = false;
Authorization = TransactionAuthorization.Simulation;
```

The journal must therefore be sufficient to build the entire response without legacy trace capture.

### Response

The response is camelCase and contains:

```json
{
  "ok": true,
  "fork": "Osaka",
  "execution": {},
  "events": [],
  "frames": [],
  "steps": [],
  "gasTree": {},
  "conservation": {}
}
```

- `events` is a discriminated union using a stable `kind` field and common sequence/frame fields.
- `frames` contains enter/exit summaries with explicit parent IDs.
- `steps` contains one journal-native opcode row per executed opcode, including snapshot fields unless disabled.
- `gasTree` contains only journal-derived nodes.
- `conservation` reports derived versus settled gas without hiding discrepancies.
- `execution` uses hex quantities for RPC consistency and includes success, error, gas used, gas limit, refund counter, and return value.

DTOs live in Core so RPC remains a transport adapter. Polymorphic internal journal records are never serialized directly.

## React migration

`schlieren-ui/src/engine/rpc.ts` sends one `schlieren_traceJournal` request. It removes the `anvil_setCode` preflight and no longer calls `debug_traceCall` for Workbench execution.

The Zustand store gains typed journal response state:

- execution summary;
- ordered journal steps;
- frames indexed by ID;
- journal gas tree;
- conservation status;
- raw typed event DTOs for advanced views.

The existing cursor remains an index into `steps`. Trace panel rows, gas heat coloring, stack, memory, and storage tabs read from journal step DTOs. Flow and gas-tree views use explicit frame IDs and parent IDs rather than depth inference.

If the endpoint returns `IsConserved = false`, the UI shows a prominent diagnostic state with the exact delta; it does not silently render a balanced tree.

## Compatibility guarantees

- `debug_traceCall` JSON remains identical.
- `debug_inspect` JSON remains identical.
- `ExecutionResult.Journal` remains excluded from JSON serialization.
- Avalonia continues using its current endpoints and DTOs.
- Stored transaction traces and mining trace persistence remain unchanged.
- Existing security detectors continue consuming `ExecutionTraceStep` until a separately designed migration.
- `GasTreeFromTrace` may remain as a compatibility implementation, but no journal-native RPC or React path may call it.

## Testing strategy

### Core ledger and tree

- Ordinary opcode exclusive charges contribute once.
- CALL inclusive deltas do not contribute.
- CALL local component charges contribute once.
- Forwarded allocation and unused return never contribute.
- Child opcodes appear only under the child frame ID.
- Exceptional burns contribute once.
- Effective refunds subtract once; refund counters do not subtract.
- Precompile, CREATE deposit/failure, collision, and calldata-floor paths conserve.
- Existing `CanonicalGasTree_TotalGasEqualsChargedGas` turns green using the journal builder.
- Existing nested-frame ownership invariant turns green using explicit frame IDs.

### Snapshot DTOs

- Journal steps are produced with `EnableTracing = false`.
- Stack is pre-op; memory/storage follow existing post-op conventions.
- Disable flags empty only the requested fields.
- Snapshot values remain unchanged after later opcodes mutate the machine.

### RPC

- Method discovery and routing include `schlieren_traceJournal`.
- Existing debug endpoint golden JSON remains unchanged.
- Ephemeral code executes successfully without changing global code, storage, balance, or nonce.
- Missing `code` executes state code.
- Invalid request shapes return invalid-params.
- Nested response exposes stable parent/child frame IDs and conserved gas.

### React

- RPC client sends no `anvil_setCode` call.
- Request includes optional code and defaults snapshot flags to enabled.
- Response mapper retains frame IDs, semantics, and snapshots.
- Cursor and state panels render journal steps.
- Gas tree renders charge, credit, and non-contributing semantics distinctly.
- Conservation failure is visible.

## Documentation

Update the root README with:

- the typed journal purpose and opt-in behavior;
- additive gas semantics;
- the journal-native gas tree;
- `schlieren_traceJournal` request/response examples;
- ephemeral code behavior and safety;
- React as the primary workbench;
- legacy endpoint compatibility;
- current limitations and follow-on security-detector migration.

Add focused RPC documentation near the endpoint implementation or in a dedicated `docs/rpc` page if that directory is introduced by the implementation plan.

## Acceptance criteria

1. The journal-native tree conserves charged gas for covered success and failure paths.
2. Nested opcodes are owned by explicit child frame IDs.
3. CALL-family inclusive observations are never summed as exclusive gas.
4. CALL local charges, allocations, and returns are explicit journal evidence.
5. Exceptional burns and effective credits appear exactly once.
6. `schlieren_traceJournal` returns journal-derived events, frames, steps, tree, and conservation DTOs.
7. Optional code executes ephemerally without global-state mutation.
8. Stack, memory, and storage are present by default and individually suppressible.
9. React uses only `schlieren_traceJournal` for Workbench execution.
10. `debug_traceCall`, `debug_inspect`, Avalonia, and existing JSON contracts remain unchanged.
11. The two formerly red gas-tree invariants pass.
12. The root README and RPC documentation describe the shipped behavior.
