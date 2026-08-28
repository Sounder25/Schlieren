# Strategic Campaign Train Status

**Started:** 2026-08-26
**Completed:** 2026-08-28
**Certificate commit:** `13fec7b`
**Result: 350/350 pass. CERTIFIED.**

## Gate Status

| Gate | Status | Commit |
|---|---|---|
| Gate 1: Apparatus trustworthy | ✅ Complete | `d63b239` |
| Gate 1.5: EELS identity model | ✅ Complete | `c8fbc7c` |
| Gate 2: Typed transaction envelope | ✅ Complete | `7143dae` |
| Gate 3: Reinspect all seven | ✅ Complete (350/350) | `13fec7b` |
| Gate 4: Repair causal families | ✅ Complete (18→0 divergences) | `13fec7b` |
| Gate 5: Final verification run | ✅ Complete (350/350 double-verified) | `13fec7b` |

## Final Campaign Results (double-verified at `13fec7b`)

| Campaign | Pass | Div | HE | Ab | Certification Run |
|---|---:|---:|---:|---:|---|
| Storage Lifecycle | 50 | 0 | 0 | 0 | `storage-lifecycle-v1_20260828134026_599272a8` |
| Call Semantics | 50 | 0 | 0 | 0 | `call-semantics-v1_20260828134236_537ecf4f` |
| Create Semantics | 50 | 0 | 0 | 0 | `create-semantics-v1_20260828134447_d17663e1` |
| Return Data | 50 | 0 | 0 | 0 | `return-data-v1_20260828135043_2be9ab06` |
| Self-Destruct | 50 | 0 | 0 | 0 | `selfdestruct-v1_20260828135534_0d5759a7` |
| Transient Storage | 50 | 0 | 0 | 0 | `transient-storage-v1_20260828135746_bd40dc3f` |
| Access List/Fee Market | 50 | 0 | 0 | 0 | `access-list-fee-market-v1_20260828140053_ab357c7e` |

## Defect History

| Campaign | Before (2026-08-26) | After (2026-08-28) | Bugs Fixed |
|---|---|---|---|
| Self-Destruct | 34/50 | 50/50 | EIP-161 cleanup (15), CREATE returnData (1) |
| Transient Storage | 48/50 → 49/50 | 50/50 | PYTHONPATH (1), Type-4 decode (1) |
| Access List/Fee Market | 49/50 | 50/50 | Type-3 decode (1) |
| Others | 200/200 | 200/200 | No change |

## Commit History

| Commit | Description |
|---|---|
| `8fcd976` | Task 0: certification intake |
| `0f12679` | Gate 1: eliminate apparatus failures |
| `d63b239` | Task 1: typed apparatus evidence |
| `2396cfa` | Task 1.5: semantic EELS provenance design |
| `c8fbc7c` | Task 1.5: replace launcher-hash gate |
| `834c2e7` | Full reinspection (282/300) |
| `18d6338` | Root cause analysis: EIP-161 |
| `5868d80` | **Fix: EIP-161 empty account cleanup** |
| `7143dae` | **Fix: type-3/4 transaction decoding** |
| `13fec7b` | **Fix: CREATE returnData leak** |

## Certificate

`harvest/ledger/certificates/2026-08-28-strategic-campaign-certificate.md`
