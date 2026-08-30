# BLS G1ADD Campaign 8 Discovery Report

Date: 2026-08-30
Decision: discovery green; not certified

## Outcome

| Status | Count |
| --- | ---: |
| Pass | 50 |
| Divergence | 0 |
| Fixture invalid | 0 |
| Harness error | 0 |
| Aborted | 0 |
| Quarantined | 0 |
| **Total** | **50** |

The focused G1ADD campaign found no Schlieren-versus-EELS disagreement and no apparatus failure in its frozen scope. There are therefore no defect clusters or repair orders for this discovery run.

## Immutable evidence

- Campaign: `precompiles-bls12-g1add-v1`
- Manifest: `5a91fc4655e21c84330611a809456cf95d436951d1febdcd00519438e426e4a8`
- Run ID: `precompiles-bls12-g1add-v1_20260830072911_e07a276d`
- Run content hash: `8a284dbaf0bbc428ed18214dd17ea10c773037bfb4e79d45d63f5bd8f1d094f7`
- Schlieren commit: `3ee3f19eb3879eb6d4413045f91b6b99a662c613`
- EELS package version: `2.19.0`
- EELS launcher SHA-256: `ee46923d2cfd47457f6324aeb5c21a5d42e363de2ed8597bbbcb69abcc56ee0f`
- Fixture-root SHA-256: `ed5e2dc9d4847fb83f1a820959308044b18a50be78d2d299b5211d85ad33738f`
- Per-case timeout: 120 seconds
- Elapsed campaign time: approximately 116 seconds

Ledger verification:

- `run.json` canonical content hash recomputed: match;
- `complete.json` run-content hash: match;
- expected, actual, and recorded totals: 50 / 50 / 50;
- non-pass case artifacts: 0;
- cluster artifacts: 0.

## Scope actually exercised

| Stratum | Prague | Osaka | Cancun | Total |
| --- | ---: | ---: | ---: | ---: |
| Valid inputs | 7 | 8 | 0 | 15 |
| Invalid encodings and points | 9 | 9 | 0 | 18 |
| CALLCODE / DELEGATECALL / STATICCALL | 6 | 6 | 0 | 12 |
| Gas boundary | 2 | 2 | 0 | 4 |
| Before-fork activation | 0 | 0 | 1 | 1 |
| **Total** | **24** | **25** | **1** | **50** |

This result proves agreement only for the frozen G1ADD cases and comparison fields. It does not prove all BLS12-381 precompiles, all EIP-2537 behavior, or all EVM conformance.

## Certification refusal

This run is intentionally not certified:

1. The EELS source checkout at commit `85aa48c742c38a2d5a876f84ebf8082a50273064` was dirty because `src/ethereum_spec_tools/evm_tools/daemon.py` was modified.
2. The EELS launcher's `--version` output inherited Schlieren's working-directory commit (`3ee3f19...`) rather than reporting trustworthy EELS source provenance.

These facts do not invalidate the discovery result, but they violate the clean-provenance gate for certification. No frozen manifest or expected result will be changed to bypass that gate.

## Regression verification

- Release build: 0 errors / 22 known warnings.
- Harvest tests: 238 passed / 0 failed / 0 skipped / 238 total.
- Core tests: 703 passed / 0 failed / 5 skipped / 708 total.

The first core-suite attempt from the linked worktree reported three fixture-path failures because that worktree had no local `fixtures/state_tests` directory. After linking the worktree's `fixtures` path to the unchanged main-checkout fixture corpus, the identical suite passed at 703 / 0 / 5. This was workspace setup, not a code correction.

## Next campaign

The next useful independent BLS campaign is G1 MSM. It should receive its own focused 50-case manifest with explicit valid, invalid, variable-length, gas-discount, call-type, and fork-activation strata. Campaign 8 remains unchanged as the G1ADD regression set.
