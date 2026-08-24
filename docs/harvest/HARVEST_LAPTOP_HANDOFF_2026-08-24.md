# Harvest Laptop Handoff — 2026-08-24

## Resume point

Repository: `https://github.com/Sounder25/Schlieren.git`

Branch: `main`

Implementation anchor before this handoff: `66463e5`

Use the latest `origin/main`. The only commits after `66463e5` should be handoff/progress documentation unless another authorized worker has advanced Task 3.

## Governing documents

Read these completely before changing code:

1. `docs/superpowers/specs/2026-08-24-harvest-certification-foundation-design.md`
2. `docs/superpowers/plans/2026-08-24-harvest-certification-foundation.md`
3. `docs/harvest/baselines/2026-08-24-pre-repair-intake.md`

The implementation plan is the acceptance contract. New blocking requirements must be committed to the plan before the affected task begins. Do not apply new criteria retroactively except for the explicit emergency cases listed in the plan.

## Completed work

### Task 0 — intake baseline

- `58648ce` — recorded the pre-repair intake baseline.
- `a107531` — corrected baseline audit labels.
- Baseline core gate: `653 passed / 0 failed / 5 skipped / 658 total`, identical across two runs.
- The taxonomy probe was discovery-only and compared zero EELS cases; it was not a conformance pass.

### Task 1 — OpSec isolation

- `b8f4f3e` — replaced process-global OpSec mutation with async-flow-scoped behavior and removed Workbench global writes.
- `b229bde`, `9fdbb1f`, `4c730c3` — hardened deterministic concurrency, direct scope, Workbench-isolation, and failure-path tests.
- Final Task 1 gate: `661 passed / 0 failed / 5 skipped / 666 total`.
- `OpSecLockout` is a static API backed by `AsyncLocal<int>` scope depth.
- Workbench keeps its toggle local and scopes only the selected execution operation.

### Task 2 — non-recursive storage lookup

- `9ea8586` — made `StateOverlay.GetStorageAtAsync` iterative, cancellation-aware, and cycle-safe.
- `5ac71f1` — consolidated the new tests into the exact planned test file and proved ancestor override traversal.
- Exact focused gate: `12 passed / 0 failed / 0 skipped / 12 total`.
- Latest full regression reported: `665 passed / 0 failed / 5 skipped / 670 total`.
- Verified behaviors include inherited lookup through 8,192 overlays, ancestor override through more than 2,048 overlays, nearest-overlay precedence, tombstone-as-zero, cancellation, shallow commit propagation, and exact cycle exception text.

### Plan governance

- `5b360a8` — added prospective acceptance/change-control and deterministic concurrency-test rules.
- `66463e5` — defined Task 3's exact credential/configuration composition contract before implementation.

## Known apparatus limitation

The Task 2 EELS-adjacent command produced `2 passed / 3 FixtureUnavailable / 5 total`. The broader corpus exists, but these required fixture directories are absent locally:

- `fixtures/state_tests/cancun/eip6780_selfdestruct/selfdestruct_revert`
- `fixtures/state_tests/cancun/eip1153_tstore/basic_tload`

This is not a Schlieren pass or engine divergence. Preserve it as `FixtureUnavailable` until the matching fixture revision is installed and pinned.

## Security condition

Two operational JWTs were committed in `Schlieren.UI/Services/HarvestService.cs`. Treat both as compromised.

Task 3 removes them from tracked source and replaces compiled configuration with these environment keys:

- `SCHLIEREN_N8N_BASE_URL`
- `SCHLIEREN_N8N_API_KEY`
- `SCHLIEREN_MCP_TOKEN`
- `SCHLIEREN_HARVEST_CORPUS`

Removing tokens from source does not revoke them. Rotation is an external action and must not be reported as complete without independent evidence. Never place replacement values in source, tests, reports, command transcripts, or chat.

## Next authorized work: Task 3 only

Task 3 is “Remove operational credentials and synchronize evidence docs.” Follow the amended file list, constructors, scope boundary, tests, scan command, build command, and commit command exactly as written in the implementation plan.

Important boundaries:

- `App` is the composition root.
- `MainWindow` receives the Harvest ViewModel; it does not construct it.
- `HarvestViewModel` receives the service/options; it does not construct the service.
- No compiled credential or corpus-directory fallback remains.
- Missing integrations produce visible unconfigured states without live network calls.
- Do not redesign the Harvest UI, n8n protocol, polling interval, corpus schema, or workflow identifiers.
- Stop after the Task 3 commit and report its exact scan, focused-test, build, and repository-status results. Do not begin Task 4 without review.

## Laptop restart commands

For a new clone:

```powershell
git clone https://github.com/Sounder25/Schlieren.git C:\projects\Schlieren
Set-Location C:\projects\Schlieren
git checkout main
git pull --ff-only origin main
git status --short
git log -5 --oneline
```

For an existing clean clone:

```powershell
Set-Location C:\projects\Schlieren
git fetch origin
git checkout main
git pull --ff-only origin main
git status --short
git log -5 --oneline
```

Before Task 3, `git status --short` must produce no output. If the laptop has local changes, do not reset, clean, overwrite, or merge them; inspect and preserve them first.

## Task 3 completion report format

Report:

- commit SHA;
- exact files changed;
- secret scan: findings and exit code, with secrets redacted;
- focused configuration tests: passed / failed / skipped / total;
- solution build result;
- confirmation that no live network dependency was used in tests;
- external rotation status, stated as complete only with evidence;
- final `git status --short` result.
