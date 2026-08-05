---
name: eels-taxonomy-drill
description: >
  Runs the full (or bounded) EELS fixture suite, automatically buckets all failures
  by category (balance, storage, nonce, receipt, code, missing_account), delta magnitude,
  and address hot spots. Compares current run against docs/eels_baseline.json to flag regressions in red.
---

# Skill: eels-taxonomy-drill

## Purpose
Runs parallel fixture sweeps, groups test failures into single-root-cause buckets, and performs automated regression diffs against `docs/eels_baseline.json`.

## Features
- **Parallel Sweep**: Runs at `Environment.ProcessorCount` using per-executor 32MB stack threads.
- **Fork-Aware Hypotheses**: Matches deltas against normative `ForkGasData` constants.
- **Regression Detection**: Reads `docs/eels_baseline.json` and flags failure increases (`🔴 REGRESSION DETECTED (+N cases)`).
- **Baseline Update**: Set `$env:EELS_UPDATE_BASELINE = "1"` to lock in a new verified baseline after fixing bugs.

## Usage

```powershell
$env:EELS_FIXTURES_ROOT  = "C:/projects/Scrutor/fixtures/state_tests/cancun"
$env:EELS_INCLUDE_SUBDIRS = "1"
$env:EELS_MAX_CASES      = "9999"
dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj --filter "EelsTaxonomyDrill"
```
