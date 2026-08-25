# Schlieren Conformance Status

## Current State: Campaign 1 Frozen

**Date:** 2026-08-25
**Commit:** dec1489 (Task 11 — CLI wiring performed on top)
**Apparatus Gate:** PASSED (6/6 calibration probes correct)

### Calibration Record

| Probe | Expected | Actual | Result |
|---|---|---|---|
| ExactMatch | Pass | Pass | ✓ |
| GasMismatch | Divergence | Divergence | ✓ |
| StatusMismatch | Divergence | Divergence | ✓ |
| StorageMismatch | Divergence | Divergence | ✓ |
| MalformedFixture | FixtureInvalid | FixtureInvalid | ✓ |
| KilledWorker | Aborted | Aborted | ✓ |

Calibration ID: `cal-20260825174020`

### Suite Gate (Three Consecutive Runs)

| Project | Run 1 | Run 2 | Run 3 | Stable |
|---|---|---|---|---|
| Schlieren.Harvest.Tests | 176P/0F/0S | 176P/0F/0S | 176P/0F/0S | ✓ |
| Schlieren.Tests | 694P/6F/5S | 694P/6F/5S | 694P/6F/5S | ✓ |

The 6 failures in Schlieren.Tests are pre-existing environmental dependencies:
- 2× AllOpcodesOsakaTest (blockchain_test fixtures not installed)
- 3× UI conformance loader tests (fixture loader path dependencies)
- 1× Reset results test (UI state dependency)

These are **not** Harvest apparatus defects and do not block Campaign 1.

### Campaign 1: storage-lifecycle-v1

- **Fixture corpus:** EELS tests@v20.0.1 (8,172 files, 54,587 admitted cases)
- **Selection policy:** storage-lifecycle greedy set-cover (25 dimensions)
- **Cases selected:** 50
- **Manifest hash:** `c9b9e05827c5baa28ece31d9c36698add019ae181b4eed6e46b39e2edcc7ff46`
- **EELS identity:** Not pinned (allowNullIdentity — live oracle not required for manifest freeze)

### Next Step

Task 13: Execute the frozen manifest against Schlieren's canonical EVM, compare outputs against fixture post-state oracle, cluster divergences, and attempt certification.

### Environment

- OS: Windows 10
- Runtime: .NET 8.0.6
- Schlieren commit: dec1489
- Fixture root: `C:\Projects\Schlieren\fixtures\fixtures\state_tests`
- EELS fixtures release: tests@v20.0.1
