# Phase 2: Journal and Main-Line Semantic Integration Plan

> Execute inline in the isolated `integration/journal-legacy-recovery` worktree. Do not mutate the dirty `main` worktree and do not begin Phase 3 without explicit approval.

**Goal:** Merge the complete morning journal implementation at `22193795b4b1fea15a068fc219ea34ca4f7abf6b` into the stabilized integration branch while preserving every main-line execution correction and the journal's typed, frame-aware semantics.

**Integration rule:** The canonical `StateTransition` and `EvmMachine` remain the only production execution path. The journal observes that execution and supplies derived gas, security, RPC, and UI projections. Legacy duplicate execution and reconstruction paths remain recoverable from `recovery/legacy-deleted-paths` but do not return to the production tree.

**Anchors:**

- Integration parent: `483eae24a12cdde30f8f515910647455fbfc4b6b`
- Main anchor: `20005b309b0a99e1997fdec94781f2a9bbc4dcdd`
- Morning journal tip: `22193795b4b1fea15a068fc219ea34ca4f7abf6b`
- Common base: `5e3e07e19d32525b733f670cc0efe11655284abf`
- Recovery source: `8d8950fb4169326f1363d289b2063b0099cce3a6`
- System-transaction correction: `3c0a6d81b0a76ade7578111a5ac37f500fb4b3ae`

## Non-negotiable invariants

1. Preserve `GasComponentScope`, `GasSemantics`, explicit frame IDs, parent IDs, typed state-effect events, exclusive gas accounting, explicit exceptional burns, and the server-built frame tree.
2. A real nested CALL assigns child opcodes and effects to the child frame ID.
3. CALL-family opcode observations are non-additive; ordinary opcode charges are additive.
4. The gas tree is rebuilt from exclusive canonical journal events, not legacy trace heuristics.
5. `schlieren_traceJournal` returns journal-derived DTOs with optional ephemeral `code`, full machine state by default, and `disableStack`, `disableMemory`, and `disableStorage` opt-outs.
6. `debug_inspect` and `debug_traceCall` retain their existing JSON shapes for Avalonia compatibility.
7. React consumes server-derived DTOs and a pre-built frame tree. It may traverse that tree but must not reconstruct ancestry, gas semantics, or severity rules.
8. Main-line protocol corrections remain intact, including system-transaction intrinsic-gas bypass, fork-gated code-size/create rules, transaction gas/refund behavior, EIP-7702 existence semantics, CREATE rollback, CALL gas edge cases, protocol-halt mapping, RPC validation, and SELFDESTRUCT behavior.
9. Do not restore a second production execution engine, trace-derived production gas tree, heuristic security-frame reconstruction, simplified intrinsic-gas calculator, or synthetic detector workbench.

## Task 1: Establish controlled merge state

**Files:**

- Modify: repository index and worktree only after this plan is committed

1. Verify the worktree is clean and HEAD is exactly the integration parent.
2. Commit this plan as a standalone audit checkpoint.
3. Start `git merge --no-ff --no-commit 22193795b4b1fea15a068fc219ea34ca4f7abf6b`.
4. Confirm `MERGE_HEAD` is the journal tip and list all unmerged paths.
5. Abort immediately if any path outside the previewed conflict set becomes unmerged.

Expected textual conflicts:

- `Schlieren.CLI/Commands/TraceCommand.cs`
- `Schlieren.Core/Execution/EvmMachine.cs`
- `Schlieren.Core/Execution/StateTransition.cs`
- `Schlieren.Core/Opcodes/SystemOpcodes.cs`
- `Schlieren.UI/Views/MainWindow.axaml`
- `schlieren-ui/src/App.tsx`
- `schlieren-ui/src/views/Workbench/MachineState.tsx`
- `schlieren-ui/src/views/Workbench/Workbench.tsx`

## Task 2: Resolve canonical engine conflicts

**Files:**

- Modify: `Schlieren.Core/Execution/EvmMachine.cs`
- Modify: `Schlieren.Core/Execution/StateTransition.cs`
- Modify: `Schlieren.Core/Opcodes/SystemOpcodes.cs`
- Review: `Schlieren.Core/Execution/ExecutionContext.cs`
- Review: `Schlieren.Core/Execution/ExecutionResult.cs`
- Review: `Schlieren.Core/Execution/IntrinsicGas.cs`
- Review: `Schlieren.Core/Execution/GasTree.cs`
- Review: `Schlieren.Core/Execution/Journal/*.cs`

1. In `EvmMachine`, retain journal opcode events and exceptional-burn events while also retaining main's protocol-halt exception mapping for out-of-gas, stack underflow/overflow, bad jumps, and invalid opcodes.
2. Ensure normal opcodes emit exclusive charges, CALL-family events remain non-additive observations/inclusive deltas, and exceptional halts explicitly consume the remaining frame allocation exactly once.
3. In `StateTransition`, accept removal of `ApplyTransactionWithGasTreeAsync` and its duplicate execution body. Keep `ApplyTransactionAsync` as the sole canonical path.
4. Confirm the canonical path keeps the `System` authorization intrinsic-gas bypass from `3c0a6d8`, while journal transaction-level gas components still reconcile with the final result.
5. In `SystemOpcodes`, combine main's fork gates with journal semantics: CREATE/CREATE2 surcharge follows `HasCreateTxSurcharge`, and the resulting charge is recorded as `ExclusiveCharge`/`CallLocal`.
6. Preserve the correct pre-EIP-150 child refund amount while annotating it as `Return`/`CallUnusedReturn`; verify the amount from the actual child allocation and stipend variables rather than selecting either conflict side blindly.
7. Review every automatically merged call/create/selfdestruct hunk for frame identity, state-effect identity, rollback disposition, gas allocation/return semantics, and main-line fork behavior.
8. Search the production tree for references to `ApplyTransactionWithGasTree`, `GasTreeFromTrace`, and removed legacy detector classes. Production references must be zero.

## Task 3: Accept retired duplicate-path deletions deliberately

**Files removed by the journal merge:**

- `Schlieren.Core/Execution/GasTreeFromTrace.cs`
- `Schlieren.Core/Security/LiveReentrancyDetector.cs`
- `Schlieren.Core/Security/LiveStorageCollisionDetector.cs`
- `Schlieren.Core/Security/ReentrancyDetector.cs`
- `Schlieren.Core/Security/StorageCollisionDetector.cs`
- `Schlieren.Tests/Security/LiveReentrancyDetectorTests.cs`
- `Schlieren.Tests/Security/LiveStorageCollisionDetectorTests.cs`
- `Schlieren.Tests/Security/ReentrancyDetectorTests.cs`
- `Schlieren.UI/Services/WorkbenchExecutionService.cs`

1. Accept these deletions in the integration tree because their old behavior is preserved at `recovery/legacy-deleted-paths`.
2. Confirm replacement coverage through `JournalSecurityAnalyzer`, typed state effects, `LegacyGasTreeProjection`, canonical journal DTOs, and their tests.
3. Do not reconnect any deleted type to RPC, Avalonia, React, or the execution engine.

## Task 4: Resolve CLI and RPC semantics

**Files:**

- Modify: `Schlieren.CLI/Commands/TraceCommand.cs`
- Review: `Schlieren.RPC/Handlers/EthHandlers.cs`
- Review: `Schlieren.RPC/RpcRouter.cs`
- Review: `Schlieren.RPC/Handlers/JournalTraceRequestParser.cs`

1. Resolve `TraceCommand` around the journal request/response flow while preserving main's safe transaction-field parsing and receipt handling.
2. Keep the explicit rejection for journal replay of contract-creation transactions unless the endpoint itself supports an unambiguous target.
3. Verify `EthHandlers` uses the canonical `IntrinsicGas.Compute(tx, block.Rules)` and does not reintroduce a simplified intrinsic calculator.
4. Verify `schlieren_traceJournal` is registered once, enables the journal, supports optional ephemeral code, defaults stack/memory/storage on, and passes opt-out flags to the assembler.
5. Verify legacy debug endpoints retain their prior public JSON contract even if their internal data now comes from journal projections.

## Task 5: Resolve Avalonia and React consumers

**Files:**

- Modify: `Schlieren.UI/Views/MainWindow.axaml`
- Review: `Schlieren.UI/Services/BytecodeExecutionService.cs`
- Review: `Schlieren.UI/Services/ConformanceRunService.cs`
- Review: `Schlieren.UI/ViewModels/WorkbenchViewModel.cs`
- Modify: `schlieren-ui/src/App.tsx`
- Review: `schlieren-ui/src/components/CaseBar.tsx`
- Modify: `schlieren-ui/src/views/Workbench/MachineState.tsx`
- Modify: `schlieren-ui/src/views/Workbench/Workbench.tsx`
- Review: `schlieren-ui/src/engine/rpc.ts`
- Review: `schlieren-ui/src/engine/store.ts`
- Review: `schlieren-ui/src/engine/journalTrace.ts`

1. In Avalonia XAML, preserve the valid style/resource structure from main while accepting removal of synthetic detector controls and bindings. Validate XAML by building the project.
2. Preserve the Phase 1A `HasSecurityFindings` compatibility property only if remaining XAML still binds to it; remove neither property nor binding independently.
3. Keep React's Phase 1A TypeScript fixes (`type` imports and layout helper names) while accepting the journal-native components and state.
4. Confirm React navigates the server-provided frame tree and does not derive parent/child relationships or severity locally.
5. Confirm the UI defaults to journal snapshots with stack, memory, and storage present and honors explicit disable flags.

## Task 6: Audit automatic merges and documentation

**Files:**

- Review: `README.md`
- Review: `Schlieren.Tests/Execution/SelfDestructAccessTests.cs`
- Review: all files changed by both sides but automatically merged
- Review: all journal DTO, analysis, EELS-alignment, and security files added by the morning branch

1. Inspect all automatic merges for duplicated methods, stale overloads, conflict-marker residue, or main fixes silently overwritten by nearby journal edits.
2. Confirm README describes the canonical journal, explicit gas semantics, frame-aware state effects, `schlieren_traceJournal`, default snapshot behavior, React DTO consumption, and unchanged legacy endpoint contracts.
3. Confirm there is no guidance directing users back to legacy diagnostic execution.

## Task 7: Phase 2 verification gate

1. Search for unresolved conflict markers and verify the index has no unmerged entries.
2. Build the full .NET solution. Required: zero errors. Record warnings separately.
3. Build the React application. Required: zero TypeScript/build errors. Record bundle-size warnings separately.
4. Run focused journal tests, including machine events, state effects, gas tree, security analysis, DTO assembly, request parsing, and EELS alignment.
5. Run the conservation and nested-frame acceptance tests that were red in Phase 1A. Required: conservation reconciles and child opcodes/effects use the explicit child frame ID.
6. Run JSON-contract tests for `debug_inspect` and `debug_traceCall`, plus endpoint tests for `schlieren_traceJournal` defaults and opt-outs.
7. Run the focused EELS suite used in Phase 1A. Compare results to the 141-pass baseline.
8. Run the full core suite. Compare against Phase 1A: the nine journal acceptance failures must be gone; fixture-path and preexisting Berlin-oracle issues must be reported, not disguised.
9. Inspect `git diff --check`, the final merge diff, and both protected worktree fingerprints.
10. Commit the merge only if the required gate passes and the only remaining failures are independently identified baseline/environment issues.
11. Write the Phase 2 report to the user-facing outputs directory and stop for explicit Phase 3 authorization.

## Stop conditions

Abort the merge and return to the committed plan checkpoint if resolution would require any of the following:

- weakening or deleting typed journal semantics;
- reintroducing a duplicate production execution path;
- changing a legacy JSON contract without explicit authorization;
- replacing an entire canonical engine file without a documented semantic comparison;
- modifying the dirty `main` worktree;
- proceeding to legacy restoration/Phase 3 work.
