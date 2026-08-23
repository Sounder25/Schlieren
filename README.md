# Schlieren — .NET 8 Ethereum Execution & Verification Engine

Schlieren is a full-stack Ethereum execution engine, EVM debugger, and specification-verification platform built on .NET 8. It ships a complete EVM interpreter, a typed frame-aware execution journal, a React inspection workbench, and an automated conformance harness against the official Ethereum Python execution specification (EELS). The Avalonia client remains available on its frozen legacy RPC contracts.

**Status:** 100% EELS conformance across every fork from Frontier through Osaka (tag `schlieren-eels-100`).

---

## Projects

| Project | Purpose |
|---|---|
| `Schlieren.Core` | EVM interpreter, state transitions, opcodes, precompiles, access tracker, fork rules, causal diagnosis engine |
| `Schlieren.RPC` | Ethereum JSON-RPC server (`eth_call`, `eth_sendRawTransaction`, `debug_traceTransaction`) |
| `Schlieren.CLI` | Command-line host and batch runner |
| `Schlieren.UI` | Avalonia .NET 8 desktop IDE — Workbench, Conformance view, Call Topology |
| `Schlieren.Tests` | Unit and integration test suite (369 tests, 369 pass) |
| `Schlieren.EELS.Tests` | EELS conformance harness + automated failure diagnosis |
| `schlieren-ui` | Primary React workbench for journal-native frame, gas, state, and EELS inspection |

---

## Quick Start

```bash
# Restore and build
dotnet restore
dotnet build

# Run unit tests
dotnet test Schlieren.Tests/Schlieren.Tests.csproj

# Run the RPC server and primary React workbench (separate terminals)
dotnet run --project Schlieren.RPC/Schlieren.RPC.csproj
cd schlieren-ui && npm install && npm run dev

# Full Osaka conformance sweep (requires fixtures — see below)
dotnet test Schlieren.EELS.Tests --settings osaka_audit.runsettings --filter "BENCHMARK_TaxonomySnapshot"
```

### Fixture Setup

Conformance fixtures are gitignored (large). Download once:

```bash
# v20.0.1 from ethereum/execution-specs (current primary source)
gh release download "tests@v20.0.1" --repo ethereum/execution-specs --pattern "fixtures.tar.gz"
tar xzf fixtures.tar.gz "fixtures/state_tests/" "fixtures/.meta/"

# Or use the PowerShell helper:
pwsh ./tools/fetch-fixtures.ps1
```

---

## Conformance

Schlieren achieves **100% EELS conformance across all forks** using the `tests@v20.0.1` fixture suite from `ethereum/execution-specs`.

| Fork | Cases | Result |
|---|---|---|
| Osaka | 14,516 | ✅ 100% |
| Prague (v20) | 6,811 | ✅ 100% |
| Cancun (v20) | 4,514 | ✅ 100% |
| Shanghai (v20) | 4,969 | ✅ 100% |
| Paris / London / Berlin / Istanbul / Byzantium | ~2,000–3,500 each | ✅ 100% |
| Homestead | 545 | ✅ 100% |
| Frontier | 557 | ✅ 100% |
| Unit Tests | 369 | ✅ 100% |

See [`CONFORMANCE_STATUS.md`](CONFORMANCE_STATUS.md) for full details and EIP coverage.

---

## React Workbench (`schlieren-ui`)

The primary UI makes one `schlieren_traceJournal` request per execution. Optional pasted bytecode runs in an ephemeral overlay and never persists to chain state. Stack, memory, and storage snapshots are returned by default.

- **Frame interferogram** — explicit parent/child frame IDs and child-owned opcode bands
- **Exclusive gas topology** — additive charges, subtractive credits, non-additive CALL allocations/returns, explicit exceptional burns, and a conservation gate
- **Machine state** — cursor-linked stack, memory, storage, gas-before/gas-after, opcode, and frame context
- **EELS alignment** — paste EIP-3155 `structLogs` and jump directly to the first PC/op/gas/depth divergence with journal-frame context

See [`docs/rpc/schlieren_traceJournal.md`](docs/rpc/schlieren_traceJournal.md) for the complete request and gas semantics.

## Legacy Avalonia IDE (`Schlieren.UI`)

- **Workbench** — Load any EELS state-test fixture or live prestate JSON, execute step-by-step with full stack/memory/storage inspection, and diff expected vs actual state
- **Conformance View** — Run and filter fork-specific EELS sweep suites; failures link directly into the Workbench
- **Call Topology Graph** — Visual inter-contract call tree with gas attribution
- **Causal Diagnosis Engine** — Automatically classifies EELS failures by gas rule (e.g. `EXP.BYTE_COST`, `CALL.NEW_ACCOUNT`, `TX.CREATE_SURCHARGE`) and links to `GAS_FORMULAS.md`
- **Gas Inspector** — Inline opcode gas badges showing exact costs and warm/cold access state
- **Hard Fork Selector** — Switch Frontier through Osaka; all fork-dependent rules apply immediately
- **Keyboard Shortcuts** — `F10` step forward, `F11` step back, `Space` toggle auto-play, `Ctrl+O` open fixture

`debug_inspect` and `debug_traceCall` keep their existing JSON shapes for Avalonia compatibility. Journal-native clients should use `schlieren_traceJournal`.

### One canonical execution path

`StateTransition.ApplyTransactionAsync` is the only transaction evaluator. Diagnostic callers enable the typed journal on that same run; journal events then drive the React trace, the legacy `debug_inspect` gas-tree projection, Avalonia gas views, and audit totals. Schlieren does not re-execute transactions or reconstruct gas from flat trace steps for diagnosis.

Prospective intrinsic calculations always receive the selected block's fork rules explicitly. Retrospective views read the intrinsic charge and settlement recorded by canonical execution, preventing a UI or RPC helper from silently applying a different fork schedule.

### Typed causal diagnosis

State, receipt, and engine differences are recorded as typed discrepancies at the comparison boundary. Diagnosis, EELS taxonomy, auditors, and both UI paths consume those typed facts; human-readable mismatch lines are rendered only for legacy output and test reports. Changing wording can therefore never change a diagnosis.

Diagnosis grades are derived from an explicit proof basis:

- `PROVEN` requires an applicable rule, an isolated first-divergence phase, exact arithmetic, and independent corroboration.
- `STRONG` requires an applicable rule and isolated phase plus exact arithmetic, corroboration, or direct execution evidence.
- `POSSIBLE` covers incomplete or non-isolated evidence.

A sender-only gas residual is `STRONG`; a matching sender/coinbase fee pair can be `PROVEN`. Existing `debug_inspect` JSON remains structurally unchanged.

---

## Architecture

```
Schlieren.Core/
  Execution/
    EvmMachine.cs         — interpreter loop
    StateTransition.cs    — transaction lifecycle (type-0 through type-4)
    ExecutionContext.cs   — per-frame state (stack, memory, access tracker, transient storage)
    Precompiles.cs        — 0x01–0x13 + 0x0100 dispatch
    Causal/               — failure diagnosis engine
  Forks/
    IForkRules.cs         — fork capability interface
    ForkRules.cs          — Frontier → Osaka class chain
  Opcodes/                — one class per opcode or opcode group
  State/
    StateOverlay.cs       — layered state with tombstone semantics
    ForkingGlobalState.cs — RPC-backed remote state (forked node mode)
    AccountDeployability.cs — EIP-7610 fail-closed collision check
```

### Fork Rules Design

Every fork-dependent constant or behaviour is declared on `IForkRules` and overridden in the appropriate `*Rules` class. Nothing is hardcoded in opcode logic. The chain is:

```
FrontierRules
  └─ HomesteadRules        (EIP-2: CREATE surcharge, deposit OOG halt, DELEGATECALL)
       └─ TangerineWhistleRules  (EIP-150: IO repricing, 63/64 forwarding)
            └─ SpuriousDragonRules  (EIP-160: EXP repricing)
                 └─ ByzantiumRules → ConstantinopleRules → IstanbulRules
                      → BerlinRules → LondonRules → ParisRules
                      → ShanghaiRules → CancunRules → PragueRules
                           └─ OsakaRules
```

---

## Gas Documentation

The gas formula book lives in [`docs/gas/`](docs/gas/):

| File | Contents |
|---|---|
| [`GAS_FORMULAS.md`](docs/gas/GAS_FORMULAS.md) | Complete protocol formula reference, all 168 rules, fork annotations |
| [`GAS_RULE_INVENTORY.md`](docs/gas/GAS_RULE_INVENTORY.md) | Source-level audit — exact file:line for every gas charge |
| [`GAS_COVERAGE_MATRIX.md`](docs/gas/GAS_COVERAGE_MATRIX.md) | Per-fork coverage matrix |

---

## Security

Schlieren has been hardened against adversarial inputs at every layer:

### EVM Execution Security
- **Call-depth bomb:** EVM enforces 1024-frame limit; CLR stack protected by `Task.Yield()` every 32 frames prevents `StackOverflowException`
- **Infinite loops:** Gas limit enforced; `OutOfGas` revert without freezing host thread
- **Memory expansion attacks:** Quadratic gas formula aborts before allocating terabyte-scale arrays
- **Precompile abuse:** Bad input sizes and insufficient gas handled gracefully; no `IndexOutOfRangeException`
- **Dirty calldata:** `MAX_UINT256` arguments decoded safely; reverts on contract `require()`, not host crash
- **SELFDESTRUCT + CREATE2:** Tombstone semantics enforced; re-deploy at same address correctly rejected within block

### RPC Server Security
- **Slowloris defense:** 5-second read timeout on entire request phase; `408 Request Timeout` on stalled connections
- **Payload limit:** 1MB cap enforced; `413 Payload Too Large` and socket close on oversized bodies
- **Malformed JSON:** `-32700 Parse error` on truncated/invalid JSON
- **Missing fields:** `-32600 Invalid Request` on missing `method`
- **Invalid method:** `-32601 Method not found` gracefully returned
- **HTTP method abuse:** `405 Method Not Allowed` on GET requests
- **Batch bomb:** 10k-request arrays rejected without host OOM

Tested via `muscle/tests/Chaos.test.csx` and `muscle/tests/RpcChaos.test.csx`.

---

## Development Tools

```
tools/
  eels_fixture_diff.py      — diff Schlieren output vs EELS expected state
  eels_loop_trace.py        — run EELS Python reference tracer to JSONL
  eels_trace_compare.py     — step-by-step structLog alignment tool
  fetch-fixtures.ps1        — download and extract official fixture archives

.agents/skills/             — in-repo agent skills (taxonomy drill, balance auditor, etc.)
```

Run settings for specific suites:

```
osaka_audit.runsettings         — full Osaka sweep (14,516 cases)
prague_v20_audit.runsettings    — Prague v20 official (6,811 cases)
sweep_<fork>.runsettings        — per-fork sweeps (Frontier through Osaka)
eip7702_audit.runsettings       — EIP-7702 SetCode subset
bls_audit.runsettings           — BLS12-381 precompiles
```

---

## License

MIT
