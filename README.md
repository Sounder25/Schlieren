# Schlieren — .NET 8 Ethereum Execution & Verification Engine

Schlieren is a full-stack Ethereum execution engine, EVM debugger, and specification-verification platform built on .NET 8. It ships a complete EVM interpreter, a fork-aware gas scheduler, an Avalonia desktop IDE (the **Workbench**), and an automated conformance harness against the official Ethereum Python execution specification (EELS).

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

---

## Quick Start

```bash
# Restore and build
dotnet restore
dotnet build

# Run unit tests
dotnet test Schlieren.Tests/Schlieren.Tests.csproj

# Run the desktop IDE
dotnet run --project Schlieren.UI/Schlieren.UI.csproj

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

## IDE Features (Schlieren.UI)

- **Workbench** — Load any EELS state-test fixture or live prestate JSON, execute step-by-step with full stack/memory/storage inspection, and diff expected vs actual state
- **Conformance View** — Run and filter fork-specific EELS sweep suites; failures link directly into the Workbench
- **Call Topology Graph** — Visual inter-contract call tree with gas attribution
- **Causal Diagnosis Engine** — Automatically classifies EELS failures by gas rule (e.g. `EXP.BYTE_COST`, `CALL.NEW_ACCOUNT`, `TX.CREATE_SURCHARGE`) and links to `GAS_FORMULAS.md`
- **Gas Inspector** — Inline opcode gas badges showing exact costs and warm/cold access state
- **Hard Fork Selector** — Switch Frontier through Osaka; all fork-dependent rules apply immediately
- **Keyboard Shortcuts** — `F10` step forward, `F11` step back, `Space` toggle auto-play, `Ctrl+O` open fixture

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
