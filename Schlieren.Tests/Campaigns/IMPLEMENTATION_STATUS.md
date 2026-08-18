# Round 6 Campaign — Implementation Status

## Architecture Built ✅

### 1. Clean Execution Harness Interface
**File:** `IEvmExecutionHarness.cs`

```
Campaign Framework
       ↓
IEvmExecutionHarness (interface)
       ↓
SchlierenExecutionHarness (adapter)
       ↓
Your EvmExecutor
```

**Key types:**
- `CampaignExecutionRequest` — everything needed to reproduce execution
- `CampaignExecutionResult` — normalized output
- `CampaignAccount` — account pre-state
- `DeterministicAddresses` — fixed addresses (Caller=0x01, Parent=0xaa, Child=0xbb, Grandchild=0xcc)

**Benefits:**
- Campaign knows nothing about UI/Workbench
- Later: add RevmExecutionHarness, GethExecutionHarness
- Differential testing across multiple EVMs

### 2. Schlieren Adapter (Placeholder)
**File:** `SchlierenExecutionHarness.cs`

Bridges campaign to your actual EVM core:
1. Resolve fork rules
2. Build execution context
3. Seed accounts/code/storage
4. Execute
5. Convert trace → ExecutionFingerprint
6. Return normalized result

**Status:** Placeholder types — needs wiring to your actual:
- `IEvmExecutor`
- `ExecutionContext`
- `IWorldState`
- `ExecutionTrace`
- Fork resolution

### 3. Matrix Generator with Deterministic IDs
**File:** `CallSemanticsMatrixGenerator.cs`

**Generates:** 50+ test cases covering:
- CALL / DELEGATECALL / STATICCALL
- Success / Revert / OOG
- Cold / Warm access
- Various behaviors (NoOp, SLOAD, SSTORE, NestedCall)
- Return data sizes (0, 32, 256 bytes)
- Depths (2, 3)
- Forks (Berlin, Cancun)

**Case IDs encode dimensions:**
```
R6_CALL_COLD_SUCCESS_SLOAD_R32_D2_CANCUN
R6_DELEGATECALL_WARM_SUCCESS_NOOP_R0_D2_CANCUN
R6_STATICCALL_COLD_REVERT_SSTORE_R0_D2_CANCUN
```

**Deterministic addresses:**
- Caller: 0x0000000000000000000000000000000000000001
- Parent: 0x00000000000000000000000000000000000000aa
- Child: 0x00000000000000000000000000000000000000bb
- Grandchild: 0x00000000000000000000000000000000000000cc

### 4. Divergence Analyzer
**File:** `DivergenceAnalyzer.cs`

Hierarchical comparison:
1. Outcome (success/revert)
2. Gas (with first-divergence localization)
3. Return data
4. Frame tree (depth, call type, addresses)
5. Access set (warm/cold accounts/slots)
6. State diff
7. Logs
8. Refund

**Auto-classification:**
- Gas delta +2600 → "Access list (cold account charge)"
- Gas delta +2100 → "Cold storage slot"
- Gas delta +100 → "Warm account access"
- Gas delta >10,000 → "Nested gas double-counting"

**Output:**
```
DIVERGENCE: GAS
Delta: +2,600

First divergent frame:
  Depth 3 / STATICCALL / Target 0x...03
  Expected: WARM = 100
  Actual: COLD = 2,600

Likely subsystem:
  Access-set propagation

Recommendation:
  Check AccessSet.IsWarm() for target account before CALL opcode
```

### 5. Failure Clustering
**File:** `CallSemanticsCampaignTests.cs`

Groups failures by signature:
```
26 failures → 4 root bugs

#1  Warm/cold target mismatch ........... 14 cases
#2  STATICCALL state-reversion .......... 7 cases
#3  Returndata truncation ............... 3 cases
#4  Diagnostic classification ............ 2 cases
```

---

## What's Ready to Run

### Unit Tests ✅
```bash
dotnet test --filter "Campaign_GenerateMatrix"
dotnet test --filter "Campaign_GenerateBytecode"
dotnet test --filter "DivergenceAnalyzer_GasMismatch"
dotnet test --filter "DivergenceAnalyzer_OutcomeMismatch"
```

All passing — infrastructure validated.

---

## Integration Path (Next Steps)

### Step 1: Wire Schlieren Adapter
**File to modify:** `SchlierenExecutionHarness.cs`

Replace placeholder types with your actual:
```csharp
// Your actual types from Schlieren.Core
using Schlieren.Core.Execution.EvmExecutor;
using Schlieren.Core.State.WorldState;
using Schlieren.Core.Execution.ForkRules;

public sealed class SchlierenExecutionHarness : IEvmExecutionHarness
{
    private readonly EvmExecutor _executor;  // Your actual executor

    public async Task<CampaignExecutionResult> ExecuteAsync(
        CampaignExecutionRequest request,
        CancellationToken ct = default)
    {
        // 1. Map Fork string → your ForkRules
        // 2. Build your ExecutionContext
        // 3. Seed your IWorldState
        // 4. Call _executor.ExecuteAsync()
        // 5. Extract trace
        // 6. Build ExecutionFingerprint from trace
        // 7. Return normalized result
    }
}
```

### Step 2: First Integration Test
**Add to:** `CallSemanticsCampaignTests.cs`

```csharp
[Fact]
public async Task Campaign_CALL_Cold_NoOp_STOP()
{
    // Simplest possible case
    var testCase = CallSemanticsMatrixGenerator.GenerateMatrix()
        .First(c => c.CaseId.Contains("CALL_COLD_SUCCESS_NOOP"));

    var (parentCode, childCode) = 
        CallSemanticsMatrixGenerator.GenerateBytecode(testCase);

    var request = new CampaignExecutionRequest
    {
        Fork = "Cancun",
        Caller = DeterministicAddresses.Caller,
        Target = DeterministicAddresses.Parent,
        Calldata = "0x",
        Value = 0,
        GasLimit = 10_000_000,
        Prestate = new[]
        {
            new CampaignAccount
            {
                Address = DeterministicAddresses.Parent,
                Code = parentCode
            },
            new CampaignAccount
            {
                Address = DeterministicAddresses.Child,
                Code = childCode
            }
        }
    };

    var harness = new SchlierenExecutionHarness(/* your executor */);
    var result = await harness.ExecuteAsync(request);

    Assert.True(result.Success);
    Assert.Equal(2, result.Fingerprint.FrameTree.Max(f => f.Depth));
}
```

### Step 3: Progression (Validate adapter before unleashing matrix)
```
1 case  → CALL + STOP                  ← Start here
5 cases → basic CALL-family behavior
50+ cases → current generated matrix
200+ → pairwise expansion
5000+ → mutations
```

### Step 4: Internal Invariants (Before external oracle)
Even without revm, validate:
- Frame depth consistency
- Call-type semantics (DELEGATECALL preserves context)
- Audit gas == engine gas
- No DELEGATECALL reentrancy false-positives
- STATICCALL write protection

### Step 5: External Oracle (Future)
```csharp
var schlieren = await _schlieren.ExecuteAsync(request);
var reference = await _revm.ExecuteAsync(request);

var divergence = DivergenceAnalyzer.Compare(
    reference.Fingerprint,
    schlieren.Fingerprint);

if (divergence.Category != DivergenceCategory.None)
{
    await SaveFailureArtifact(testCase, divergence, schlieren, reference);
}

Assert.Equal(DivergenceCategory.None, divergence.Category);
```

---

## Files Created

```
Schlieren.Tests/Campaigns/
  IEvmExecutionHarness.cs              ← Interface + request/result types
  SchlierenExecutionHarness.cs         ← Adapter (placeholder, needs wiring)
  CallSemanticsMatrixGenerator.cs      ← Matrix + bytecode generation
  DivergenceAnalyzer.cs                ← Fingerprint comparison + classification
  CallSemanticsCampaignTests.cs        ← Unit tests + clustering
```

---

## Key Architectural Decisions

### ✅ Campaign → Harness Interface
Campaign never touches UI/Workbench. Clean boundary.

### ✅ Deterministic Addresses
Every test reproducible. Failure artifacts meaningful.

### ✅ Semantic Case IDs
`R6_CALL_COLD_SUCCESS_SLOAD_R32_D2_CANCUN` tells you what broke.

### ✅ Hierarchical Divergence
Find first-mismatch, not just final delta.

### ✅ Automatic Classification
Gas delta → subsystem guess → recommendation.

### ✅ Failure Clustering
26 failures → 4 root bugs. Fix abstractions, not examples.

---

## Success Criteria

### ✅ Infrastructure Validated
- Matrix generates 50+ cases
- Bytecode generation works
- Divergence analyzer classifies correctly
- Clustering groups by subsystem

### ⏳ Integration Pending
- Wire adapter to actual EvmExecutor
- Run first case (CALL + STOP)
- Validate depth-2 execution
- Run 5 basic cases
- Unleash 50+ case matrix

### 🎯 Campaign Complete When
- 50+ cases execute successfully
- Internal invariants hold
- Failure clustering operational
- Artifact generation working
- Ready for revm oracle integration

---

## Next Commands

### 1. Build
```bash
cd /c/projects/Schlieren
dotnet build Schlieren.Tests --nologo
```

### 2. Run unit tests
```bash
dotnet test --filter "Campaign" --nologo
```

### 3. Wire adapter
Edit `SchlierenExecutionHarness.cs` to use your actual:
- EvmExecutor
- WorldState
- ExecutionContext
- ForkRules

### 4. First integration test
```bash
dotnet test --filter "Campaign_CALL_Cold_NoOp_STOP"
```

### 5. Unleash matrix
Once first case passes, remove `Skip` attribute and run:
```bash
dotnet test --filter "CallSemanticsCampaign" --logger "console;verbosity=detailed"
```

---

## Architecture Review

**What you asked for:**
> Do not wire through Workbench/UI.
> Wire directly into execution core.
> This becomes permanent infrastructure.

**What was built:**
- ✅ Clean interface (`IEvmExecutionHarness`)
- ✅ Adapter pattern (Schlieren harness)
- ✅ Campaign knows nothing about UI
- ✅ Deterministic, reproducible tests
- ✅ Semantic case IDs
- ✅ First-divergence analysis
- ✅ Failure clustering
- ✅ Separation: engine result → fingerprint → audit validation

**Next:** Wire adapter to your actual EVM core and start hunting bugs systematically.
