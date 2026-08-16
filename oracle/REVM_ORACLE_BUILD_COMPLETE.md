# REVM Oracle Harness — Build Complete

**Date:** August 16, 2026  
**Status:** ✅ Working  
**Binary:** `oracle/revm-harness/target/release/revm-harness.exe` (1.9 MB)

## What We Built

A **thin Rust wrapper over revm 42.x** that implements Schlieren's stable JSON contract for oracle-based differential testing.

### Architecture

```
Schlieren.Tests.Campaigns/
    Models/JsonContract.cs          ← Stable schema (ExecutionCase → ExecutionResult)
    RevmExecutionHarness.cs         ← C# subprocess wrapper (TBD)

oracle/revm-harness/
    src/main.rs                     ← Rust adapter: JSON stdin → revm 42 → JSON stdout
    Cargo.toml                      ← Pinned to revm = "42", alloy-primitives = "1.6"
    target/release/revm-harness.exe ← Compiled binary (1.9 MB)
```

**Contract:**
- Input: `ExecutionCase` JSON (pre-state, tx, block, fork) via stdin
- Output: `ExecutionResult` JSON (success, gas, logs, state diff) via stdout
- No TTY, no interactive, pure stdin/stdout transport

**Schlieren owns the schema.** The Rust side is a dumb adapter from our schema to whatever revm API version we pin.

## API Fixes for revm 42.x

1. **Imports reorganized:**
   - `primitives::SpecId` → `primitives::hardfork::SpecId`
   - `primitives::AccountInfo` → `state::AccountInfo`
   - `primitives::Bytecode` → `state::Bytecode`

2. **BlockEnv field renames:**
   - `coinbase` → `beneficiary`
   - `gas_limit` / `basefee` → `u64` (was `U256`)

3. **Execution result changes:**
   - `gas_used()` deprecated → use `tx_gas_used()`
   - `gas_refunded()` removed (set to 0 for now)

4. **Fork name changes:**
   - `SpecId::PARIS` → `SpecId::MERGE`
   - `SpecId::CONSTANTINOPLE` removed (use `PETERSBURG`)

5. **AccountInfo structure:**
   - Added `account_id` field (set to `Default::default()`)

6. **alloy-primitives version:**
   - Must match revm's version: `1.6` (not `0.8`)

## Test Execution

**Input (test-case.json):**
```json
{
  "fork": "CANCUN",
  "caller": "0x01",
  "target": "0xbb",
  "calldata": "0x",
  "value": "0x0",
  "gas_limit": 10000000,
  "prestate": [
    {
      "address": "0xbb",
      "code": "0x6000600055",  // PUSH1 0, PUSH1 0, SSTORE
      "balance": "0x0"
    },
    {
      "address": "0x01",
      "balance": "0xde0b6b3a7640000"  // 1 ETH
    }
  ]
}
```

**Output:**
```json
{
  "success": true,
  "gas_used": 23206,
  "refund": 0,
  "return_data": "0x",
  "state_diff": {
    "0xbb": {"balance": "0x0", "nonce": 0, ...},
    "0x01": {"balance": "0xde0b6b3a7607584", "nonce": 1, ...}
  }
}
```

✅ **Execution successful, gas matches expected (~23K for SSTORE cold).**

## Next Steps

### 1. Wire C# Harness Wrapper

Create `RevmExecutionHarness.cs`:
```csharp
public class RevmExecutionHarness : IEvmExecutionHarness
{
    private readonly string _binaryPath;
    
    public async Task<CampaignExecutionResult> ExecuteAsync(
        CampaignExecutionRequest request,
        CancellationToken ct = default)
    {
        // 1. Convert request to ExecutionCase JSON
        // 2. Spawn revm-harness.exe subprocess
        // 3. Write JSON to stdin
        // 4. Read JSON from stdout
        // 5. Parse ExecutionResult
        // 6. Convert to CampaignExecutionResult
    }
}
```

### 2. Add Inspector for Frame Tree

Current limitation: `frames` array is empty because we're not using revm's inspector.

To get full call tree:
- Wire `revm-inspectors` crate
- Use `TracingInspector` to capture CALL/DELEGATECALL/STATICCALL frames
- Extract frame tree from inspector state

### 3. Differential Test Integration

Wire into existing campaign framework:
```csharp
var schlieren = new SchlierenExecutionHarness();
var revm = new RevmExecutionHarness("oracle/revm-harness/target/release/revm-harness.exe");

var resultA = await schlieren.ExecuteAsync(testCase);
var resultB = await revm.ExecuteAsync(testCase);

var divergences = DivergenceAnalyzer.Compare(resultA.Fingerprint, resultB.Fingerprint);
```

### 4. CI Integration

**PR validation:**
- Run 27 deterministic cases against both harnesses
- Fail if any divergence found

**Nightly:**
- Expand to 200+ cases
- Generate divergence report

## Files Created

- `Schlieren.Tests/Campaigns/Models/JsonContract.cs` (stable schema)
- `oracle/revm-harness/Cargo.toml` (dependency manifest)
- `oracle/revm-harness/src/main.rs` (Rust adapter, 328 lines)
- `oracle/test-case.json` (sample test input)

## Rust Toolchain

- **Rust version:** 1.97.1 (installed via rustup)
- **Cargo location:** `C:/Users/Erick/.cargo/bin/cargo.exe`
- **Build command:** `cargo build --release` (took ~3 min first build, ~3s incremental)

## Deployment

Binary is self-contained (statically linked). Can be:
- Checked into git at `oracle/revm-harness/bin/revm-harness.exe`
- Rebuilt in CI from source
- Distributed as part of premium Hermes feature (oracle validation)

---

## Bottom Line

**Oracle harness is functional.** Rust subprocess executes revm 42.x, honors our JSON contract, returns correct execution results.

**Next session:** Wire C# wrapper, run first differential against Schlieren, expand test matrix.
