# Gas Rule Inventory and Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce an exhaustive, reviewable inventory of Scrutor's current gas constants, formulas, fork gates, accounting movements, and test coverage so the executable per-fork schedule can be implemented without omissions.

**Architecture:** This workstream is discovery-only and owns documentation files, not production code. It maps every current gas-affecting path to a proposed stable rule identifier, formula, fork behavior, source boundary, and tests, then provides a per-fork coverage matrix and migration-risk report for the schedule implementer.

**Tech Stack:** .NET 8/C#, Markdown, PowerShell, ripgrep, Git.

## Global Constraints

- Read `docs/superpowers/specs/2026-08-11-executable-fork-gas-schedule-design.md` completely before beginning.
- Do not modify `Scrutor.Core`, `Scrutor.UI`, `Scrutor.RPC`, `Scrutor.CLI`, or any test project in this workstream.
- Do not use an external EVM client or runtime comparison tool.
- Treat `IForkRules` as a useful partial source, not as proof of complete coverage.
- Include every supported fork from `Scrutor.Core/Forks/Fork.cs`; do not silently group forks whose schedules differ.
- Record formulas exactly as implemented, including suspicious or duplicated behavior. Do not correct production logic.
- Use repository-relative source paths and 1-based line numbers.
- Use no `TBD`, `TODO`, unnamed catch-all rows, or unsupported claims.
- Commit only the two owned documentation files.

---

### Task 1: Establish the Supported-Fork and Source Baseline

**Files:**
- Read: `docs/superpowers/specs/2026-08-11-executable-fork-gas-schedule-design.md`
- Read: `Scrutor.Core/Forks/Fork.cs`
- Read: `Scrutor.Core/Forks/IForkRules.cs`
- Read: `Scrutor.Core/Forks/ForkRules.cs`
- Create later: `docs/gas/GAS_RULE_INVENTORY.md`
- Create later: `docs/gas/GAS_COVERAGE_MATRIX.md`

**Interfaces:**
- Consumes: Current fork enum, fork-rule inheritance, and approved gas-schedule design.
- Produces: An exact ordered fork list and source-search boundary used by Tasks 2–5.

- [ ] **Step 1: Confirm the workspace and protect unrelated changes**

Run:

```powershell
git status --short
git branch --show-current
```

Expected: the current branch is reported. Record pre-existing changes and do not stage or alter them.

- [ ] **Step 2: Read the approved design and fork definitions completely**

Run:

```powershell
Get-Content -Raw docs/superpowers/specs/2026-08-11-executable-fork-gas-schedule-design.md
Get-Content -Raw Scrutor.Core/Forks/Fork.cs
Get-Content -Raw Scrutor.Core/Forks/IForkRules.cs
Get-Content -Raw Scrutor.Core/Forks/ForkRules.cs
```

Expected: the fork order, existing gas API, and design requirements are available before inventory begins.

- [ ] **Step 3: Capture the complete production and test search sets**

Run:

```powershell
rg --files Scrutor.Core | Sort-Object
rg --files Scrutor.Tests Scrutor.EELS.Tests | Sort-Object
```

Expected: all C# production and test paths are visible. Exclude generated `bin` and `obj` content from every later search.

### Task 2: Build the Formula and Constant Inventory

**Files:**
- Create: `docs/gas/GAS_RULE_INVENTORY.md`
- Read: all `Scrutor.Core/**/*.cs`
- Read: relevant `Scrutor.Tests/**/*.cs` and `Scrutor.EELS.Tests/**/*.cs`

**Interfaces:**
- Consumes: Supported-fork list and approved formula categories.
- Produces: A row for every current gas-affecting production path and a stable proposed `GasRuleId` for use by schedule implementation.

- [ ] **Step 1: Locate candidate gas sites with independent searches**

Run all searches; do not rely on only one keyword:

```powershell
rg -n --glob '*.cs' --glob '!**/bin/**' --glob '!**/obj/**' "Gas|gas|Refund|refund|Stipend|stipend|Warm|Cold|warm|cold" Scrutor.Core
rg -n --glob '*.cs' --glob '!**/bin/**' --glob '!**/obj/**' "ConsumeGas|RefundGas|GasUsed|GasLimit|GasRefundCounter|CalculateGasCost|ComputeIntrinsicGas" Scrutor.Core
rg -n --glob '*.cs' --glob '!**/bin/**' --glob '!**/obj/**' "[0-9]_[0-9]|[0-9]{2,}" Scrutor.Core/Forks Scrutor.Core/Execution Scrutor.Core/Opcodes
```

Expected: overlapping candidate sets covering constants, formulas, transfers, refunds, and settlement. Manually inspect every candidate before including or excluding it.

- [ ] **Step 2: Create the inventory document with fixed sections**

Create `docs/gas/GAS_RULE_INVENTORY.md` with these sections in this order:

```markdown
# Scrutor Gas Rule Inventory

## Method and Scope
## Supported Forks
## Inventory Summary
## Transaction Entry and Intrinsic Gas
## Fixed Opcode Gas
## Dynamic Opcode, Memory, Copy, Hash, and Log Gas
## Account and Storage Access Gas
## SSTORE Charges and Refunds
## CALL-Family Gas and Frame Transfers
## CREATE-Family and Code-Deposit Gas
## SELFDESTRUCT Gas
## Precompile Gas
## Exceptional Halt, Refund Cap, and Settlement
## Duplicate or Conflicting Implementations
## Uncovered or Ambiguous Paths
## Existing Test Coverage
## Recommended Migration Slices
```

Expected: every approved coverage category has a dedicated section.

- [ ] **Step 3: Use one exact row schema for every gas rule**

Each inventory table must use these columns:

```text
Proposed Rule ID | Operation | Current Formula/Constant | Inputs and Branches | Fork Behavior | Charge/Movement Kind | Production Source | Existing Tests | Findings
```

Rules for filling rows:

- `Proposed Rule ID` uses uppercase dotted identifiers such as `OP.SLOAD`, `MEMORY.EXPANSION`, `CALL.VALUE_TRANSFER`, `TX.REFUND_CAP`, or `PRECOMPILE.MODEXP`.
- `Current Formula/Constant` states the actual arithmetic, not only a method name.
- `Inputs and Branches` lists every state fact or condition used by the implementation.
- `Fork Behavior` names each distinct era and value/formula.
- `Charge/Movement Kind` is one of `Charge`, `TransferOut`, `TransferIn`, `Return`, `RefundCounterDelta`, `Burn`, `Settlement`, or `Validation`.
- `Production Source` contains repository-relative path and 1-based line number.
- `Existing Tests` contains exact test path and test method, or `None found` after searching.
- `Findings` records duplication, hard-coding, missing fork abstraction, questionable semantics, or `No issue observed`.

Expected: a reviewer can implement a typed rule without reopening the source merely to discover its inputs or fork variants.

- [ ] **Step 4: Inventory frame movements separately from opcode charges**

For CALL, CALLCODE, DELEGATECALL, STATICCALL, CREATE, and CREATE2, create separate rows for:

```text
base/access charge
value-transfer charge
new-account charge
memory expansion
EIP-150 forwarding limit
pre-EIP-150 forwarded-gas behavior
stipend grant
child transfer out
unused child gas return
exceptional child burn
refund-counter propagation or rollback
```

Expected: forwarded gas is never represented as consumed gas, and every parent/child movement can later receive a journal identifier.

- [ ] **Step 5: Inventory formula tests and identify missing vectors**

Run:

```powershell
rg -n --glob '*.cs' --glob '!**/bin/**' --glob '!**/obj/**' "Gas|Refund|Sstore|Call|Create|Precompile|Memory|Intrinsic" Scrutor.Tests Scrutor.EELS.Tests
```

Expected: each production inventory row points to exact existing tests or explicitly says `None found`. The findings identify whether tests assert totals only or formula components and fork boundaries.

### Task 3: Build the Per-Fork Coverage Matrix

**Files:**
- Create: `docs/gas/GAS_COVERAGE_MATRIX.md`
- Read: `docs/gas/GAS_RULE_INVENTORY.md`
- Read: `Scrutor.Core/Forks/Fork.cs`
- Read: `Scrutor.Core/Forks/ForkRules.cs`

**Interfaces:**
- Consumes: Proposed rule identifiers and exact fork variants from the inventory.
- Produces: A matrix proving whether each gas rule is defined, inherited, overridden, inactive, scattered, or missing for every supported fork.

- [ ] **Step 1: Create the matrix document with fixed legend**

Create `docs/gas/GAS_COVERAGE_MATRIX.md` beginning with:

```markdown
# Scrutor Per-Fork Gas Coverage Matrix

## Legend

- `D` — defined directly for this fork
- `I` — inherited unchanged from the previous fork
- `O` — overridden by this fork
- `N/A` — operation or feature is inactive on this fork
- `S` — implemented through scattered production logic rather than the fork schedule
- `M` — missing or not demonstrably represented

## Coverage Matrix
## Fork Transition Changes
## Missing Coverage by Severity
## Proposed Schedule Overlay Order
## Validation Summary
```

Expected: status meanings are unambiguous and distinguish scattered implementation from true schedule coverage.

- [ ] **Step 2: Add one row for every proposed rule identifier**

The table's first columns are:

```text
Rule ID | Category | Frontier | Homestead | ...all remaining Fork enum members in order... | Evidence
```

Expected: every inventory rule appears exactly once, every supported fork has a status, and `Evidence` cites the relevant fork rule or production path.

- [ ] **Step 3: Document every fork transition**

Under `Fork Transition Changes`, create one subsection per fork after Frontier. List only rules added, disabled, or changed at that transition, with the associated EIP when known from repository comments or tests.

Expected: no transition is omitted, even when it states that no gas change was found in the current implementation.

- [ ] **Step 4: Rank missing coverage**

Classify every `M` or `S` item:

- Critical: can change transaction validity, all remaining gas, settlement, or nested-frame conservation.
- High: dynamic state-dependent charge or refund.
- Medium: fixed or length-dependent opcode/precompile pricing.
- Low: metadata, naming, or test-vector gap that does not change execution today.

Expected: the ranking states why each item has that severity and proposes the migration slice that should own it.

### Task 4: Validate Completeness and Internal Consistency

**Files:**
- Modify: `docs/gas/GAS_RULE_INVENTORY.md`
- Modify: `docs/gas/GAS_COVERAGE_MATRIX.md`

**Interfaces:**
- Consumes: Completed inventory and matrix.
- Produces: Self-consistent discovery artifacts with no placeholders and documented limitations.

- [ ] **Step 1: Cross-check inventory-to-matrix identity**

Extract all proposed rule identifiers from both documents and verify that the sets match. A small temporary command or script may be used, but do not commit it.

Expected: no identifier appears only in one document and no identifier is duplicated within a document.

- [ ] **Step 2: Re-run source searches for missed sites**

Repeat the three production searches from Task 2 and account for every candidate. Add missed rules or record a concrete exclusion rationale in `Method and Scope`.

Expected: there are no unexplained gas-related production candidates.

- [ ] **Step 3: Run placeholder and formatting checks**

Run:

```powershell
rg -n -i "TBD|TODO|FIXME|placeholder|fill in|implement later" docs/gas/GAS_RULE_INVENTORY.md docs/gas/GAS_COVERAGE_MATRIX.md
git diff --check -- docs/gas/GAS_RULE_INVENTORY.md docs/gas/GAS_COVERAGE_MATRIX.md
```

Expected: the placeholder search returns no matches and `git diff --check` reports no errors.

- [ ] **Step 4: Verify ownership boundaries**

Run:

```powershell
git status --short
git diff --name-only
```

Expected: this workstream changed only `docs/gas/GAS_RULE_INVENTORY.md` and `docs/gas/GAS_COVERAGE_MATRIX.md`. If unrelated changes existed before the workstream, they remain unstaged and unmodified.

### Task 5: Commit and Hand Off the Inventory

**Files:**
- Commit: `docs/gas/GAS_RULE_INVENTORY.md`
- Commit: `docs/gas/GAS_COVERAGE_MATRIX.md`

**Interfaces:**
- Consumes: Validated inventory and matrix.
- Produces: One reviewable commit and a concise handoff for the schedule implementer.

- [ ] **Step 1: Stage only owned files**

Run:

```powershell
git add -- docs/gas/GAS_RULE_INVENTORY.md docs/gas/GAS_COVERAGE_MATRIX.md
git diff --cached --name-only
git diff --cached --check
```

Expected: exactly the two owned documentation files are staged and the cached diff has no formatting errors.

- [ ] **Step 2: Commit the discovery artifacts**

Run:

```powershell
git commit -m "docs: inventory per-fork gas rules and coverage"
```

Expected: one commit is created containing only the two documentation files.

- [ ] **Step 3: Report the handoff**

Return:

- Commit hash
- Total proposed rule identifiers
- Count of `D`, `I`, `O`, `N/A`, `S`, and `M` matrix cells
- Five highest-risk scattered or missing rules
- Any current implementation conflicts that require a design decision
- Confirmation that no production or test code was modified

Expected: the primary agent can use the artifacts to finalize implementation task boundaries without repeating repository discovery.
