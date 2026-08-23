# Canonical Diagnostic Execution Design

**Date:** 2026-08-23
**Status:** Approved in chat; written review pending
**Scope:** Delete the duplicate diagnostic transaction evaluator and every simplified or ruleless intrinsic-gas calculator. Derive diagnostic gas views from the canonical typed execution journal without changing legacy RPC JSON shapes.

## Problem

Schlieren currently has one authoritative transaction path, `StateTransition.ApplyTransactionAsync`, and a second diagnostic implementation, `ApplyTransactionWithFrameAsync`, reached through `ApplyTransactionWithGasTreeAsync`. The diagnostic copy repeats validation, intrinsic gas, access warming, execution, code deposit, refund, and settlement logic. It can therefore execute a different protocol from the canonical path.

Diagnostic gas views also have independent arithmetic:

- `GasTreeFromTrace` reconstructs frames and costs from flat trace steps.
- `GasTreeBuilder` guesses warm/cold storage classification, memory expansion, unused gas, and intrinsic components.
- `IntrinsicGas.Compute(Transaction)` and `TryCompute(Transaction, out ...)` silently select the latest fork.
- RPC has a private base/calldata/create-only intrinsic calculator.
- the Avalonia audit exporter manually adds `21,000`, calldata byte costs, and depth-one trace costs.

These paths can disagree with the fork rules and with the transaction that actually ran. A diagnostic tool must explain canonical execution, never perform or approximate a second execution.

## Goals

1. Leave `StateTransition.ApplyTransactionAsync` as the only transaction evaluator.
2. Make the typed `ExecutionJournal` the only source for diagnostic gas trees and conservation.
3. Require every prospective intrinsic calculation to receive the active `IForkRules` explicitly.
4. Use recorded canonical intrinsic and settlement events for retrospective views.
5. Preserve the existing `debug_inspect` and `debug_traceCall` JSON contracts.
6. Keep the Avalonia UI functional while the React UI remains the primary journal-native client.
7. Add architectural tests that prevent duplicate evaluators and ruleless calculators from returning.

## Non-goals

- Changing opcode, transaction, refund, or settlement behavior.
- Redesigning `schlieren_traceJournal` JSON.
- Removing `debug_inspect`, `debug_traceCall`, or Avalonia in this slice.
- Recalibrating causal diagnosis rules.
- Adding a second journal schema or a second gas-tree model with independent arithmetic.

## Chosen Architecture

### One execution path

All callers execute through:

```csharp
Task<ExecutionResult> ApplyTransactionAsync(
    Transaction tx,
    IGlobalState state,
    BlockContext block,
    bool commit = true,
    CancellationToken ct = default);
```

Callers that need diagnostic gas information set `Transaction.EnableJournal = true` before this call. `ExecutionResult.Journal` is then the immutable record of the execution used to produce that same result.

The following APIs and their supporting plumbing are deleted:

- `StateTransition.ApplyTransactionWithGasTreeAsync`
- `StateTransition.ApplyTransactionWithFrameAsync`
- `GasFrameNode`
- the `parentGasFrame` argument threaded through internal execution
- `ExecutionContext.GasFrame`

No replacement evaluator is introduced.

### One gas tree

`JournalGasTree.Build(ExecutionJournal, ExecutionResult)` remains the only gas-tree builder. It consumes exclusive journal events and returns:

- the frame-aware tree;
- derived charged gas;
- settled gas;
- an explicit conservation delta;
- `IsConserved` without inserting balancing buckets.

`GasTreeFromTrace` and the heuristic `GasTreeBuilder` are deleted. Their guesses about depth, warm/cold state, memory expansion, calldata, and unused gas are not retained.

The old `GasTreeNode` shape may remain only as a display compatibility type. A small projection maps `JournalGasNode` recursively into `GasTreeNode` or directly into `InspectGasNode`; it performs no arithmetic and copies `Label`, exclusive amount, total, and children from the journal tree. `GasTreeRenderer` may continue to render that compatibility shape for Avalonia.

If an inspection caller supplies a result without a journal, assembly fails explicitly with a stable `InvalidOperationException` explaining that canonical journal capture is required. It must not fall back to trace reconstruction.

### Intrinsic gas semantics

`IntrinsicGas.Compute(Transaction, IForkRules)` is the sole prospective intrinsic calculator. `ComputeFloor(Transaction)` remains because the EIP-7623 token floor is transaction-data arithmetic and its fork activation is decided by the canonical caller.

The following ruleless overloads are deleted:

```csharp
IntrinsicGas.Compute(Transaction)
IntrinsicGas.TryCompute(Transaction, out ulong)
```

`TryCompute(Transaction, IForkRules, out ulong)` may remain as a convenience wrapper because it requires an explicit schedule and delegates to the canonical formula.

RPC estimation and `debug_whyNot` use the exact `BlockContext.Rules` selected for the request:

```csharp
var intrinsicGas = IntrinsicGas.Compute(tx, blockContext.Rules);
```

The private RPC `ComputeIntrinsicGas` helper is deleted. Tests that previously relied on the latest-fork overload pass an explicit fork schedule.

Retrospective consumers do not recompute intrinsic gas. They read the `IntrinsicGasChargedEvent` emitted by the canonical run. This includes Workbench result metadata, gas-tree display, and audit reporting.

### RPC compatibility

`schlieren_traceJournal` remains journal-native and unchanged.

`debug_inspect` sets `EnableJournal = true`, performs one canonical execution, and projects the resulting journal tree into the existing `InspectGasNode` DTO. Property names, casing, nesting, number formatting, trace fields, diagnosis fields, and optional disable flags remain unchanged.

`debug_traceCall` keeps its current response and behavior. It does not gain or lose fields in this slice.

Legacy request `mismatches` remains accepted for JSON compatibility, though diagnosis continues to use typed discrepancies only.

### Avalonia compatibility

`BytecodeExecutionService` enables journal capture on its canonical transaction. `WorkbenchRunResult.GasTree` remains available for existing bindings, but it is a projection of `JournalGasTree`, not a trace reconstruction. `WorkbenchRunResult.IntrinsicGas` is read from the canonical `IntrinsicGasChargedEvent`; internal/system transactions report zero when no event exists.

`GenerateAuditReportAsync` uses the last canonical run's settled or derived journal gas. It does not sum depth-one trace steps or add hardcoded base/calldata gas. If no canonical run with a journal exists, report generation uses zero or reports unavailable according to the exporter's existing empty-state behavior; it never invents a total.

## Data Flow

```text
Transaction + BlockContext.Rules
            |
            v
StateTransition.ApplyTransactionAsync  (only evaluator)
            |
            +--> ExecutionResult
            |
            +--> ExecutionJournal
                    |
                    +--> JournalTraceAssembler --> schlieren_traceJournal
                    |
                    +--> JournalGasTree
                           |
                           +--> legacy InspectGasNode projection --> debug_inspect
                           |
                           +--> legacy GasTreeNode projection --> Avalonia renderer
                           |
                           +--> canonical totals --> audit report
```

## Deletion Boundaries

Production code must contain no:

- transaction method whose name or behavior represents a diagnostic re-execution;
- second copy of validation, warming, code-deposit, refund, or settlement flow;
- gas tree derived from `ExecutionTraceStep`;
- intrinsic calculator that selects `ForkRulesFactory.Latest` implicitly;
- manual `21,000 + calldata + create` formula outside `IntrinsicGas`;
- audit total formed by adding intrinsic constants to trace sums.

Comments and historical design documents may describe removed paths, but current code and README documentation must identify the journal as canonical.

## Failure Behavior

- `JournalGasTree.Build` continues to expose non-conservation; it does not hide it.
- Journal-required assemblers reject a missing journal explicitly.
- RPC converts an impossible missing-journal condition into its existing internal execution error handling; normal handlers always enable capture first.
- Intrinsic arithmetic overflow continues to use checked arithmetic from `IntrinsicGas.Compute`.
- Fork selection errors continue to be rejected by existing request validation.

## Testing Strategy

Tests are written and observed failing before production deletion or migration.

### Architecture gates

- Reflection asserts `StateTransition` exposes no `ApplyTransactionWithGasTreeAsync` or `ApplyTransactionWithFrameAsync`.
- Reflection asserts `IntrinsicGas` exposes no public `Compute(Transaction)` or `TryCompute(Transaction, out ulong)` overload.
- Source-level architecture checks assert no production manual intrinsic constants/formulas outside `IntrinsicGas` and fork schedules.
- Source-level architecture checks assert no gas-tree builder consumes `ExecutionTraceStep`.

### Canonical behavior

- A journal-enabled external transaction records exactly one intrinsic event and one settlement event.
- Nested CALL frames retain explicit parent/child IDs in the tree after legacy frame plumbing is removed.
- Exceptional burns and CALL-family non-additive movements remain represented and conserve.
- Journal disabled versus enabled produces identical execution result and state.

### Fork correctness

- Frontier CREATE estimation uses Frontier intrinsic rules and does not add the Homestead surcharge.
- Homestead CREATE includes the surcharge.
- Berlin access-list, Shanghai initcode-word, and Prague authorization/floor cases use their selected schedules.

### Compatibility

- Existing `debug_inspect` golden JSON shape remains identical.
- `debug_traceCall` contract tests remain unchanged and pass.
- `schlieren_traceJournal` frame/gas/conservation tests remain unchanged and pass.
- Avalonia Workbench gas-tree and audit-report tests assert totals come from the same canonical result/journal.

## Completion Criteria

The migration is complete when:

1. the duplicate evaluator and frame plumbing are deleted;
2. heuristic trace-derived gas-tree code is deleted;
3. ruleless and simplified intrinsic calculators are deleted;
4. all diagnostic gas views originate from `ExecutionResult.Journal`;
5. legacy RPC shapes remain unchanged;
6. focused architecture, journal, RPC, and Workbench tests pass;
7. the full repository test result is recorded honestly, including unrelated fixture-dependent failures.
