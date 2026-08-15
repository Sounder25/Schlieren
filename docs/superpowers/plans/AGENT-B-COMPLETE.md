# Agent B (Hermes) — RPC Backend Complete ✅

**Branch:** `codex/gas-rule-inventory`  
**Commits:** `f4298ad`, `38f7f8b`  
**Status:** All steps complete, all tests green

---

## Summary

Agent B implemented the RPC backend for Schlieren's inspection system, adding enhanced trace fields and the new `debug_inspect` endpoint.

---

## Step B1: Add Enhanced Fields to debug_trace* ✅

**Commit:** `f4298ad` - feat(rpc): add gasCostDec, contract, caller, callType, output to debug_trace*

### What Was Added

Added 5 new fields to `structLogs` in all `debug_trace*` methods:
- `gasCostDec` — decimal string representation of gas cost
- `contract` — address of contract being executed
- `caller` — address of the caller (msg.sender)
- `callType` — type of call frame (Root, Call, DelegateCall, etc.)
- `output` — return data from CALL/CREATE opcodes

### Implementation

**Files Modified:**
- `Schlieren.RPC/Handlers/EthHandlers.cs`
  - Updated `BuildTraceResponse()` to include new fields
  - Updated `BuildTraceResponseFromStored()` to include new fields
  - Added `ParseHexGasCostAsDecimal()` helper

**Tests:**
- Enhanced `DebugTraceAdvancedRpcTests.cs` with field assertions
- All 9 DebugTrace* tests pass
- All 54 RPC tests pass

### Verification

```bash
dotnet test Schlieren.Tests --filter "FullyQualifiedName~DebugTrace" --nologo -v q
# Passed: 9/9

dotnet test Schlieren.Tests --filter "FullyQualifiedName~RPC" --nologo -v q
# Passed: 54/54
```

### Notes

- Additive only — old Geth traces still work
- Fields map directly from `ExecutionTraceStep` properties
- Backward compatible with existing clients

---

## Step B2: Register debug_inspect Endpoint ✅

**Commit:** `38f7f8b` - feat(rpc): add debug_inspect endpoint

### What Was Added

Implemented the new `debug_inspect` RPC method that calls `InspectionAssembler.FromCanonical` and returns:
- Execution result (success, gas, error)
- Enhanced trace with all B1 fields
- Gas tree with hierarchical breakdown
- Causal diagnosis with PROVEN/STRONG/POSSIBLE grades

### Implementation

**Files Modified:**
- `Schlieren.RPC/Handlers/EthHandlers.cs`
  - Added `HandleDebugInspect()` method
  - Parses fork, mismatches, expectException, expectedReceiptSuccess
  - Builds `InspectRequest` and calls `InspectionAssembler.FromCanonical`
  
- `Schlieren.RPC/Server/RpcRouter.cs`
  - Registered `debug_inspect` in method list
  - Added route to `HandleDebugInspect`

**Tests Created:**
- `Schlieren.Tests/RPC/DebugInspectRpcTests.cs`
  - `DebugInspect_ReturnsInspectResultWithDiagnosis` — verifies structure
  - `DebugInspect_HandlesReverts` — verifies failure diagnosis
  - `DebugInspect_RespectsForkParameter` — verifies fork selection

### Verification

```bash
dotnet test Schlieren.Tests --filter "FullyQualifiedName~DebugInspect" --nologo -v q
# Passed: 3/3

dotnet test Schlieren.Tests --filter "FullyQualifiedName~RPC" --nologo -v q
# Passed: 57/57 (54 existing + 3 new)
```

### API Example

Request:
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "debug_inspect",
  "params": [{
    "from": "0x1000000000000000000000000000000000000001",
    "to": null,
    "data": "0x6000",
    "gas": "0x186a0",
    "gasPrice": "0xa",
    "fork": "Prague",
    "mismatches": [
      "balance mismatch for 0xf6c3a9edc1afa0ad5b720e4d42e1437c43d3b3ff: expected=0xf4240, actual=0xa6040"
    ]
  }]
}
```

Response:
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "ok": true,
    "fork": "Prague",
    "execution": {
      "success": true,
      "error": "None",
      "gasUsed": "0x5208",
      "gasLimit": "0x186a0",
      "refundCounter": "0x0",
      "returnValue": "0x"
    },
    "trace": {
      "structLogs": [
        {
          "pc": 0,
          "op": "PUSH1",
          "gas": "0x186a0",
          "gasCost": "0x3",
          "gasCostDec": "3",
          "depth": 1,
          "stack": [],
          "memory": [],
          "storage": {},
          "contract": "0x...",
          "caller": "0x...",
          "callType": "Root",
          "output": null
        }
      ]
    },
    "gasTree": {
      "label": "TX",
      "gas": 21000,
      "totalGas": 21003,
      "children": []
    },
    "diagnosis": {
      "fingerprint": "TX.CREATE_SURCHARGE",
      "firstPhase": "intrinsic",
      "root": {
        "ruleId": "TX.CREATE_SURCHARGE",
        "title": "CREATE transaction intrinsic gas surcharge",
        "grade": "PROVEN",
        "score": 100,
        "phase": "intrinsic",
        "why": "CREATE transactions pay 32,000 gas upfront",
        "proof": "to == null",
        "consequences": "Sender pays 32k before execution starts",
        "likelyFix": "This is correct protocol behavior",
        "codeBoundary": "StateTransition.cs",
        "protocolRule": "EIP-2",
        "gasDelta": 32000
      },
      "candidates": []
    }
  }
}
```

---

## Step B3: Verify debug_whyNot ✅

**Status:** No changes needed

### Verification

```bash
dotnet test Schlieren.Tests --filter "DisplayName~WhyNot" --nologo -v q
# Passed: 3/3
```

All existing `debug_whyNot` tests pass without modification.

---

## Step B4: Full Debug* Suite Report ✅

### Final Test Results

```bash
dotnet test Schlieren.Tests --filter "FullyQualifiedName~Debug" --nologo -v q
# Passed: 15/15
```

**Test Breakdown:**
- DebugTrace* tests: 9/9 ✅ (with enhanced field assertions)
- DebugWhyNot tests: 3/3 ✅
- DebugInspect tests: 3/3 ✅ (new)

**Total RPC Tests:** 57/57 ✅

---

## Dependencies Verified

✅ **InspectionAssembler exists** — `Schlieren.Core/Execution/Inspect/InspectionAssembler.cs`  
✅ **InspectRequest exists** — `Schlieren.Core/Execution/Inspect/InspectRequest.cs`  
✅ **InspectDtos exist** — `Schlieren.Core/Execution/Inspect/InspectDtos.cs`  
✅ **All Agent A work (A3) is present**

---

## Next Steps

Backend is ready for frontend integration. The RPC layer now provides:

1. **Enhanced traces** (`debug_trace*`) with `gasCostDec`, `contract`, `caller`, `callType`, `output`
2. **Inspection endpoint** (`debug_inspect`) with execution + trace + gasTree + diagnosis
3. **Backward compatibility** — all existing RPC clients still work

Frontend can now POST to `debug_inspect` and receive:
- Full execution trace with enhanced fields (B1)
- Hierarchical gas tree
- Causal diagnosis with PROVEN/STRONG/POSSIBLE grades (B2)

---

## Commits

1. **f4298ad** - feat(rpc): add gasCostDec, contract, caller, callType, output to debug_trace* (B1)
2. **38f7f8b** - feat(rpc): add debug_inspect endpoint (B2)

All steps complete. Ready to merge to main.
