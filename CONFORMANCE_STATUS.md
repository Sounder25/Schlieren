# Schlieren Conformance Status

## Current State: Campaign 1 Certified

**Date:** 2026-08-25

Campaign 1 completed through the production subprocess path with an independently
invoked EELS 2.19.0 oracle. All certification gates passed.

### Certificate

- Certificate: `cert-20260825224015-673d69`
- Run: `storage-lifecycle-v1_20260825222546_61c42767`
- Manifest: `64d1a71f69d31696fc33cd323361cb51439c76ed7988bfaf09d75cb55afb197e`
- Schlieren execution commit: `cf20f21`
- Result: 50 pass, 0 divergence, 0 fixture invalid, 0 harness error,
  0 aborted, 0 quarantined
- Run content hash: `dc545aeec0b93dc0345a22fe69a8c5b27136f5e43bf083a3e0851a86edb96c28`

### Closed Repair Families

| Family | Before | After | Repair commit |
|---|---:|---:|---|
| `Unknown/Account/Balance` | 14 | 0 | `6f6bea7` |
| `Unknown/Gas/GasUsed` | 7 | 0 | `6f6bea7` |
| `Unknown/Storage/StorageValue` | 21 | 0 | `cf20f21` |
| `Unknown/Gas/RefundCounter` | 29 | 0 | `cf20f21` |

The balance and storage families were hex-quantity normalization defects in the
comparison apparatus. The gas-used family was caused by omitted EIP-2930 access
lists during fixture materialization. Fixture post-state does not provide an
authoritative refund counter, so refund comparison is now performed only when an
independent expected value exists.

### Suite Gate

Suite gate: `harvest/ledger/suite-gate-0ae041e.json`

- Harvest tests: 188 passed, 0 failed, 0 skipped, three consecutive runs
- Main tests: 701 passed, 0 failed, 5 skipped, three consecutive runs
- All six TRX SHA-256 hashes recorded
- `certificationEligibility: true`

The previous suite instability came from hard-coded fixture paths and concurrent
mutation of static pathological-case generator state. Both causes are repaired.

### Calibration And Identity

- Calibration: `cal-20260825174020`, all 6 probes classified correctly
- EELS: ethereum-spec-evm 2.19.0
- EELS executable SHA-256: `c2a25c7f60a104f0cc024748256526a6fe511193bf320c98834dba55ad58bb10`
- Fixture root SHA-256: `ed5e2dc9d4847fb83f1a820959308044b18a50be78d2d299b5211d85ad33738f`
- Fixture suite: tests@v20.0.1
- Runtime: .NET 8.0.29

### Audit Remediation

All nine Harvest audit findings remain resolved: subprocess execution, suite-gate
parsing, independent manifest lookup, calibration and regression inputs, exact
CaseId selection, fixture and EELS identity, secret scanning, repair lifecycle,
and ledger case-count validation.
