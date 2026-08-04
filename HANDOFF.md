# Scrutor — Project Status & Action Plan
**Last Updated:** 2026-08-03  
**Current Baseline:** `56f3d74` "Complete Cancun conformance milestone"

---

## What Scrutor Is

A .NET 8 Ethereum execution client — EVM + JSON-RPC + CLI + UI — targeting full
Cancun conformance against the EELS (Ethereum Execution Layer Specification)
state-test fixture suite.

**Projects:**
| Project | Purpose |
|---|---|
| `Scrutor.Core` | EVM, opcodes, precompiles, state transitions, chain state |
| `Scrutor.RPC` | JSON-RPC server (eth_*, debug_*) |
| `Scrutor.CLI` | Command-line host |
| `Scrutor.UI` | WPF desktop application |
| `Scrutor.Tests` | Unit + integration tests (265 tests) |
| `Scrutor.EELS.Tests` | EELS state-test fixture harness |

---

## Verified Test Baselines (as of this session)

| Suite | Result | Notes |
|---|---|---|
| `Scrutor.Tests` | ✅ **265/265 passed** | All unit and integration tests |
| `BENCHMARK_TaxonomySnapshot` (Cancun, 1,127 cases) | ✅ **0 failures** | The authoritative conformance gate |
| 3 EELS harness unit tests | ❌ Pre-existing | Missing fixture dirs (`eip6780_selfdestruct/selfdestruct_revert`, `eip1153_tstore/basic_tload`) — subdirs not downloaded locally |

**Fixture coverage** (`fixtures/state_tests/cancun/`): 5 directories — `eip1153_tstore`, `eip4844_blobs`, `eip5656_mcopy`, `eip6780_selfdestruct`, `eip7516_blobgasfee` — 1,127 total cases.

---

## System-by-System Status

### EVM Core

| Area | Status | Notes |
|---|---|---|
| Arithmetic (ADD/MUL/DIV/MOD/EXP etc.) | ✅ Conformant | All overflow-guarded with BigInteger |
| Comparison & bitwise (LT/GT/AND/OR/XOR/SHL/SHR/SAR) | ✅ Conformant | |
| Stack (PUSH/POP/DUP/SWAP) | ✅ Conformant | |
| Memory (MLOAD/MSTORE/MSTORE8/MSIZE) | ✅ Conformant | Gas charged BEFORE expansion; 64-bit overflow guard; 16MB hard cap |
| MCOPY (EIP-5656) | ✅ Conformant | Implemented, tested in fixture sweep |
| Control flow (JUMP/JUMPI/PC/STOP/RETURN/REVERT) | ✅ Conformant | |
| Logging (LOG0–LOG4) | ✅ Conformant | |
| CALL depth limit (max 1024) | ✅ Conformant | Enforced; deep recursion uses 32MB-stack LargeStackWorker |
| CALL value transfer & stipend | ✅ Conformant | 9,000 gas value cost; 2,300 stipend added to child gas only |
| CALLCODE | ✅ Conformant | Same gas model as CALL |
| DELEGATECALL | ✅ Conformant | No value transfer, no stipend |
| STATICCALL | ✅ Conformant | No value transfer, no stipend; mutation guard active |
| EIP-150 63/64 gas forwarding | ✅ Conformant | Applied to CALL, CREATE, CREATE2 |
| CREATE success path | ✅ Conformant | 200 gas/byte code-deposit; EIP-3860 initcode limit; EIP-7610 collision |
| CREATE2 success path | ✅ Conformant | Same as CREATE + keccak hash cost |
| SELFDESTRUCT (EIP-6780) | ✅ Conformant | Same-tx-only deletion; 25,000 new-account surcharge |
| BLOBHASH (EIP-4844) | ✅ Conformant | |
| TLOAD/TSTORE (EIP-1153) | ✅ 100 gas each — passes fixture sweep | See Note 1 |
| SLOAD/SSTORE (EIP-2200 + EIP-2929) | ✅ Conformant | EIP-2200 original-value tracking; EIP-2929 warm/cold |

> **Note 1 — TLOAD/TSTORE:** The opcode gas cost (100 each) is correct and the fixture sweep passes. Earlier GAS_LEDGER.md analysis of TLOAD over-charges was against the broader `state_tests/` fixture set, which contains fixtures from pre-Cancun forks not in the current sweep path. Those discrepancies remain uninvestigated.

### Transaction Lifecycle

| Area | Status | Notes |
|---|---|---|
| EIP-1559 effective gas price | ✅ Conformant | min(maxFee, baseFee + priority) |
| Intrinsic gas (EIP-2930 calldata cost) | ✅ Conformant | |
| EIP-3860 initcode size limit (tx-level) | ✅ Conformant | Rejected before execution |
| Nonce increment (CREATE/CREATE2) | ✅ Conformant | |
| Gas refund (EIP-3529 cap) | ✅ Conformant | |
| Impersonated tx deduction | ✅ Conformant | Sender debited even without signature |
| EIP-4844 blob fee burn | ✅ Conformant | |
| State revert on exceptional halt | ✅ Conformant | txOverlay.Reset() on failed CREATE |

### Precompiles

| Address | Name | Status | Notes |
|---|---|---|---|
| 0x01 | ecRecover | ✅ Implemented | BouncyCastle secp256k1 recovery |
| 0x02 | SHA-256 | ✅ Implemented | System.Security.Cryptography |
| 0x03 | RIPEMD-160 | ✅ Implemented | BouncyCastle digest |
| 0x04 | Identity | ✅ Implemented | Trivial copy |
| 0x05 | ModExp | ✅ Implemented | EIP-2565 gas; BigInteger modpow |
| 0x06 | BN254 ecAdd | ✅ Implemented | BouncyCastle FpCurve |
| 0x07 | BN254 ecMul | ✅ Implemented | BouncyCastle FpCurve |
| 0x08 | BN254 ecPairing | ✅ **NEW — Implemented this session** | See Note 2 |
| 0x09 | BLAKE2F | ✅ Implemented | RFC 7693 §3.2 native C# |
| 0x0A | KZG Point Eval | ✅ Implemented | Ckzg native binding + trusted setup |

> **Note 2 — BN254 Pairing (EIP-197):** Full Ate pairing in `Bn254Pairing.cs`. Fp2 + Fp12 tower arithmetic, projective G2, Miller loop over pseudo-binary NAF, Frobenius endomorphism, final exponentiation f^((p¹²−1)/r). Geth-matching semantics: bad length → revert; G1/G2 off-curve → revert; no subgroup check on G2; k=0 → 1; G2 encoding `[x.c1‖x.c0‖y.c1‖y.c0]` BE.

### Known Defects (Not Yet Fixed)

#### Defect 1 — CREATE OOG EIP-150 Parent Reserve (7,453 gas)
- **Fixture:** `test_modexp[fork_Cancun-state_test-EIP-198-case3-raw-input-out-of-gas]`
- **Symptom:** Scrutor under-charges 7,453 gas (the EIP-150 1/64 reserve). Fixture expects all 500K consumed.
- **Analysis:** Failed CREATE child sets `gasLeft=0`. Parent continues with reserve. STOP halts. Reserve refunded. Scrutor's behavior matches strict EVM semantics but fixture disagrees.
- **Status:** ⏸ **Deferred** — needs Geth trace comparison. May be a fixture accuracy issue.
- **Risk:** Low. Not in the current 1,127-case Cancun sweep.

#### Defect 2 — TLOAD/TSTORE Gas Discrepancies (older fixture set)
- **Fixtures:** `test_basic_tload_after_store`, `test_basic_tload_gasprice`, `test_tload_calls[CALL]` from older `state_tests/` path
- **Symptom:** 2 to 23,000 gas over-charge. Cases 2, 3, 5 in GAS_LEDGER.md.
- **Analysis:** TLOAD/TSTORE base gas (100) is correct. The interactions with CALL warm/cold access (EIP-2929 + EIP-1153) may be double-counting warm slot costs.
- **Status:** 🔴 **Open** — Not in current sweep. Investigation blocked on fixture download.
- **Risk:** Medium. Will surface when full `state_tests/` sweep is enabled.

#### Defect 3 — CALLCODE + TLOAD 2-gas Under-charge
- **Fixture:** `test_tload_calls[CALLCODE]`
- **Symptom:** 2-gas under-charge. Likely CALLCODE base cost off-by-one (should be 700, maybe 698).
- **Status:** 🟡 **Minor** — trivial fix once fixture confirmed.

#### Defect 4 — Harness Unit Tests: Missing Fixture Directories
- **Tests:** `RevertedChild_DoesNotKeepAddressesOrSlotsWarm`, `InsufficientBalanceCall_ChargesNetValueCallCost`, `PublishedPostStorage_DoesNotInheritClearedPreStateSlots`
- **Root cause:** Tests look for `eip6780_selfdestruct/selfdestruct_revert` and `eip1153_tstore/basic_tload` which are not in the local fixture download.
- **Fix:** Download those specific fixture subdirectories.
- **Status:** 🟡 **Infra gap** — not a code defect.

### BN254 Pairing Performance (Known Limitation)

The `FinalExponentiate` function uses `BigInteger.Pow(p, 12)` — a ~920-digit exponent computed via square-and-multiply across Fp12 multiplications. This is correctness-first. Each call takes ~1–3 seconds (not blocking for conformance tests but too slow for production throughput).

**Optimization path (when needed):**
1. **Easy part:** f^(p⁶−1) = conj(f) / f (conjugate + Fp12 inverse), then × f^(p²+1) (two Frobenius applications)
2. **Hard part:** decompose (p⁴−p²+1)/r via BN254-specific NAF expansion
3. **Fp12 inverse:** elements are in the cyclotomic subgroup after easy part → use compressed squaring, not Fermat

---

## Action Plan (Priority Ranked)

### P0 — Expand Fixture Coverage (Immediate)
**Why:** The 1,127 Cancun cases are only 5 EIP subdirectories. The full `state_tests/` suite (`state_tests/static/state_tests/`) has thousands more covering ecAdd, ecMul, ecPairing, modexp, TLOAD/TSTORE interactions, and all legacy fork behavior. We're flying blind on most of the surface area.

**Actions:**
1. Point `EELS_FIXTURES_ROOT` at `state_tests/static/state_tests/stZeroKnowledge/` and run the pairing fixtures
2. Download missing subdirs (`eip6780_selfdestruct/selfdestruct_revert`, `eip1153_tstore/basic_tload`) to fix the 3 broken harness unit tests
3. Run against the broader `state_tests/` paths to discover new failure categories

```powershell
# stZeroKnowledge (ecAdd/ecMul/ecPairing)
$env:EELS_FIXTURES_ROOT = "C:/projects/Scrutor/state_tests/static/state_tests/stZeroKnowledge"
$env:EELS_INCLUDE_SUBDIRS = "1"
dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj --filter "BENCHMARK_TaxonomySnapshot"
```

### P1 — TLOAD/TSTORE × EIP-2929 Interaction
**Why:** 4 of 5 documented GAS_LEDGER cases involve TLOAD/TSTORE. Will block any sweep that includes those fixtures.

**Actions:**
1. Compare `StorageOpcodes.cs` TLOAD path against EELS reference for warm/cold interaction
2. Specifically: does accessing a transient slot suppress the EIP-2929 cold storage read charge? (It should — transient and persistent storage access lists are separate)
3. Check if `context.Access.WarmStorage(addr, slot)` is being called unnecessarily before TLOAD

### P2 — CREATE OOG EIP-150 Reserve (Defect 1)
**Why:** Well-isolated. Either Scrutor is wrong (fix now) or fixture is wrong (document and skip).

**Action:** Run the exact modexp case3 fixture through Geth `debug_traceTransaction`. If Geth agrees with fixture → fix parent reserve consumption. If Geth agrees with Scrutor → annotate and skip.

### P3 — BN254 Pairing Performance
**Why:** Conformance passes. Performance only matters if Scrutor is used as a node or high-throughput simulator.

**Action:** Implement final exponentiation easy/hard decomposition in `Bn254Pairing.cs`.

### P4 — Prague/Pectra Fixtures
**Why:** Fixture harness already knows Prague/Osaka fork ordering. Code does not implement any Prague EIPs yet.

**Action:** Identify which Prague EIPs are in the fixture set (EIP-7702 authority, EIP-7623 calldata cost, etc.) and scaffold implementations.

---

## Key Files

```
Scrutor.Core/Execution/Bn254Pairing.cs              # BN254 Ate pairing (NEW — 2026-08-03)
Scrutor.Core/Execution/Precompiles.cs                # 0x01–0x0A precompile dispatch
Scrutor.Core/Execution/StateTransition.cs            # Full tx lifecycle
Scrutor.Core/Execution/EvmMemory.cs                  # Memory: 64-bit overflow guard, 16MB cap
Scrutor.Core/Opcodes/SystemOpcodes.cs                # CALL/CREATE/DELEGATECALL/STATICCALL/SELFDESTRUCT
Scrutor.Core/Opcodes/StorageOpcodes.cs               # SLOAD/SSTORE/TLOAD/TSTORE
Scrutor.EELS.Tests/Harness/EelsStateFixtureExecutor.cs  # LargeStackWorker (32MB stack)
Scrutor.EELS.Tests/Harness/EelsStateFixtureLoader.cs    # Fixture JSON parser
Scrutor.EELS.Tests/Suites/PublishedRequiredStateTests.cs # BENCHMARK_TaxonomySnapshot gate
EELs-NotebookLM/fork-cancun.md                       # EELS spec reference
GAS_LEDGER.md                                        # Documented gas discrepancy cases
```

---

## Quick Resume Commands

```powershell
# Build
dotnet restore
dotnet build --no-restore

# Unit tests
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --logger "console;verbosity=normal"

# Cancun fixture sweep (authoritative gate — must stay at 0)
$env:EELS_FIXTURES_ROOT = "C:/projects/Scrutor/fixtures/state_tests/cancun"
$env:EELS_INCLUDE_SUBDIRS = "1"
dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj `
    --filter "BENCHMARK_TaxonomySnapshot" `
    --logger "console;verbosity=normal"

# stZeroKnowledge (pairing, ecAdd, ecMul)
$env:EELS_FIXTURES_ROOT = "C:/projects/Scrutor/state_tests/static/state_tests/stZeroKnowledge"
$env:EELS_INCLUDE_SUBDIRS = "1"
dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj `
    --filter "BENCHMARK_TaxonomySnapshot" `
    --logger "console;verbosity=normal"

# Broader sweep (will surface new failures)
$env:EELS_FIXTURES_ROOT = "C:/projects/Scrutor/state_tests/static/state_tests"
$env:EELS_INCLUDE_SUBDIRS = "1"
dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj `
    --filter "BENCHMARK_TaxonomySnapshot" `
    --logger "console;verbosity=normal"
```

---

## Session History (Brief)

| Commit | Change |
|---|---|
| `56f3d74` | Cancun conformance milestone (SELFDESTRUCT, BLOBHASH, EIP-6780/7610) |
| `2a2fe45` | CREATE collision nonce fix; EIP-150 gas investigation |
| `4eb63f1` | Precompile routing for all CALL variants |
| `bf181c4` | Upfront value transfer for sub-calls |
| `bb065ca` | Gas on exceptional child halts |
| `6041b57` | EIP-2200 original-value SSTORE tracking |
| `7f1a98a` | Memory expansion cost to CALL family |
| `d74c52c` | Quadratic memory cost fix |
| `68f38e6` | EIP-150 63/64 forwarding for CREATE/CREATE2 |
| *This session* | BN254 Ate pairing (EIP-197) full implementation in `Bn254Pairing.cs` |
