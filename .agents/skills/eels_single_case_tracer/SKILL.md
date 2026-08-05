---
name: eels-single-case-tracer
description: >
  Runs ONE fixture case in complete isolation through Scrutor and emits a full
  EIP-3155 structLog JSON (PC, opcode, gas, gasCost, stack, memory, storage at
  every step), a gas accounting breakdown (intrinsic + EVM + refund + account
  deltas), and a pre→post account state diff. Use this to find the exact opcode
  step where gas diverges after eels-taxonomy-drill has identified the failure.
---

# Skill: eels-single-case-tracer

## Purpose
Produce a step-by-step EIP-3155 structLog for ONE fixture case so you can find
the exact opcode where Scrutor diverges from EELS expectations.

## When to use
- `eels-taxonomy-drill` has identified a failing case or a consistent delta.
- You need to know WHICH opcode caused the gas discrepancy.
- You want a structured JSON trace to diff against a Geth `debug_traceTransaction` output.

## Command

```powershell
# Minimal: point at a fixture directory and filter to one case
$env:EELS_FIXTURES_ROOT  = "C:/projects/Scrutor/fixtures/state_tests/cancun/eip1153_tstore"
$env:EELS_CASE_FILTER    = "test_basic_tload_after_store"
$env:EELS_REQUIRED_FORK  = "Cancun"
$env:EELS_INCLUDE_SUBDIRS = "1"
dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj --filter "SingleCaseTrace"
```

### Environment Variables
| Variable | Default | Description |
|---|---|---|
| `EELS_FIXTURES_ROOT`   | `fixtures/state_tests` | Directory containing fixture JSONs |
| `EELS_CASE_FILTER`     | *(empty = first case)* | Substring match on case_id |
| `EELS_REQUIRED_FORK`   | `Cancun` | Fork name |
| `EELS_INCLUDE_SUBDIRS` | `0` | `1` = recurse into subdirectories |
| `EELS_STRUCT_LOG_OUT`  | `TestResults/struct_log_<ts>.json` | Custom output path for structLog |
| `EELS_MAX_CASES`       | `200` | Max cases to load before applying CASE_FILTER |

## Output

### Console Summary
```
Tracing case: test_basic_tload_after_store_d0g0v0  [Cancun]
Fixture: fixtures/state_tests/cancun/eip1153_tstore/test_basic_tload_after_store.json

── TRANSACTION ──────────────────────────────────────────────────────
  gasLimit     : 100,000
  gasUsed      : 87,004
  gasRefunded  : 0
  gasRemaining : 12,996
  success      : True
  trace steps  : 142

── ACCOUNT DIFF (pre → post) ────────────────────────────────────────
  0xCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC
    balance  +0  (0x0 → 0x0)
    storage[0x0000...0001]  0x0 → 0x1234

── VERDICT ──────────────────────────────────────────────────────────
  ✅  PASS — state and receipt match fixture expectations.
```

### StructLog JSON (EIP-3155 format)
Written to `TestResults/struct_log_<timestamp>.json`:

```json
{
  "caseId": "test_basic_tload_after_store_d0g0v0",
  "fork": "Cancun",
  "gasUsed": 87004,
  "gasRefunded": 0,
  "success": true,
  "error": null,
  "structLogs": [
    {
      "pc": 0,
      "op": "PUSH1",
      "gas": 79979,
      "gasCost": "0x3",
      "depth": 1,
      "stack": [],
      "memory": "",
      "storage": {}
    },
    ...
  ]
}
```

## Comparing Against Geth structLog
To find the FIRST diverging step between Scrutor and Geth:

```python
import json

scrutor = json.load(open("TestResults/struct_log_scrutor.json"))["structLogs"]
geth    = json.load(open("geth_trace.json"))["result"]["structLogs"]

for i, (s, g) in enumerate(zip(scrutor, geth)):
    if s["pc"] != g["pc"] or s["gas"] != g["gas"]:
        print(f"DIVERGENCE at step {i}:")
        print(f"  Scrutor: PC={s['pc']} op={s['op']} gas={s['gas']} gasCost={s['gasCost']}")
        print(f"  Geth:    PC={g['pc']} op={g['op']} gas={g['gas']} gasCost={g['gasCost']}")
        break
```

## Workflow Integration
```
eels-taxonomy-drill          ←── Find which category fails and what delta
       ↓
eels-fixture-diff            ←── Confirm exact mismatch for one case
       ↓
eels-single-case-tracer      ←── Get structLog, find diverging opcode
       ↓
Fix in Scrutor.Core          ←── Edit gas schedule or opcode implementation
       ↓
eels-taxonomy-drill (again)  ←── Verify delta bucket disappeared
```
