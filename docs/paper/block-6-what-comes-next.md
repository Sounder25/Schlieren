# Schlieren: Deterministic Execution Intelligence for the EVM
## Block 6 — What Comes Next

---

### What Was Built

Everything described in Blocks 1 through 5 is implemented and verified on `feature/journal-gas-tree-rpc-react`. The canonical typed journal covers gas, frames, storage, balances, nonce, code, logs, and self-destruct. The journal-native security analyzers are live. The RPC delivers a server-built frame tree. React renders it. The legacy heuristics are gone.

What follows is what this foundation enables — the next surface of work that was not possible before this branch existed.

---

### The API Path

Schlieren runs locally today. The RPC endpoint is `localhost:8545`. The React UI is served from `localhost:3333` in development and packaged via Tauri for desktop distribution.

The future API path:

1. **Hosted RPC**: `schlieren_traceJournal` becomes available at a public endpoint, callable from any client. Users submit a transaction spec (bytecode, calldata, fork, optional code for ephemeral execution) and receive a journal-derived inspection result.

2. **API authentication**: users register, obtain an API key, and submit requests that are metered per transaction. Pricing tiers structure usage: individual researchers, audit firms, continuous integration pipelines.

3. **Result storage**: journals are retained for a configurable period, allowing users to retrieve past analyses without re-execution. This is particularly valuable for long-running investigations where the analyst returns days later.

4. **Batch analysis**: a conformance runner API accepts a corpus of fixtures and returns aggregated results. This is the productized form of the existing regression harness.

5. **Integration hooks**: webhooks for automated pipelines. A CI system can submit transactions from every PR, receive findings, and fail builds that introduce security patterns.

The journal architecture makes all of this possible because it produces output that is:

- **Compact**: events are smaller than full trace steps. A typical transaction produces a few kilobytes of journal data.
- **Self-contained**: the journal contains everything needed to derive findings. No external state references, no reconstruction.
- **Cacheable**: the same transaction inputs produce the same journal. Results can be cached indefinitely.
- **Auditable**: every finding is traceable to its evidence. No uncheckable claims.

---

### What This Enables That Didn't Exist

To be explicit about what Schlieren can do after this architecture that nothing else can do:

**Exact gas attribution in the presence of nested calls**. No other tool can tell you the precise gas cost of a specific opcode in a specific frame because no other tool records frame identity at execution time. Existing tools approximate from depth. Schlieren records the frame ID and computes attribution exactly.

**Reentrancy detection without DELEGATECALL false positives**. Existing detectors must heuristically filter DELEGATECALL patterns. Schlieren knows the storage owner and code owner for every frame and detects reentrancy by ancestor storage-context presence. The false positive class is not "reduced" — it is eliminated.

**Storage collision detection with commit semantics**. Existing detectors report a collision and stop. Schlieren reports the collision, the frame geometry, the precise write, and whether the write survived — because the frame ancestry and resolution are in the journal.

**Forensic evidence from reverted paths**. Existing tools discard reverted execution. Schlieren preserves every event with its causal frame and marks it `Reverted`. An attempted attack that failed is visible, analyzable, and linkable to the instruction that tried it.

**Checkable security findings**. Every finding points to sequence numbers. You can verify the finding by reading the journal. This is not possible in any system that produces findings from post-hoc reconstruction.

**Simulation-aware persistence tracking**. Existing tools cannot distinguish "this effect was reverted by the EVM" from "this effect was discarded by the simulation." Schlieren records both as separate facts. An analysis running in simulation mode knows exactly what would have happened if it were persisted — and reports it correctly.

---

### The Larger Claim

This paper has argued that Schlieren's architecture represents a different approach than what the major EVM analysis tools do. The difference is not about features, performance, or integration. It is epistemological.

A trace-based tool answers: "what does the trace look like if we run a detector against it?"

Schlieren answers: "what happened, and what does that prove?"

The difference is that Schlieren's answer can be verified. Not believed. Verified. The journal is not an approximation of execution. It is execution's own record of itself. Any claim derived from the journal is a claim about the execution — not a claim about a reconstruction.

Most tooling in any domain is incremental: it tries to do the same thing better. Schlieren does not try to do trace analysis better. It does something different: it observes rather than reconstructs.

That is the contribution. And the contribution is now built, tested, and running.

---

*End of paper.*
