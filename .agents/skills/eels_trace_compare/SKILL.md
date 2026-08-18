---
name: eels-trace-compare
description: >
  Diffs two EIP-3155 structLog JSON files step-by-step (e.g., Schlieren trace vs
  EELS Python reference output via ethereum-spec-evm.exe) and prints the exact step
  index, PC, opcode, and gas delta where execution diverges.
---

# Skill: eels-trace-compare

## Purpose
Compares two step-by-step execution traces (structLog format) or generates the
canonical EELS Python reference trace directly using `ethereum-spec-evm.exe`.

## Usage

### Mode A: Trace file vs EELS Python Reference (Ground Truth)
```powershell
python tools/eels_trace_compare.py TestResults/struct_log_schlieren.json --eels-fixture fixtures/state_tests/cancun/stExample/add11.json
```

### Mode B: Direct Trace JSON File Comparison
```powershell
python tools/eels_trace_compare.py TestResults/struct_log_schlieren.json geth_trace.json --label1 Schlieren --label2 Geth
```

## Output Structure

```
══════════════════════════════════════════════════════════════════════
  COMPARING TRACES
  Schlieren    : TestResults/struct_log_schlieren.json  (142 steps)
  EELS Spec  : TestResults/struct_log_eels.json     (142 steps)
══════════════════════════════════════════════════════════════════════

── FIRST DIVERGENCE DETECTED ────────────────────────────────────────
  Step Index : 43
  Reason     : Remaining gas mismatch: 76,400 vs 78,500 (Δ = -2,100)

  Field           | Schlieren                   | EELS Spec               
  ----------------+---------------------------+--------------------------
  PC              | 102                       | 102                      
  Opcode          | SLOAD                     | SLOAD                    
  Gas Remaining   | 76,400                    | 78,500                   
  Gas Cost        | 2,100                     | 100                      
```
