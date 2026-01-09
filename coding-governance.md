# Scrutor Development & QA Governance (v1.0)

**Project Goal:** Build a Windows-Native Ethereum Node optimized for High-Concurrency (Ryzen 9 7900X). Lead QA Standards: US Army Quality Assurance / Web3 Security Auditor Grade.

---

## I. Operational Guardrails (The "No-Filler" Mandate)

1. **Zero-Stub Policy:** Explicitly prohibited from generating `TODO`, `FIXME`, `/* implementation here */`, or `unimplemented!()` macros. Every function must be complete, compiled, and logically sound.

2. **Context-Aware Development:** You are building for **Windows (x64)**. Do not suggest Unix-specific wrappers or WSL2 dependencies. Utilize **IOCP (I/O Completion Ports)** and **Windows Thread Scheduling** for maximum performance.

3. **Functional Integrity (Strict):** Functions must not exceed **50 lines of code**. If logic requires more, refactor into semantic helper functions. Prioritize **Cognitive Complexity < 15**.

---

## II. Version Control & Workflow

1. **Atomic Branching:** Every feature or bug fix must occur on a dedicated branch named `feature/[task-name]` or `fix/[task-name]`.

2. **Commit Standard:** Use **Conventional Commits** (e.g., `feat(p2p): implement async windows socket handler`). Commits must be atomic; do not bundle unrelated changes.

3. **State Protection:** No agent is authorized to merge to `main`. All merges require a **Summary of Evidence (SoE)** artifact (see Section IV).

---

## III. Performance Architecture (Target: "The Beast")

1. **Concurrency First:** Scrutor must utilize the **12-core/24-thread** capacity of the Ryzen 9 7900X. Avoid global mutexes that cause thread contention.

2. **Memory Management:** Optimize for the **64GB RAM** overhead. Use **zero-copy deserialization** for block data and state transitions where possible.

3. **Low-Level Optimization:** Prioritize **SIMD instructions** for cryptographic hashing (Keccak-256) and signature verification.

---

## IV. Definition of Done (DoD) & Verification

An agent may only request a human review once the following **Summary of Evidence (SoE)** is generated and attached to the PR:

### Test Artifacts

- **100% Pass rate** on Unit Tests for new logic.
- **Integration test logs** showing successful state transition for an Ethereum mainnet block fork.
- **Fuzzing Log:** Minimum 5-minute session (Foundry/Echidna) with **0 invariant violations**.

### Performance Trace

- A **execution trace or flamegraph** showing multi-threaded utilization.
- **Memory profile** proving no leaks during 1,000 simulated transactions.

### Security Affirmation

- A brief statement identifying **potential attack vectors** in the new code and how they were mitigated (e.g., reentrancy, integer overflow, Windows-specific privilege escalation).

---

## V. Critical Failure Triggers (Immediate Rejection)

1. Code that relies on `unsafe` blocks without an **explicit, documented justification**.

2. Code that uses **generic error handling** (`catch`, `except`, `unwrap`) instead of specific Windows/EVM error types.

3. Any PR that **lacks the required Summary of Evidence**.

---

## VI. Three-Silo Deployment Architecture

The Scrutor development is divided into **three non-overlapping lanes**. The Agent Manager must enforce strict directory isolation and automated verification for each silo to maintain the integrity of "The Beast's" output.

---

### 🔴 Lane 1: The Execution Core (Active)

**Objective:** High-performance EVM primitive implementation.

**Domain:** `Scrutor.Core`

**Active Task:** Finalizing arithmetic opcodes and stack/memory/storage types.

**Verification:** 100% Pass Rate on `Lane1_CoreEvmTests.cs`.

**Flagging:** Raise `L1_CORE_READY` only when all unit tests pass on a clean `dotnet build` with no warnings.

---

### 🔵 Lane 2: The JSON-RPC Gateway

**Objective:** Build the Windows-native communication layer using IOCP.

**Domain:** `Scrutor.RPC`

**Primary Tasks:**

- Define JSON-RPC 2.0 request/response models.
- Implement an ASP.NET Core Kestrel with **Windows I/O Completion Ports (IOCP)** for high-concurrency handling.
- Expose `eth_blockNumber`, `eth_chainId`, `eth_getBalance`, and `eth_accounts`.

**Verification:** Successful `curl` or Postman handshake returning valid hex-encoded Ethereum data types.

**Flagging:** Raise `L2_RPC_ACTIVE` once the server survives a 10,000-request burst test without dropping connections.

---

### 🟢 Lane 3: The Command & Control (System Ops)

**Objective:** Standardize user interaction and configuration.

**Domain:** `/src/bin/scrutor-cli` & `/src/config`

**Primary Tasks:**

- Build the CLI parser (Must map 1:1 with standard `anvil` flags for ecosystem compatibility).
- Implement a robust configuration loader supporting `.toml` and `.json`.
- Establish the project-wide test infrastructure (integration test harnesses).

**Verification:** `scrutor --help` outputs all flags; `scrutor --config my_config.toml` successfully overrides default state.

**Flagging:** Raise `L3_CLI_READY` when the config manager can successfully serialize/deserialize a full node state.

---

## VII. Agent Manager "Triple-Lane" Operational Protocol

### Strict Isolation

**No agent is permitted to write outside its assigned directory.**

### Shared Interface

Any change to common types (e.g., `BlockHeader`) must be:

- Proposed in `/src/common`
- Flagged with a **Protocol Change Flag** for cross-lane review

### Review & Pivot Workflow

When an agent raises a flag (`L1_CORE_READY`, `L2_RPC_ACTIVE`, or `L3_CLI_READY`), it must:

1. Generate the **Summary of Evidence (SoE)** per Section IV.
2. Move its current branch to `pending-review/[lane]`.
3. Pull the next task from its specific lane queue.
4. **NEVER** cross into another lane's work queue.

### Hardware Pinning

Instruct the OS to prioritize **Lane 1 and Lane 2** for **performance cores (P-Cores)** to ensure low-latency execution and networking. Lane 3 may utilize efficiency cores (E-Cores) for system operations.

---

## Implementation Instructions for the Agent Manager

1. Post this file to the repository root.

2. Command the Agent Manager: *"Initialize project 'Scrutor'. Read `coding-governance.md`. You are now the Systems Engineer. Every task you execute must follow these protocols without exception. If you cannot provide the 'Summary of Evidence', do not present the code for review."*

3. **Current Action Item:** *"Initialize Agents 2 and 3 based on the Blue and Green job descriptions. Agent 1 remains on the Core Engine. All agents must follow the `.coding-governance.md` protocol. Do not interrupt Agent 1's opcode testing until Lane 2 and 3 are stabilized."*
