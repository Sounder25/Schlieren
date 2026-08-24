# Schlieren: Deterministic Execution Intelligence for the EVM
## Block 1 — The Problem With Post-Hoc Analysis

---

### The Illusion of Observation

Every major Ethereum analysis tool in use today shares a fundamental architectural assumption so deeply embedded it is rarely stated: that you can understand what an execution *did* by examining what it *left behind*.

Tenderly reconstructs call frames from RPC trace depth fields. Foundry's `--verbose` flag walks a flat array of opcode steps and infers nesting from stack depth changes. Hardhat's stack traces parse revert strings and error selectors. EthTx.info renders a call tree by grouping steps that share a depth value. The approaches differ in polish and presentation. The assumption is identical.

That assumption is wrong — not approximately wrong, not wrong in edge cases. Wrong in the way that matters most: it cannot be corrected by making the approximation more precise. The problem is not the quality of the reconstruction. The problem is that reconstruction is being done at all.

---

### What Reconstruction Actually Is

When a tool reconstructs a call frame from depth changes, it is solving an inverse problem. The execution happened. State changed. Gas was consumed. The engine moved on. Now the tool is trying to work backward from the evidence to a cause.

This is forensics, not observation. It has the same fundamental limitation as all forensics: the evidence is incomplete, the reconstruction is probabilistic, and the conclusions are hedged by everything the evidence does not contain.

Consider what a flat EVM trace actually records:

- The program counter before each opcode
- The opcode mnemonic
- The gas remaining before execution
- The depth counter
- The stack, memory, and storage snapshots at that step

What it does not record:

- Which frame owns this step
- Why the depth changed (CALL vs DELEGATECALL vs STATICCALL vs CREATE)
- Whether this frame's effects will survive into committed state
- Whether a parent frame will revert after this child succeeds
- The causal relationship between an SSTORE at depth 3 and a REVERT at depth 2
- Which storage address is the semantic owner of a write vs which address is executing the code

None of these can be recovered from the trace with certainty. They can only be inferred. And inference produces false positives.

---

### The False Positive Problem

False positives in EVM security analysis are not a tuning problem. They are a structural consequence of building detectors on inferred frame identity.

Reentrancy detectors that reconstruct frames from depth changes cannot distinguish a legitimate DELEGATECALL proxy executing implementation code from a genuine external re-entry. Both produce identical depth signatures. The detector must guess based on address patterns, call type fields that may be unreliable, or heuristics about which contracts "look like" proxies.

Storage collision detectors that walk backward through depth to identify proxy and implementation relationships fail silently on any proxy pattern that does not match the expected shape. They also cannot determine whether a suspected collision write survived into committed state or was rolled back by a parent frame revert. A write that never commits is reported with the same severity as one that does.

Gas attribution tools that sum depth-1 opcode costs and add 21,000 for intrinsic gas produce numbers that are wrong for any transaction with nested calls. The CALL opcode's `gasCost` field in a flat trace includes the gas allocated to the child frame. If the tool then also sums child opcodes, it double-counts. If it excludes child opcodes, it undercounts. The correct number requires knowing the exact allocation boundary, which is not recorded in the flat trace.

These are not bugs that can be patched. They are consequences of the architectural choice to reconstruct rather than observe.

---

### Why This Has Been Acceptable

The reconstruction approach has been acceptable for one reason: it works most of the time.

For simple transactions — a single-frame ERC-20 transfer, a straightforward Uniswap swap, a basic USDC approval — the flat trace contains enough information to reconstruct what happened with high confidence. The depth changes cleanly. There is no ambiguous frame ownership. The gas math is simple. The output is correct.

The problem surfaces at exactly the transactions that matter most for security analysis:

- Multi-hop DEX routes with deeply nested DELEGATECALL chains
- Proxy contracts executing implementation code that writes to proxy storage
- Flash loan callbacks that re-enter lending pools
- MEV sandwich attacks with complex internal call structures
- Contracts that combine CREATE2 deployment with immediate initialization calls

These are the transactions where false positives are most damaging, where gas attribution is most critical, and where the distinction between a reverted attack attempt and a successful exploit matters most. And these are precisely the transactions where reconstruction fails.

---

### The Deeper Problem: Two Executions

The deepest structural flaw in the reconstruction approach is not that it gets answers wrong. It is that it requires running the execution twice.

The first execution is the one the EVM actually performed. It has a definitive result: gas used, state changes, return data, logs, receipts.

The second execution is the one the analysis tool performs mentally — walking the trace, inferring frames, reconstructing the call tree, applying heuristics, producing findings.

These two executions are not the same. The second one operates on incomplete information. It applies approximations. It makes assumptions the first execution never made. And because it is a separate computation from the one that produced the authoritative result, it can produce a different answer.

When the analysis disagrees with the execution, the execution is always correct. The analysis is always the one that is wrong. This means every finding from a reconstruction-based tool carries an irreducible uncertainty: is this finding about what actually happened, or about what the reconstruction inferred happened?

Schlieren eliminates the second execution entirely.

---

### The Principle

The solution is not a better reconstruction algorithm. It is observation at the source.

If the engine records what it does at the moment it does it — not afterward, not from the outside, but from within the authoritative execution path — then there is no reconstruction. There is no inference. There is no second execution.

The engine knows exactly when a frame opens and closes. It knows the precise call type, the storage owner, the code address. It knows which frame owns each gas charge. It knows whether a child frame committed or reverted at the moment that resolution occurs. It knows the exact sequence of state effects and their causal opcodes.

Recording these facts during execution is not a second pass. It is the first pass, extended to capture what was always knowable but never written down.

That is what Schlieren does. The journal is not a trace. It is not a reconstruction. It is a contemporaneous record of the execution as it occurred, written by the only witness with complete knowledge: the engine itself.

Everything else — gas trees, security findings, frame-aware diagnostics, React UI evidence views — derives from that record. Not from an approximation of it. From it.

---

*Next: Block 2 — The Architecture. How one canonical execution produces a typed, append-only journal, and what that journal contains.*
