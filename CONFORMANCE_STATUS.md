# Schlieren EELS Conformance Status

**Last Updated:** 2026-08-24
**Commit:** `d77dfe2` (tag: `schlieren-eels-100`) — EELS conformance baseline
**Phase 0 credential repair:** Task 3 (`security: externalize harvest credentials`) — see `docs/security/JOURNAL_SECURITY_EVIDENCE.md`
**Fixture Source:** `ethereum/execution-specs` — `tests@v20.0.1` (Jul 2, 2026)

---

## Summary — 100% Across All Forks

| Suite | Fixture Source | Cases | Passing | Status |
|---|---|---|---|---|
| **Osaka** | tests@v20.0.1 | 14,516 | 14,516 | ✅ **100%** |
| **Prague** | tests@v20.0.1 | 6,811 | 6,811 | ✅ **100%** |
| **Cancun** | tests@v20.0.1 | 4,514 | 4,514 | ✅ **100%** |
| **Shanghai** | tests@v20.0.1 | 4,969 | 4,969 | ✅ **100%** |
| **Paris** | tests@v20.0.1 | ~3,000 | ~3,000 | ✅ **100%** |
| **London** | tests@v20.0.1 | ~3,000 | ~3,000 | ✅ **100%** |
| **Berlin** | tests@v20.0.1 | ~2,742 | ~2,742 | ✅ **100%** |
| **Istanbul** | tests@v20.0.1 | ~2,075 | ~2,075 | ✅ **100%** |
| **Byzantium** | tests@v20.0.1 | ~1,845 | ~1,845 | ✅ **100%** |
| **Constantinople** | tests@v20.0.1 | ~1,600 | ~1,600 | ✅ **100%** |
| **Tangerine Whistle** | tests@v20.0.1 | ~1,200 | ~1,200 | ✅ **100%** |
| **Spurious Dragon** | tests@v20.0.1 | ~1,100 | ~1,100 | ✅ **100%** |
| **Homestead** | tests@v20.0.1 | 545 | 545 | ✅ **100%** |
| **Frontier** | tests@v20.0.1 | 557 | 557 | ✅ **100%** |
| **Prague (v5.4.0 frozen)** | v5.4.0 | 2,010 | 2,010 | ✅ **100%** |
| **Cancun (v5.4.0 frozen)** | v5.4.0 | 2,032 | 2,032 | ✅ **100%** |
| **Unit Tests** | — | 369 | 369 | ✅ **100%** |

---

## EIP Coverage by Fork

### Osaka (EIPs implemented 2026-08-11–15)

| EIP | Name | Cases | Status |
|---|---|---|---|
| EIP-7951 | P256Verify Precompile (0x0100) | 397 | ✅ 100% |
| EIP-7939 | CLZ Opcode (0x1E) | 579 | ✅ 100% |
| EIP-7883 | ModExp Gas Increase | 168 | ✅ 100% |
| EIP-7825 | Transaction Gas Limit Cap (2²⁴) | — | ✅ Implemented |

### Prague

| EIP | Name | Cases | Status |
|---|---|---|---|
| EIP-7702 | Set-Code Transactions (type-4) | 546 | ✅ 100% |
| EIP-7623 | Calldata Cost Floor | — | ✅ 100% |
| EIP-2537 | BLS12-381 Precompiles (0x0B–0x11) | — | ✅ 100% |
| EIP-3541 | Reject EF-prefixed runtime code | — | ✅ |
| EIP-3860 | Initcode size limit + word gas | — | ✅ |

### Cancun

| EIP | Name | Status |
|---|---|---|
| EIP-1153 | Transient storage (TLOAD/TSTORE) | ✅ |
| EIP-4844 | Blob transactions (type-3) | ✅ |
| EIP-5656 | MCOPY | ✅ |
| EIP-6780 | SELFDESTRUCT restriction | ✅ |

### Frontier / Homestead (fork-specific fixes — 2026-08-15)

| Rule | Description | Fix |
|---|---|---|
| `TX.CREATE_SURCHARGE` | Frontier: no 32000 surcharge for CREATE tx | `HasCreateTxSurcharge` flag |
| `CREATE.DEPOSIT_OOG` | Frontier: deposit OOG deploys empty code, succeeds | `HasCreateDepositOogHalt` flag |
| `DELEGATECALL` activation | Frontier: 0xF4 = INVALID | `HasDelegateCall` gate on entry |
| `CALL.PRE_EIP150_CHARGE` (CALLCODE) | Homestead: missing 9000 value-transfer + 2300 stipend | `OpcodeCallCode` pre-150 path |
| `CALL.PRECOMPILE_DISPATCH` | CALLCODE/DELEGATECALL to precompile skipped dispatch | `StateTransition` codeAddress check |
| `OP.EXP` byte cost | Frontier–Tangerine: 10/byte, not 50 | `IForkRules.ExpByteCost` |

---

## Known Non-Issues

**`ported_static` exclusion:** The official v20 gate explicitly excludes `ported_static` fixtures (legacy static test ports with known edge cases). The UI conformance view excludes them by default; `osaka_audit.runsettings` does not include them.

**Pre-existing `OverflowException` in `test_random_statetest384`:** One case in `ported_static` triggers a `UInt64` overflow on an extreme nonce value. This is a ported-static edge case outside the spec gate, not a protocol bug.

---

## Run Commands

```bash
# Full Osaka sweep (primary gate)
dotnet test Schlieren.EELS.Tests --settings osaka_audit.runsettings --filter "BENCHMARK_TaxonomySnapshot"

# Per-fork sweep
dotnet test Schlieren.EELS.Tests --settings sweep_frontier.runsettings --filter "BENCHMARK_TaxonomySnapshot"
dotnet test Schlieren.EELS.Tests --settings sweep_homestead.runsettings --filter "BENCHMARK_TaxonomySnapshot"
# ... sweep_byzantium, sweep_berlin, sweep_london, sweep_paris, sweep_shanghai, sweep_cancun, sweep_prague

# Full taxonomy report (writes TestResults/taxonomy_<timestamp>.md)
dotnet test Schlieren.EELS.Tests --settings osaka_audit.runsettings --filter "EelsTaxonomyDrill"

# Single-case trace (set EELS_CASE_FILTER in runsettings)
dotnet test Schlieren.EELS.Tests --settings <custom>.runsettings --filter "SingleCaseTrace"

# Unit tests
dotnet test Schlieren.Tests/Schlieren.Tests.csproj
```
