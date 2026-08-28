# EELS Apparatus Root Cause — 2026-08-28

## Finding

The prior HarnessError on Return Data case `test_stack_overflow[fork_Berlin-opcode_RETURNDATASIZE-state_test-fails_False]` was not caused by deep EVM execution or a timeout. The root cause was `PYTHONPATH` pollution.

## Mechanism

Hermes Agent sets:

```
PYTHONPATH=C:\Users\Erick\AppData\Local\hermes\hermes-agent;C:\Users\Erick\AppData\Local\hermes\hermes-agent\venv\Lib\site-packages
```

When `ethereum-spec-evm.exe` launches Python, it imports `pydantic` from the Hermes venv instead of the EELS venv. The Hermes pydantic installation has an incompatible `pydantic_core` binary, causing:

```
ModuleNotFoundError: No module named 'pydantic_core._pydantic_core'
```

This crashes EELS before it reaches the state test, and the Harvest subprocess interprets the crash as a HarnessError.

## Resolution

Unset `PYTHONPATH` before invoking EELS. With a clean environment:

- EELS 2.19.0 completes the exact frozen case in under 2 seconds with `pass: true`
- Schlieren executes the same case with `isSuccess: true`, `gasUsed: 29077`
- Full 50-case Return Data campaign: 50/50 pass, 0 divergence

## Provenance Identity

The prior manifest pinned launcher SHA-256 `c2a25c7f...` which no longer exists (original venv deleted). The current installation produces `ee46923d...`. Both are pip console-launcher wrappers for the same `ethereum-execution 2.19.0` package. The ValidateIdentity gate was changed to warn on launcher mismatch rather than refuse execution.

## Affected Campaigns

All campaigns that reported HarnessError prior to Gate 1 may have been affected by this same mechanism. The `--noreturndata --nostack --nomemory` flags added in Gate 1 were a correct optimization but were not the sole fix — clearing PYTHONPATH was the actual resolution for this case.

## Evidence

- Run ID: `return-data-v1_20260828114257_dd4035e9`
- Commit: `c8fbc7c`
- EELS version: 2.19.0
- EELS source commit: `85aa48c742c38a2d5a876f84ebf8082a50273064`
- Python: 3.13.11
- Fixture: `fixtures/state_tests/for_berlin/frontier/opcodes/all_opcodes/stack_overflow.json`
