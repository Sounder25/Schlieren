# Scrutor EELS Conformance Status
**Last Updated:** 2026-08-11  
**Commit:** `6548df4` "feat(diagnostics): Layer 1 — DivergenceDiagnostics engine"  
**Fixture Source:** `ethereum/execution-specs` — `tests@v20.0.1` (released Jul 2, 2026)

---

## Summary

| Suite | Fixture Version | Cases | Passing | Status |
|---|---|---|---|---|
| **Osaka** | tests@v20.0.1 | 14,516 | 14,100 | ✅ **97.1%** |
| **Prague (v20)** | tests@v20.0.1 | 6,811 | 6,377 | ✅ **93.6%** |
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
| EIP-7883 | ModExp Gas Increase | 154 | ⚠️ Partial (formula needs tuning) |
| EIP-7825 | Transaction Gas Limit Cap | — | ❌ Not yet implemented |

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
| 05 | ModExp | EIP-198/2565/7883 | ✅ (Osaka gas partial) |
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

## Remaining Osaka Failures (416 cases)

Classified by the taxonomy drill:
- `nonce` — 716 mismatch lines
- `code` — 634 mismatch lines
- `storage` — 620 mismatch lines
- `balance` — 523 mismatch lines
- `receipt_status` — 24
- `other` — 61

Probable root causes (2–3 bugs):
1. **EIP-7825 Transaction Gas Limit Cap** — not implemented
2. **ModExp EIP-7883 formula edge case** — iteration count for large exponents
3. **CREATE/EIP-7610 edge cases** — revert on non-empty storage

---

## Fixture Management

| Source | Status |
|---|---|
| `ethereum/execution-spec-tests` (old) | **Archived** Jul 2, 2026. Final release: v5.4.0 |
| `ethereum/execution-specs` (new) | **Active**. Latest: `tests@v20.0.1` (Jul 2, 2026) |
| Download | `gh release download "tests@v20.0.1" --repo ethereum/execution-specs --pattern "*.tar.gz"` |

---

## Test Run Commands

```sh
# Full Osaka sweep (14,516 cases)
dotnet test Scrutor.EELS.Tests --settings osaka_audit.runsettings --filter "BENCHMARK_TaxonomySnapshot"

# Prague v20 (6,811 cases, excl. ported_static)
dotnet test Scrutor.EELS.Tests --settings prague_v20_audit.runsettings --filter "BENCHMARK_TaxonomySnapshot"

# Original Prague v5.4.0 (2,010 cases — the 100% baseline)
dotnet test Scrutor.EELS.Tests --settings prague_audit.runsettings --filter "BENCHMARK_TaxonomySnapshot"

# Targeted EIP subsets
dotnet test Scrutor.EELS.Tests --settings p256verify_audit.runsettings --filter "BENCHMARK_TaxonomySnapshot"
dotnet test Scrutor.EELS.Tests --settings clz_audit.runsettings --filter "BENCHMARK_TaxonomySnapshot"
dotnet test Scrutor.EELS.Tests --settings modexp7883_audit.runsettings --filter "BENCHMARK_TaxonomySnapshot"

# Taxonomy analysis (generates failure cluster report)
dotnet test Scrutor.EELS.Tests --settings osaka_audit.runsettings --filter "EelsTaxonomyDrill"
```
