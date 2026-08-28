# Strategic Campaign Certification — 2026-08-28

## Certificate

**350/350 strategic campaign cases pass with zero divergences.**

This certificate attests that the Schlieren EVM at commit `13fec7b` produces identical post-state to the EELS 2.19.0 reference implementation for all 350 cases across 7 frozen campaign manifests, verified by two consecutive full inspection runs on the same commit.

## Certified Campaigns

| # | Campaign | Manifest Hash | Cases | Result | Certification Run |
|---|---|---|---:|---|---|
| 1 | Storage Lifecycle | `64d1a71f...` | 50 | 50/50 ✅ | `storage-lifecycle-v1_20260828134026_599272a8` |
| 2 | Call Semantics | `e20d55ed...` | 50 | 50/50 ✅ | `call-semantics-v1_20260828134236_537ecf4f` |
| 3 | Create Semantics | `986d3408...` | 50 | 50/50 ✅ | `create-semantics-v1_20260828134447_d17663e1` |
| 4 | Return Data | `c2443f28...` | 50 | 50/50 ✅ | `return-data-v1_20260828135043_2be9ab06` |
| 5 | Self-Destruct | `90f041ed...` | 50 | 50/50 ✅ | `selfdestruct-v1_20260828135534_0d5759a7` |
| 6 | Transient Storage | `171209fd...` | 50 | 50/50 ✅ | `transient-storage-v1_20260828135746_bd40dc3f` |
| 7 | Access List/Fee Market | `ebc6f5d9...` | 50 | 50/50 ✅ | `access-list-fee-market-v1_20260828140053_ab357c7e` |

## Provenance

| Field | Value |
|---|---|
| Schlieren commit | `13fec7bcca76c5b17dde1c19989e368a712a58ca` |
| EELS version | 2.19.0 |
| EELS source commit | `85aa48c742c38a2d5a876f84ebf8082a50273064` |
| EELS source tree SHA-256 | `793296a2492e4c6f4d70679f9a73aa2d03ef19f68058465492555a37b9912c49` |
| Python | 3.13.11 |
| OS | Windows 10.0.26200 |
| Runtime | .NET 8.0 |
| Machine | The-Beast (6 cores) |
| Unit test suite | 702 pass / 0 fail / 5 skip |
| Harvest test suite | 233 pass / 0 fail |

## Bugs Fixed in This Certification Train

| Bug | Cases Fixed | Root Cause | Commit |
|---|---|---|---|
| EIP-161 empty account cleanup | 15 | SELFDESTRUCT to zero-balance beneficiary created ghost accounts; no post-tx empty-account pruning | `5868d80` |
| Type-3/4 transaction decoding | 2 | Harvest executor didn't parse blob hashes, MaxFeePerBlobGas, or AuthorizationList from fixtures | `7143dae` |
| CREATE returnData leak | 1 | Top-level CREATE tx exposed init code RETURN output as transaction-level returnData | `13fec7b` |
| EELS PYTHONPATH pollution | — | Hermes venv pydantic leaked into EELS via PYTHONPATH; not a Schlieren code bug | `c8fbc7c` |
| Launcher-hash identity gate | — | pip console-launcher SHA changed on venv recreation; gate replaced with semantic provenance | `c8fbc7c` |

## What This Certificate Proves

- Schlieren correctly implements EIP-6780 (SELFDESTRUCT restriction) including same-tx deletion
- Schlieren correctly implements EIP-161 (empty account cleanup at transaction finalization)
- Schlieren correctly handles type-0 through type-4 transactions including blob gas and EIP-7702 authorizations
- Schlieren correctly isolates CREATE init-code output from transaction-level return data
- Schlieren matches EELS on storage lifecycle, CALL semantics, CREATE semantics, RETURN data handling, SELFDESTRUCT lifecycle, transient storage with delegation reentry, and blob fee settlement

## What This Certificate Does NOT Prove

- Full conformance across all 14,516+ Osaka v20.0.1 fixtures (only 350 strategic cases certified here)
- Frontier/Homestead edge cases (5 known open failures in those forks)
- Behavior of fixture-dependent tests when local fixture directories are absent
- Performance, networking, or block production correctness
- Correctness under concurrent/parallel transaction execution

## Verification

To reproduce: pull commit `13fec7b`, set `EELS_EXE` and `EELS_FIXTURES_ROOT`, unset `PYTHONPATH`, and run each campaign manifest via `dotnet run --project Schlieren.CLI -c Release -- harvest campaign run <manifest> --ledger harvest/ledger --timeout-seconds 120`. All 7 must report State=Completed, Pass=50, Divergence=0.
