# Scrutor — Move Forward Plan
**Date:** 2026-08-11  
**Baseline:** Osaka 97.1%, Prague v20 93.6%, Layer 1 Diagnostics in place

---

## Phase 1 — Osaka 100% (Est. 1–2 weeks)

Close the remaining 416 Osaka failures.

| Work Item | Est. Effort | Expected Cases Fixed |
|---|---|---|
| EIP-7883 ModExp formula — fix iteration count edge case | 2–4 hours | ~154 |
| EIP-7825 Transaction Gas Limit Cap | 1 day | ~100+ |
| CREATE/EIP-7610 revert on non-empty storage | 1–2 days | ~100+ |
| Trace remaining balance/nonce residuals | 1–2 days | ~60 |

**Gate:** 14,516 / 14,516 = 100% on `tests@v20.0.1` Osaka suite.

---

## Phase 2 — Diagnostics Integration (Est. 1 week)

Wire Layer 1 into the conformance panel and make it the default output format.

| Work Item | Est. Effort | Status |
|---|---|---|
| Wire `DivergenceDiagnostics` into `EelsTaxonomyDrill` report output | 2 hours | ✅ Done — `Layer1DiagnosisBridge` + report section |
| Wire into Conformance UI panel — show diagnosis per failure, not just raw diff | 1 day | ✅ Done — case inspector + failure list L1 strip |
| Layer 2: Structural pattern rules (15–20 rules from hard-won knowledge) | 2 days | ✅ Done — 20 rules; tightened on 2500-case Osaka smoke |
| Layer 3: Auto trace comparison (representative case per cluster) | 3–5 days | ⬜ Next |
| Layer 4: Remediation advisor (fact/diagnosis/suggestion separation) | 2 days | ⬜ |

**Gate:** The Conformance panel shows `Diagnosis` objects with confidence levels, not raw storage/balance diffs. ✅ (L1+L2)

**Taxonomy path (live):** every failed case → `Layer1DiagnosisBridge.DiagnoseCase` (L1 gas constants + L2 structural) → aggregated buckets → `## Layer 1–2 Diagnoses`.

**Layer 2 rules:** EIP-2200 stipend, EIP-3541 EF-prefix, EIP-7610 collision, EIP-7702 warm/nonce, EIP-7825 gas cap, EIP-7883 ModExp, EIP-3529 refund, EIP-2929 access, coinbase tip, EIP-161 empty, exceptional halt, EIP-7623, precompile gas, SELFDESTRUCT, CREATE/initcode, unexpected account, receipt OOG, Osaka feature gate, balance residual.

---

## Phase 3 — UI Completion (Est. 1–2 weeks)

| Work Item | Est. Effort |
|---|---|
| Fix ConformanceView.axaml build (xmlns placement) | 30 min |
| Wire fork dropdown to real `IForkRules` in Workbench (not just label) | 1–2 days |
| Clean up demo trace vs live trace path | 1 day |
| Wire `DivergenceDiagnostics` into `debug_whyNot` RPC endpoint | 1 day |
| Export conformance report as structured Markdown (with diagnoses) | 1 day |

**Gate:** `dotnet run --project Scrutor.UI` launches, Conformance tab runs live, diagnoses appear.

---

## Phase 4 — AWS Cloud Deployment (Est. 2–4 weeks)

| Work Item | Est. Effort |
|---|---|
| Dockerize Scrutor.RPC as standalone execution API | 2–3 days |
| Deploy to AWS EC2 (r6i.2xlarge) with ALB | 1–2 days |
| Add API key authentication layer | 1 day |
| Fork-from-mainnet: hot cache of recent Ethereum state | 1–2 weeks |
| Conformance CI: run 14,516 Osaka cases on every push (CodeBuild) | 1–2 days |
| Web workbench UI (browser-based, backed by JSON-RPC) | 2–3 weeks |

**Gate:** External users can call Scrutor over HTTPS, execute transactions, and get traced results with diagnoses.

---

## Phase 5 — Product (Est. 4–8 weeks)

| Work Item | Description |
|---|---|
| Solidity → bytecode compilation (wire `solc`) | Work from source, not hex |
| Mainnet fork replay UX | Paste RPC + block, replay any tx in isolation |
| Audit report generation v2 (with Layer 1–3 diagnoses) | Client-deliverable output |
| Beta program: 5–10 real auditors using it | Feedback → iterate |
| AWS Web3 blog post publish | Drive inbound |
| Pricing model | Per-query API vs seat license vs hybrid |

---

## Architectural Decisions Made This Session

| Decision | Rationale |
|---|---|
| Exclude `ported_static` from v20 Prague sweep | 2,135 deep-recursion tests hang the 32MB stack worker. Need separate non-async runner. |
| Per-item thread in LargeStackWorker | Prevents one StackOverflow from killing the shared worker loop |
| P256Verify at two-byte address (0x0100) | First precompile outside the single-byte id space. Required new `IsPrecompile` routing. |
| `DivergenceDiagnostics` in `Scrutor.Core` (not Tests) | It's a product feature, not a test utility. Ships with the engine. |
| Fork-gate pattern: `IForkRules.HasEipXXXX` | One new bool per EIP, override in the fork class. No scattered conditionals. |

---

## Key Numbers to Remember

| Metric | Value |
|---|---|
| Osaka (tests@v20.0.1) | **97.1%** — 14,100 / 14,516 |
| Prague (v5.4.0 — public claim) | **100%** — 2,010 / 2,010 |
| Cancun (v5.4.0 — public claim) | **100%** — 2,032 / 2,032 |
| Cases fixed 2026-08-11 | **1,308** |
| New EIPs implemented | 3 (P256Verify, CLZ, ModExp repricing) |
| New precompiles | 1 (P256Verify at 0x0100 — 20th precompile) |
| Conformance UI | Built, needs xmlns fix to compile |
| Diagnostics engine | Layer 1 complete, compiles, in Scrutor.Core |
| Public repo | live at github.com/Sounder25/scrutor-evm |
| Landing page | live at sounder25.github.io/scrutor-evm |
| Contact form | delivering to e.turner@soundersolution.com |
