# Schlieren Conformance Status

## Current State: Campaign 1 Frozen (Corrected)

**Date:** 2026-08-25
**Commit:** c0744d1 + amendment (EELS identity fix)
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

**Known pre-existing failures (6):** These are environmental, not Harvest apparatus defects:
- 2× AllOpcodesOsakaTest (blockchain_test fixtures not installed — only state_tests present)
- 3× UI conformance loader tests (fixture loader path dependencies)
- 1× Reset results test (UI state dependency)

These do NOT satisfy a clean suite gate for certification. Task 13 certification will record these as an unmet gate unless resolved.

### Campaign 1: storage-lifecycle-v1

- **Fixture corpus:** EELS tests@v20.0.1 (8,172 files, 54,587 admitted cases)
- **Selection policy:** storage-lifecycle greedy set-cover (25 dimensions)
- **Cases selected:** 50
- **Manifest hash:** `a045393de4906bcbe2eca805a8d0b7b0d8a44761af6dc2839179422c6bb7437d`
- **EELS identity (pinned):**
  - Executable SHA-256: `c2a25c7f60a104f0cc024748256526a6fe511193bf320c98834dba55ad58bb10`
  - Reported version: `2.19.0`
  - Specs commit: `5b2b22c75f69bda02615204396b70a91e00529e0`

### Historical (superseded) manifest

The manifest `c9b9e05827c5baa28ece31d9c36698add019ae181b4eed6e46b39e2edcc7ff46` was frozen without EELS identity (`allowNullIdentity: true`). It is retained as historical evidence but is **not certifiable**. The corrected manifest above is the Campaign 1 certification target.

### Next Step

Task 13: Execute the frozen manifest against Schlieren's canonical EVM, compare outputs against fixture post-state oracle, cluster divergences, and attempt certification.

### Environment

- OS: Windows 10
- Runtime: .NET 8.0.6
- Schlieren commit: c0744d1 (+ Task 12 amendment)
- Fixture root: `C:\Projects\Schlieren\fixtures\fixtures\state_tests`
- EELS fixtures release: tests@v20.0.1
- EELS executable: `C:\projects\eels-venv\Scripts\ethereum-spec-evm.exe` (v2.19.0)
