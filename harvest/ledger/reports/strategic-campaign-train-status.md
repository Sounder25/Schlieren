# Strategic Campaign Train Status

**Started:** 2026-08-26
**Base commit:** `0f12679`
**Current commit:** `c8fbc7c`
**Target:** 300/300 umbrella certificate (Campaigns 2-7) + Storage 50/50 prerequisite

## Current gate status

| Gate | Status | Blocking |
|---|---|---|
| Gate 1: Apparatus trustworthy | ✅ Complete | — |
| Gate 1.5: EELS identity model | ✅ Complete (`c8fbc7c`) | — |
| Gate 2: Typed transaction envelope | ⏳ Pending | — |
| Gate 3: Reinspect all six | ✅ Complete (2026-08-28, 282/300) | — |
| Gate 4: Repair causal families | ⏳ In progress | — |
| Gate 5: Final same-commit inspection | ⏳ Pending | Gate 4 |

## Campaign current state (full reinspection at `c8fbc7c`)

| Campaign | Pass | Div | HE | Ab | Run ID | Status |
|---|---:|---:|---:|---:|---|---|
| Storage Lifecycle | 50 | 0 | 0 | 0 | `storage-lifecycle-v1_20260828120144_ccb072e5` | ✅ |
| Call Semantics | 50 | 0 | 0 | 0 | `call-semantics-v1_20260828115739_fc0d72f3` | ✅ |
| Create Semantics | 50 | 0 | 0 | 0 | `create-semantics-v1_20260828115946_569cecad` | ✅ |
| Return Data | 50 | 0 | 0 | 0 | `return-data-v1_20260828114257_dd4035e9` | ✅ |
| Self-Destruct | 34 | 16 | 0 | 0 | `selfdestruct-v1_20260828115134_c863fe0d` | ❌ Engine defects |
| Transient Storage | 49 | 1 | 0 | 0 | `transient-storage-v1_20260828115334_fa37a6f4` | ❌ Engine defect |
| Access List/Fee Market | 49 | 1 | 0 | 0 | `access-list-fee-market-v1_20260828115534_93848716` | ❌ Engine defect |

**Total: 282/300 pass. 18 real Schlieren divergences. 0 apparatus failures.**

## Confirmed defect map

- **Self-Destruct (16 divergences):** EIP-6780 account lifecycle — reentrant selfdestruct, same-tx create+destroy
- **Transient Storage (1 divergence):** Down from 2; one prior divergence was apparatus noise (PYTHONPATH)
- **Access List/Fee Market (1 divergence):** Blob gas subtraction

## Repair orders

| Family | Cases | Campaign | Priority |
|---|---:|---|---|
| Self-Destruct account lifecycle | 16 | selfdestruct-v1 | 1 |
| Transient Storage | 1 | transient-storage-v1 | 2 |
| Blob gas subtraction | 1 | access-list-fee-market-v1 | 3 |

## Commits

| Commit | Description |
|---|---|
| `8fcd976` | Task 0: record strategic campaign certification intake |
| `0f12679` | Gate 1: eliminate apparatus failures |
| `d63b239` | Task 1: preserve typed harvest apparatus evidence |
| `2396cfa` | Task 1.5 design: semantic EELS provenance |
| `c8fbc7c` | Task 1.5: replace launcher-hash gate, full reinspection |
