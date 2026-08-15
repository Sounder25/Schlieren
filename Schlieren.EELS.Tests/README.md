# Schlieren.EELS.Tests — Conformance Harness

Automated conformance harness against the official Ethereum Python execution specification (EELS), using state-test fixtures from `ethereum/execution-specs`.

## Status: 100% across all forks

See [`../CONFORMANCE_STATUS.md`](../CONFORMANCE_STATUS.md) for the full matrix.

---

## Run Settings

| File | Purpose | Cases |
|---|---|---|
| `osaka_audit.runsettings` | Full Osaka sweep — **primary gate** | 14,516 |
| `prague_v20_audit.runsettings` | Prague v20 official (excl. ported_static) | 6,811 |
| `sweep_<fork>.runsettings` | Per-fork sweep (frontier → osaka) | varies |
| `eip7702_audit.runsettings` | EIP-7702 SetCode subset | 546 |
| `eip7623_audit.runsettings` | EIP-7623 calldata floor | — |
| `bls_audit.runsettings` | BLS12-381 precompiles | — |
| `cancun_v20_audit.runsettings` | Cancun v20 | 4,514 |
| `quick_audit.runsettings` | Static/legacy state tests | — |

## Test Filters

```bash
# Snapshot gate — reports TotalCases/FailedCases
--filter "BENCHMARK_TaxonomySnapshot"

# Full taxonomy drill — writes TestResults/taxonomy_<ts>.md
--filter "EelsTaxonomyDrill"

# Single case with full structLog trace
--filter "SingleCaseTrace"

# Balance auditor — 5-term sender+coinbase ledger
--filter "EelsBalanceAudit"
```

## Environment Variables (via .runsettings)

| Variable | Description |
|---|---|
| `EELS_REQUIRED_FORK` | Fork name, e.g. `Osaka`, `Frontier` |
| `EELS_FIXTURES_ROOT` | Path to `fixtures/state_tests/for_<fork>/` |
| `EELS_INCLUDE_SUBDIRS` | `1` to recurse subdirectories |
| `EELS_MAX_CASES` | Cap on cases loaded (omit or `99999` for all) |
| `EELS_CASE_FILTER` | Substring filter for case ID |
| `EELS_STRUCT_LOG_OUT` | Output path for SingleCaseTrace structLog JSON |

## Tools

```
tools/eels_fixture_diff.py      — diff Schlieren vs EELS for a fixture+case
tools/eels_loop_trace.py        — run EELS Python reference tracer → JSONL
tools/eels_trace_compare.py     — align Schlieren structLog vs EELS JSONL step-by-step
```
