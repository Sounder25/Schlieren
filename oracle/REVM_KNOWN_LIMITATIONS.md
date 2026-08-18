# REVM 42.x Known Oracle Limitations

This document records cases where REVM 42.x diverges from the Ethereum specification
(EELS) and therefore produces false-positive divergences in the Schlieren synthetic
hardening campaign. These are **not Schlieren bugs**.

When a campaign run surfaces a divergence that matches a limitation listed here,
it should be classified as `OracleKnownBug` and excluded from defect counts.

---

## REVM-BUG-001: Berlin SSTORE Clear Refund Not Applied

**Status:** Open (REVM 42.x, as of 2026-08-16)  
**Affected forks:** Berlin (pre-London)  
**EELS reference:** `ethereum/execution-specs/src/ethereum/forks/berlin/vm/instructions/storage.py`

### Symptom

For a CALL that results in an SSTORE clearing a non-zero slot to zero:

```
original = 0xAA  (non-zero)
current  = 0xAA  (same, slot not touched this tx)
new      = 0x00  (clear to zero)
```

REVM reports `gas_used = 23828` and `refund = 0`.  
Schlieren reports `gas_used = 14314`.  
EELS reports `gas_used = 14314` ✓

### Root Cause

EIP-2200 §3 specifies `REFUND_STORAGE_CLEAR = 15000` gas when:
- `original_value != 0`
- `current_value != 0`
- `new_value == 0`

EELS applies this correctly:
```python
if original_value != 0 and current_value != 0 and new_value == 0:
    evm.refund_counter += REFUND_STORAGE_CLEAR  # 15000
```

Berlin refund cap: `min(tx_gas_used_before_refund // 2, refund_counter)`  
`= min(28628 // 2, 15000) = min(14314, 15000) = 14314`  
`tx_gas_used_after_refund = 28628 - 14314 = 14314`

REVM 42.x computes `ResultGas::tx_gas_used()` as
`max(total_gas_spent − refunded, floor_gas)` but its `refunded` field is 0
for this case — the SSTORE clear refund is not accumulated.

### Verification

```
ethereum-spec-evm statetest --json berlin_xtozero_test.json
→ gasUsed=0x1dcc (7628 EVM gas), refund=15000 (structLog)
→ final = 21000 + 7628 - 14314 = 14314  ✓
```

Regression test: `EelsOracleVerification.Berlin_XToZero_Schlieren_Matches_EELS`

### Campaign Signature

```
Category    : *-SStore
Fork        : Berlin
StoragePattern: XToZero  (pre-state slot non-zero, new value = 0)
DiffKind    : GasMismatch
Delta       : -9514  (= 23828 - 14314)
```

Any campaign run that surfaces `GasMismatch delta=-9514` on `Fork=Berlin`
with `StoragePattern=XToZero` is this REVM bug, not a Schlieren defect.
Escalate to EELS for verdict before attributing to Schlieren.

---

## How to Escalate a REVM Divergence to EELS

1. Identify the case JSON from `~/SyntheticResults/<run>/FAM-*/`
2. Run the targeted probe:
   ```
   dotnet test --filter "FullyQualifiedName~EelsOracleVerification"
   ```
3. Or write a targeted `EelsExecutionHarness` comparison for the specific case.
4. If `Schlieren == EELS`: record as REVM bug here, suppress in campaign.
5. If `Schlieren != EELS`: real Schlieren defect, fix and add regression test.

---

## Suppression in Campaign

REVM-BUG-001 is suppressed in `SyntheticDifferentialRunner` via
`IsKnownRevmLimitation(SyntheticCase, ExecutionDiff)`:

```csharp
// Berlin SSTORE clear refund not applied by REVM
if (c.Fork == "Berlin"
    && c.StoragePattern == StoragePattern.XToZero
    && diff.GasMismatch
    && diff.GasDelta == -9514)
    return true;
```
