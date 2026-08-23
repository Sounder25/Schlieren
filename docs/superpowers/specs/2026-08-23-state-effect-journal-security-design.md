# Typed State-Effect Journal and Frame-Aware Security Design

Date: 2026-08-23

Status: approved architecture, pending specification review

## Purpose

Extend Schlieren's canonical execution journal from gas and machine-state observation into state-effect forensics. Reentrancy and storage-collision analysis must use explicit frame identity, ancestry, storage ownership, code ownership, and state-lifecycle outcomes instead of reconstructing frames from trace depth.

The result must distinguish three different claims:

1. An operation was observed during execution.
2. The operation remained on the transaction's successful EVM path or was rolled back by its frame ancestry.
3. A successful transaction result was persisted to the backing state or intentionally discarded as a simulation.

These are facts from one canonical execution. They are not separate evaluators or alternate execution paths.

## Goals

- Record typed persistent-storage reads and writes.
- Record typed transient-storage reads and writes because transient locks are security-relevant.
- Record value transfers, nonce changes, code lifecycle, logs, and self-destruct effects.
- Record the authoritative state checkpoint and resolution for every execution frame.
- Derive each observation's final execution disposition from its full ancestor chain.
- Represent top-level persistence separately from EVM commit/revert semantics.
- Replace depth-based reentrancy and storage-collision heuristics with one journal-native security analyzer.
- Return proof-linked security evidence through `schlieren_traceJournal` for React.
- Preserve the JSON contracts of `debug_inspect` and `debug_traceCall` exactly.
- Preserve execution behavior, gas, state, receipts, logs, and return data.

## Non-goals

- Proving that a contract is exploitable for every possible input.
- Persisting journals for mined transactions in this slice.
- Streaming or compressing large journal responses in this slice.
- Redesigning legacy Avalonia DTOs.
- Treating a reverted attack attempt as a successful exploit.
- Introducing a second security implementation over flat `ExecutionTraceStep` data.

## Core semantics

### One execution, one append-only journal

`StateTransition.ApplyTransactionAsync` remains the only transaction evaluator. `StateTransition`, `EvmMachine`, and authoritative opcode/state boundaries append immutable observations to the existing `ExecutionJournal`. Instrumentation does not perform state writes, retries, replay, or diagnostic re-execution.

### Execution disposition

Every EVM state observation is owned by a frame. Transaction-protocol effects such as nonce consumption, gas refund, and miner payment are explicitly transaction-scoped and do not invent a frame owner. For a frame-scoped observation, `JournalAnalysis` walks the frame's ancestors:

- `Survived`: the owning frame and every ancestor resolved successfully.
- `Reverted`: the owning frame or at least one ancestor rolled back.

The analysis records `RevertedByFrameId` for a reverted observation. A child frame that succeeds but whose parent later fails is therefore `Reverted`, not `Survived`.

Reads remain historical facts even on a reverted path. Their disposition describes the path on which they occurred; it does not imply that reading state can itself be undone.

### Persistence disposition

Persistence is evaluated only after execution disposition:

- `CommittedToState`: the effect survived and the top-level caller requested `commit: true`.
- `SimulationDiscarded`: the effect survived but the top-level caller requested `commit: false`.
- `NotApplicable`: the effect was reverted before top-level persistence.

`schlieren_traceJournal` normally returns `Survived` plus `SimulationDiscarded` for effects from a successful simulation. It must never label them EVM reverts.

### Security claim boundary

Schlieren may prove execution facts such as:

> Frame 3 executed `SSTORE` against the proxy's storage while running implementation code, and that write survived all ancestor frames.

That does not by itself prove universal exploitability. Security findings therefore contain both:

- an execution-fact grade, derived only from journal evidence; and
- a risk classification, derived from the detector rule.

Reverted attack attempts remain visible as informational evidence. They cannot produce a high-severity committed-vulnerability finding.

## Journal vocabulary

All new events inherit `ExecutionJournalEvent` and therefore carry `Sequence`, `FrameId`, and `ParentFrameId`. State-effect events also carry `InstructionId`, `Pc`, and `Opcode` when caused by an EVM instruction.

### Instruction identity

`EvmMachine` allocates one monotonic `InstructionId` before executing each opcode and places it in the execution context for the duration of that instruction. The resulting `OpcodeGasEvent` and every state-effect event emitted by the opcode share that ID. This creates an exact causal link without relying on adjacent event ordering or matching repeated program counters.

Transaction-level protocol effects use `Scope = Transaction`, omit frame and instruction identity, and use a typed reason. Frame-level effects use `Scope = Frame` and require a valid frame ID.

### Frame state lifecycle

- `FrameStateCheckpointEvent`: emitted immediately after the frame's state overlay is created.
- `FrameStateResolvedEvent`: emitted once with `Commit` or `Rollback` at the same authoritative branch that resolves the frame.
- `TransactionPersistenceEvent`: emitted once with `CommittedToState` or `SimulationDiscarded` after top-level settlement.

Frame `Commit` means that the frame's effects survive into its parent EVM state. For the root frame it means successful EVM resolution; external persistence remains the separate transaction event.

### State observations

- `StorageReadEvent`: storage address, slot, value, and warm/cold status when applicable.
- `StorageWriteEvent`: storage address, slot, original value, value before this instruction, and requested value.
- `TransientStorageReadEvent`: storage address, slot, and value.
- `TransientStorageWriteEvent`: storage address, slot, previous value, and requested value.
- `BalanceTransferEvent`: sender, recipient, amount, and typed reason such as transaction value, call value, self-destruct, gas refund, miner fee, or protocol reward.
- `NonceChangedEvent`: address, previous value, current value, and typed reason.
- `CodeChangedEvent`: address, action (`Created`, `Installed`, `Cleared`, or `Deleted`), previous/new code hashes, and sizes. Full code bytes are not duplicated into every event.
- `LogEmittedEvent`: emitting address, topics, and data.
- `SelfDestructEvent`: contract, beneficiary, transferred balance, fork-dependent deletion eligibility, and whether deletion was scheduled.

Events store typed values, not rendered diagnostic strings. DTO rendering happens at the RPC boundary.

## Authoritative instrumentation boundaries

Instrumentation is added only where the engine already knows the semantic fact:

- Frame checkpoint and resolution: `StateTransition.ExecuteInternalAsync`, adjacent to `StateOverlay` creation and its success/failure resolution.
- Storage: `SLOAD` and `SSTORE`, using the values already loaded for gas calculation.
- Transient storage: `TLOAD` and `TSTORE`, at their existing frame-aware storage boundary.
- Internal and transaction value: the existing transfer branches in `StateTransition` and CALL-family opcodes.
- Code lifecycle: CREATE/CREATE2 deployment, EIP-7702 designation, creation rollback, and account deletion boundaries.
- Logs: LOG0 through LOG4 when the `TransactionLog` is created.
- Self-destruct: `OpcodeSelfDestruct` where beneficiary, balance, and fork rule are known.
- Settlement transfers: the existing refund and coinbase payment branches.

The implementation must not wrap every `IGlobalState` setter and interpret low-level assignments after the fact. That would confuse semantic transfers with bookkeeping and would record overlay propagation as duplicate effects.

## Explicit call identity correction

Security analysis cannot be authoritative while frame creation guesses the call type. The current `DetermineCallType` path treats a non-null code address as `DELEGATECALL`, which conflates `CALLCODE` and `DELEGATECALL`.

The canonical recursive call interface will pass the actual `CallType` explicitly into frame creation. `FrameEnteredEvent` will also expose both:

- `ContractAddress` / storage owner; and
- `CodeAddress` / executing code owner.

This is an observability correction only. It must not change call semantics, gas, or state behavior.

## Journal analysis model

Create one read-only `JournalAnalysis` projection. It indexes the immutable journal once and exposes:

- frames by ID and parent ID;
- exact ancestor paths;
- instructions and their effects;
- state observations with execution and persistence dispositions;
- storage owner versus code owner;
- effects grouped by frame, account, slot, and instruction;
- gas-tree and conservation references by event sequence.

All advanced analyzers consume this projection. Individual detectors must not parse raw event arrays independently or rebuild frame stacks.

Malformed journals fail closed with a typed analysis error. Examples include missing frame resolution, duplicate frame IDs, an effect referencing an unknown frame, or multiple transaction-persistence events. RPC returns a structured internal error; it does not silently downgrade to flat-trace heuristics.

## Journal-native security analysis

### Reentrancy

The analyzer identifies re-entry using explicit active ancestry rather than depth:

1. A descendant frame enters a storage context already present in its ancestor chain.
2. The relationship is a real external re-entry, not ordinary `DELEGATECALL` execution using the proxy's storage context.
3. The analyzer links the entry edge, re-entry edge, relevant reads/writes, and frame resolutions.
4. Checks-effects-interactions evidence is based on journal order and exact parent return boundaries.
5. The finding reports whether relevant effects survived or reverted.

A reverted re-entry attempt is retained at informational severity. A surviving execution can receive a higher risk severity only when the detector has the required state-interaction evidence.

### Storage collision

The analyzer no longer walks backward through trace depth to guess proxy and implementation addresses. A collision candidate requires:

1. A frame whose explicit call type is `DELEGATECALL` or `CALLCODE`, interpreted according to their actual storage semantics.
2. Executing code ownership distinct from the storage ownership when the call type permits it.
3. A typed storage write in that frame.
4. A rule match, such as an EIP-1967 reserved slot or configured layout conflict.

The finding links the call frame, code owner, storage owner, write event, slot rule, and final disposition. Reverted writes are informational; surviving writes are graded according to the collision rule and evidence.

### Finding DTO

Each finding contains:

- stable finding ID and rule ID;
- category and severity;
- execution-fact grade;
- summary rendered from typed fields;
- primary frame and instruction IDs;
- supporting event sequences;
- frame ancestry;
- execution and persistence dispositions;
- affected addresses and storage slots;
- explicit limitations on what the evidence proves.

## RPC and React

`schlieren_traceJournal` gains additive fields:

- `stateEffects`: the analyzed state observations and dispositions;
- `securityFindings`: journal-native proof-linked findings.

The raw `events` collection also includes the new typed journal event kinds. Existing fields retain their meaning. State effects and findings are returned by default. A future payload-control option may omit derived security findings, but raw state-effect capture remains enabled whenever the journal is enabled.

React uses the derived DTOs rather than reimplementing ancestry or severity rules in TypeScript. It can show:

- surviving versus reverted-path effects;
- simulation-discarded versus persisted effects;
- a finding's exact frame and opcode;
- storage owner and code owner side by side;
- the ancestor frame responsible for rollback.

`debug_inspect` and `debug_traceCall` retain byte-for-byte-compatible JSON shapes. Avalonia may consume the new analyzer internally during migration, but no new legacy DTO contract is introduced.

## Removal of legacy heuristics

Once journal-native detector tests are green:

- replace `ReentrancyDetector`'s depth-stack algorithm;
- replace `StorageCollisionDetector`'s backward depth scan;
- migrate `LiveReentrancyDetector` and `LiveStorageCollisionDetector` to journal-derived observations or remove them if they only serve the retired Avalonia path;
- update regression and Workbench consumers to use `JournalAnalysis`;
- remove detector code that accepts flat traces when no compatibility consumer remains;
- remove or isolate the synthetic Workbench trace generator so fabricated steps cannot be mistaken for canonical evidence.

There must be one detector implementation. Compatibility adapters may render results, but they may not preserve an independent heuristic analyzer.

## Performance and capture behavior

State-effect events are substantially smaller than full stack, memory, and storage snapshots. Capture is append-only and proportional to actual effects. The analyzer builds indexes in one pass over the journal.

Journal-disabled execution must allocate no state-effect collections and must preserve current behavior. Journal-enabled execution may perform reads only when the value is already required for execution or gas calculation; instrumentation must not introduce remote fork-provider reads merely to enrich diagnostics.

## Testing strategy

### Behavior parity

For each supported effect category, execute the same transaction with journaling disabled and enabled and assert identical:

- success/error;
- gas used and refund counter;
- return data and logs;
- committed state and receipt-relevant results;
- legacy trace steps when tracing is enabled.

### Lifecycle and disposition

- Successful child and successful root: child effect is `Survived`.
- Successful child followed by parent revert: child effect is `Reverted` with the parent frame ID.
- Child revert followed by successful parent: child effect is `Reverted`; parent effects survive.
- Successful `commit: true` execution: surviving effect is `CommittedToState`.
- Successful `commit: false` execution: surviving effect is `SimulationDiscarded`.
- Reverted effect: persistence is `NotApplicable`.
- Every opened frame checkpoint resolves exactly once.

### Effect coverage

- SLOAD and SSTORE values, including no-op writes and original/current/new values.
- TLOAD/TSTORE behavior across nested success and rollback.
- CALL value transfer success and rollback.
- CREATE/CREATE2 code installation and failed creation cleanup.
- EIP-7702 code designation changes.
- LOG events that survive and logs discarded by ancestor revert.
- SELFDESTRUCT before and after EIP-6780 behavior.
- Refund and coinbase settlement transfers.

### Security correctness

- True nested re-entry is identified by frame ancestry.
- Ordinary DELEGATECALL proxy execution is not labeled reentrancy.
- Reverted re-entry remains visible at informational severity.
- A successful child re-entry later reverted by its parent is not labeled committed.
- DELEGATECALL writes identify proxy storage owner and implementation code owner.
- CALLCODE and DELEGATECALL retain distinct call types.
- Reserved-slot writes are linked to exact event and final disposition.
- Nested frames at repeated depths cannot be confused.

### Contracts and architecture

- Golden JSON tests prove `debug_inspect` and `debug_traceCall` do not change.
- `schlieren_traceJournal` additive DTO tests cover every new event and finding field.
- Reflection/source architecture tests reject the deleted flat-trace detector algorithms and guessed call-type path.
- React parser and view-model tests cover additive fields, missing optional fields during rolling upgrades, and evidence navigation.

## Delivery boundaries

This design should be implemented in ordered slices:

1. Correct explicit call identity and add instruction correlation.
2. Add frame lifecycle and storage/transient-storage events with disposition analysis.
3. Add transfers, nonce, code, logs, and self-destruct effects.
4. Build `JournalAnalysis` and its invariant validation.
5. Replace reentrancy and storage-collision detectors.
6. Add RPC DTOs and React evidence views.
7. Remove obsolete heuristic and synthetic consumers.

Every slice begins with failing tests and ends with behavior-parity verification. No slice may restore diagnostic re-execution or simplified state inference.

## Acceptance criteria

The work is complete when:

1. State effects are captured during the one canonical execution.
2. Every effect has exact frame and instruction provenance where applicable.
3. Successful child effects are marked reverted when any ancestor rolls back.
4. Simulation discard is distinct from EVM revert.
5. CALLCODE and DELEGATECALL frame metadata are correct.
6. Reentrancy and storage-collision analysis uses explicit frames and typed effects only.
7. Reverted attempts remain visible but cannot be graded as committed vulnerabilities.
8. Findings link to supporting event sequences and state their proof limitations.
9. Existing RPC JSON contracts remain unchanged.
10. Journal-disabled and journal-enabled executions remain behaviorally identical.
11. No duplicate flat-trace security implementation remains in active use.
