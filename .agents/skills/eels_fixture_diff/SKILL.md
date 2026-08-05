---
name: eels-fixture-diff
description: >
  Given a failing EELS fixture JSON and case_id, runs it through Scrutor and the
  EELS Python reference runner (ethereum-spec-evm.exe), diffing execution traces
  step-by-step to find the exact opcode where divergence begins.
---

# Skill: eels-fixture-diff

## Purpose
End-to-end single-command pipeline that runs a failing fixture case through Scrutor AND the official EELS Python spec reference (`ethereum-spec-evm.exe`), diffing pre/post state, gas accounting, and step-by-step structLog execution.

## Usage

```powershell
python tools/eels_fixture_diff.py <fixture.json> <case_id> [--fork Cancun] [--step-trace]
```

### Options
- `--step-trace`: Emits Scrutor structLog JSON, generates official EELS Python reference trace via `ethereum-spec-evm.exe`, and runs `eels_trace_compare.py` automatically.

### Example
```powershell
python tools/eels_fixture_diff.py fixtures/state_tests/cancun/stExample/add11.json add11_Cancun --step-trace
```

## Output Breakdown
1. **Pre-State & Tx Summary**: Calldata, value, sender, to address, gas limit.
2. **Scrutor Execution Result**: Mismatch table (`balance`, `nonce`, `storage`, `receipt`).
3. **Gas Accounting**: Intrinsic gas, EVM gas used, unused refund, sender & coinbase deltas.
4. **Step-by-Step Opcode Diff**: (With `--step-trace`) Side-by-side comparison of PC, opcode, gas, and stack top at the exact point of divergence.
