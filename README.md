# Scrutor — .NET 8 Ethereum Execution & Verification Engine

Scrutor is a high-performance .NET 8 Ethereum execution client, EVM security debugger, and specification verification platform.

The solution includes the core execution engine, JSON-RPC services, a command-line host, an Avalonia desktop IDE, unit tests, and an EELS state-test conformance harness.

## Projects

- `Scrutor.Core` — EVM execution, state transitions, opcodes, precompiles, access tracking, and security detectors (Reentrancy, Storage Collision).
- `Scrutor.RPC` — Ethereum JSON-RPC server (`eth_call`, `eth_sendRawTransaction`, `debug_traceTransaction`).
- `Scrutor.CLI` — Command-line host & runner.
- `Scrutor.UI` — Modern Avalonia .NET 8 EVM Security & Execution IDE.
- `Scrutor.Tests` — Core unit and integration test suite (**303 tests**).
- `Scrutor.EELS.Tests` — Conformance adapter & automated debugging suite for published EELS state-test fixtures.

---

## Scrutor IDE Features (`Scrutor.UI`)

- **Top Application Menu Bar**: Desktop menus (`File`, `Edit`, `EVM Engine`, `Tools`, `Help`) with hotkeys (`Ctrl+O`, `Ctrl+Shift+O`, `Ctrl+S`, `Alt+F4`).
- **Native OS Open File & Folder Dialogs**: Open custom `.sol`, `.yul`, `.json`, `.hex`, or `.txt` contract files and workspace directories natively.
- **EVM Hard Fork Selector & Block Configurator**: Switch hard forks (`Cancun`, `Prague`, `Shanghai`, `London`, `Berlin`) and configure `BaseFeePerGas`, `GasLimit`, `ChainId`, and `Coinbase`.
- **Interactive EVM Step Scrubber**: Step through contract bytecode line-by-line or toggle automated playback (`▶ PLAY` / `⏸ PAUSE`).
- **Full Keyboard Debugger Shortcuts**: `F10` (Step Forward), `F11` (Step Back), `Space` (Toggle Auto-Play), `Home` (Jump Start), `End` (Jump End).
- **Inline Opcode Gas Badges**: Displays exact gas costs directly on active execution lines (e.g. `[PUSH1 • 3]`, `[SLOAD • 2100 ❄ COLD]`).
- **Live EELS Spec Audit Drawer**: Shows real-time EELS spec citations (`sstore(evm)`, `sload(evm)`) and exact gas formula breakdowns (`COLD_ACCESS + STORAGE_SET`).
- **One-Click Audit Report Exporter**: Generate professional Markdown security audit reports (`AUDIT_REPORT.md`) with Reentrancy & Proxy Storage Collision findings.
- **Call Topology Graph**: Visual inter-contract call topology and depth tracking.

---

## Build and Test

```powershell
dotnet restore
dotnet build --no-restore
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --no-build
```

### EELS Conformance & Debugging Suite

Scrutor includes an automated 5-tool EELS debugging toolchain:

```powershell
$env:EELS_FIXTURES_ROOT = "C:/projects/Scrutor/fixtures/state_tests/cancun"
$env:EELS_INCLUDE_SUBDIRS = "1"

# 1. Taxonomy Drill — bucket all failures by category & delta magnitude
dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj --filter "EelsTaxonomyDrill"

# 2. Balance Auditor — 5-term gas ledger reconstruction
dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj --filter "EelsBalanceAudit"

# 3. Single-Case Step Tracer — emit full EIP-3155 structLog
$env:EELS_CASE_FILTER = "callBasic"
dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj --filter "SingleCaseTrace"

# 4. StructLog Step-Diff — find exact step & opcode where execution diverges
python tools/eels_trace_compare.py <scrutor_log.json> <reference_log.json>

# 5. Log Auditor — audit event topics, data payloads, and logsBloom filters
dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj --filter "EelsLogAudit"
```

See [Scrutor.EELS.Tests/README.md](Scrutor.EELS.Tests/README.md) for harness configuration details.

---

## Cancun Conformance Baseline

As of **2026-08-05**:

- `dotnet build Scrutor.sln`: **Build succeeded with 0 errors**.
- `Scrutor.Tests`: **303 passed, 0 failed**.
- `Scrutor.EELS.Tests`: Conformance suite and 5-tool EELS taxonomy suite active.
- CI Gate: Automated PR conformance check via `.github/workflows/eels-gate.yml`.

---

## Hermes Agent Skills

Custom skills for autonomous agent execution are defined in `.agents/skills/`:
- `eels-taxonomy-drill`
- `eels-balance-auditor`
- `eels-single-case-tracer`
- `eels-trace-compare`
- `eels-log-auditor`
- `eels-fixture-diff`
