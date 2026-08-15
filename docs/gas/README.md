# Schlieren Gas Diagnosis Foundation

## Why This Work Exists

Schlieren's Case Inspector is intended to identify the actual cause of an EVM mismatch, not merely report that observed gas differs from expected gas. Accurate diagnosis requires one authoritative, fork-aware description of every gas charge, refund, transfer, activation rule, exceptional burn, and settlement adjustment.

## Authoritative files (this folder)

| File | Role |
|---|---|
| `GAS_FORMULAS.md` | **Start here.** Every rule ID, the formula, fork range, and movement kind. Reconciled 2026-08-15. |
| `GAS_RULE_INVENTORY.md` | Source-level ledger: inputs, production file:line, tests, open/resolved findings. |
| `GAS_COVERAGE_MATRIX.md` | Same 177 IDs × 14 forks (`D` / `I` / `O` / `N/A` / `S` / `M`). |
| `../superpowers/plans/2026-08-14-executable-gas-schedule-completion.md` | Engine migration plan (`Schlieren.Core.Gas`). Not started. |

`inv_00_header.md` through `inv_03_*.md` are leftover transfer fragments. Do not edit them as ledgers.

## Scope

177 unique rule IDs: 168 protocol + 9 diagnostic. The matrix has one row per ID and 14 fork cells per row.

## Reading the matrix

- `D` — defined directly for the fork
- `I` — inherited unchanged from the preceding fork
- `O` — overridden by the fork
- `N/A` — operation or feature is inactive on the fork
- `S` — behavior exists but is scattered outside a typed fork schedule
- `M` — required behavior is missing or cannot be demonstrated

`S` and `M` count cells, not independent defects. A single scattered rule inherited across many forks creates multiple `S` cells.

## 2026-08-15 checkpoint

- Osaka official: 14,516 / 14,516
- Prague official: 6,811 / 6,811 (excl. ported_static)
- Cancun / Shanghai official v20: 100%
- Typed `Schlieren.Core.Gas` schedule: **not present**. Formulas still live in `IForkRules`, opcode literals, `IntrinsicGas`, `Precompiles`, `StateTransition`.
- Matrix cells after reconciliation: `D=13`, `I=148`, `O=24`, `N/A=423`, `S=1738`, `M=132`
- Closed on the canonical path since discovery `806dd2d`: SDIV/MOD bytes; CREATE/CREATE2 memory + EIP-3860 gates; LOG static; Shanghai coinbase warm; Osaka `0x0100` warm; local activation for SHL/SHR/SAR, CLZ, PUSH0, SELFBALANCE, BASEFEE, BLOB*, MCOPY, TLOAD/TSTORE; Osaka tx gas cap
- Still open: listed in `GAS_FORMULAS.md` section 12 and the matrix **Missing Coverage by Severity** section

## Intended architecture (unchanged)

Build an immutable typed gas schedule beginning with Frontier. Each later fork inherits and declares only its changes. Canonical execution emits a gas journal (`GasRuleId` + evaluated inputs). Case Inspector consumes that journal. Do not keep a second constant catalog in diagnostics.

## Guardrails

- Do not pull in another EVM as a runtime dependency.
- Do not make Case Inspector infer protocol behavior from balance deltas alone.
- Do not maintain gas formulas independently in execution and diagnostics.
- Do not treat host safety limits as protocol gas rules.
- Do not mark an `M` or `S` cell resolved until execution and diagnosis share the typed rule and tests cover the fork boundary.
- Do not renumber rule IDs casually.
- When production gas behavior changes, update **formulas, inventory, and matrix together**.
