# Scrutor EELS Conformance Status
**Last Updated:** 2026-08-07  
**Commit:** `bac7046` "feat(prague): 99.95% → 100% — EIP-2200 SSTORE reentrancy guard (CALL_STIPEND)"

---

## Summary

| Fork | Cases | Passing | Status |
|------|-------|---------|--------|
| **Prague** | 2,010 | **2,010** | ✅ 100% |
| **Cancun** | 2,032 | **2,032** | ✅ 100% |
| **Unit tests** | 303 | **303** | ✅ 100% |

Scrutor achieves **full conformance** on both Cancun and Prague EELS state-test fixture suites.

---

## Prague EIP Coverage

| EIP | Name | Cases | Status |
|-----|------|-------|--------|
| EIP-7702 | Set Code Transactions | 546 | ✅ 100% |
| EIP-7623 | Increase Calldata Cost | — | ✅ 100% |
| EIP-2537 | BLS12-381 Precompiles | — | ✅ 100% |
| EIP-3541 | Reject EF-prefixed code | — | ✅ 100% |

---

## Prague Milestone History

| Date | Commit | Score | Fix |
|------|--------|-------|-----|
| 2026-08-07 | `6ac392f` | 93.6% → 99.85% (2007/2010) | EIP-7702 conformance (CALLCODE/DELEGATECALL delegation, type-4 validation, EF-prefix rejection, auth parsing) |
| 2026-08-07 | `14535d2` | 99.85% → 99.95% (2009/2010) | 3 edge cases: EF-prefix ExceptionalHalt gas, nonce-overflow warm guard, nonce validity |
| 2026-08-07 | `bac7046` | 99.95% → **100%** (2010/2010) | EIP-2200 SSTORE reentrancy guard (`gas_left ≤ 2300 → OOG`) |

---

## Precompile Status

| 0x | Name | Status | Notes |
|---|---|---|---|
| 01 | ecRecover | ✅ | BouncyCastle secp256k1 |
| 02 | SHA-256 | ✅ | BCL |
| 03 | RIPEMD-160 | ✅ | BouncyCastle |
| 04 | Identity | ✅ | |
| 05 | ModExp | ✅ | EIP-2565 |
| 06 | BN254 ecAdd | ✅ | BouncyCastle FpCurve |
| 07 | BN254 ecMul | ✅ | BouncyCastle FpCurve |
| 08 | BN254 ecPairing | ✅ | Full Ate pairing in `Bn254Pairing.cs` |
| 09 | BLAKE2F | ✅ | RFC 7693 native C# |
| 0A | KZG Point Eval | ✅ | Ckzg + `kzg_trusted_setup.txt` |
| 0B | BLS12-381 G1Add | ✅ | EIP-2537 |
| 0C | BLS12-381 G1Mul | ✅ | EIP-2537 |
| 0D | BLS12-381 G1MSM | ✅ | EIP-2537 |
| 0E | BLS12-381 G2Add | ✅ | EIP-2537 |
| 0F | BLS12-381 G2Mul | ✅ | EIP-2537 |
| 10 | BLS12-381 G2MSM | ✅ | EIP-2537 |
| 11 | BLS12-381 Pairing | ✅ | EIP-2537 |
| 12 | BLS12-381 MapG1 | ✅ | EIP-2537 |
| 13 | BLS12-381 MapG2 | ✅ | EIP-2537 |

---

## Key Implementation Notes

### EIP-2200 SSTORE Reentrancy Guard
EELS `sstore()` raises `OutOfGasError` immediately if `gas_left ≤ CALL_STIPEND (2300)`,
**before** any storage read/write. This is the EIP-2200 reentrancy guard.
`OpcodeSStore.ExecuteAsync()` now checks this at entry.

### EIP-7702 Authorization Loop
- ChainId=0 matches any chain; non-zero must match block chainId.
- Nonce overflow (`auth.Nonce ≥ U64.MAX_VALUE`) → skip **without warming** the signer.
- Bad signature → skip but **still warm** the signer address.
- `IsValid=false` auths: warm signer, no code/nonce write.

### EIP-3541 (EOF Rejection)
- Top-level CREATE returning EF-prefixed code: `ExceptionalHalt` — consume **all** `executionGasLimit`.
- Sub-call CREATE/CREATE2: reverse value transfer, zero nonce/code in overlay.

---

## Test Run Commands

```sh
# Full Prague sweep (2010 cases)
dotnet test Scrutor.EELS.Tests --settings prague_audit.runsettings --filter "BENCHMARK_TaxonomySnapshot"

# EIP-7702 only (546 cases)
dotnet test Scrutor.EELS.Tests --settings eip7702_audit.runsettings --filter "BENCHMARK_TaxonomySnapshot"

# Unit tests (303 cases)
dotnet test Scrutor.Tests

# EELS reference trace (requires eth-keys in execution-specs env)
# cd C:\projects\execution-specs
# python tools/eels_loop_trace.py --fixture <path.json> --out trace.jsonl
```
