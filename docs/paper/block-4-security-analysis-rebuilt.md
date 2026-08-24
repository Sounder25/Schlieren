# Schlieren: Deterministic Execution Intelligence for the EVM
## Block 4 — Security Analysis Rebuilt

---

### The Cost of Heuristics

The reentrancy and storage collision detectors in most EVM analysis tools are engineering achievements. They handle a wide range of contract patterns, they produce useful results, and they have been tuned over years of real-world use.

They are also fundamentally approximate. And approximation in security analysis has a specific cost: every approximation is a false positive waiting for the right conditions to trigger.

Schlieren's existing `ReentrancyDetector` reconstructs call frames by watching depth fields change. It identifies reentrancy by comparing contract addresses across depth levels and inferring which frames share a storage context. It is clever. It works on standard patterns. And it cannot tell DELEGATECALL proxy execution from external reentrancy, because both produce identical depth signatures.

The existing `StorageCollisionDetector` walks backward through trace depth to identify proxy-implementation pairs. It works when contracts follow expected proxy patterns. It fails silently when they do not. It has no way to determine whether a suspected collision write survived or was rolled back.

Both detectors produce findings that are, at best, strongly suggestive. Neither can produce a finding that is traceable to specific, verifiable evidence. Both will be replaced.

---

### Reentrancy: Frame Ancestry vs Depth Reconstruction

The heuristic definition of reentrancy is: depth increases, then we see the same contract address again at a deeper depth. That's the signal the current detector fires on.

The correct definition is: a frame re-enters a storage context already active in its ancestry chain.

These are not the same definition. The heuristic definition produces false positives on any contract that legitimately calls back to a context it previously touched — a factory that creates a contract and then calls into it, a router that delegates to an implementation and the implementation calls back to a library, an EIP-1167 minimal proxy being used exactly as designed. All of these produce the same depth signature as reentrancy. None of them are reentrancy.

The journal-native definition is exact. Given any `FrameEnteredEvent`, `JournalAnalysis` can walk the ancestor chain by following `ParentFrameId` pointers. If the entering frame's `ContractAddress` appears in any ancestor's `ContractAddress` field, and the call type is `Call` or `CallCode` (not `DelegateCall` to the same contract's logic, which is normal proxy operation), then re-entry of a storage context has occurred.

This eliminates the false positive class entirely. A DELEGATECALL into implementation code is not reentrancy under this definition — the storage context being accessed was already the caller's. A new external CALL into a contract that is already in the ancestor chain is reentrancy under this definition — the external re-entry is explicit in the frame event.

The additional checks — checks-effects-interactions ordering, whether relevant writes occurred before or after the re-entry, whether the re-entered balance slot was already modified — are verified from journal event sequences, not inferred from trace patterns.

---

### Reentrancy Finding Structure

A journal-native reentrancy finding contains:

**Entry edge**: the `FrameEnteredEvent` that initiated the call chain into the vulnerable contract. Frame ID, call type, contract address, code address, instruction ID of the CALL opcode, gas forwarded.

**Re-entry edge**: the `FrameEnteredEvent` that re-entered the storage context. Frame ID (must appear in ancestry of the entry edge's frame), call type, instruction ID of the re-entering CALL opcode.

**Relevant writes**: `StorageWriteEvent` records from the re-entered frame showing what state changed during re-entry, with original values, pre-write values, and written values.

**Balance effects**: `BalanceTransferEvent` records showing value movement during re-entry.

**Ordering evidence**: the sequence numbers of the relevant events, establishing whether the write that should have prevented re-entry had already occurred at the time of re-entry.

**Frame resolutions**: the `FrameStateResolvedEvent` for every frame in the ancestry chain, establishing execution disposition.

**Severity derivation**: `High` if all ancestor frames committed, `Informational` if any ancestor reverted. This is not a human judgment — it follows directly from the execution disposition.

Every element of this finding points to a specific journal event with a specific sequence number. The finding is navigable, verifiable, and reproducible. Run the same transaction again with journaling enabled and you get the same finding with the same evidence.

---

### Storage Collision: Explicit Geometry vs Backward Scan

Storage collision detection today requires the detector to infer that a DELEGATECALL is happening — from depth changes and call type fields that may not be reliably populated — and then guess which contract is the proxy and which is the implementation.

The journal eliminates both inferences.

`FrameEnteredEvent` for a DELEGATECALL frame carries two explicit addresses:
- `ContractAddress`: the storage owner (the proxy). Writes in this frame modify the proxy's storage.
- `CodeAddress`: the code owner (the implementation). The bytecode running is from the implementation.

This is not inferred. It is recorded at the moment the frame opens by the engine that set up the call. The DELEGATECALL geometry is a fact in the journal, not a guess from the analysis.

The collision detector then asks a simple question: given a `StorageWriteEvent` in a DELEGATECALL frame, is the slot being written a known collision candidate?

Collision candidates come from:

- **EIP-1967 reserved slots**: `0x360894...`, `0xb53127...`, `0x4910fd...`, and related admin/beacon/rollback slots are reserved for proxy metadata. Code writing to these slots from implementation code is a direct protocol violation.
- **Configured layout conflicts**: when the storage layout of the implementation overlaps with state variables the proxy itself declares, any write into the overlap zone is a collision.
- **Unguarded slot 0**: implementation contracts that use slot 0 for a state variable, executed via DELEGATECALL from a proxy that also uses slot 0 — a classic collision pattern.

For each candidate, the finding includes:

**Frame geometry**: `FrameEnteredEvent` with `ContractAddress` (storage owner) and `CodeAddress` (code owner), establishing that storage and code ownership are dissociated.

**Write evidence**: `StorageWriteEvent` with the exact slot, original value, pre-write value, and requested value.

**Slot rule**: which collision rule fired and why the slot is sensitive.

**Execution disposition**: whether the write survived all ancestor frames. A write to an EIP-1967 admin slot that was immediately reverted by the outer transaction is a different finding from one that committed.

**Persistence disposition**: `CommittedToState`, `SimulationDiscarded`, or `NotApplicable`.

---

### The False Positive That Cannot Happen

Consider the most common false positive in storage collision detection: a contract that uses DELEGATECALL to a utility library to perform computation, where the library writes to a local variable in slot 0, and the proxy also has a variable in slot 0.

Under the heuristic approach, this fires as a collision. The detector sees DELEGATECALL, sees a write to slot 0 in the proxy's storage context, matches its collision rule, produces a finding. The developer spends time investigating. It is not a vulnerability — the proxy's slot 0 variable is a counter the library intentionally updates, and it is documented as such.

Under the journal approach, this does not fire as a high-severity finding — assuming the collision rule is applied correctly. But more importantly: if it does fire, the finding is verifiable. The developer can see the exact `StorageWriteEvent` sequence number, navigate to it in the React UI, see the frame's `ContractAddress` and `CodeAddress`, see the slot's original and written values, and immediately determine whether this is intentional or a genuine collision.

The journal does not guarantee zero false positives — the detector rules can still be wrong. But it guarantees that every finding is checkable. A checkable false positive is resolved in seconds. An uncheckable false positive generates hours of manual investigation.

---

### The Duplicate Engine Problem

The most dangerous form of architectural debt in a security analysis tool is not broken code. It is correct code in the wrong place — a second implementation of the same logic, running in parallel, that nobody remembers is there.

The audit conducted before the legacy deletion identified exactly this. The regression runner was recalculating intrinsic gas from calldata strings, estimating nested frame gas from depth fields, and re-running security heuristics over flattened trace steps. None of this was labeled as a second truth engine. It had accumulated incrementally over time, each addition locally reasonable, the collective effect invisible until the audit forced a full inventory.

The audit also found Avalonia's bytecode path generating security findings from fabricated depth snapshots — a synthetic trace generator producing outputs that looked like analysis evidence but were constructed from heuristic reconstruction, not journal facts.

The deletion was surgical: 1,280 lines removed. Avalonia's bytecode path and the regression harness now both consume the same `JournalAnalysis` as `schlieren_traceJournal`. The synthetic demo no longer invents findings. The frozen `debug_inspect` and `debug_traceCall` JSON contracts were not touched.

The significance is not the line count. It is the guarantee that follows from the deletion: there is no longer a path through which a transaction can produce different security findings depending on which consumer ran it. One analyzer. One evidence model. One result.

---

### What Security Analysis Becomes

With journal-native detectors in place, security analysis in Schlieren stops being pattern matching on trace shapes and becomes proof generation over execution facts.

A reentrancy finding is not "this looks like reentrancy." It is "re-entry of frame 3's storage context occurred at frame 7; frame 7 wrote slot 0x02 before frame 3 updated it; both frames committed; here are the sequence numbers."

A storage collision finding is not "this might be a collision." It is "DELEGATECALL frame 5 executed code from 0xImpl while writing to 0xProxy's storage at slot 0x360894...; the write survived; this slot is reserved by EIP-1967."

These are auditable claims. They can be verified by reading the transaction trace. They can be reproduced by running the transaction again. They reference specific events in a specific journal. They are either correct or they are not — and if they are not, the error is in the detector rule, which can be fixed, not in a probabilistic reconstruction that cannot be improved.

This is what security tooling looks like when it is built on a deterministic execution record rather than an approximation of one.

---

*Next: Block 5 — Results. What the journal architecture has already proven in production tests, the false positive classes eliminated, and the concrete numbers that demonstrate the improvement.*
