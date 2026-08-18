# Round 6: Call Semantics & Frame Integrity Campaign

## Goal

Attack the entire call subsystem systematically instead of testing one contract at a time.

**Why now:** Round 5 proved nested frames work. Nested execution is where most subtle EVM bugs hide.

---

## Test Matrix (Pairwise Combinations → 200-500 Cases)

| Dimension | Values |
|-----------|--------|
| **Call type** | `CALL`, `DELEGATECALL`, `STATICCALL`, `CALLCODE` |
| **Child result** | success, revert, out-of-gas |
| **Target state** | code-present, empty account, nonexistent |
| **Access warmth** | cold, warm |
| **Value transfer** | 0, non-zero (1 wei) |
| **Child behavior** | no-op, SLOAD, SSTORE, LOG, nested CALL |
| **Returndata size** | 0, 1, 31, 32, 33, 256 bytes |
| **Call depth** | 1, 2, 3, 5 |
| **Memory expansion** | none, small (32), large (1024) |
| **Fork** | Berlin, London, Shanghai, Cancun |

**Do NOT multiply exhaustively** — that's 100,000+ cases immediately.

Use **pairwise testing** to cover interactions efficiently.

---

## Example Test Case

```
CASE R6-0047

Fork: Cancun
Call type: CALL
Target: cold, code-present
Value: 0 wei
Child behavior: SLOAD slot 0, RETURN 32 bytes
Return size: 32 bytes
Depth: 2
Memory expansion: none

Expected:
  Success: true
  Gas: 24,821 (21,000 + 2,600 cold + 2,100 SLOAD + 15 RETURN + 106 overhead)
  ReturnData: 0x0000...0000 (32 bytes)
  StateDiff: none
  Logs: none
```

---

## Execution Fingerprint (Hierarchical Comparison)

Compare structured results, not just final gas:

```json
{
  "outcome": "success",
  "gasUsed": 24821,
  "returnData": "0x0000...0000",
  "refund": 0,
  "logs": [],
  "stateDiff": {},
  
  "frameTree": [
    {
      "depth": 1,
      "callType": "Root",
      "codeAddress": "0x...aa",
      "contextAddress": "0x...aa",
      "caller": "0x...01",
      "value": "0",
      "gasProvided": 50000,
      "gasConsumed": 24821,
      "success": true
    },
    {
      "depth": 2,
      "callType": "Call",
      "codeAddress": "0x...bb",
      "contextAddress": "0x...bb",
      "caller": "0x...aa",
      "value": "0",
      "gasProvided": 47195,
      "gasConsumed": 2221,
      "success": true,
      "returnData": "0x0000...0000"
    }
  ],
  
  "accesses": {
    "coldAccounts": ["0x...bb"],
    "warmAccounts": ["0x...aa"],
    "coldSlots": [],
    "warmSlots": []
  }
}
```

---

## First-Divergence Analysis

When SCHLIEREN != revm, don't read 4,000 trace steps.

**Find the first mismatch:**

```
DIVERGENCE: GAS

Final:
  Expected: 44,821
  Actual:   47,421
  Delta:    +2,600

First divergent frame:
  Depth: 3
  Call type: STATICCALL
  Target: 0x...03

Expected target access: WARM = 100
SCHLIEREN: COLD = 2,600

Likely subsystem:
  Access-set propagation across nested frames

Affected fork: Berlin+
EIP: EIP-2929 (Berlin gas changes)
```

---

## Failure Clustering

**Don't fix 26 individual tests. Find 4 root bugs.**

### Example Campaign Result

```
Campaign: R6_CallSemantics
Total cases: 237
Passed: 211
Failed: 26

Clusters:
  #1  Warm/cold target mismatch ........... 14 cases
  #2  STATICCALL state-reversion mismatch . 7 cases
  #3  Returndata truncation ............... 3 cases
  #4  Diagnostic classification ............ 2 cases

Action: Fix 4 subsystems, not 26 examples.
```

### Cluster Signature

```
CLUSTER A: Access State Propagation

Failures: 14
Common first divergence: CALL-family target access charge
Forks: Berlin, London, Shanghai, Cancun
Common pattern: Target previously touched in ancestor frame

Likely subsystem:
  AccessSet / TransactionWarmState

Test cases:
  R6-0047, R6-0053, R6-0081, ...
  
Fix location:
  Schlieren.Core/State/AccessSet.cs
  
Expected impact:
  All 14 cases should turn green after fix.
```

---

## Structured Mutations (After Deterministic Matrix Passes)

Take each passing case and mutate one property:

**Seed case:**
```
CALL → child SLOAD → RETURN 32 bytes
```

**Mutations (single-property changes):**
```
CALL        → STATICCALL
CALL        → DELEGATECALL
slot 0      → random slot
cold        → warm
return 32   → return 31
return 32   → return 33
gas 50k     → gas 2300
success     → REVERT (change child to revert)
depth 2     → depth 3
value 0     → value 1 wei
Berlin      → London
```

**Why structured:** When `cold → warm` causes +2,500 gas divergence, you know exactly what broke.

---

## Frame Model Refinement

Round 5 exposed semantic ambiguity. Formalize:

```csharp
public sealed class ExecutionFrame
{
    // Identity
    public int Depth { get; init; }
    public CallType Type { get; init; }
    
    // Addresses
    public Address CodeAddress { get; init; }       // Where code lives
    public Address ContextAddress { get; init; }    // Storage/balance context
    public Address Caller { get; init; }
    
    // Call parameters
    public UInt256 Value { get; init; }
    public byte[] Input { get; init; }
    
    // Gas
    public ulong GasProvided { get; init; }
    public ulong GasConsumed { get; init; }
    
    // Result
    public bool Success { get; init; }
    public byte[] ReturnData { get; init; }
}
```

**Semantics:**

| CallType | CodeAddress | ContextAddress |
|----------|-------------|----------------|
| **CALL** | child | child |
| **DELEGATECALL** | implementation | proxy |
| **STATICCALL** | child | child (read-only) |
| **CALLCODE** | child | caller |

This distinction enables correct:
- Gas attribution
- Security analysis (reentrancy, delegation)
- Diagnostic classification (proxy, library)
- Trace export

---

## Three Testing Speeds

### 1. PR Validation (Fast)
```
Cases: 100-300
Time: <2 minutes
Scope: Golden corpus + smoke tests
Runs: Every commit
```

### 2. Nightly Campaign (Deep)
```
Cases: 5,000-50,000
Oracle: revm + geth
Features:
  - Generated matrix
  - Mutations
  - Failure clustering
  - First-divergence analysis
  - Artifact generation
Runs: Every night
```

### 3. Weekly Deep Campaign (Exhaustive)
```
Cases: 50,000+
Scope:
  - Cross-fork validation
  - Large mutation corpus
  - Real mainnet state
  - Long execution chains (depth 10+)
  - New compiler patterns
  - Edge-case fuzzing
Runs: Weekly
```

---

## Implementation Checklist

### Phase 1: Matrix Generator
- [ ] Build call-case matrix (200-500 pairwise combinations)
- [ ] Generate minimal bytecode for each case programmatically
- [ ] Define expected behavior (from spec or revm)

### Phase 2: Execution Fingerprint
- [ ] Normalize SCHLIEREN output
- [ ] Normalize revm output
- [ ] Build hierarchical comparison (outcome → gas → frames → accesses)
- [ ] Implement first-divergence locator

### Phase 3: Failure Clustering
- [ ] Extract failure signatures
- [ ] Cluster by common pattern
- [ ] Generate cluster reports with likely subsystem
- [ ] Link to test case IDs

### Phase 4: Frame Model Refinement
- [ ] Add `CodeAddress` + `ContextAddress` to ExecutionFrame
- [ ] Update trace export
- [ ] Update security detectors (use ContextAddress for reentrancy)
- [ ] Update diagnostic detectors (use CodeAddress + ContextAddress for proxy)

### Phase 5: Mutation Engine
- [ ] Single-property mutations for passing cases
- [ ] Generate 10-20 variants per seed
- [ ] Semantic labeling (what changed)

### Phase 6: Integration
- [ ] Add R6 campaign to test suite
- [ ] Configure nightly run
- [ ] Set up failure artifact storage
- [ ] Build cluster dashboard

---

## Success Criteria

### Campaign Passes When:
1. ✅ **211/237 deterministic cases pass** (90%+)
2. ✅ **All failures clustered into <10 root bugs**
3. ✅ **First-divergence analysis auto-generated**
4. ✅ **No false-positive security findings on legitimate DELEGATECALL**
5. ✅ **Frame model correctly distinguishes CodeAddress vs ContextAddress**

### Long-Term Success:
- **1,000+ mutation variants** pass after deterministic matrix green
- **Weekly mainnet corpus** (real contracts) runs without new failures
- **Cross-fork validation** (Berlin/London/Cancun/Prague) consistent

---

## Anti-Patterns to Avoid

### ❌ Don't Fix Examples
```csharp
if (selector == 0x0c55699c)  // NO
```

### ✅ Fix Abstractions
```csharp
// Update AccessSet.PropagateToChildFrame()
```

### ❌ Don't Fix 26 Individual Bugs
### ✅ Find 4 Root Causes, Fix Subsystems

### ❌ Don't Compare Only Final Results
### ✅ Use First-Divergence Analysis

---

## Expected Bug Families

Based on nested execution complexity:

1. **Access-set propagation** — warm/cold state across frames
2. **STATICCALL write protection** — state modification detection
3. **Gas forwarding** — EIP-150 63/64 rule at depth
4. **Returndata handling** — size mismatches, RETURNDATACOPY bounds
5. **Frame stack integrity** — depth limits, context preservation
6. **CREATE in nested context** — nonce handling, collision
7. **Refund accounting** — SSTORE refunds across revert boundaries
8. **Diagnostic classification** — DELEGATECALL vs CALL vs real reentrancy

---

## Outcome

**Round 6 is not one contract.**

**Round 6 is 200-500 deterministic cases attacking call semantics.**

**Result:**
- Subsystem-level validation
- Failure clustering reveals root bugs
- First-divergence analysis accelerates triage
- Mutation testing prevents regressions
- Frame model refinement enables correct semantics

**After Round 6:** SCHLIEREN's call subsystem is systematically validated. Move to next campaign (CREATE lifecycle, precompiles, memory semantics, etc.).
