# Single-Case Execution Ledger — EELS Fixture Gas Accounting

**Generated:** 2026-07-24  
**Purpose:** Isolate exactly where Scrutor's gas accounting diverges from EELS fixtures

---

## Case 1: modexp case3 (CREATE code-deposit OOG)

**Fixture:** `tests/byzantium/eip198_modexp_precompile/test_modexp.py::test_modexp[fork_Cancun-state_test-EIP-198-case3-raw-input-out-of-gas]`

**Transaction:**
- GasLimit: 500,000
- GasPrice: 10 wei
- BaseFee: 7 wei
- Priority Fee: 3 wei (legacy tx: gasPrice - baseFee)
- Value: 0
- To: 0x0000000000000000000000000000000000001000 (wrapper contract with CREATE)

**Fixture Expectation:**
```
Sender pays:    500,000 gas × 10 = 5,000,000 wei
Coinbase gets:  500,000 gas × 3 = 1,500,000 wei (priority fee portion)
BaseFee burned: 500,000 gas × 7 = 3,500,000 wei
```

**Scrutor Result:**
```
Sender pays:    492,547 gas × 10 = 4,925,470 wei
Coinbase gets:  492,547 gas × 3 = 1,477,641 wei
BaseFee burned: 492,547 gas × 7 = 3,447,829 wei
```

**Discrepancy:**
```
Sender under-charged: 7,453 gas
Coinbase under-paid:  7,453 gas
Total discrepancy:    7,453 gas (the EIP-150 parent reserve)
```

**Execution Status:** FAILED (initcode OOG)

**Hypothesis:** Scrutor correctly preserves the EIP-150 1/64 parent reserve (7,453 gas) after CREATE child fails. The wrapper contract's STOP opcode completes normally, leaving the reserve unspent and refundable. Fixture expects all 500K gas consumed.

**Resolution Status:** ⏸️ DEFERRED — Needs EELS Python reference execution and Geth trace comparison to establish ground truth

---

## Case 2: TLOAD after TSTORE

**Fixture:** `tests/cancun/eip1153_tstore/test_basic_tload.py::test_basic_tload_after_store[fork_Cancun-state_test]`

**Transaction:**
- GasLimit: 5,000,000
- GasPrice: 10 wei
- BaseFee: 7 wei
- Priority Fee: 3 wei
- Value: 0
- To: 0x0000000000000000000000000000000000001000

**Fixture Expectation:**
```
Sender pays:    43,519 gas × 10 = 435,190 wei
Coinbase gets:  43,519 gas × 3 = 130,557 wei
```

**Scrutor Result:**
```
Sender pays:    56,515 gas × 10 = 565,150 wei
Coinbase gets:  56,515 gas × 3 = 169,545 wei
```

**Discrepancy:**
```
Scrutor OVER-charged by: 12,996 gas
```

**Execution Status:** SUCCESS

**Hypothesis:** TLOAD/TSTORE gas costs incorrect. Likely:
- Base cost wrong (100 instead of 5?)
- Warm/cold access logic inverted
- Missing EIP-1153 transient storage semantics

**Resolution Status:** 🔴 NEEDS INVESTIGATION

---

## Case 3: TLOAD with gas price

**Fixture:** `tests/cancun/eip1153_tstore/test_basic_tload.py::test_basic_tload_gasprice[fork_Cancun-state_test]`

**Transaction:**
- GasLimit: 5,000,000
- GasPrice: 10 wei
- BaseFee: 7 wei
- Priority Fee: 3 wei
- Value: 0
- To: 0x0000000000000000000000000000000000001000

**Fixture Expectation:**
```
Sender pays:    53,925 gas × 10 = 539,250 wei
Coinbase gets:  53,925 gas × 3 = 161,775 wei
```

**Scrutor Result:**
```
Sender pays:    77,292 gas × 10 = 772,920 wei
Coinbase gets:  77,292 gas × 3 = 231,876 wei
```

**Discrepancy:**
```
Scrutor OVER-charged by: 23,367 gas
```

**Execution Status:** SUCCESS

**Hypothesis:** Same TLOAD/TSTORE issue as Case 2, compounded by multiple operations

**Resolution Status:** 🔴 NEEDS INVESTIGATION

---

## Case 4: TLOAD with CALLCODE

**Fixture:** `tests/cancun/eip1153_tstore/test_tload_calls.py::test_tload_calls[fork_Cancun-state_test-call_type_CALLCODE]`

**Transaction:**
- GasLimit: 5,000,000
- GasPrice: 10 wei
- BaseFee: 7 wei
- Priority Fee: 3 wei
- Value: 0
- To: 0x0000000000000000000000000000000000001100

**Fixture Expectation:**
```
Sender pays:    78,358 gas × 10 = 783,580 wei
Coinbase gets:  78,358 gas × 3 = 235,074 wei
```

**Scrutor Result:**
```
Sender pays:    78,356 gas × 10 = 783,560 wei
Coinbase gets:  78,356 gas × 3 = 235,068 wei
```

**Discrepancy:**
```
Scrutor UNDER-charged by: 2 gas
```

**Execution Status:** SUCCESS

**Hypothesis:** Tiny discrepancy suggests base opcode cost off by 1-3 gas (e.g., CALLCODE should be 700 gas but Scrutor charges 697 or 703)

**Resolution Status:** 🟡 MINOR — Check CALLCODE base cost in EVM interpreter

---

## Case 5: TLOAD with CALL

**Fixture:** `tests/cancun/eip1153_tstore/test_tload_calls.py::test_tload_calls[fork_Cancun-state_test-call_type_CALL]`

**Transaction:**
- GasLimit: 5,000,000
- GasPrice: 10 wei
- BaseFee: 7 wei
- Priority Fee: 3 wei
- Value: 0
- To: 0x0000000000000000000000000000000000001100

**Fixture Expectation:**
```
Sender pays:    39,358 gas × 10 = 393,580 wei
Coinbase gets:  39,358 gas × 3 = 118,074 wei
```

**Scrutor Result:**
```
Sender pays:    44,155 gas × 10 = 441,550 wei
Coinbase gets:  44,155 gas × 3 = 132,465 wei
```

**Discrepancy:**
```
Scrutor OVER-charged by: 4,797 gas
```

**Execution Status:** SUCCESS

**Hypothesis:** CALL + TLOAD interaction. Either:
- CALL warm/cold access cost wrong
- TLOAD costs wrong
- EIP-2929 access list logic incorrect

**Resolution Status:** 🔴 NEEDS INVESTIGATION

---

## Summary Table

| Case | Test Type | Gas Discrepancy | Direction | Status |
| :--- | :--- | ---: | :--- | :--- |
| 1 | CREATE OOG | 7,453 | Under | ⏸️ Deferred (EIP-150 reserve semantics unclear) |
| 2 | TLOAD basic | 12,996 | Over | 🔴 Bug |
| 3 | TLOAD gasprice | 23,367 | Over | 🔴 Bug |
| 4 | CALLCODE + TLOAD | 2 | Under | 🟡 Minor |
| 5 | CALL + TLOAD | 4,797 | Over | 🔴 Bug |

---

## Pattern Analysis

**TLOAD/TSTORE (EIP-1153) gas costs are systematically wrong:**
- All 4 non-CREATE cases involve transient storage operations
- All show over-charging (except case 4's tiny 2-gas under-charge)
- Magnitude varies (2 to 23K gas), suggesting multiple interacting issues

**Priority investigation targets:**
1. **TLOAD/TSTORE base costs** — Should be 100 gas (warm) per EIP-1153
2. **Transient storage warm/cold tracking** — First access to a slot costs more
3. **CALL family warm/cold interaction** — EIP-2929 access lists + EIP-1153

**CREATE EIP-150 reserve question:**
- Isolated, well-understood discrepancy
- Requires EELS/Geth ground-truth comparison before changing

---

## Next Actions

### 1. Verify TLOAD/TSTORE Implementation ✅ HIGH PRIORITY

**Check:**
```bash
rg -n "TLOAD|TSTORE|EIP.*1153" Scrutor.Core/Opcodes/
```

**Expected costs (EIP-1153):**
- TLOAD: 100 gas
- TSTORE: 100 gas
- No cold/warm distinction for transient storage (always warm)

### 2. Run Single Isolated Test with Full Trace

```bash
dotnet test Scrutor.EELS.Tests \
  --filter "FullyQualifiedName~test_basic_tload_after_store" \
  --logger "console;verbosity=detailed" \
  -- \
  EELS_FIXTURES_ROOT=fixtures/state_tests/cancun/eip1153_tstore
```

Capture:
- Opcode-level gas charges
- Stack state before/after TLOAD/TSTORE
- Transient storage state

### 3. Compare Against EELS Python Reference

```bash
cd /c/projects/execution-specs
python3 -m pytest \
  tests/cancun/eip1153_tstore/test_basic_tload.py::test_basic_tload_after_store \
  -v \
  --showlocals
```

### 4. Differential Against Geth

```bash
geth --dev \
  --http \
  --http.api debug,eth \
  --verbosity 5 \
  2>&1 | tee geth_case2_trace.log
```

Then submit the exact transaction and compare `debug_traceTransaction` output

---

## Accounting Reconciliation Formula

For each test case, the following must hold:

```
senderPost = senderPre - (gasUsed × gasPrice) - value
coinbasePost = coinbasePre + (gasUsed × priorityFee)
baseFeePost = baseFeeCollector + (gasUsed × baseFee)  [burned]
```

Where:
- `gasUsed = intrinsicGas + evmGasUsed - refund` (capped at gasUsed/5 per EIP-3529)
- `priorityFee = min(maxPriorityFeePerGas, maxFeePerGas - baseFee)`
- `effectiveGasPrice = baseFee + priorityFee`

For legacy transactions (type 0):
- `priorityFee = gasPrice - baseFee`
- `effectiveGasPrice = gasPrice`

All 5 test cases use legacy tx (TxType: 0), so:
- `gasPrice = 10`
- `baseFee = 7`
- `priorityFee = 3`

**Scrutor's accounting internally appears consistent** (sender + coinbase always match within ±1 gas due to rounding). The discrepancy is in **total gas consumed**, not in fee distribution.

---

## Critical Insight

The **"393,580 gas discrepancy"** mentioned in earlier analysis **is NOT a single systemic bug**. It's actually:

```
Case 5 discrepancy = 393,580 wei ÷ 10 wei/gas = 39,358 gas EXPECTED
                     441,550 wei ÷ 10 wei/gas = 44,155 gas ACTUAL
                     Difference = 4,797 gas over-charge
```

The "393K" number was the **fixture's expected sender balance delta**, not the gas discrepancy itself.

**The actual systemic pattern is: TLOAD/TSTORE operations are over-charging by 2-23K gas per transaction**, depending on usage pattern.

This completely changes the diagnosis from "transaction harness bug" to "opcode implementation bug."
