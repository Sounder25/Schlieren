# Schlieren: Deterministic Execution Intelligence for the EVM
## Block 2 — The Architecture

---

### One Execution

The entire architecture of Schlieren's analysis layer rests on a single constraint: there is exactly one execution of each transaction, and everything the system knows derives from that execution.

This sounds obvious. It is not. Most EVM analysis tools violate it silently. They run the execution, discard the internal state, keep only the flat trace, and then perform a second analytical pass that reconstructs what the execution did from that trace. As described in Block 1, the reconstruction pass operates on incomplete information and produces findings that may not correspond to what actually occurred.

Schlieren's approach is different in kind, not in degree. `StateTransition.ApplyTransactionAsync` is the only transaction evaluator. It runs once. During that single run, the engine appends typed observations to an `ExecutionJournal`. When the run completes, the journal is a complete, causally ordered, frame-aware record of the execution. No second pass. No reconstruction. No inference about what probably happened.

All downstream consumers — the gas tree, the React UI, the security detectors, the RPC inspection endpoint — read from that journal. They do not re-examine the trace. They do not re-run the execution. They consume facts.

---

### The Journal

The `ExecutionJournal` is an append-only sequence of typed events. It is created at the start of each transaction and closed when the transaction settles. Once closed, it is immutable.

Every event inherits from `ExecutionJournalEvent` and carries:

- **Sequence** — a monotonic counter that establishes total ordering across the entire transaction, including all nested frames
- **FrameId** — the identifier of the frame that produced this event
- **ParentFrameId** — the identifier of the parent frame, establishing the ancestry chain

This is the minimal set of fields that makes the journal analyzable. Sequence gives you order. FrameId gives you ownership. ParentFrameId gives you the ancestry chain needed to determine execution disposition.

Beyond these base fields, events are typed. Each event type records exactly the facts that are semantically meaningful for its category — nothing more. The journal is not a trace with extra fields. It is a vocabulary of distinct observations.

---

### Instruction Identity

One of the subtle design problems in any execution journal is causal linking: when multiple events are emitted during a single opcode's execution, how do you know they belong to the same instruction?

The naive answer — correlate by sequence proximity — is fragile. Opcodes can emit a gas event, a storage event, and a balance event in sequence, but if any intervening instrumentation fires between them, sequence proximity breaks.

Schlieren solves this with instruction identity. Before executing each opcode, `EvmMachine` allocates a monotonic `InstructionId` and places it in the execution context for the duration of that instruction. Every event emitted by that opcode — the `OpcodeGasEvent`, any `StorageWriteEvent`, any `BalanceTransferEvent` — carries the same `InstructionId`.

This creates an exact causal link between all effects of a single instruction without relying on ordering assumptions. Given any storage write, you can find its gas cost. Given any gas charge, you can find what state effects it caused. Given any security finding, you can trace back to the exact opcode at the exact program counter in the exact frame that produced the evidence.

Transaction-level protocol effects — intrinsic gas charges, refunds, coinbase payments — may omit `InstructionId` and use a typed reason instead. These are not opcode emissions; they are protocol mechanics, and they say so explicitly.

---

### Frame Lifecycle

Every call frame in the EVM has a lifecycle: it opens, it executes, it resolves. Resolution is either commit (the frame's effects propagate to its parent) or rollback (they do not).

In the flat trace model, this lifecycle is implicit. You infer a frame opened when depth increases. You infer it resolved when depth decreases. You guess whether it committed or rolled back from the next opcode's context.

In the journal model, the lifecycle is explicit:

- **`FrameEnteredEvent`** — emitted when a frame opens, with the call type, contract address (storage owner), and code address (executing code owner). These are two distinct fields because DELEGATECALL and CALLCODE dissociate them.
- **`FrameStateCheckpointEvent`** — emitted immediately after the frame's state overlay is created.
- **`FrameStateResolvedEvent`** — emitted once when the frame closes, with an explicit `Commit` or `Rollback` flag.
- **`TransactionPersistenceEvent`** — emitted once at the end of the transaction, recording whether surviving effects were committed to the backing state or discarded as a simulation.

The distinction between `FrameStateResolvedEvent` and `TransactionPersistenceEvent` is not redundant. A frame that resolves with `Commit` has succeeded under EVM rules — its effects propagate to its parent. That is a different fact from whether the caller asked for those effects to be written to the persistent backing state. A security analysis tool running in simulation mode should not label a successful storage write as "reverted" merely because the simulation did not persist it.

---

### Call Type Correction

There is a correctness problem in the current codebase that the journal architecture makes visible and requires fixing: the existing `DetermineCallType` logic treats any frame with a non-null code address as `DELEGATECALL`, conflating it with `CALLCODE`.

`DELEGATECALL` and `CALLCODE` have identical depth signatures. Both dissociate storage owner from code owner. But they differ in one critical way: `DELEGATECALL` preserves the caller's `msg.sender` and `msg.value`, while `CALLCODE` uses its own. For storage collision analysis, they have the same implication. For security analysis more broadly, they do not.

More importantly, a journal that records `CALLCODE` as `DELEGATECALL` is recording a false fact. A finding derived from that false fact carries the wrong evidence. The correction is not an optimization — it is a precondition for the journal being trustworthy.

The fix: pass the actual `CallType` explicitly into frame creation at the canonical recursive call interface. `FrameEnteredEvent` exposes both `ContractAddress` (storage owner) and `CodeAddress` (executing code owner). This is an observability correction only — it does not change call semantics, gas, or state behavior.

---

### Gas Semantics

The journal's gas model was the first part of the architecture to be built, and it establishes the pattern for everything that followed.

Gas events are tagged with a `GasSemantics` enum that says what the gas charge *means*, not just how much it is:

- **`ExclusiveCharge`** — a charge that belongs entirely to this frame and no ancestor
- **`Allocation`** — gas forwarded into a child frame (this gas will be accounted in the child)
- **`ExceptionalBurn`** — gas consumed by an out-of-gas or invalid opcode condition
- **`RefundCounter`** — a credit added to the refund accumulator
- **`Return`** — unused gas returned to the parent frame
- **`Credit`** — the effective refund applied at transaction settlement

This tagging is what makes gas conservation verifiable without arithmetic on the flat trace. The `TransactionSettledEvent` records `ChargedGas` — the total gas the transaction cost the caller. A correct execution satisfies: the sum of all `ExclusiveCharge` events plus intrinsic plus calldata equals `ChargedGas`. That invariant can be checked mechanically against the journal without any knowledge of fork rules, opcode costs, or frame structure.

The `DifferentialRegressionRunner` previously computed this by summing depth-1 opcode costs and adding 21,000 for intrinsic gas. That calculation was wrong for any transaction with nested frames and was the root cause of the `Smoke_MinimalReturn` test failure. It now reads `ChargedGas` directly from the journal's `TransactionSettledEvent`. The test passes.

---

### The Analysis Model

The journal is immutable. To query it efficiently, `JournalAnalysis` builds a projection over the events in one pass.

The projection exposes:

- frames indexed by ID and parent ID
- exact ancestor paths for any frame
- instructions and their associated events (grouped by `InstructionId`)
- state observations with computed execution and persistence dispositions
- storage owner vs code owner for every frame
- effects grouped by account, slot, frame, and instruction
- gas-tree references by event sequence

All advanced analyzers — reentrancy, storage collision, gas conservation — consume this projection. They do not parse raw event arrays independently. They do not rebuild frame stacks. They ask the projection questions and receive typed answers.

The projection also validates journal integrity. A journal with missing frame resolution, duplicate frame IDs, an effect referencing an unknown frame, or multiple transaction-persistence events is malformed. The analysis fails closed with a typed error. No silent downgrade to flat-trace heuristics.

This is not defensiveness for its own sake. It is a semantic guarantee: if `JournalAnalysis` returns a result, that result is derived from a valid causal record. If it cannot, it says so explicitly.

---

### What This Changes

The practical consequence of this architecture is that Schlieren's analysis layer is separated from its execution layer by a typed, verifiable interface.

The execution layer produces facts. The analysis layer consumes them. Security detectors, gas trees, React UI components, RPC endpoints — none of them need to understand EVM execution mechanics. They need to understand the journal vocabulary.

This separation has a property that no reconstruction-based tool can offer: the analysis is as authoritative as the execution. Not approximately as authoritative. Not authoritative modulo inference errors. Exactly as authoritative — because it reads from the same record the execution wrote.

A finding that says "this SSTORE occurred in frame 3 while executing implementation code, and frame 3's effects survived into committed state" is a statement about the execution journal. The journal was written by the engine during the execution. The finding is therefore a statement about the execution itself.

That is not a claim any reconstruction-based tool can make.

---

### The RPC Contract

The analysis projection does not stay inside the engine. It is serialized through `schlieren_traceJournal` and delivered to the React UI as a structured DTO.

The shape of that DTO reflects a deliberate architectural choice made at commit `98d4049`: the server builds the frame tree. React receives it. React does not reconstruct it.

The response carries:

- **`frameTree`** — a recursive tree of frame nodes, each with nested `children`, ordered `ancestorIds`, per-frame `stateEffectIds`, and per-frame `securityFindingIds`
- **`frames`** — the existing flat frame array, unchanged, for backward compatibility
- **`events`** — the flat event sequence, for consumers that need total ordering

The significance of the tree-first shape is this: before `98d4049`, React received events and ancestry data and reconstructed the frame tree in TypeScript. That TypeScript reconstruction code was removed with this commit. It no longer exists.

This is the same architectural principle applied at the API boundary that was applied inside the engine. The engine does not reconstruct — it observes. The API does not transfer raw data for the client to analyze — it transfers analysis. The client renders.

`securityFindingIds` in the tree nodes is structurally ready. The security analyzer is not yet complete, so the arrays are currently empty. The wire contract is established. When the analyzer is finished, findings will populate into exactly the nodes where the evidence lives — no schema changes required, no client updates required.

`frameTree: null` is a valid response for executions that ran without journaling or against an older endpoint version. React handles this gracefully, falling back to the flat frame display.

---

*Next: Block 3 — The Proof Model. Three distinct claims: observed, survived, persisted. Why they are different, how the journal separates them, and what it means for security analysis.*
