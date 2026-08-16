# Campaign Expansion: 27 → 137 → Differential Testing at Scale

**Date:** August 16, 2026  
**Branch:** `codex/gas-rule-inventory`  
**Status:** Foundation complete, ready for oracle-driven expansion

---

## What We Fixed

### Precompile Coverage Bug
**Before:** 29 precompile cases generated → 4 survived deduplication  
**After:** 29 precompile cases generated → 29 preserved  

**Root cause:** Deduplication was grouping by `CaseId` (human-readable string) assembled *before* optional fields (PrecompileTarget, GasLimit) were set. Cases with different precompiles but identical base dimensions got the same ID and were deduplicated.

**Fix:** Implemented `GetCanonicalFingerprint()` that includes ALL semantic dimensions:
```csharp
Fork | Type | Target | PrecompileTarget | Access | Value | 
Behavior | Result | ReturnSize | Depth | GasLimit
```

Deduplication now operates on canonical fingerprints. CaseId remains human-readable metadata.

---

## Current State

### Base Campaign Matrix: 137 Cases
- **44** baseline (depth 2, zero value, core semantics)
- **29** precompile variations (PRE1–PRE9, all preserved)
- **27** value transfer scenarios (1 wei, boundaries, 1 ETH)
- **27** depth variations (3, 4, 5)
- **12** storage pattern tests (cold→warm transitions)
- **9** gas boundary conditions
- **6** exotic behaviors (SELFDESTRUCT, CREATE, LOG)

### New: Interaction Matrix Generator
Skeleton created for **~100 high-value interaction cases**:

#### State Modification in Read-Only Context
- STATICCALL → SSTORE (should revert)
- STATICCALL → LOG (should revert)
- STATICCALL → SELFDESTRUCT (should revert)
- STATICCALL → CREATE (should revert)
- STATICCALL with value (should fail at call site)

#### Value Transfer Edge Cases
- CALL(value) → insufficient balance
- CALL(value) → nonexistent account (creates + transfers)
- CALL(value) → empty account (resurrects)
- CALL(value) → precompile (Identity/ModExp accept, others reject)

#### Nested Call Failures
- CALL → child OOG (parent continues, success=0, empty returndata)
- CALL → child REVERT (state rolled back, returndata available)
- DELEGATECALL → child REVERT (parent storage unchanged)

#### Creation Lifecycle
- CALL → CREATE → REVERT in init code (no deployment, gas consumed)
- CALL → CREATE2 → REVERT (address not reserved)
- CALL → CREATE2 collision (returns zero address)

#### Storage + Revert Interactions
- SSTORE → REVERT (rolled back, gas consumed)
- 3× SSTORE → REVERT (cold→warm gas charged, all rolled back)
- Parent SSTORE → CALL → child SSTORE → child REVERT (selective rollback)

#### Returndata Boundaries
- 0, 31, 32, 33, 255, 256, 257-byte returns
- CALL → REVERT(32) → RETURNDATACOPY (revert reason extraction)

#### DELEGATECALL / CALLCODE Context Semantics
- DELEGATECALL → SSTORE (writes to caller storage)
- DELEGATECALL → SELFDESTRUCT (destroys caller, not callee)
- DELEGATECALL → BALANCE (reads caller balance)
- DELEGATECALL → CALLVALUE (gets caller msg.value)
- CALLCODE variations (deprecated but must work)

---

## Architecture: Campaign Separation

Created focused campaign structure to prevent "call semantics" becoming a dumping ground:

```
Schlieren.Tests/Campaigns/
├── CallSemanticsMatrixGenerator.cs    (core call semantics, 137 cases)
├── InteractionMatrixGenerator.cs      (interaction semantics, ~100 cases)
├── Precompiles/                       (precompile-specific edge cases)
├── GasBoundaries/                     (N-1/N/N+1 boundary testing)
├── StorageTransitions/                (SSTORE refund rules, cold→warm)
├── Returndata/                        (size boundaries, RETURNDATACOPY edge cases)
├── CreateLifecycle/                   (CREATE/CREATE2 collisions, init code)
├── RevertRollback/                    (selective rollback, nested reverts)
├── ExceptionalHalts/                  (OOG, invalid opcodes, stack limits)
└── Generated/                         (seeded differential generation)
```

Each campaign is self-contained with its own generator, test harness, and expected-outcome definitions.

---

## Oracle Integration: REVM 42.x Subprocess

**Built:** `oracle/revm-harness/target/release/revm-harness.exe` (1.9 MB, revm 42.0.1)  
**Transport:** stdin/stdout JSON  
**Contract:** `JsonExecutionCase` (input) → `JsonExecutionFingerprint` (output)

### Test JSON
```json
{
  "fork": "CANCUN",
  "caller": "0x01...",
  "target": "0xbb...",
  "calldata": "0x",
  "value": "0x0",
  "gas_limit": 10000000,
  ...
}
```

### Result JSON
```json
{
  "success": true,
  "gas_used": 23206,
  "refund": 0,
  "return_data": "0x",
  "frames": [],
  "logs": [],
  "state_diff": {...}
}
```

**Status:** Validated with single test case. Ready for batch differential testing.

---

## Next Steps

### 1. Wire REVM Oracle to Test Runner
Create `DifferentialTestHarness`:
```csharp
foreach (var testCase in cases)
{
    var schlierenResult = ExecuteSchlieren(testCase);
    var revmResult = ExecuteRevm(testCase);
    
    if (!ResultsMatch(schlierenResult, revmResult))
    {
        SaveDivergence(testCase, schlierenResult, revmResult);
        Assert.Fail($"Divergence in {testCase.CaseId}");
    }
}
```

### 2. Add N-1 / N / N+1 Gas Boundary Generation
For each operation:
- Required gas - 1 (should OOG)
- Required gas (should succeed)
- Required gas + 1 (should succeed)

Target operations:
- 63/64ths forwarding
- 2300 stipend
- Cold/warm account access
- Cold/warm storage
- Memory expansion
- CALL base cost
- Value transfer surcharge
- New account surcharge
- CREATE/CREATE2
- SSTORE transitions

### 3. Implement Seeded Differential Generation
```
Seed (uint64)
  ↓
Deterministic Case Generator
  ├─ Random but reproducible prestate
  ├─ Random but reproducible bytecode
  └─ Random but reproducible tx parameters
  ↓
Execute: REVM vs Schlieren
  ↓
Agreement? → Next seed
Divergence? → Persist reproducer + metadata
```

**Goal:** 5,000+ deterministic cases with automatic divergence capture.

### 4. Divergence Persistence Schema
Every mismatch saves:
- `seed` (uint64, for reproduction)
- `case.json` (JsonExecutionCase input)
- `fork` (Berlin/London/Shanghai/Cancun)
- `revm_result.json` (JsonExecutionFingerprint from oracle)
- `schlieren_result.json` (JsonExecutionFingerprint from Schlieren)
- `difference` (structured diff: gas delta, state delta, log delta)
- `bytecode` (hex)
- `prestate` (accounts, storage, balances)

Stored in `Campaigns/Generated/divergences/<seed>.json`.

Becomes the **hardening corpus** — every historical divergence is a regression test.

---

## Metrics

| Metric | Round 6 | Current | Target |
|--------|---------|---------|--------|
| **Test cases** | 27 | 137 | 5,000+ |
| **Precompile coverage** | 0 | 29 (all) | 29 × N variations |
| **Gas boundary tests** | 1 (OOG) | 9 | ~200 (N-1/N/N+1 per operation) |
| **Interaction tests** | 0 | 0 (defined) | ~100 |
| **Semantic density** | Medium | Medium | High |
| **Oracle validation** | Manual | Ready | Automated |
| **Divergence corpus** | N/A | 0 | Auto-growing |

---

## Why This Matters

### Before
- **27 hand-crafted cases** with manual expected outcomes
- Precompiles missing entirely
- Gas boundaries: 1 OOG case
- Interactions: minimal
- Expansion bottleneck: writing assertions

### After
- **137 validated base cases** with canonical deduplication
- **29 precompile cases** preserved (PRE1–PRE9)
- **~100 interaction cases** defined (not yet wired)
- **Oracle ready** for differential testing
- Expansion path: **seeded generation → 5,000+ cases** with zero manual assertions

### The Real Win
**REVM oracle unlocks scaling from 137 → 5,000+ without writing 5,000 assertions.**

Every case is:
1. Deterministically generated from a seed
2. Executed by both Schlieren and REVM
3. Automatically compared
4. Divergences persisted as regression tests

This is **systematic EVM hardening**, not a larger unit-test suite.

---

## Commands

```bash
# Count current cases
dotnet test --filter "FullyQualifiedName~MatrixCountTests"

# Run base campaign (Schlieren only, no oracle)
dotnet test --filter "FullyQualifiedName~CallSemanticsCampaignTests"

# Test REVM oracle (single case)
cat oracle/test-case.json | oracle/revm-harness/target/release/revm-harness.exe

# Run differential test (TODO: implement)
dotnet test --filter "FullyQualifiedName~DifferentialCampaignTests"

# Generate 1000 seeded cases and validate (TODO: implement)
dotnet test --filter "FullyQualifiedName~GeneratedCampaignTests" -- --seed-count 1000
```

---

## Files Modified

- `Schlieren.Tests/Campaigns/CallSemanticsMatrixGenerator.cs` — canonical fingerprinting, 137 cases
- `Schlieren.Tests/Campaigns/InteractionMatrixGenerator.cs` — NEW, interaction semantics skeleton
- `Schlieren.Tests/Campaigns/MatrixCountTests.cs` — NEW, validation of case counts
- `Schlieren.Tests/Campaigns/Models/JsonContract.cs` — stable JSON I/O for oracle (from earlier session)
- `oracle/revm-harness/` — REVM 42.x subprocess oracle (built, tested, ready)

---

## Bottom Line

**Precompile bug fixed. Interaction semantics defined. Oracle ready. Path to 5,000+ deterministic cases clear.**

Next session: wire the oracle, add N-1/N/N+1 gas boundaries, implement seeded generation, watch the divergence corpus grow.
