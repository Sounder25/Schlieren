# BLS G1ADD Campaign 8 Intake

Date: 2026-08-30
State: pre-discovery intake

## Baseline

- Schlieren commit: `e928b12a198eb8f86154acf82afd4f4e6f2c4699`
- Branch: `feature/bls-campaign-selection`
- Isolated worktree: `.worktrees/bls-campaign-selection`
- Release build: 0 errors, 22 known warnings
- Harvest tests: 233 passed / 0 failed / 0 skipped / 233 total
- Original seven certified campaigns: historical evidence only; unchanged by Campaign 8

## Eligible EIP-2537 corpus

The fixture inventory found 130 JSON files containing 2,974 raw entries and 1,992 distinct case IDs.

| Operation | Distinct cases |
| --- | ---: |
| G2 MSM | 424 |
| G1 MSM | 418 |
| G2 ADD | 194 |
| G1 ADD | 182 |
| G2 MUL | 180 |
| G1 MUL | 178 |
| Pairing | 176 |
| Map Fp to G1 | 80 |
| Map Fp2 to G2 | 74 |
| Variable-length contracts | 72 |
| EIP mainnet coverage | 7 |
| Precompile-before-fork | 7 |

Fork distribution across distinct case IDs:

| Fork | Cases |
| --- | ---: |
| Osaka | 1,007 |
| Prague | 978 |
| Cancun | 7 |

## Rejected broad draft

The preserved broad draft has manifest hash `420043057ffa63bd0f28ebad28f9d2945c433b3656a04119a52f33b9c0402af2` and labels itself as coverage for all BLS12-381 precompiles.

Its selection is valid and fixture-hash clean, but it is not representative of that label:

- 42 of 50 cases are G1ADD;
- 30 of 50 cases are invalid-input cases;
- 26 cases are specifically G1ADD invalid-input cases;
- G1 MSM, G2 MSM, G2 MUL, pairing, and both map operations receive only one case each;
- G2 ADD and several broad semantic categories have no meaningful independent sample.

Root cause: after the greedy keyword selector covers each score keyword once, remaining cases have equal scores and ordinal case-ID ordering fills the batch from the earliest family. The draft remains historical evidence and must not be edited or called certified.

## Campaign 8 frozen scope

Campaign ID: `precompiles-bls12-g1add-v1`

| Stratum | Prague | Osaka | Cancun | Total |
| --- | ---: | ---: | ---: | ---: |
| Valid inputs | 7 | 8 | 0 | 15 |
| Invalid encodings and points | 9 | 9 | 0 | 18 |
| CALLCODE / DELEGATECALL / STATICCALL | 6 | 6 | 0 | 12 |
| Gas boundary | 2 | 2 | 0 | 4 |
| Before-fork activation | 0 | 0 | 1 | 1 |
| **Total** | **24** | **25** | **1** | **50** |

Cases within a stratum are chosen deterministically across the ordinal-sorted candidate range. Selection includes both range endpoints when more than one case is requested. A stratum shortage fails manifest creation; no other stratum may silently fill the gap.

## Certification boundary

The first execution is a discovery run. It may identify apparatus failures or Schlieren divergences, but it is not a certificate. Certification requires the same immutable 50-case manifest to reach 50 pass and zero non-pass outcomes under the repository's existing clean-provenance, regression, repair-order, and append-only ledger gates.
