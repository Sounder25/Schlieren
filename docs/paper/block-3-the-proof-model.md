# Schlieren: Deterministic Execution Intelligence for the EVM
## Block 3 — The Proof Model

---

### Three Claims

A security finding is a claim. Claims have different strengths. Before Schlieren can produce a finding, it must be clear what kind of claim the finding makes — because confusing one kind with another is how false positives and misleading severity ratings happen.

There are exactly three distinct claims a tool can make about an EVM state effect:

**Claim 1: An operation was observed.**
The engine executed an SSTORE. The journal recorded it. This is a historical fact. It does not say whether the write survived. It does not say whether it was persisted to the backing state. It says the instruction executed.

**Claim 2: The operation remained on the transaction's successful EVM path.**
The frame that produced the SSTORE committed. Every ancestor frame also committed. The write is part of the canonical EVM result of the transaction. This is a stronger claim than observation — it requires knowing the outcome of the entire ancestor chain, not just the frame that executed the instruction.

**Claim 3: The operation was persisted to the backing state.**
The caller requested that the transaction's result be applied to the persistent state. The write is now on-chain. This is independent of Claim 2 — a simulation can execute and commit under EVM rules while the caller intentionally discards the result.

Most tools conflate these three claims. They report observed effects as if they were committed effects. They label simulation-discarded effects as reverted. They cannot distinguish a write that a parent frame rolled back from a write the simulation simply chose not to persist.

Schlieren tracks all three independently, for every effect, from the same canonical execution.

---

### Execution Disposition

Execution disposition answers Claim 2: did this effect survive under EVM rules?

After the transaction completes, `JournalAnalysis` walks the ancestor chain of every state effect. The rule is simple:

- An effect is **Survived** if its owning frame resolved with `Commit` and every ancestor frame also resolved with `Commit`.
- An effect is **Reverted** if its owning frame resolved with `Rollback`, or if any ancestor frame resolved with `Rollback`.

For reverted effects, the analysis records `RevertedByFrameId` — the specific ancestor that caused the rollback. This is not just a boolean. It is a causal pointer. Given a storage write that did not survive, you can identify exactly which frame in the call chain reverted and why.

One case deserves explicit attention: a child frame that succeeds but whose parent later fails. Under EVM rules, the child's effects do not survive — they are rolled back when the parent reverts. Schlieren records these effects as `Reverted` with the parent's frame ID as the `RevertedByFrameId`. This is a real phenomenon in flash loan attacks and reentrancy patterns: the attacker's callback executes successfully but the outer transaction eventually fails, rolling back the theft. An analysis tool that only looks at child frame outcomes will misclassify this as a successful exploit. Schlieren will not.

Reads have execution disposition too, but it means something different. A storage read that occurs on a reverted path still happened — it informed the execution that reverted. Disposition for reads describes the path on which they occurred; it does not imply the read can be undone. This distinction matters for evidence: reads on reverted paths are forensically significant even when writes on those paths are not.

---

### Persistence Disposition

Persistence disposition answers Claim 3: were the surviving effects written to the backing state?

This is evaluated after execution disposition, and only for effects that survived:

- **CommittedToState**: the effect survived under EVM rules, and the caller requested `commit: true`. The write is persistent.
- **SimulationDiscarded**: the effect survived under EVM rules, but the caller requested `commit: false`. The write was correct under EVM semantics but intentionally not applied.
- **NotApplicable**: the effect was reverted before reaching the persistence decision. There is nothing to persist or discard.

The `schlieren_traceJournal` endpoint runs with `commit: false` by default — it simulates execution to produce inspection data without mutating chain state. This means a successful SSTORE in a simulation returns `Survived` with `SimulationDiscarded`. It must never be labeled as `Reverted`. The effect executed correctly. The simulation chose not to persist it. Those are different facts.

This separation prevents a class of confusion that affects every other EVM inspection tool: the conflation of "this write didn't make it to chain state" with "this write was reverted by the EVM." The first is a caller decision. The second is an EVM outcome. Treating them as the same produces incorrect security findings.

---

### The Forensic Principle

Reverted effects are evidence. They must not be erased.

In traditional trace analysis, reverted effects are often invisible — the tool sees a REVERT, backs up to the last successful state, and presents only what survived. The reverted path is treated as if it did not happen.

This is analytically wrong. A failed reentrancy attack that was reverted by the outer transaction is still evidence that the attack was attempted. A write to a reserved storage slot that was rolled back by a parent frame is still evidence that a collision pattern was executed. A value transfer that was reverted by an out-of-gas condition is still evidence of what the contract attempted.

Schlieren preserves all of this. Reverted effects remain in the journal. They receive execution disposition `Reverted` with a causal pointer to the reverting frame. They are returned in `schlieren_traceJournal` results. They appear in the React UI, clearly labeled with their disposition.

The critical constraint is severity: a reverted effect cannot produce a high-severity committed-vulnerability finding. The evidence exists. The severity is bounded by the fact that it did not survive.

A reverted reentrancy attempt is `Informational`. A surviving reentrancy attempt with confirmed state corruption is `High`. These are not the same finding with different labels. They are different claims about different facts. The journal makes this distinction automatic because execution disposition is a derived fact, not a human judgment.

---

### What the Journal Cannot Prove

Clarity about what a tool proves requires equal clarity about what it does not prove.

Schlieren can prove:

> Frame 3 executed SSTORE at sequence 148, writing slot 0x01 in the proxy's storage context while running implementation code. Frame 3 resolved with Commit. Frames 2 and 1 resolved with Commit. The effect disposition is Survived. The transaction ran with commit: false, so persistence disposition is SimulationDiscarded.

Schlieren cannot prove:

> This contract is exploitable for all possible inputs.

The journal records one execution of one transaction. It proves what happened in that execution with complete fidelity. It does not prove what would happen in a different execution with different inputs, a different caller, different chain state, or a different block timestamp.

Security detectors built on the journal can identify patterns — reentrancy structure, storage collision geometry, proxy delegation relationships — and assess their severity based on what the evidence shows. They can make claims of the form: "given the execution we observed, the following pattern is present and the following effects survived." They cannot make claims of the form: "this contract will always be exploitable."

This limitation is not a weakness. It is honesty. A finding that states its evidence and its limits is more valuable than a finding that claims more than it can support. Every Schlieren finding explicitly states what the evidence proves and what it does not.

---

### Proof-Linked Findings

A finding that cannot be traced back to its evidence is not a finding. It is an opinion.

Every security finding produced by Schlieren carries:

- **Primary frame ID** — the frame in which the relevant pattern was detected
- **Primary instruction ID** — the specific opcode that is the focus of the finding
- **Supporting event sequences** — the journal sequence numbers of the events that constitute the evidence
- **Frame ancestry** — the full ancestor chain, showing which frames committed and which reverted
- **Execution disposition** — `Survived` or `Reverted` for the primary effect
- **Persistence disposition** — `CommittedToState`, `SimulationDiscarded`, or `NotApplicable`
- **Affected addresses and storage slots** — the precise state locations involved
- **Proof limitations** — an explicit statement of what the evidence does and does not demonstrate

This is not metadata added for completeness. It is the difference between an assertion and an argument. A finding without supporting evidence is asking the user to trust the detector. A finding with supporting evidence is showing the user the facts and letting them verify the reasoning.

In the React UI, every finding is navigable: click the primary instruction ID to jump to that step in the disassembly view; click a supporting event sequence to see the state at that moment; trace the frame ancestry to understand why a particular effect did or did not survive.

The finding is not a conclusion handed down from above. It is an entry point into the evidence.

---

### The Claim Boundary in Practice

To make this concrete: consider a standard reentrancy attack.

An attacker contract calls into a vulnerable lending pool. The pool executes a transfer before updating its internal balance. The attacker's receive function re-enters the pool and drains additional funds.

In the journal:

1. **Frame 1** (root): attacker calls pool. `FrameEnteredEvent`, CallType `Call`, code and storage both `pool`.
2. **Frame 2**: pool executes transfer to attacker. `BalanceTransferEvent`, `FrameId: 2`.
3. **Frame 3**: attacker's receive function re-enters pool. `FrameEnteredEvent`, CallType `Call`. Journal analysis identifies that frame 3's contract address is already present in the ancestry of frame 2.
4. Frame 3 executes another withdrawal. `StorageWriteEvent`, `FrameId: 3`, modifying pool's balance slot.
5. Frames 3, 2, 1 all resolve with `Commit`.

The reentrancy detector, consuming `JournalAnalysis`, identifies:
- Frame 3 entered a storage context (pool's address) already present in its ancestor chain
- This is an external re-entry, not ordinary delegation
- The write in frame 3 survived (all ancestors committed)
- The balance slot was written before it was updated for the first withdrawal (checks-effects-interactions violation evident from event ordering)

Finding: `Reentrancy`, severity `High`, disposition `Survived/SimulationDiscarded`.

The evidence cited: `FrameEnteredEvent` at frame 3, `StorageWriteEvent` at sequence N, ancestor chain [frame 1 → frame 2 → frame 3], all resolving `Commit`.

Now suppose the outer transaction reverts — the attacker made an error and the entire transaction fails. The journal records all the same events. The execution dispositions change: all effects in all frames become `Reverted`. The finding becomes: `Reentrancy`, severity `Informational`, disposition `Reverted` (by frame 1).

Same pattern. Same evidence. Different claim. Different severity. Automatically derived from the same record. No human judgment required to distinguish them.

---

*Next: Block 4 — Security Analysis Rebuilt. How reentrancy and storage collision detection are reconstructed from scratch on journal-native proof, and what changes about their precision and their limits.*
