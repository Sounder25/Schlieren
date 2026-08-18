# Differential Regression Testing Framework

## Philosophy

Manual testing discovers bugs once. Automated regression prevents them forever.

This framework turns every discovered defect into a permanent guard by:
1. Encoding the failing case as a golden corpus test
2. Validating execution + intelligence invariants automatically
3. Saving complete failure artifacts for triage
4. Running on every build/CI pass

## Architecture

```
RegressionCase (input)
    ↓
DifferentialRegressionRunner
    ↓
Execute with SCHLIEREN
    ↓
8 Automatic Invariants
    ↓
Pass / Fail + Artifacts
```

## Invariants Checked

### Execution Correctness
1. **Success/Revert** — outcome matches expected
2. **Exact Gas** — gas usage matches expected value
3. **Max Depth** — call depth matches (detects missing/extra frames)
4. **Return Data** — returndata bytes match exactly

### Accounting Correctness
5. **Trace Gas = Audit Gas** — catches double-counting (Bug #3)
6. **No Nested Gas Double-Count** — parent CALL overhead vs child execution

### Intelligence Correctness
7. **DELEGATECALL ≠ Reentrancy** — proxy execution not flagged (Bug #4)
8. **Diagnostic Count** — expected diagnostic findings
9. **Reentrancy Count** — expected security findings

## Golden Corpus

Hand-picked "known hard" contracts that exposed real bugs:

| Round | Contract | Bug Caught | Status |
|-------|----------|------------|--------|
| **R1** | TokenLib (0x4EBF...254a) | False-positive storage recommendation | ⏳ Needs bytecode |
| **R4** | EIP-1967 Proxy (empty impl) | Diagnostic vs vulnerability confusion | ⏳ Needs bytecode |
| **R5** | EIP-1967 Proxy (real impl) | **Bug #3:** Gas double-count (+2,279)<br>**Bug #4:** DELEGATECALL false-positive reentrancy (33 findings) | ⏳ Needs bytecode |
| **Smoke** | Minimal return | Framework validation | ✅ Ready |

## Failure Artifacts

When a test fails, the runner auto-saves:

```
artifacts/failures/
  R5_SuccessfulDelegatecall_20260816_143027.trace.json
  R5_SuccessfulDelegatecall_20260816_143027.summary.txt
```

**Summary format:**
```
REGRESSION FAILURE: R5_SuccessfulDelegatecall
Status: NestedGasDoubleCounting

Gas double-count detected: trace=28,279, audit=30,558, delta=+2,279

Execution:
  Gas Used: 28,279
  Steps: 123
  Max Depth: 2

Expected:
  Gas: 28,279
  Max Depth: 2
  Diagnostics: 0
  Reentrancy: 0

Artifacts:
  Full trace: artifacts/failures/R5_...trace.json
```

## Usage

### Run All Golden Tests
```bash
dotnet test --filter "GoldenCorpus"
```

### Run Specific Round
```bash
dotnet test --filter "Round5"
```

### Add New Case
```csharp
[Fact]
public async Task Round6_YourBugHere()
{
    var testCase = new RegressionCase
    {
        Name = "Round6_YourBugHere",
        ContractCode = "0x...",
        ContractAddress = "0x...",
        Calldata = "0x...",
        
        // Execution
        ExpectedSuccess = true,
        ExpectedGas = 42000,
        ExpectedMaxDepth = 2,
        ExpectedReturnData = "0x0000...0000",
        
        // Intelligence
        ExpectedDiagnosticCount = 0,
        ExpectedReentrancyCount = 0
    };

    var result = await DifferentialRegressionRunner.RunCaseAsync(testCase);
    Assert.Equal(RegressionStatus.Pass, result.Status);
}
```

## Next Steps

### Immediate (Today)
1. ✅ Framework complete
2. ⏳ Extract bytecode from `muscle/` traces for R1, R4, R5
3. ⏳ Populate `ExpectedMaxDepth` + `ExpectedReturnData` for all cases
4. ⏳ Run `dotnet test --filter GoldenCorpus` until green
5. ⏳ Add to CI pipeline

### Future Expansion

#### More Invariants
- STATICCALL cannot mutate state
- DELEGATECALL preserves storage address
- DELEGATECALL preserves msg.sender
- Warm/cold access accounting
- Refund accounting
- Log event validation

#### Reference Comparison
Compare SCHLIEREN against:
- `revm` (Rust EVM)
- `py-evm` / EELS (Python reference)
- `geth debug_traceCall` (mainnet oracle)

Divergence → automatic classification → issue filed

#### Corpus Expansion
```
/corpus
  /dispatch     — function selector edge cases
  /storage      — SLOAD/SSTORE patterns
  /calls        — CALL/DELEGATECALL/STATICCALL
  /create       — CREATE/CREATE2 + collisions
  /proxies      — EIP-1967, minimal, beacon
  /guards       — library context, reentrancy locks
  /precompiles  — ecrecover, sha256, modexp, etc.
  /real-mainnet — actual deployed contracts
```

#### Mutation Testing
For every bug, generate a family:
- **R5 → 20 variants:**
  - DELEGATECALL child returns
  - DELEGATECALL child reverts
  - DELEGATECALL child OOG
  - Target empty/warm/cold
  - Nested DELEGATECALL → CALL
  - DELEGATECALL → STATICCALL
  - etc.

One bug discovery → 20 regression tests

## Workflow Change

### Before
```
Run contract
Inspect trace
Notice bug
Reason manually
Patch
Rerun
Repeat forever
```

### After
```
Generate case
Run: dotnet test --filter GoldenCorpus
Pass → done
Fail → artifacts saved → triage → fix → commit
```

**Manual testing stops.**  
**Systematic regression begins.**

## Status Summary

**Framework:** ✅ Complete  
**Bug #3 detector:** ✅ Gas double-count invariant  
**Bug #4 detector:** ✅ DELEGATECALL reentrancy invariant  
**Failure artifacts:** ✅ Auto-saved on mismatch  
**Golden corpus:** ⏳ 3/4 cases need bytecode  
**CI integration:** ⏳ Pending corpus completion  

---

**Result:** 24 hours of manual debugging → permanent automated guards.
