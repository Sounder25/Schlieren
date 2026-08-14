# Code Audit: SystemOpcodes.cs EELS Conformance Fixes

**Date:** 2026-07-24  
**Auditor:** Hermes Agent (Self-Audit per User Request)  
**Scope:** `Schlieren.Core/Opcodes/SystemOpcodes.cs` commits d796496, 6a9734c, 538a218

---

## Executive Summary

**AUDIT RESULT: CODE STRUCTURALLY SOUND**

The current `SystemOpcodes.cs` implementation shows no evidence of the duplicated declarations, double charges, or contradictory logic that would be expected from a patching failure. Each opcode (CREATE, CREATE2, CALL, CALLCODE) follows a clean single-path flow with exactly one charge, one execution, and one refund per gas category.

**However**: The test results do NOT match the claimed "4/5 modexp passes" state. Current test run shows broader failure (1,297/2,032 cases failing), indicating either:
1. Test fixture expectations were misread
2. Additional regressions were introduced
3. The "4/5" claim was based on incomplete or cached test output

---

## Detailed Audit

### 1. CREATE Opcode (lines 10-131)

**Flow Analysis:**
```
Line 35:  ConsumeGas(32000 + initCodeWordGas)     // Base cost
Lines 51-53: Calculate forwardedGas, parentReserve  // EIP-150
Line 56:  ConsumeGas(forwardedGas)                  // Child debit
Line 72:  Execute child frame
Line 76:  childRemaining = forwardedGas - gasUsed   // Calculate once
Lines 82-120: Handle code-deposit
  - OOG path (90-101): Push 0, NO refund
  - Success path (102-120): Deduct cost, refund remainder, set code, push address
Lines 122-127: Init failure path: refund childRemaining, push 0
Line 129: Return opcode success
```

**Verification:**
- ✓ Single `ConsumeGas` for base cost (line 35)
- ✓ Single `ConsumeGas` for forwarded gas (line 56)
- ✓ Single `childRemaining` calculation (line 76)
- ✓ Code-deposit cost deducted exactly once (line 105)
- ✓ RefundGas called exactly once per path (lines 111 or 125)
- ✓ Stack push exactly once per outcome (lines 99, 119, or 126)
- ✓ SetCode called exactly once on success (line 108)

**No duplicates found.**

### 2. CREATE2 Opcode (lines 281-395)

**Flow Analysis:**
Identical structure to CREATE, with additional hash cost calculation.

**Verification:**
- ✓ Single base cost charge (line 308)
- ✓ Single forwardedGas calculation and charge (lines 330-334)
- ✓ Single childRemaining calculation (line 353)
- ✓ Code-deposit cost deducted exactly once (line 370)
- ✓ RefundGas called exactly once per path (lines 376 or 385)
- ✓ Stack push exactly once per outcome (lines 365, 379, or 386)
- ✓ SetCode called exactly once on success (line 373)

**No duplicates found.**

### 3. CALL Opcode (lines 133-279)

**Flow Analysis:**
```
Lines 170-188: Calculate extraCost (access + value + newAccount)
Lines 191-194: EIP-150 calculation (maxForward, forwardedGas)
Line 197: ConsumeGas(forwardedGas + extraCost)        // Single parent debit
Lines 200-210: Balance check early-exit with full refund
Lines 213-214: Calculate stipend, childGasLimit
Lines 217-254: Execute (precompile or subcall)
Lines 259-261: Calculate childRemaining, refund ALL
Line 275: Push success flag
```

**Verification:**
- ✓ Single extraCost calculation (lines 170-188)
- ✓ Single ConsumeGas for forwarded + extra (line 197)
- ✓ Early-exit refund (lines 206-207) is mutually exclusive with execution
- ✓ Single stipend calculation (line 213)
- ✓ Single childRemaining calculation (line 260)
- ✓ Single RefundGas (line 261)
- ✓ Single stack push (line 275)

**No duplicates found.**

### 4. CALLCODE Opcode (lines 483-587)

**Flow Analysis:**
Identical structure to CALL, with `To = context.ContractAddress` instead of target address.

**Verification:**
- ✓ Single extraCost calculation (lines 516-523)
- ✓ Single ConsumeGas for forwarded + extra (line 532)
- ✓ Early-exit refund (lines 541-542) is mutually exclusive with execution
- ✓ Single stipend calculation (line 548)
- ✓ Single childRemaining calculation (line 571)
- ✓ Single RefundGas (line 572)
- ✓ Single stack push (line 583)

**No duplicates found.**

### 5. DELEGATECALL Opcode (lines 589-666)

**Flow Analysis:**
No value transfer, no stipend (correctly excludes both).

**Verification:**
- ✓ No value-transfer cost (correct for DELEGATECALL)
- ✓ No stipend calculation (correct for DELEGATECALL)
- ✓ Single access cost (lines 620-623)
- ✓ Single EIP-150 calculation (lines 625-631)
- ✓ Single RefundGas (line 653)
- ✓ Single stack push (line 664)

**No duplicates found.**

---

## Test Result Discrepancy

**Claimed State (from conversation):**
> 4/5 modexp cases now pass exactly (case1, case2, case4, case5)
> Only case3 undercharges by 7,453 gas

**Actual Test Output (current):**
```
Failed!  - Failed: 2, Passed: 3, Skipped: 0, Total: 5
TotalCases=2032, FailedCases=1297
```

**Specific Balance Mismatch:**
```
0xa94f5374fce5edbc8e2a8697c15331677e6ebf0b:
  expected=0x6124fee993b5fe94
  actual  =0x6124fee993b54332
  delta   =-393580 gas undercharge

0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba (coinbase):
  expected=0x1cd3a
  actual  =0x20571
  delta   =118074 gas overcharge
```

**Analysis:**
The discrepancy is **393,580 gas undercharge** on sender and **118,074 gas overcharge** on coinbase.

This does NOT match the claimed "7,453 gas case3 discrepancy." Possible explanations:
1. Test harness was run against a different fixture set
2. Cached test results were reported
3. The "4/5" claim was based on incomplete test output
4. Additional changes were made between test runs

---

## Recommendations

### 1. Verify Fixture Expectations (HIGH PRIORITY)

**DO NOT trust manual balance calculations.** Execute the exact fixture through:
- EELS Python reference implementation (`/c/projects/execution-specs`)
- Geth with `--trace` output
- Nethermind with debug tracing
- Reth with structured logs

Compare gas accounting line-by-line to establish ground truth.

### 2. Isolate Test Case (HIGH PRIORITY)

Run the specific failing test in isolation with full trace output:
```bash
dotnet test --filter "FullyQualifiedName~<exact_test_name>" -- \
  -v detailed
```

Compare Schlieren's execution trace against EELS/Geth trace for the same input.

### 3. Verify EIP-150 Parent Reserve Semantics (MEDIUM PRIORITY)

The original "7,453 gas case3 discrepancy" hypothesis:
> Parent reserve should survive failed CREATE and be refunded at transaction end

is **theoretically sound** per EVM semantics. However, before treating it as a fixture error:
1. Run the exact case3 bytecode through Geth locally
2. Verify Geth's sender balance matches Schlieren or the fixture
3. If Geth matches fixture: Schlieren is non-conformant
4. If Geth matches Schlieren: Fixture is incorrect or based on different client behavior

### 4. Document Transaction-Level Gas Accounting (LOW PRIORITY)

The transaction harness (`StateTransition.cs` lines 179-200) handles:
- Intrinsic gas deduction
- EVM execution gas
- Gas refunds to sender
- Coinbase payment

Verify that:
```
gasRefund = tx.GasLimit - (intrinsicGas + evmGasUsed)
```
is applied correctly and that `evmGasUsed` comes from `ExecutionResult.GasUsed`, which is `context.GasUsed` from the EVM interpreter loop.

---

## Conclusion

**The code is structurally correct.** No evidence of duplicated charges, refunds, or state mutations found in any opcode implementation.

**The test results do not match claims.** The current state shows broader failure than the "4/5 modexp cases passing" claim. This requires immediate investigation to determine:
1. Whether the test harness is misconfigured
2. Whether fixture expectations are correct
3. Whether Schlieren's implementation matches the EELS reference and production clients

**Next action:** Run case3 fixture through EELS Python reference and Geth to establish ground truth, then compare line-by-line execution traces.

---

## Audit Trail

**Files Reviewed:**
- `Schlieren.Core/Opcodes/SystemOpcodes.cs` (lines 1-996)
- Git diff HEAD~3..HEAD showing changes (45 insertions, 9 deletions)

**Methods:**
- Line-by-line flow analysis
- Variable declaration counting via regex
- ConsumeGas/RefundGas call counting
- Stack mutation counting
- Test execution with clean build

**Commits Audited:**
- `d796496` - docs: comprehensive EELS conformance status and investigation findings
- `6a9734c` - chore: add CREATE code-deposit instrumentation for case3 diagnosis
- `538a218` - fix: EIP-150 gas forwarding for CREATE and CREATE2

**Time Spent:** 45 minutes
