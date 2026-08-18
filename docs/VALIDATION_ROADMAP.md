# SCHLIEREN Validation Roadmap

## Current State (2026-01-16)

✅ **Framework complete** — DifferentialRegressionRunner with 8 invariants  
✅ **Bug #3 + #4 fixed** — Gas double-count, DELEGATECALL false-positive  
⏳ **Golden corpus** — 3/4 cases need bytecode extraction  

## Phase 1: Golden Corpus Green (Next 24 Hours)

**Goal:** `dotnet test --filter GoldenCorpus` passes on every commit.

### Tasks
1. **Extract bytecode from muscle/ traces:**
   - R1: `muscle/round1-tokenlib-trace.json` → library guard bytecode
   - R4: `muscle/proxy-empty-calldata.json` → proxy runtime
   - R5: `muscle/round5-weth-proxy-prestate.json` → proxy + impl code

2. **Complete expected outputs:**
   ```csharp
   ExpectedMaxDepth = 2,
   ExpectedReturnData = "0x0000000000000000000000000000000000000000000000000000000000000000"
   ```

3. **Run until green:**
   ```bash
   dotnet test --filter GoldenCorpus --logger "console;verbosity=detailed"
   ```

4. **Add to CI** — every PR must pass golden corpus.

**Outcome:** Manual Rounds 1-5 become permanent regression guards.

---

## Phase 2: Reference EVM Oracle (High Leverage)

**Goal:** Automatically derive expectations from `revm` / `geth` instead of hand-coding them.

### Architecture

```
Input: contract + calldata + pre-state
   |
   +--> SCHLIEREN ──┐
   |                |
   +--> revm    ────┼──> Normalizer ──> Diff Engine ──> Classifier
   |                |
   +--> geth    ────┘
```

### Normalizer
Convert each EVM's output to canonical format:
```csharp
public sealed class NormalizedResult
{
    public bool Success { get; init; }
    public ulong GasUsed { get; init; }
    public byte[] ReturnData { get; init; }
    public Dictionary<string, string> StorageDiff { get; init; }
    public List<NormalizedLog> Logs { get; init; }
    public List<NormalizedFrame> CallTree { get; init; }
}

public sealed class NormalizedFrame
{
    public int Depth { get; init; }
    public CallType Type { get; init; }
    public string Target { get; init; }
    public ulong GasIn { get; init; }
    public ulong GasOut { get; init; }
    public bool Success { get; init; }
}
```

### Diff Engine
Compare normalized outputs field-by-field:
```csharp
public enum DivergenceCategory
{
    None,               // Perfect match
    GasOnly,            // Success + returndata match, gas differs
    StateDivergence,    // Storage writes differ
    OutcomeDivergence,  // Success/revert mismatch
    ReturnDataDivergence,
    CallTreeDivergence, // Different external calls
    LogDivergence,
    RefundDivergence
}
```

### Classifier
Automatic root-cause attribution:
```csharp
public sealed class DivergenceReport
{
    public DivergenceCategory Category { get; init; }
    public string FirstMismatch { get; init; }  // "Depth 2 / DELEGATECALL / PC 0x015C"
    public long DeltaGas { get; init; }
    public string LikelyCause { get; init; }    // "Nested gas double-count"
    public string RecommendedAction { get; init; } // "Check parent CALL gasCost attribution"
}
```

### Integration

**Option A: CLI wrapper around revm**
```bash
revm-runner --input input.json --output output.json
```

**Option B: Rust FFI to revm** (faster, tighter integration)
```csharp
[DllImport("schlieren_revm")]
extern static IntPtr revm_execute(byte[] input);
```

**Option C: Standalone revm HTTP service** (easiest initially)
```bash
POST /execute
{
  "bytecode": "0x...",
  "calldata": "0x...",
  "accounts": [...],
  "fork": "Cancun"
}
```

### Outcome
Stop hand-coding expectations. Let revm be the oracle:

```csharp
[Theory]
[InlineData("0x602a60005260206000f3", "")] // Minimal return
[InlineData("0x600160015500", "")]         // SSTORE
public async Task Differential_AgainstRevm(string bytecode, string calldata)
{
    var schlierenResult = await RunSchlieren(bytecode, calldata);
    var revmResult = await RunRevm(bytecode, calldata);
    
    var divergence = DiffEngine.Compare(schlierenResult, revmResult);
    
    Assert.Equal(DivergenceCategory.None, divergence.Category);
}
```

---

## Phase 3: Automated Mutation & Fuzzing

**Goal:** Generate thousands of cases automatically and only manually inspect divergences.

### Mutation Engine
For every golden case, generate a family:
```csharp
public static IEnumerable<RegressionCase> MutateCase(RegressionCase seed)
{
    // Change calldata selector
    yield return seed with { Calldata = MutateSelector(seed.Calldata) };
    
    // Warm vs cold access
    yield return seed with { PreState = AddWarmSlot(seed.PreState) };
    
    // Different fork
    yield return seed with { Fork = "Berlin" };
    yield return seed with { Fork = "London" };
    
    // Empty vs non-empty storage
    yield return seed with { PreState = ClearStorage(seed.PreState) };
    
    // Revert vs success paths
    yield return seed with { Calldata = TriggerRevert(seed.Calldata) };
}
```

**R5 (nested DELEGATECALL) → 20+ variants:**
- Child returns success
- Child reverts
- Child OOG
- Target account empty
- Target warm vs cold
- Nested DELEGATECALL → CALL
- DELEGATECALL → STATICCALL
- Different fork rules (Berlin, London, Cancun, Prague)

### Corpus Generator
Pull real contracts from Etherscan/mainnet:
```csharp
var topProxies = await EtherscanClient.GetTopProxies(limit: 100);
var topDeFi = await EtherscanClient.GetContractsByTag("DeFi", limit: 100);

foreach (var contract in topProxies.Concat(topDeFi))
{
    var code = await EtherscanClient.GetBytecode(contract.Address);
    var traces = await EtherscanClient.GetRecentTraces(contract.Address, limit: 10);
    
    foreach (var trace in traces)
    {
        yield return new RegressionCase
        {
            Name = $"Mainnet_{contract.Name}_{trace.TxHash[..8]}",
            ContractCode = code,
            Calldata = trace.Input,
            PreState = trace.StateAccess
        };
    }
}
```

### Overnight Run
```bash
schlieren-validate --corpus mainnet-proxies --oracle revm --output report.html
```

**Output:**
```
12,482 cases

Perfect match:    12,461 (99.8%)
Gas-only delta:        18 (0.1%)
State divergence:       2 (0.02%)
Outcome mismatch:       1 (0.008%)

Failure artifacts saved:
  artifacts/failures/divergence_20260116_235901/
    18 gas-only cases
    2 state divergence traces
    1 outcome mismatch (CRITICAL)
```

Only manually inspect the 21 failures, not all 12,482 cases.

---

## Phase 4: Protocol Diagnostician (Future Vision)

The same differential machinery becomes a general-purpose EVM divergence analyzer.

**Use cases beyond SCHLIEREN validation:**

### A. User transaction debugging
```bash
schlieren diagnose --tx 0xabcd1234... --fork Cancun
```

**Output:**
```
DIVERGENCE DETECTED

Expected (mainnet):
  Success
  Gas: 42,000

Actual (simulation):
  Revert
  Gas: 38,521
  Error: EvmError::OutOfGas

First mismatch:
  Depth 2 / CALL to 0x1234... / PC 0x02F1

Likely cause:
  EIP-150 gas forwarding rule violation.
  Parent forwarded 63/64 × remaining, child needed more.

Recommendation:
  Check CALL argument stack at step 47.
  Verify gasleft() >= 38,522 before external call.
```

### B. Fork implementation validation
```bash
schlieren compare --client geth --client erigon --corpus eip-tests
```

Validates two client implementations against each other + reference.

### C. Smart contract auditing
```bash
schlieren audit MyContract.sol --oracles revm,geth --scenarios critical-paths.json
```

Differential execution across multiple EVMs catches:
- Gas optimization bugs
- Fork-specific behavior
- State transition edge cases

---

## Summary

**Immediate (This Week):**
1. ✅ Framework complete
2. ⏳ Golden corpus green
3. ⏳ Add to CI

**High Leverage (Next):**
4. Integrate revm as reference oracle
5. Normalize + diff + classify
6. Auto-save divergence artifacts

**Scale (Future):**
7. Mutation families from every bug
8. Mainnet corpus generator
9. Overnight fuzzing runs
10. Protocol diagnostician for any EVM

**Result:**  
Manual Rounds stop → Automated differential begins → Scale to thousands of cases → Become a general EVM divergence analyzer.

The framework you built in the last 24 hours becomes the foundation for systematic EVM correctness validation.
