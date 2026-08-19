# Schlieren Mainnet Harvester (n8n Architecture)

The Schlieren Harvester is decoupled into two dedicated n8n workflows that separate **Candidate Discovery** from **Execution & Diagnostic Processing**.

---

## Architecture Overview

```
                      ETHEREUM MAINNET
                             │
                             ▼
     ┌─────────────────────────────────────────────────┐
     │      Workflow A: Mainnet Scanner (Discovery)    │
     │  - Scans block range (checkpoint + 1 → head)    │
     │  - Up to MAX_BLOCKS_PER_RUN = 25 per execution  │
     │  - Safe bootstrap: finalized - 20 (no genesis)  │
     │  - Fetches block with full tx objects (true)    │
     │  - Non-discarding scoring:                      │
     │      • to == null        → CONTRACT_CREATION    │
     │      • Known Protocol    → KNOWN_PROTOCOL       │
     │      • input.length >= 10→ CONTRACT_CALL        │
     │      • input == 0x & val → UNKNOWN_VALUE_TRANS  │
     │  - Enqueues candidates: status = DISCOVERED     │
     │  - Advances block checkpoint ONLY after persist │
     └───────────────────────┬─────────────────────────┘
                             │
                      Harvest Queue /
                     Data Table Store
                             │
                             ▼
     ┌─────────────────────────────────────────────────┐
     │      Workflow B: Candidate Capture & Ingestion  │
     │  - Claims top N candidates by priority score    │
     │  - Marks status: IN_PROGRESS                    │
     │  - Fetches actual receipt (ground truth)        │
     │  - Captures exact pre-state via                 │
     │    debug_traceTransaction:                      │
     │    { tracer: "prestateTracer",                  │
     │      tracerConfig: { diffMode: false } }        │
     │  - Schlieren reconstructs ExecutionFixture      │
     │  - Replays transaction through Schlieren's      │
     │    full StateTransition processor               │
     │    (intrinsic gas + calldata floor + refunds +  │
     │     EVM execution + state transitions)          │
     │  - Diffs against mainnet receipt (gas/status)   │
     │  - Records Conformance Status:                  │
     │      • EXECUTED_PASS (matching outcome & gas)   │
     │      • EXECUTED_DIVERGENCE (gas/status diff)    │
     │      • CAPTURE_FAILED / EXECUTION_FAILED        │
     └─────────────────────────────────────────────────┘
```

---

## Result Taxonomy

Transactions are evaluated against Ethereum mainnet receipts as ground truth:

| Outcome Category | Description |
|---|---|
| `DISCOVERED` | Candidate identified during block scan and stored in queue |
| `IN_PROGRESS` | Currently being captured and processed by worker |
| `EXECUTED_PASS` | Schlieren matched Ethereum mainnet ground truth exactly (both Succeeded or both Reverted with identical gas and logs) |
| `EXECUTED_DIVERGENCE` | Execution diverged from mainnet (status mismatch, gas delta, or log mismatch) |
| `CAPTURE_FAILED` | RPC failure fetching receipt, header, or pre-state |
| `EXECUTION_FAILED` | Internal runner error or unhandled exception |
| `RETRY` | Transient network error scheduled for re-execution |

---

## How to Use

**You control when harvest runs. Nothing executes automatically.**

1. Open your local n8n instance (`http://localhost:5678`).
2. Go to **Workflows → Import from File**.
3. Import:
   - `workflow_a_mainnet_scanner.json`
   - `workflow_b_candidate_capture.json`
4. To harvest: open WF-A and click **Execute workflow**. This scans ~25 mainnet blocks and fills the internal queue.
5. Then open WF-B and click **Execute workflow**. This pulls 5 candidates from the queue, fetches their prestate, and writes fixture JSON files to `muscle/corpus/`.
6. In Schlieren, open the **Harvest tab** to see what was collected. Click **Load in Workbench** on any entry to investigate it.

**RPC:** Both workflows use `https://rpc.ankr.com/eth` by default. For `debug_traceTransaction` prestate capture, set `debugRpc` in Worker Config to an Alchemy or QuickNode endpoint with debug methods enabled. WF-B falls back gracefully without it.
