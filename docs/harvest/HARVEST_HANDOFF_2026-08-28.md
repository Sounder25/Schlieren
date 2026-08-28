# Harvest Certification Handoff — 2026-08-28

## Authoritative stopping point

- Branch: `main`
- Task 1 implementation: `d63b239` (`fix: preserve typed harvest apparatus evidence`)
- Task 1 status: complete
- Resume point: Task 2 in `docs/superpowers/plans/2026-08-26-strategic-campaign-certification.md`

Pull `origin/main` on the laptop. Do not repeat Task 1 and do not repair an EVM family yet.

## What Task 1 established

- `EELS_EXE` is required. There is no compiled machine-specific fallback.
- A campaign refuses to create a run unless the configured EELS executable exists and its SHA-256/version match the frozen manifest.
- Oracle and worker failures retain typed reason, elapsed time, exit code, stream hashes, retention mode, and executable identity.
- Oracle failures remain `HarnessError`; worker failures and cancellation remain `Aborted`.
- Existing ledger JSON remains compatible: `attemptEvidence` is omitted when null.
- No campaign execution semantics were changed and no campaign was rerun.

## Verification at handoff

| Check | Result |
|---|---|
| Task 1 focused tests | 70 passed / 0 failed |
| Full `Schlieren.Harvest.Tests` | 225 passed / 0 failed |
| Full CLI tests | 18 passed / 0 failed |
| Full `Schlieren.Tests` | 702 passed / 0 failed / 5 skipped |
| Release solution build | succeeded / 0 errors |
| Historical ledger comparison | succeeded without rewriting evidence |
| EELS fixture-dependent solution tests | known missing local fixture directories; not counted as conformance passes |

The combined solution test was allowed to run for roughly 15 minutes. It reported the known absent EELS fixture directories, completed Harvest 225/225, and then remained in the CPU-active long core test host. That combined invocation was stopped because the same core project had already passed independently. This is recorded as an interrupted aggregate run, not a clean solution-test pass.

## Laptop prerequisites

Set `EELS_EXE` to the absolute path of the EELS 2.19.0 executable whose SHA-256 is frozen in the campaign manifest. Set `EELS_FIXTURES_ROOT` to the complete state-test fixture root. If either identity or fixture availability is wrong, stop and correct the apparatus; do not weaken the check.

## One next action

Execute Task 2 exactly as written: reproduce

`tests/frontier/opcodes/test_all_opcodes.py::test_stack_overflow[fork_Berlin-opcode_RETURNDATASIZE-state_test-fails_False]`

through pinned EELS and the Harvest boundary, then add bounded diagnostic retention without changing execution. The exact frozen case must succeed three times before rerunning the unchanged 50-case Return Data manifest. Substitution is forbidden.

## Campaign status remains historical

- Storage Lifecycle: 50/50 historical certificate
- Call Semantics: 50/50 baseline
- Create Semantics: 50/50 baseline
- Return Data: 50/50 apparatus-fixed baseline
- Self-Destruct: 34/50
- Transient Storage: 48/50
- Access List/Fee Market: 49/50

These are not a new same-commit certificate. Final certification still requires the later same-provenance inspection train.
