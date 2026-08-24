# Harvest Pre-Repair Intake Baseline

**Date:** 2026-08-24
**Commit:** `d43c8c564f30974f299676da4b7776d393a4a3a5`
**Branch:** `main`
**Working tree:** clean (0 modified/untracked)

## Environment

| Property | Value |
|---|---|
| .NET SDK | 8.0.424 (commit 5cbde90d8f) |
| MSBuild | 17.11.48+02bf66295 |
| OS | Windows 10.0.26200 (win-x64) |
| Processors | 6 |
| EELS executable | `ethereum-spec-evm` 2.19.0 (available on PATH) |
| EELS fixtures | Present at `fixtures/` (state_tests, blockchain_tests) |
| `EELS_FIXTURES_ROOT` | Not set (tests use relative path) |

## Core Solution Test Gate

**Scope:** `Schlieren.Tests/Schlieren.Tests.csproj` only. This measures unit, integration, regression, campaign, and architecture tests. It does **not** include any EELS state-test or blockchain-test conformance sweep. No EELS cases were enumerated, executed, or compared in these runs.

### Run 1

| Metric | Value |
|---|---|
| Project | `Schlieren.Tests` |
| Total | 658 |
| Passed | 653 |
| Failed | 0 |
| Skipped | 5 |
| Duration | 25s |
| Host termination | None |

### Run 2

| Metric | Value |
|---|---|
| Project | `Schlieren.Tests` |
| Total | 658 |
| Passed | 653 |
| Failed | 0 |
| Skipped | 5 |
| Duration | 25s |
| Host termination | None |

### Consistency

Runs 1 and 2 are **identical** in totals, pass/fail/skip, and test identities. No flaky test observed in this pair.

### TRX Artifact Status

TRX files were not retained. The `--logger "trx;..."` flag was not used in the intake measurement commands (they used `--nologo` output parsing only). This is a **baseline limitation**: no durable machine-readable artifact exists for these two runs.

**Correction for Task 12:** The certification gate's three-run suite requirement must use `--logger "trx;LogFileName=..."`, retain all three TRX files under a non-gitignored path or reference their SHA-256 hashes in the suite-gate record, and refuse certification if any TRX is absent or its hash does not match the recorded value.

### Skipped Tests (5)

1. `GoldenCorpusTests.Round1_LibraryGuard_NoFalsePositiveStorage` — needs real TokenLib bytecode
2. `GoldenCorpusTests.Round5_SuccessfulDelegatecall_NoGasDoubleCount_NoReentrancyFalsePositive` — needs proxy+impl bytecode
3. `CallSemanticsCampaignTests.Campaign_RunSingleCase_CALL_Success` — enable after first case passes
4. `Round5ProxyWithImplementationTests.ProxyWithImplementation_ExecutesNestedCall_NoUnresolvedDiagnostic` — truncated proxy bytecode (jump targets exceed code length)
5. `GoldenCorpusTests.Round4_ProxyUnresolved_DiagnosticNotVulnerability` — needs real bytecode

## Known-Defect Probes

### OpSecLockoutTests

| Metric | Value |
|---|---|
| Total | 14 |
| Passed | 14 |
| Failed | 0 |

**Note:** Existing tests pass because they run sequentially. The process-global `IsEnabled` race is not exercised by current tests — it manifests only under concurrent async execution with WorkbenchViewModel construction. The defect remains latent.

### OverlayIsolationTests

| Metric | Value |
|---|---|
| Total | 8 |
| Passed | 8 |
| Failed | 0 |

**Note:** Existing tests use shallow overlay chains. The `StateOverlay.GetStorageAtAsync` stack-overflow occurs with deeply nested overlays (e.g., during full taxonomy sweeps with many sub-call frames). The defect is latent in this probe but documented as the cause of prior taxonomy host aborts.

### EelsTaxonomyDrill (discovery-only — zero EELS cases compared)

| Metric | Value |
|---|---|
| Total | 1 |
| Passed | 1 |
| Failed | 0 |
| Duration | <1ms |
| Host termination | None |
| EELS cases enumerated | 0 |
| EELS cases compared | 0 |

**Note:** The taxonomy drill requires `EELS_FIXTURES_ROOT` and specific runsettings to actually sweep cases. With default settings it runs its discovery path only — no EELS cases were enumerated or compared. This result proves only that the harness starts without crash under default configuration. It does not confirm the overlay stack-overflow is resolved, and it provides zero conformance evidence.

## Credential Findings

| File | Line | Finding |
|---|---|---|
| `Schlieren.UI/Services/HarvestService.cs` | 16 | Embedded JWT `N8nApiKey = "eyJhbG...Idzw"` |
| `Schlieren.UI/Services/HarvestService.cs` | 17 | Embedded JWT `McpToken = "eyJhbG...zMFE"` |
| `Schlieren.UI/Services/HarvestService.cs` | 72 | Bearer header constructed from `McpToken` |

**Classification:** Operational credentials committed in tracked source. Must be externalized and rotated (deletion from source does not revoke the tokens).

`Schlieren.RPC/Server/RpcRouter.cs:167` — `anvil_showPrivateKey` is a dev-mode RPC handler name, not an embedded credential.

`tools/harvest.py` — `fixture_key` is a dictionary key name, not a credential.

## Limitations

- The taxonomy sweep was not exercised with real EELS case enumeration. The stack-overflow defect may still abort long-running taxonomy runs with deep overlay chains.
- Only `Schlieren.Tests` was measured. `Schlieren.EELS.Tests` full sweep was not attempted due to the known host-abort risk and the time constraint.
- The credential scan is regex-based and may have false negatives for obfuscated or indirectly referenced secrets.
- TRX files were not retained (gitignored). Measurements are recorded by command output only.

## Apparatus Status

| Gate | Status |
|---|---|
| Clean tree | ✅ |
| Build succeeds | ✅ |
| Consecutive identical runs | ✅ (2/2 identical) |
| OpSec race latent | ⚠️ Not exercised — defect documented |
| Overlay stack-overflow latent | ⚠️ Not exercised — defect documented |
| Taxonomy host-abort risk | ⚠️ Not tested with full sweep |
| Tracked credentials | ❌ 2 JWTs in HarvestService.cs |
| EELS executable available | ✅ |
| Fixture corpus available | ✅ |

## Conclusion

The apparatus builds cleanly and the core test suite is stable (653/0/5 across two runs). Three Phase 0 defects remain latent: the OpSec global-state race, the StateOverlay recursive traversal, and embedded operational credentials. These must be resolved before Campaign 1 can issue a conformance result.
