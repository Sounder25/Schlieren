# Phase 2 Work Orders for Scrutor Agents

## 🔴 Agent 1: Lane 1 - Execution Core (Instruction Set Expansion)

**Role:** Senior VM Engineer
**Objective:** Expand EVM Instruction Set (Comparisons, Logic, Flow)
**Governance Override:**

1. **Read `coding-governance.md` immediately.**
2. **Strict Rule:** No function may exceed **50 lines**. Refactor ruthlessly.
3. **Zero-Stub:** Every opcode must be fully implemented.

**Tasks:**

1. Locate `Scrutor.Core`.
2. Implement `ComparisonOpcodes` (LT, GT, EQ, ISZERO).
3. Implement `BitwiseOpcodes` (AND, OR, XOR, NOT, BYTE).
4. Implement `ControlFlowOpcodes` (JUMP, JUMPI, PC, JUMPDEST).
5. **Deliverable:** 100% Unit Test pass rate for new opcodes.

---

## 🔵 Agent 2: Lane 2 - State Machine (Global State & Mempool)

**Role:** Systems Architect
**Objective:** Implement Global State & Mempool
**Governance Override:**

1. **Read `coding-governance.md` immediately.**
2. **Strict Rule:** No function may exceed **50 lines**. Use sub-routines.
3. **Performance:** `TxMempool` must use efficient sorting (not basic Lists).

**Tasks:**

1. Locate `Scrutor.Core` (State domain).
2. Implement `GlobalState` class (Accounts, Storage maps).
3. Implement `TxMempool` with priority ordering (`GasPrice` / `MaxFee`).
4. Wire `eth_getBalance` in `Scrutor.RPC` to read from `GlobalState`.
5. **Deliverable:** Functioning state persistence in memory and mempool ordering.

---

## 🟢 Agent 3: Lane 3 - Operations (Forking Infrastructure)

**Role:** Integration Engineer
**Objective:** Implement Forking Infrastructure
**Governance Override:**

1. **Read `coding-governance.md` immediately.**
2. **Strict Rule:** No function may exceed **50 lines**.
3. **Reliability:** `ForkProvider` must handle network jitter (Polly retries).

**Tasks:**

1. Locate `Scrutor.Core` (Fork domain).
2. Implement `ForkProvider` to fetch data from remote RPC (`--fork-url`).
3. Implement `BlockCache` to store fetched blocks.
4. Wire the `--fork-url` CLI option to initialize this provider in `Program.cs`.
5. **Deliverable:** Verified block fetching from remote endpoint.
