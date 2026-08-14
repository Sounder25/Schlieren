# Schlieren — Project Status & Handoff
**Last Updated:** 2026-08-07  
**Current Baseline:** `bac7046` "feat(prague): 99.95% → 100% — EIP-2200 SSTORE reentrancy guard (CALL_STIPEND)"

---

## What Schlieren Is

A .NET 8 Ethereum execution client — EVM + JSON-RPC + CLI + UI — targeting full
Prague conformance against the EELS (Ethereum Execution Layer Specification)
state-test fixture suite.

**Projects:**
| Project | Purpose |
|---|---|
| `Schlieren.Core` | EVM, opcodes, precompiles, state transitions, chain state |
| `Schlieren.RPC` | JSON-RPC server (eth_*, debug_*) |
| `Schlieren.CLI` | Command-line host |
| `Schlieren.UI` | WPF desktop application |
| `Schlieren.Tests` | Unit + integration tests (303 tests) |
| `Schlieren.EELS.Tests` | EELS state-test fixture harness |

---

## Conformance Status ✅

| Suite | Cases | Result |
|---|---|---|
| Prague (v5.4.0 fixtures) | 2,010 | **100%** ✅ |
| Cancun (v5.4.0 fixtures) | 2,032 | **100%** ✅ |
| Unit tests | 303 | **100%** ✅ |

---

## Quick Start

```sh
# Build
dotnet build Schlieren.EELS.Tests/Schlieren.EELS.Tests.csproj -v q

# Unit tests (303 cases)
dotnet test Schlieren.Tests/Schlieren.Tests.csproj

# Full Prague sweep (2,010 cases) — the conformance gate
dotnet test Schlieren.EELS.Tests --settings prague_audit.runsettings --filter "BENCHMARK_TaxonomySnapshot"

# EIP-7702 only (546 cases)
dotnet test Schlieren.EELS.Tests --settings eip7702_audit.runsettings --filter "BENCHMARK_TaxonomySnapshot"

# Single case trace (set EELS_CASE_FILTER env in runsettings)
dotnet test Schlieren.EELS.Tests --settings <foo>.runsettings --filter "SingleCaseTrace"
```

---

## Run Settings (tools/)

| File | Purpose |
|---|---|
| `prague_audit.runsettings` | Full Prague sweep (2,010 cases) — **primary gate** |
| `eels_strict.runsettings` | All forks, all cases |
| `eip7702_audit.runsettings` | EIP-7702 SetCode subset (546 cases) |
| `eip7623_audit.runsettings` | EIP-7623 calldata cost subset |
| `bls_audit.runsettings` | BLS12-381 precompile subset |
| `bls_strict.runsettings` | BLS strict sweep |
| `bls_calltypes.runsettings` | BLS CALL-variant coverage |
| `bls_gas.runsettings` | BLS gas metering |
| `quick_audit.runsettings` | Static/legacy state tests |
| `balance_audit.runsettings` | Refund/balance-focused cases |

---

## EELS Reference Tracer

`tools/eels_loop_trace.py` — runs any fixture through EELS Prague Python and emits a JSONL
gas trace (one `{op, pc, depth, gas}` per opcode). Used to diff against Schlieren's structLog
to find the exact opcode/frame where gas diverges.

```sh
# Requires: pip install eth-keys  (in execution-specs env)
cd C:\projects\execution-specs
python tools/eels_loop_trace.py --fixture <fixture.json> --out trace.jsonl
```

Also available: `tools/eels_fixture_diff.py` and `tools/eels_trace_compare.py`
for broader fixture diffing and two-trace comparison.

---

## Key Files

```
Schlieren.Core/Execution/StateTransition.cs            # Full tx lifecycle, EIP-7702 auth loop
Schlieren.Core/Execution/Precompiles.cs                # 0x01–0x13 precompile dispatch
Schlieren.Core/Opcodes/SystemOpcodes.cs                # CALL/CREATE/DELEGATECALL/STATICCALL/SELFDESTRUCT + EIP-3541/7702
Schlieren.Core/Opcodes/StorageOpcodes.cs               # SLOAD/SSTORE (EIP-2200 guard) / TLOAD/TSTORE
Schlieren.Core/Execution/Bn254Pairing.cs               # BN254 Ate pairing (EIP-197)
Schlieren.EELS.Tests/Harness/EelsStateFixtureExecutor.cs  # LargeStackWorker (32MB stack)
Schlieren.EELS.Tests/Harness/EelsStateFixtureLoader.cs    # Fixture JSON parser + auth IsValid marking
Schlieren.EELS.Tests/Suites/PublishedRequiredStateTests.cs # BENCHMARK_TaxonomySnapshot gate
EELs-NotebookLM/fork-prague.md                       # EELS spec reference (full Prague source)
C:\projects\execution-specs\src\ethereum\forks\prague\ # Live EELS Python implementation
tools/eels_loop_trace.py                             # EELS reference tracer
CONFORMANCE_STATUS.md                                # Detailed per-EIP conformance tracking
```

---

## Critical EVM Pitfalls (Hard-Won)

### 1. EIP-2200 SSTORE Reentrancy Guard
EELS `sstore()`: `if gas_left <= CALL_STIPEND (2300): raise OutOfGasError` — fires **before** any storage read.  
`OpcodeSStore.ExecuteAsync()` checks `gasRemaining <= 2300UL` at entry and returns OOG immediately.  
Without this, recursive CALL loops run extra iterations.

### 2. EIP-3541 Top-Level CREATE (ExceptionalHalt)
When top-level CREATE returns EF-prefixed runtime code (`0xEF...`), this is an `ExceptionalHalt`
in EELS — consume **all** `executionGasLimit`, not just the gas used so far.

### 3. EIP-7702 Nonce Overflow (U64.MAX_VALUE)
`auth.Nonce >= U64.MAX_VALUE` → return `None` **before** `accessed_addresses.add(authority)`.
The signer is **not warmed** when nonce overflows. (Bad signature → still warm but no write.)

### 4. EIP-7702 Auth Loop Order
Per EELS `validate_authorization`:
1. ChainId check → skip (no warm)
2. Nonce overflow check → skip (no warm)
3. `recover_authority()` → `None` on bad sig (no warm)
4. `accessed_addresses.add(authority)` ← warm happens **here**

### 5. EELS Reference Tracing
When a gas delta spans 100+ nested frames, the only reliable method is:
run EELS Python side-by-side and compare opcode traces. The reference tracer
(`tools/eels_loop_trace.py`) captures `(op, pc, depth, gas)` at every `OpStart`.

---

## Session History

| Commit | Change |
|---|---|
| `56f3d74` | Cancun conformance milestone (SELFDESTRUCT, BLOBHASH, EIP-6780/7610) |
| `...` | BN254 Ate pairing (EIP-197) full implementation |
| `...` | Prague EIP-2537 BLS12-381 precompiles |
| `...` | Prague EIP-7623 calldata cost |
| `6ac392f` | Prague EIP-7702: 93.6% → 99.85% (CALLCODE/DELEGATECALL delegation, type-4 validation, EF-prefix, auth parsing) |
| `14535d2` | Prague EIP-7702: 99.85% → 99.95% (nonce overflow warm guard, EF-prefix ExceptionalHalt gas, nonce U64 check) |
| `bac7046` | Prague: 99.95% → **100%** (EIP-2200 SSTORE reentrancy guard `gas_left ≤ 2300`) |
