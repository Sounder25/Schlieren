# Schlieren Gas Diagnosis Foundation

## Why This Work Exists

Schlieren's Case Inspector is intended to identify the actual cause of an EVM mismatch, not merely report that observed gas differs from expected gas. Accurate diagnosis requires one authoritative, fork-aware description of every gas charge, refund, transfer, activation rule, exceptional burn, and settlement adjustment.

The documents in this directory are the audited foundation for that model. They capture the discovery baseline committed as `806dd2d`; they are not yet the centralized gas-schedule implementation. Production fixes made after that checkpoint must be reconciled into the inventory and matrix as part of the implementation plan.

## Final Documents

- `GAS_RULE_INVENTORY.md` is the source-level inventory. It records the exact formula, inputs and branches, fork behavior, movement kind, implementation location, test coverage, and audit findings for each rule.
- `GAS_COVERAGE_MATRIX.md` maps every inventory rule across all 14 supported forks. It distinguishes schedule-owned behavior from scattered implementation, missing coverage, inactive features, inheritance, and fork overrides.
- `../superpowers/plans/2026-08-14-executable-gas-schedule-completion.md` is the executable implementation plan and current recovery checkpoint. Future agents should work through that plan in order rather than inventing a second migration path.

The verified scope is 177 unique rule IDs: 168 protocol rules and 9 diagnostic rules. The matrix contains exactly one row for every inventory rule and exactly 14 fork-status cells per row.

## Reading the Matrix

- `D` — defined directly for the fork
- `I` — inherited unchanged from the preceding fork
- `O` — overridden by the fork
- `N/A` — operation or feature is inactive on the fork
- `S` — behavior exists but is scattered outside the fork schedule
- `M` — required behavior is missing or cannot be demonstrated

`S` and `M` counts are coverage cells, not counts of independent defects. A single scattered rule inherited across many forks creates multiple `S` cells.

## Verified Baseline

At the time this handoff was written:

- Inventory IDs: 177
- Matrix IDs: 177
- Missing, extra, or duplicate IDs: 0
- Matrix cells: `D=13`, `I=148`, `O=24`, `N/A=416`, `S=1581`, `M=296`
- Targeted test result before the documentation commit: 120 passed across `Schlieren.Tests` and `Schlieren.EELS.Tests`
- Fresh full `Schlieren.Tests` result on 2026-08-14: 329 passed and 1 failed. The known failure is `ForkingGlobalState_UnfetchedRemoteStorage_ReturnsUnknownPresence`, whose expected storage-presence contract is Task 1 of the executable plan.
- No production or test code was changed by this documentation phase

The inventory was derived from the local Schlieren implementation and supported-fork scope. Future agents must update both final documents when production gas behavior changes.

## Current Branch History

The discovery documents were committed in `806dd2d`. Subsequent commits on `codex/gas-rule-inventory` began correcting independently discovered conformance problems before the typed schedule migration:

- `2957e05` — CREATE/CREATE2 memory expansion and EIP-170 maximum-code-size handling
- `a82041b` — corrected the SDIV/MOD opcode-byte assignment and added regression coverage
- `131cd37` — propagated static context into CALLCODE child frames and added the executable gas-schedule plan
- `ee2a1d6` — corrected post-Paris opcode `0x44` fixture input to use PREVRANDAO

The inventory and matrix are therefore a migration ledger, not proof that every listed finding is still unresolved. Task 1 of the executable plan explicitly reconciles those documents with the post-discovery commits before schedule implementation begins.

## Highest-Priority Findings

Start with the `Missing Coverage by Severity` section in `GAS_COVERAGE_MATRIX.md`. The most important findings are:

1. Opcode activation is not consistently fork-gated, so later opcodes may execute on earlier forks instead of producing an invalid-opcode exceptional halt.
2. Gas-tree diagnostic re-execution can use a different lifecycle from canonical execution, making its explanation disagree with the transaction that actually ran.
3. CREATE/CREATE2 deployment-failure paths can mishandle rollback, access warmth, memory-expansion charging, and exceptional gas burn.
4. Case Inspector gas inference contains fork-blind constants and unsafe algebra, including sign/direction problems in balance-to-gas reasoning.
5. Several protocol rules are incomplete or incorrect at fork boundaries, including BALANCE pricing, initial warmth, authorization refunds, SELFDESTRUCT refunds, and ModExp limits/pricing.

These findings should not be fixed as isolated constants. The goal is to remove the possibility of fork-blind diagnosis by making execution and diagnosis resolve the same typed rule.

## Intended Architecture

Build an immutable typed gas schedule beginning with Frontier. Each subsequent fork inherits the previous schedule and declares only its changes. A resolved rule should contain, as applicable:

- stable rule ID and category
- activation range
- exact formula or constant
- required operands and state inputs
- warm/cold and other conditional branches
- charge, refund, reservation, forwarding, return, burn, or settlement semantics
- source/spec provenance
- deterministic test vectors

Canonical execution must emit a gas journal identifying the resolved rule and its evaluated inputs at every gas-affecting event. Case Inspector and gas-tree reporting should consume that journal and schedule metadata rather than re-executing the transaction or maintaining a second constant catalog.

## Recommended Implementation Order

1. Introduce the typed rule identifiers, fork overlays, and schedule resolver without changing execution behavior.
2. Add table-driven transition tests proving every fork override and every inactive-to-active boundary.
3. Migrate fixed opcode costs and opcode activation together.
4. Migrate memory, copy, hash, log, account-access, and storage formulas.
5. Migrate CALL/CREATE/SELFDESTRUCT gas movement and rollback semantics.
6. Migrate precompiles, transaction intrinsic gas, refunds, and final settlement.
7. Add the canonical gas journal and make diagnostics consume it.
8. Delete duplicate diagnostic execution and fork-blind constant matching only after parity tests pass.

For each migrated rule, preserve or add vectors for the activation fork, the immediately preceding fork, branch boundaries, overflow/large-input behavior, exceptional halt, and refund or returned-gas behavior.

## Guardrails for Future Agents

- Do not pull in another EVM implementation as a runtime dependency. External specifications or clients may be used only as review references when explicitly authorized.
- Do not make Case Inspector infer protocol behavior from balance deltas alone.
- Do not maintain gas formulas independently in execution and diagnostics.
- Do not treat host safety limits as protocol gas rules; represent host policy separately.
- Do not mark an `M` or `S` cell resolved until canonical execution and diagnostic attribution share the typed rule and tests cover its fork boundary.
- Do not renumber rule IDs casually. They are intended to become stable identifiers in traces, tests, and diagnoses.
- Keep the inventory and coverage matrix synchronized with code changes.

## Repository Hygiene

The files named `inv_00_header.md` through `inv_03_dynamic_memory_copy_hash_log.md` are historical assembly fragments from the difficult document transfer. They were included in the original discovery commit, but they are not authoritative and should not be used or updated as migration ledgers. The three authoritative files in this directory are this README, `GAS_RULE_INVENTORY.md`, and `GAS_COVERAGE_MATRIX.md`.
