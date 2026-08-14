# Schlieren EELS Conformance Status
**Last Updated:** 2026-08-11  
**Baseline commit:** `a744e63` (EIP-7825 + EIP-7883)  
**Fixture Source:** `ethereum/execution-specs` — `tests@v20.0.1` (released Jul 2, 2026)  
**Full Osaka report:** `Schlieren.EELS.Tests/TestResults/taxonomy_20260811_040812.md`

---

## Summary

| Suite | Fixture Version | Cases | Passing | Status |
|---|---|---|---|---|
| **Osaka** | tests@v20.0.1 | 14,516 | **14,197** | ✅ **97.80%** (was 97.1% / 14,100) |
| **Prague (v20)** | tests@v20.0.1 | 6,811 | 6,377 | ✅ **93.6%** *(not re-measured this run)* |
| **Prague (v5.4.0)** | v5.4.0 | 2,010 | 2,010 | ✅ **100%** |
| **Cancun (v5.4.0)** | v5.4.0 | 2,032 | 2,032 | ✅ **100%** |
| **Unit Tests** | — | 303 | 303 | ✅ **100%** |

> **Note:** The `tests@v20.0.1` fixture suite (from the new `ethereum/execution-specs` repo) contains
> 7× more cases than v5.4.0. The v5.4.0 Prague/Cancun suites remain at 100% — those were the final
> release from the now-archived `ethereum/execution-spec-tests` repo.

---

## Osaka EIP Coverage (New — 2026-08-11)

| EIP | Name | Cases | Status |
|---|---|---|---|
| EIP-7951 | P256Verify Precompile (0x0100) | 397 | ✅ 100% |
| EIP-7939 | CLZ Opcode (0x1E) | 579 | ✅ 100% |
| EIP-7883 | ModExp Gas Increase | 168 | ✅ 100% (complexity=16 / 2·words²; floor 500; no /3) |
| EIP-7825 | Transaction Gas Limit Cap | — | ✅ Implemented (`tx.gas > 16_777_216` → invalid) |

---

## Prague EIP Coverage (Confirmed)

| EIP | Name | Cases | Status |
|---|---|---|---|
| EIP-7702 | Set Code Transactions (type 4) | 546 | ✅ 100% |
| EIP-7623 | Increase Calldata Cost | — | ✅ 100% |
| EIP-2537 | BLS12-381 Precompiles (0x0b–0x13) | — | ✅ 100% |
| EIP-3541 | Reject EF-prefixed code | — | ✅ 100% |
| EIP-3860 | Initcode size limit | — | ✅ 100% |

---

## Precompile Status (20 Total)

| 0x | Name | EIP | Status |
|---|---|---|---|
| 01 | ecRecover | — | ✅ |
| 02 | SHA-256 | — | ✅ |
| 03 | RIPEMD-160 | — | ✅ |
| 04 | Identity | — | ✅ |
| 05 | ModExp | EIP-198/2565/7883 | ✅ (Osaka EIP-7883 100%) |
| 06 | BN254 ecAdd | EIP-196 | ✅ (+ invalid input fix) |
| 07 | BN254 ecMul | EIP-196 | ✅ (+ invalid input fix) |
| 08 | BN254 ecPairing | EIP-197 | ✅ (+ G2 subgroup check) |
| 09 | BLAKE2F | EIP-152 | ✅ |
| 0A | KZG Point Eval | EIP-4844 | ✅ |
| 0B–13 | BLS12-381 (9 precompiles) | EIP-2537 | ✅ |
| 0100 | P256Verify (secp256r1) | EIP-7951 | ✅ **NEW** |

---

## Session History (2026-08-11)

| Commit | Change | Cases Fixed |
|---|---|---|
| `2eeab76` | EIP-7951 P256Verify precompile at 0x0100 | +397 |
| `728baf4` | BN254 G2 subgroup check + invalid-input consume-all-gas | +143 |
| `f388c02` | EIP-7939 CLZ opcode (0x1E) | +579 |
| `656d196` | EIP-7883 ModExp gas increase (partial) | +166 |
| `6548df4` | Layer 1 DivergenceDiagnostics engine | — (infrastructure) |

**Total: 1,308 cases fixed in one session (88.1% → 97.1%)**

---

## Remaining Osaka Failures (**319 cases** — post 7825/7883)

Measured 2026-08-11 via `osaka_audit.runsettings` + `EelsTaxonomyDrill` (~3m 39s).

Mismatch lines (not unique cases):
- `storage` — 535
- `balance` — 359
- `nonce` — 93
- `other` — 54
- `receipt_status` — 24
- `code` — 22
- `missing_account` — 7

**Delta since prior baseline (14,100 pass / 416 fail → 14,197 pass / 319 fail): +97 cases fixed.**

Probable remaining root causes (from Layer 1–2):
1. ~~CREATE / EIP-7610 collision~~ — **fixed** (CREATE + top-level parity with CREATE2 `account_deployable`)
2. **Tx applied when should reject** — High, 23× (validation gaps beyond plain 7825)
3. **Gas residuals** — +199000 (32×), CALL_STIPEND 2300 (19×), +25000 new-account (25×)
4. **Unexpected / empty-account** — Medium, EIP-161 touch/delete
5. **Precompile invalid-success** — Certain on ecrecover folder (may still be noisy)

Dedicated suites at 100%: EIP-7825 (35), EIP-7883 (168), EIP-7610 create_collision (50).

---

## Fixture Management

| Source | Status |
|---|---|
| `ethereum/execution-spec-tests` (old) | **Archived** Jul 2, 2026. Final release: v5.4.0 |
| `ethereum/execution-specs` (new) | **Active**. Latest: `tests@v20.0.1` (Jul 2, 2026) |
| Download | `pwsh ./tools/fetch-fixtures.ps1` (or `gh release download "tests@v20.0.1" --repo ethereum/execution-specs --pattern "fixtures.tar.gz"`) |

---

## Test Run Commands

```sh
# Full Osaka sweep (14,516 cases)
dotnet test Schlieren.EELS.Tests --settings osaka_audit.runsettings --filter "BENCHMARK_TaxonomySnapshot"

# Prague v20 (6,811 cases, excl. ported_static)
dotnet test Schlieren.EELS.Tests --settings prague_v20_audit.runsettings --filter "BENCHMARK_TaxonomySnapshot"

# Original Prague v5.4.0 (2,010 cases — the 100% baseline)
dotnet test Schlieren.EELS.Tests --settings prague_audit.runsettings --filter "BENCHMARK_TaxonomySnapshot"

# Targeted EIP subsets
dotnet test Schlieren.EELS.Tests --settings p256verify_audit.runsettings --filter "BENCHMARK_TaxonomySnapshot"
dotnet test Schlieren.EELS.Tests --settings clz_audit.runsettings --filter "BENCHMARK_TaxonomySnapshot"
dotnet test Schlieren.EELS.Tests --settings modexp7883_audit.runsettings --filter "BENCHMARK_TaxonomySnapshot"

# Taxonomy analysis (generates failure cluster report)
dotnet test Schlieren.EELS.Tests --settings osaka_audit.runsettings --filter "EelsTaxonomyDrill"
```
