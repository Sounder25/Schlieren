# Schlieren: Deterministic Execution Intelligence for the EVM

## Purpose

Schlieren records execution facts inside the canonical EVM run instead of reconstructing them afterward from a flat trace. `StateTransition.ApplyTransactionAsync` remains the sole transaction evaluator. When journaling is enabled, the evaluator and `EvmMachine` append immutable, typed observations to one `ExecutionJournal`.

The journal is evidence produced by execution. Derived projections—including the exclusive gas tree, frame hierarchy, state-effect dispositions, RPC DTOs, and security findings—must never re-run the transaction or infer frame ancestry from trace depth.

## Why source observation matters

A conventional opcode trace records useful machine state, but depth alone cannot prove:

- which call-family opcode created a frame;
- whether code and storage belong to different accounts under `DELEGATECALL` or `CALLCODE`;
- which frame owns a state effect or gas charge;
- whether a successful child was later rolled back by an ancestor;
- whether surviving EVM state was persisted or intentionally discarded by simulation.

Those facts exist at execution time. Schlieren records them there.

## Canonical execution and journal identity

Every journal event carries a transaction-wide monotonic `Sequence`, optional `FrameId`, optional `ParentFrameId`, and optional `InstructionId`.

Before a valid opcode executes, `EvmMachine` allocates one `InstructionId`. The opcode observation and every semantic state effect emitted during that instruction share the ID. Transaction-level protocol effects may omit instruction identity and instead carry a typed reason.

State effects also receive a monotonic `EffectId`. The journal exposes a read-only event collection and defensively copies mutable snapshot payloads.

## Explicit frame geometry

The recursive subcall boundary receives the exact `CallType`. It does not infer call identity from `isStatic`, a creation address, or the presence of a separate code address.

Each `FrameEnteredEvent` records:

- the exact call type (`Call`, `CallCode`, `DelegateCall`, `StaticCall`, `Create`, or `Create2`);
- `ContractAddress`, the execution/storage context;
- optional `CodeAddress`, the separate executing-code owner;
- the parent frame and depth;
- the frame gas limit.

This preserves the distinction between `CALLCODE` and `DELEGATECALL` and exposes proxy geometry without a backward scan through trace steps.

## Frame lifecycle and disposition

Each opened frame has one checkpoint and one final resolution:

- `FrameStateCheckpointEvent` follows creation of the frame overlay.
- `FrameStateResolvedEvent(Commit)` means the frame survived into its parent EVM state.
- `FrameStateResolvedEvent(Rollback)` means the frame effects did not survive.
- `TransactionPersistenceEvent` independently records `CommittedToState` or `SimulationDiscarded`.

Successful `CREATE` and `CREATE2` initcode does not resolve early. Code-size, EF-prefix, and code-deposit validation complete first. A failed deposit resolves the creation frame once as rollback.

`JournalAnalysis` validates frame, checkpoint, resolution, ancestry, effect-scope, and transaction-persistence invariants. For every effect it derives:

- execution disposition: `Survived` or `Reverted`;
- the nearest `RevertedByFrameId`, when applicable;
- persistence disposition: `CommittedToState`, `SimulationDiscarded`, or `NotApplicable`.

Execution and persistence are intentionally separate. A successful dry run survives EVM execution but is discarded by simulation; it is not an EVM revert.

## Gas as an exclusive ledger

Gas events declare semantics rather than relying on a caller to interpret raw deltas:

- `ExclusiveCharge`: additive work owned by one scope;
- `Allocation`: non-additive gas forwarded to a child;
- `Return`: non-additive unused child gas returned to its parent;
- `ExceptionalBurn`: additive gas destroyed by exceptional execution;
- `RefundCounter`: a change to the protocol refund accumulator;
- `Credit`: the effective settlement refund, subtracted once;
- `Observation` and `InclusiveFrameDelta`: evidence, not additive charges.

The gas tree is rebuilt exclusively from journal events. CALL-family allocations are never added once in the parent and again in the child. Conservation compares journal-derived gas with canonical settlement.

## Typed state effects

Implemented state observations currently include:

- persistent storage reads and writes;
- transient storage reads and writes;
- balance transfers, including value movement and settlement transfers;
- nonce changes with typed reasons;
- code installation, replacement, and removal;
- emitted logs with topics and data;
- self-destruct intent, beneficiary transfer, and deletion eligibility.

Persistent writes carry storage owner, slot, original value, previous value, requested value, warm/cold status, frame, PC, opcode, and instruction identity. Transient writes carry previous and requested values without adding remote state reads.

Mutable byte arrays and collections are defensively copied when recorded, so later machine mutation cannot rewrite evidence already in the journal.

## Server-derived RPC contract

`schlieren_traceJournal` returns journal-derived data for the React workbench. Existing `debug_inspect` and `debug_traceCall` JSON contracts remain unchanged.

The new endpoint returns both the existing flat `frames` array and an authoritative server-built `frameTree`. Each tree node contains:

- its complete frame DTO;
- ordered `ancestorIds`;
- direct `stateEffectIds`;
- direct `securityFindingIds`;
- recursively ordered `children`.

React traverses the supplied children. It does not group flat frames by `parentId` or reconstruct ancestry. During rolling upgrades, a missing tree normalizes to `null`; the client does not fall back to heuristics.

Stack, memory, and storage snapshots are included by default and may be disabled independently. Optional ephemeral `code` executes at the requested address in discarded state.

## Security proof boundary

A security finding must be a bounded claim about observed execution, not a claim that a contract is exploitable for every input. A complete finding links its primary frame and instruction to supporting journal sequences, ancestry, affected addresses/slots, execution disposition, persistence disposition, and an explicit limitation.

Reverted evidence remains useful forensic evidence. It is graded informational and marked `Reverted` / `NotApplicable`, rather than presented as a committed high-severity vulnerability. `JournalSecurityAnalyzer` is the sole active reentrancy and delegate-storage-collision detector. The flat-trace batch/live detectors and synthetic detector demo have been removed.

## Implemented and verified on this branch

- typed gas journal and conservation model;
- explicit nested frame IDs and parent relationships;
- exact call identity, including `CALLCODE` and `CREATE2`;
- one instruction ID per executed opcode;
- frame checkpoints, deferred creation resolution, and transaction persistence;
- validated state-effect disposition analysis;
- persistent and transient storage events;
- typed balance, nonce, code, log, and self-destruct effects;
- additive state-effect DTOs and a server-built frame tree;
- proof-linked reentrancy and delegate-storage-collision findings;
- React traversal of the server-built tree with no TypeScript ancestry or severity reconstruction;
- EIP-3155 projection and deterministic first-divergence comparison with journal frame context;
- journal-derived Avalonia and regression security consumers.

## Verification boundary

The architecture is implemented. A release claim still requires fresh focused, EELS, React, and full-suite runs in the release environment. Missing external fixture directories and failures reproduced unchanged on the base branch must be reported separately from journal regressions. No test count or zero-regression claim belongs in this document unless generated from such a recorded run.
