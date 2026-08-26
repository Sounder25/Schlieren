# Strategic Campaign Certification Intake

**Date:** 2026-08-26
**Commit:** `0f1267969785a7cd71593083d40dbedc8b587d5b`
**Tree:** clean
**OS:** Windows 10.0.26200 (MINGW64_NT x86_64)
**Runtime:** .NET SDK 8.0.424
**Processors:** 6 (12 logical)

## Immutable inputs

### Manifests (all 50 cases, frozen)

| Campaign | Manifest Hash | File SHA-256 |
|---|---|---|
| Storage Lifecycle | `64d1a71f69d31696fc33cd323361cb51439c76ed7988bfaf09d75cb55afb197e` | `f44a29e69200f431...` |
| Call Semantics | `e20d55edb7e1fdd237df690d522cf217e4852d4dd03e10a864329a078b9d29b2` | `362dde221091439a...` |
| Create Semantics | `986d34083db2d9d57ca85df71f33ce4b75e09f4864519fc92d755570dccadb6a` | `3495ec6340026860...` |
| Return Data | `c2443f285e5f3ab4a6da403c24c1f25c11377d42d4ecb591a83763dd554e8c0b` | `f2d3f4a337fe1865...` |
| Self-Destruct | `90f041ed06ff6b54891eec791d527c16f4397b9131219fd1aafe56efd947e397` | `decd5a7fba48d658...` |
| Transient Storage | `171209fd3a8d54d5189f30c87da5e846a70c066f9ce2c36f469659575a0ec715` | `57f240bd3465ffa8...` |
| Access List/Fee Market | `ebc6f5d9b4106a1f24de28f6ecb73c84ab0c1f57822a2978cd3f8acf03409ef0` | `8a51348b05ece2cd...` |

### EELS Oracle

- **Executable:** `ethereum-spec-evm.exe`
- **Version:** 2.19.0 (Git commit: deec6412e7b264b1a54f40ca0e280e47d312d580)
- **Executable SHA-256:** `337a69fb156667f6b1b3ca7a34267144eedea4999bd2409ce5d5c9666f345441`
- **Fixture root:** `fixtures/state_tests` (relative to repo root)

## Historical evidence reconciliation

| Campaign | Evidence Commit | State | Pass | Div | FI | HE | Ab | Q | Total |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|
| Storage Lifecycle | `cf20f21` | certified | 50 | 0 | 0 | 0 | 0 | 0 | 50 |
| Call Semantics | `aa491c9` | completed | 50 | 0 | 0 | 0 | 0 | 0 | 50 |
| Create Semantics | `aa491c9` | completed | 50 | 0 | 0 | 0 | 0 | 0 | 50 |
| Return Data | `deec641` (Gate 1 fix) | completed | 50 | 0 | 0 | 0 | 0 | 0 | 50 |
| Self-Destruct | `aa491c9` | inspectionFailed | 34 | 16 | 0 | 0 | 0 | 0 | 50 |
| Transient Storage | `aa491c9` | inspectionFailed | 48 | 2 | 0 | 0 | 0 | 0 | 50 |
| Access List/Fee Market | `deec641` (Gate 1 fix) | inspectionFailed | 49 | 1 | 0 | 0 | 0 | 0 | 50 |

### Non-pass case IDs

**Self-Destruct (16 divergences):**
- `tests/cancun/eip6780_selfdestruct/test_selfdestruct.py::test_create_and_destroy_multiple_contracts_same_tx` (1)
- `tests/cancun/eip1153_tstore/test_tstorage_selfdestruct.py::test_reentrant_selfdestructing_call` (15 variants across Cancun, Prague, Osaka)

**Transient Storage (2 divergences):**
- `tests/ported_static/stEIP1153_transientStorage/test_14_revert_after_nested_staticcall.py::test_14_revert_after_nested_staticcall[fork_Cancun-state_test]`
- `tests/prague/eip7702_set_code_tx/test_set_code_txs.py::test_set_code_to_tstore_reentry[fork_Osaka-call_opcode_CALL-state_test-return_opcode_RETURN]`

**Access List/Fee Market (1 divergence):**
- `tests/cancun/eip4844_blobs/test_blob_txs.py::test_blob_gas_subtraction_tx[fork_Cancun-max_blobs-state_test--100_wei_mid_execution--tx_max_fee_per_blob_gas_multiplier_1-no_calldata-tx_value_0-tx_max_pr...]`

## Unit test baseline (two consecutive runs)

| Run | Schlieren.Tests | Schlieren.Harvest.Tests |
|---|---|---|
| 1 | 701 pass / 0 fail / 5 skip / 706 total | 195 pass / 0 fail / 0 skip / 195 total |
| 2 | 701 pass / 0 fail / 5 skip / 706 total | 195 pass / 0 fail / 0 skip / 195 total |

Identical test identities and totals. No blocking apparatus defect.

## Apparatus status after Gate 1 fix (`0f12679`)

- Return Data: **0 HarnessError** (resolved by `--noreturndata --nostack --nomemory` EELS flags)
- Access List: **0 HarnessError** (resolved by correcting pass/isSuccess cross-validation)
- All campaigns: **0 Aborted**

## Limitations

- EELS.Tests: 11 failures due to missing fixture directories (fixture-dependent, not a production defect)
- One transient test-ordering flake observed in full-solution parallel run; not reproducible in isolation
- Historical Call/Create/Return Data runs were on earlier commits; must be reinspected on final commit
- Storage certificate `cert-20260825224015-673d69` was issued at `cf20f21`; must be renewed on final commit
