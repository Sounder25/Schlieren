# Executable Fork Gas Schedule Completion Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Replace Scrutor's scattered gas constants, duplicated transaction accounting, and heuristic gas diagnosis with one typed, executable, per-fork gas schedule that drives both execution and Case Inspector evidence.

**Architecture:** Add an immutable `Scrutor.Core.Gas` subsystem containing stable rule IDs, typed calculation contexts, fork overlays, calculation results, journal sinks, and invariant validation. Migrate the current execution paths category by category while preserving their public behavior. Canonical transaction execution optionally emits the detailed journal; the gas tree and diagnostics become projections over that journal instead of recalculating gas independently.

**Tech Stack:** C# 12 / .NET 8, xUnit, the existing EELS fixture harness, Roslyn analyzers, BenchmarkDotNet.

**Global Constraints:**

- Treat `docs/gas/GAS_RULE_INVENTORY.md` and `docs/gas/GAS_COVERAGE_MATRIX.md` as the migration ledger. Every one of the 177 IDs must end as implemented or explicitly diagnostic-only.
- Keep `IForkRules` responsible for non-gas feature activation, but move all gas constants and formulas to `ForkGasSchedule`.
- No runtime dependency on EELS, Geth, or another client.
- Do not change a formula and migrate its architecture in the same commit. First pin current/canonical behavior with vectors, then move it unchanged, then make any protocol correction in a separate commit.
- Forwarded gas, stipend, unused child gas, refunds, burns, and fee settlement are movements, not opcode charges.
- Normal execution uses `NullGasJournalSink`; diagnostic execution uses `RecordingGasJournalSink`. Both call the same formulas.
- Checked arithmetic and explicit overflow outcomes are mandatory at every 256-bit-to-host boundary.
- A diagnosis may be `Certain` only when a conservation failure or a unique full-ledger counterfactual identifies one rule/decision.
- Keep the repository buildable and the focused tests green after every task.

---

## Recovery checkpoint

The workflow is currently at the handoff between discovery and implementation:

- The design is approved in `docs/superpowers/specs/2026-08-11-executable-fork-gas-schedule-design.md`.
- The discovery artifacts are committed in `806dd2d`: 168 protocol rules plus 9 diagnostic rules across Frontier through Osaka.
- The original discovery checklist was not updated, but its intended inventory and matrix deliverables exist and their IDs reconcile 177-to-177.
- Production still has no `Scrutor.Core.Gas` namespace, `GasRuleId`, `GasCalculation`, executable schedule, journal, or coverage validator.
- Gas remains split among `IForkRules`, opcode literals, `IntrinsicGas`, `Precompiles`, `StateTransition`, and `ExecutionContext`.
- `ApplyTransactionWithGasTreeAsync` is a second transaction evaluator and already differs from the canonical path, including legacy intrinsic-gas defaults and a hard-coded refund divisor.
- `GasTreeBuilder` infers warmth and memory expansion from aggregate opcode costs; `DivergenceDiagnostics` maintains a second flat gas-constant catalog.
- Commit `a82041b` fixed the SDIV/MOD byte swap and cleared 70 Osaka cases in one change. The full Osaka sweep moved from 115 failures to 45 failures out of 14,516 tests: 14,471 passing, or 99.69%. The inventory still lists the old byte assignments.
- Current unit baseline: 329 pass, 1 fail. `ForkingGlobalState_UnfetchedRemoteStorage_ReturnsUnknownPresence` expects unknown storage to block CREATE2, while `AccountDeployability` now permits unknown-without-known-writes. Resolve that stale contract before using a green-suite gate.

---

### Task 1: Close the discovery phase and establish a trustworthy baseline

**Files:**

- Modify: `docs/superpowers/plans/2026-08-11-gas-rule-inventory-and-coverage.md`
- Modify: `docs/gas/GAS_RULE_INVENTORY.md`
- Modify: `docs/gas/GAS_COVERAGE_MATRIX.md`
- Modify: `Directory.Packages.props`
- Modify: `Scrutor.sln`
- Modify if canonical behavior requires it: `Scrutor.Core/State/AccountDeployability.cs`
- Modify: `Scrutor.Tests/Execution/SelfDestructAccessTests.cs`
- Modify: `two_ops_audit.runsettings`
- Create: `Scrutor.Benchmarks/Scrutor.Benchmarks.csproj`
- Create: `Scrutor.Benchmarks/GasScheduleBenchmarks.cs`
- Create: `docs/gas/GAS_SCHEDULE_BASELINE.md`
- Test: `Scrutor.Tests/Lane1_CoreEvmTests.cs`
- Test: `Scrutor.Tests/Lane2_CallSemanticsTests.cs`
- Test: `Scrutor.Tests/Execution/PrecompileGasScheduleTests.cs`

- [ ] **Step 1: Reconcile the one failing unit-test contract**

Reconcile `AccountDeployability.IsDeployableAsync` with the existing inventory finding and EIP-7610 collision rule. Because remote storage that is `Unknown` is not proven empty, restore fail-closed deployment (`storage == StoragePresence.Empty`) unless a published fixture proves a different behavior. Keep the existing test assertion that CREATE2 pushes zero. Do not mix this correctness fix with gas-schedule code.

- [ ] **Step 2: Verify the recently committed opcode/call semantics**

Run:

```powershell
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter "FullyQualifiedName~ArithmeticOpcodeTests|FullyQualifiedName~CallGasForwardingTests|FullyQualifiedName~PrecompileGasScheduleTests" --nologo
dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj --settings two_ops_audit.runsettings --filter "FullyQualifiedName~BENCHMARK_TaxonomySnapshot_AlwaysReportsCurrentMismatchCounts" --nologo
```

Before running the sweep, change `two_ops_audit.runsettings` to use `EELS_FIXTURES_ROOT=C:/projects/Scrutor/fixtures/state_tests/for_osaka/ported_static/vmArithmeticTest/two_ops`, `EELS_REQUIRED_FORK=Osaka`, and `EELS_INCLUDE_SUBDIRS=1`; remove the ignored `EELS_CASE_FILTER`. Expected: SDIV/MOD tests and all 609 `two_ops` cases pass. The CALLCODE test must agree with canonical EELS behavior: CALLCODE itself does not reject non-zero value in a static parent, while the child message inherits the parent's static flag.

- [ ] **Step 3: Repair the discovery record**

Change the inventory rows to `SDIV (0x05)` and `MOD (0x06)`, update their source evidence, mark the original discovery plan complete, and add a dated checkpoint noting `806dd2d`, `2957e05`, and `a82041b`. Mark the EIP-7610 unknown-storage finding resolved only after its focused test passes.

Record the Osaka conformance checkpoint explicitly: `Total=14,516; Passed=14,471; Failed=45; PassRate=99.69%; PriorFailed=115; CasesClearedBySdivModFix=70`. Preserve the 45 case IDs and mismatch taxonomy as the regression baseline for each migration milestone.

- [ ] **Step 4: Capture a pre-migration performance baseline**

Add a small BenchmarkDotNet project with fixed-opcode, memory/copy, SSTORE-heavy, nested-call, precompile, and transaction-settlement workloads. Add the central `BenchmarkDotNet` package version, include the project in `Scrutor.sln`, run Release benchmarks with no schedule/journal code present, and record runtime, allocation, machine, runtime, and commit hash in `GAS_SCHEDULE_BASELINE.md`.

```powershell
dotnet run -c Release --project Scrutor.Benchmarks/Scrutor.Benchmarks.csproj -- --filter "*GasSchedule*"
```

- [ ] **Step 5: Establish the correctness baseline gate**

Run:

```powershell
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --nologo
dotnet build Scrutor.sln --nologo
```

Expected: zero failed unit tests and a successful solution build. If another `testhost` holds an EELS output DLL, wait for that run to finish instead of killing an unrelated process.

- [ ] **Step 6: Commit the checkpoint**

```powershell
git add docs/superpowers/plans/2026-08-11-gas-rule-inventory-and-coverage.md docs/gas/GAS_RULE_INVENTORY.md docs/gas/GAS_COVERAGE_MATRIX.md docs/gas/GAS_SCHEDULE_BASELINE.md Directory.Packages.props Scrutor.sln Scrutor.Benchmarks Scrutor.Core/State/AccountDeployability.cs Scrutor.Tests/Execution/SelfDestructAccessTests.cs two_ops_audit.runsettings
git commit -m "docs(gas): close inventory phase and pin migration baseline"
```

### Task 2: Introduce the typed gas domain and checked calculations

**Files:**

- Create: `Scrutor.Core/Gas/GasRuleId.cs`
- Create: `Scrutor.Core/Gas/GasRuleMetadata.cs`
- Create: `Scrutor.Core/Gas/GasCalculation.cs`
- Create: `Scrutor.Core/Gas/GasContexts.cs`
- Create: `Scrutor.Core/Gas/IGasRule.cs`
- Create: `Scrutor.Core/Gas/GasMath.cs`
- Test: `Scrutor.Tests/Gas/GasCalculationTests.cs`
- Test: `Scrutor.Tests/Gas/GasMathTests.cs`

- [ ] **Step 1: Write failing value-object and arithmetic tests**

Cover component summation, signed refund deltas, duplicate component names, checked add/multiply, word rounding, and values above the host memory limit. The core result shape is:

```csharp
public readonly record struct GasCalculation(
    GasRuleId RuleId,
    Fork Fork,
    ulong ChargedGas,
    long RefundCounterDelta,
    GasDisposition Disposition,
    ImmutableArray<GasComponent> Components,
    ImmutableArray<GasDecision> Decisions);
```

- [ ] **Step 2: Run the new tests and confirm they fail to compile**

```powershell
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter "FullyQualifiedName~Scrutor.Tests.Gas" --nologo
```

- [ ] **Step 3: Implement the minimum typed model**

Use a non-generic metadata interface plus typed calculation interface:

```csharp
public interface IGasRule
{
    GasRuleId Id { get; }
    GasRuleMetadata Metadata { get; }
}

public interface IGasRule<in TContext> : IGasRule
{
    GasCalculation Calculate(in TContext context);
}
```

Add contexts for transaction entry, memory, access, SSTORE, call, create, precompile, exceptional halt, and settlement. Contexts carry immutable facts only; they do not read global state themselves.

- [ ] **Step 4: Run focused tests and commit**

```powershell
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter "FullyQualifiedName~Scrutor.Tests.Gas" --nologo
git add Scrutor.Core/Gas Scrutor.Tests/Gas
git commit -m "feat(gas): add typed calculations and checked arithmetic"
```

### Task 3: Resolve immutable per-fork schedules and validate all 177 IDs

**Files:**

- Create: `Scrutor.Core/Gas/GasRuleCatalog.cs`
- Create: `Scrutor.Core/Gas/GasCoverageManifest.cs`
- Create: `Scrutor.Core/Gas/ForkGasSchedule.cs`
- Create: `Scrutor.Core/Gas/ForkGasScheduleBuilder.cs`
- Create: `Scrutor.Core/Gas/ForkGasSchedules.cs`
- Modify: `Scrutor.Core/Forks/IForkRules.cs`
- Modify: `Scrutor.Core/Forks/ForkRules.cs`
- Test: `Scrutor.Tests/Gas/ForkGasScheduleTests.cs`
- Test: `Scrutor.Tests/Gas/GasCoverageManifestTests.cs`

- [ ] **Step 1: Encode the inventory IDs once**

Create `GasRuleCatalog.All` from the 177 stable IDs. Keep the 9 `DIAG.*` IDs marked `DiagnosticOnly`; they are covered by the manifest but are not executable charges.

- [ ] **Step 2: Write failing schedule-validation tests**

For all 14 `Fork` values, assert immutable singleton resolution, no duplicate IDs, no missing active protocol rule, no reachable inactive rule, valid activation ordering, and exact manifest/catalog equality.

- [ ] **Step 3: Implement Frontier base plus fork overlays**

`ForkGasScheduleBuilder` starts with Frontier rules, then applies ordered overlays through the requested fork. Expose the result without removing legacy gas members yet:

```csharp
public interface IForkRules
{
    Fork Fork { get; }
    ForkGasSchedule GasSchedule { get; }
    // Existing feature flags remain during migration.
}
```

- [ ] **Step 4: Make schedule completeness a startup/test invariant**

`ForkGasSchedules.For(fork)` must validate once during singleton construction and throw a detailed exception containing fork, missing ID, and expected activation.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter "FullyQualifiedName~ForkGasScheduleTests|FullyQualifiedName~GasCoverageManifestTests" --nologo
git add Scrutor.Core/Gas Scrutor.Core/Forks Scrutor.Tests/Gas
git commit -m "feat(gas): add validated per-fork schedules"
```

### Task 4: Migrate transaction entry and foundational opcode formulas

**Files:**

- Create: `Scrutor.Core/Gas/Rules/TransactionGasRules.cs`
- Create: `Scrutor.Core/Gas/Rules/OpcodeGasRules.cs`
- Create: `Scrutor.Core/Gas/Rules/MemoryGasRules.cs`
- Modify: `Scrutor.Core/Execution/IntrinsicGas.cs`
- Modify: `Scrutor.Core/Execution/EvmMemory.cs`
- Modify: `Scrutor.Core/Execution/EvmMachine.cs`
- Modify: `Scrutor.Core/Opcodes/ArithmeticOpcodes.cs`
- Modify: `Scrutor.Core/Opcodes/BitwiseOpcodes.cs`
- Modify: `Scrutor.Core/Opcodes/ComparisonOpcodes.cs`
- Modify: `Scrutor.Core/Opcodes/ControlFlowOpcodes.cs`
- Modify: `Scrutor.Core/Opcodes/ExecutionOpcodes.cs`
- Modify: `Scrutor.Core/Opcodes/LoggingOpcodes.cs`
- Modify: `Scrutor.Core/Opcodes/MemoryCopyOpcode.cs`
- Modify: `Scrutor.Core/Opcodes/MemoryOpcodes.cs`
- Modify: `Scrutor.Core/Opcodes/StackOpcodes.cs`
- Modify: `Scrutor.Core/Opcodes/StateOpcodes.cs`
- Test: `Scrutor.Tests/Gas/TransactionGasRuleTests.cs`
- Test: `Scrutor.Tests/Gas/OpcodeGasRuleTests.cs`
- Test: `Scrutor.Tests/Gas/MemoryGasRuleTests.cs`
- Test: `Scrutor.EELS.Tests/Conformance/CancunOpcodeGasConformanceTests.cs`

- [ ] **Step 1: Add boundary vectors before moving formulas**

Pin every transaction/intrinsic component, all fixed opcode tiers, memory `3*w + floor(w*w/512)`, copy words, KECCAK words, EXP bytes, and LOG topics/data. Include zero length, 31/32/33 bytes, current-size no-op, quadratic boundary, and checked overflow cases.

- [ ] **Step 2: Implement rules that return named components**

Example memory result components: `old_linear`, `old_quadratic`, `new_linear`, `new_quadratic`, and `expansion_delta`. `ChargedGas` must equal the component sum asserted by `GasCalculation`.

- [ ] **Step 3: Route execution through the schedule**

Retain `IOpcode.ExecuteAsync` during this slice, but replace literals/formulas with schedule calls. `IntrinsicGas.Compute(tx, rules)` becomes a compatibility wrapper over the transaction rule. Remove the forkless `IntrinsicGas.Compute(tx)` call sites from production.

- [ ] **Step 4: Run focused and conformance tests**

```powershell
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter "FullyQualifiedName~TransactionGasRuleTests|FullyQualifiedName~OpcodeGasRuleTests|FullyQualifiedName~MemoryGasRuleTests|FullyQualifiedName~IntrinsicGasScheduleTests" --nologo
dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj --filter "FullyQualifiedName~CancunOpcodeGasConformanceTests" --nologo
```

- [ ] **Step 5: Commit**

```powershell
git add Scrutor.Core/Gas/Rules Scrutor.Core/Execution Scrutor.Core/Opcodes Scrutor.Tests/Gas Scrutor.EELS.Tests/Conformance/CancunOpcodeGasConformanceTests.cs
git commit -m "refactor(gas): migrate intrinsic opcode and memory formulas"
```

### Task 5: Migrate access, storage, and SSTORE eras

**Files:**

- Create: `Scrutor.Core/Gas/Rules/AccessGasRules.cs`
- Create: `Scrutor.Core/Gas/Rules/StorageGasRules.cs`
- Modify: `Scrutor.Core/Opcodes/ExecutionOpcodes.cs`
- Modify: `Scrutor.Core/Opcodes/StateOpcodes.cs`
- Modify: `Scrutor.Core/Opcodes/StorageOpcodes.cs`
- Modify: `Scrutor.Core/Forks/IForkRules.cs`
- Modify: `Scrutor.Core/Forks/ForkRules.cs`
- Test: `Scrutor.Tests/Gas/AccessGasRuleTests.cs`
- Test: `Scrutor.Tests/Gas/SstoreGasRuleTests.cs`

- [ ] **Step 1: Add the complete SSTORE era matrix**

Cover Frontier, Tangerine Whistle, Constantinople-fix/Istanbul EIP-2200 branches, Berlin cold surcharge, and London refund reductions. Every vector records original/current/new values, warmness, remaining gas, charge, refund delta, and exceptional-halt outcome.

- [ ] **Step 2: Implement explicit access decisions**

Return decisions such as `address_was_warm`, `slot_was_warm`, `account_exists`, and `delegation_target_was_warm`. Mutating the access tracker happens only after a successful calculation identifies the protocol-defined touch point.

- [ ] **Step 3: Replace `SloadCost`, `SstoreBaseCost`, `ExtAccountCost`, and `ExtCodeHashCost` execution use**

Keep obsolete wrappers temporarily, mark them `[Obsolete]`, and assert no production caller remains before removal in Task 11.

- [ ] **Step 4: Run tests and commit**

```powershell
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter "FullyQualifiedName~AccessGasRuleTests|FullyQualifiedName~SstoreGasRuleTests|FullyQualifiedName~SelfDestructAccessTests" --nologo
git add Scrutor.Core/Gas/Rules Scrutor.Core/Opcodes Scrutor.Core/Forks Scrutor.Tests/Gas
git commit -m "refactor(gas): migrate access and storage schedules"
```

### Task 6: Model CALL, CREATE, SELFDESTRUCT, and frame movements

**Files:**

- Create: `Scrutor.Core/Gas/Rules/CallGasRules.cs`
- Create: `Scrutor.Core/Gas/Rules/CreateGasRules.cs`
- Create: `Scrutor.Core/Gas/Rules/SelfDestructGasRules.cs`
- Create: `Scrutor.Core/Gas/GasMovement.cs`
- Modify: `Scrutor.Core/Opcodes/SystemOpcodes.cs`
- Modify: `Scrutor.Core/Execution/ExecutionContext.cs`
- Modify: `Scrutor.Core/Execution/ExecutionResult.cs`
- Test: `Scrutor.Tests/Gas/CallGasRuleTests.cs`
- Test: `Scrutor.Tests/Gas/CreateGasRuleTests.cs`
- Test: `Scrutor.Tests/Gas/FrameMovementTests.cs`
- Test: `Scrutor.Tests/Lane2_CallSemanticsTests.cs`
- Test: `Scrutor.Tests/Execution/ContractCreationLifecycleTests.cs`
- Test: `Scrutor.Tests/Execution/ExceptionalChildGasTests.cs`

- [ ] **Step 1: Write vectors for every early exit and fork branch**

Include pre-EIP-150 forwarding, 63/64 cap, stipend, insufficient balance, depth limit, cold/warm access, new-account charge, CREATE memory and initcode words, code deposit, EIP-170 size failure, EF-prefix failure, collision, and successful/failed child gas return.

- [ ] **Step 2: Separate direct charges from movements**

Represent parent-to-child forwarding as paired `TransferOut`/`TransferIn`, stipend as `TransferIn`, unused child gas as `Return`, exceptional remaining gas as `Burn`, and refund-counter changes separately. Stop using `RefundGas` to describe child gas return.

- [ ] **Step 3: Consolidate duplicated CALL-family code around shared facts**

Build one `CallGasContext` from CALL/CALLCODE/DELEGATECALL/STATICCALL operands, then let call type determine value transfer, static-call request, caller/target/code address, and stipend. Preserve CALLCODE's inherited static child behavior.

- [ ] **Step 4: Run focused tests and commit**

```powershell
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter "FullyQualifiedName~CallGasRuleTests|FullyQualifiedName~CreateGasRuleTests|FullyQualifiedName~FrameMovementTests|FullyQualifiedName~CallGasForwardingTests|FullyQualifiedName~ContractCreationLifecycleTests|FullyQualifiedName~ExceptionalChildGasTests" --nologo
git add Scrutor.Core/Gas Scrutor.Core/Opcodes/SystemOpcodes.cs Scrutor.Core/Execution Scrutor.Tests
git commit -m "refactor(gas): model call create and frame gas movements"
```

### Task 7: Migrate every precompile schedule and failure budget

**Files:**

- Create: `Scrutor.Core/Gas/Rules/PrecompileGasRules.cs`
- Modify: `Scrutor.Core/Execution/Precompiles.cs`
- Modify: `Scrutor.Core/Execution/Bls12381Precompiles.cs`
- Modify: `Scrutor.Core/Execution/Bn254Pairing.cs`
- Modify: `Scrutor.Core/Opcodes/SystemOpcodes.cs`
- Modify: `Scrutor.Core/Execution/StateTransition.cs`
- Test: `Scrutor.Tests/Gas/PrecompileGasRuleTests.cs`
- Test: `Scrutor.Tests/Execution/PrecompileGasScheduleTests.cs`
- Test: `Scrutor.Tests/Execution/Eip7883ModExpGasTests.cs`

- [ ] **Step 1: Pin formula and invalid-input vectors**

Cover ECRECOVER, SHA256, RIPEMD160, IDENTITY, both ModExp eras plus Osaka, BN254 add/mul/pairing, BLAKE2F, KZG, nine BLS operations, and P256VERIFY. Include zero input, truncated input, invalid points, non-integral pairing input, ModExp length caps, and exact/one-less gas budgets.

- [ ] **Step 2: Move only gas calculation into schedule rules**

Cryptographic validation remains in the existing executors. A precompile calculation records length/word/point counts, fork formula selection, minimum clamp, and required gas before execution begins.

- [ ] **Step 3: Use the same rule for top-level and nested precompile calls**

Remove any separate gas path from `StateTransition` or `PrecompileExecutor`; both request the same `PrecompileGasContext` and apply the same `GasCalculation`.

- [ ] **Step 4: Run tests and commit**

```powershell
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter "FullyQualifiedName~PrecompileGasRuleTests|FullyQualifiedName~PrecompileGasScheduleTests|FullyQualifiedName~Eip7883ModExpGasTests" --nologo
git add Scrutor.Core/Gas/Rules/PrecompileGasRules.cs Scrutor.Core/Execution Scrutor.Core/Opcodes/SystemOpcodes.cs Scrutor.Tests
git commit -m "refactor(gas): migrate precompile gas schedules"
```

### Task 8: Unify refund caps and transaction fee settlement

**Files:**

- Create: `Scrutor.Core/Gas/Rules/SettlementGasRules.cs`
- Create: `Scrutor.Core/Gas/TransactionGasLedger.cs`
- Modify: `Scrutor.Core/Execution/StateTransition.cs`
- Modify: `Scrutor.Core/Execution/ExecutionResult.cs`
- Test: `Scrutor.Tests/Gas/SettlementGasRuleTests.cs`
- Test: `Scrutor.Tests/Execution/TransactionValueJournalingTests.cs`
- Test: `Scrutor.Tests/Execution/BlobTransactionFeeTests.cs`

- [ ] **Step 1: Write a complete settlement matrix**

Cover pre-London 1/2 refund cap, London+ 1/5 cap, Prague calldata floor ordering, legacy gas price, EIP-1559 effective price and base-fee burn, blob fee burn, unused gas return, price-cap refund, failed value restoration, and coinbase priority-fee credit.

- [ ] **Step 2: Implement one canonical transaction ledger**

The ledger must expose intrinsic charge, execution charge, gross used, refund counter, applied refund, floor adjustment, net used, sender upfront debit/returns, coinbase credit, base-fee burn, and blob-fee burn.

- [ ] **Step 3: Delete the second evaluator body**

Refactor `ApplyTransactionWithGasTreeAsync` to call the canonical evaluator with a recording sink. It may package extra output, but it must not recompute intrinsic gas, refund caps, fees, or state transitions.

- [ ] **Step 4: Assert value conservation and commit**

```powershell
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter "FullyQualifiedName~SettlementGasRuleTests|FullyQualifiedName~TransactionValueJournalingTests|FullyQualifiedName~BlobTransactionFeeTests" --nologo
git add Scrutor.Core/Gas Scrutor.Core/Execution Scrutor.Tests
git commit -m "refactor(gas): unify transaction settlement ledger"
```

### Task 9: Add the opt-in gas journal and enforce conservation

**Files:**

- Create: `Scrutor.Core/Gas/Journal/IGasJournalSink.cs`
- Create: `Scrutor.Core/Gas/Journal/NullGasJournalSink.cs`
- Create: `Scrutor.Core/Gas/Journal/RecordingGasJournalSink.cs`
- Create: `Scrutor.Core/Gas/Journal/GasJournalEntry.cs`
- Create: `Scrutor.Core/Gas/Journal/GasJournalValidator.cs`
- Modify: `Scrutor.Core/Execution/ExecutionContext.cs`
- Modify: `Scrutor.Core/Execution/EvmMachine.cs`
- Modify: `Scrutor.Core/Execution/StateTransition.cs`
- Test: `Scrutor.Tests/Gas/GasJournalTests.cs`
- Test: `Scrutor.Tests/Gas/GasConservationTests.cs`

- [ ] **Step 1: Write failing journal schema tests**

Assert transaction ID, fork, frame ID, parent frame, PC, opcode, rule ID, gas before/after, components, decisions, movement pair ID, outcome, and immutable inputs for recalculation.

- [ ] **Step 2: Implement sinks with no normal-path explanation allocations**

`NullGasJournalSink` is a singleton whose methods inline to no work. `RecordingGasJournalSink` enforces entry and captured-byte limits but never drops entries required to balance a frame.

- [ ] **Step 3: Emit at the application boundary**

Each execution site records the immutable `GasCalculation` it actually applied. Do not reconstruct entries from `ExecutionResult.GasUsed` after execution.

- [ ] **Step 4: Implement validators**

Check component sums, gas-before/after, paired transfers, child return bounds, frame opening/closing, refund counter and cap, exceptional burns, transaction settlement, sender/coinbase/burn conservation, and journal completeness.

- [ ] **Step 5: Run nested-frame tests and commit**

```powershell
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter "FullyQualifiedName~GasJournalTests|FullyQualifiedName~GasConservationTests|FullyQualifiedName~DeepCallRecursionTests|FullyQualifiedName~ChildRefundJournalTests" --nologo
git add Scrutor.Core/Gas/Journal Scrutor.Core/Execution Scrutor.Tests/Gas
git commit -m "feat(gas): journal exact movements and enforce conservation"
```

### Task 10: Rebuild GasTree and diagnostics as journal projections

**Files:**

- Modify: `Scrutor.Core/Execution/GasTree.cs`
- Create: `Scrutor.Core/Gas/Diagnostics/GasDiagnosis.cs`
- Create: `Scrutor.Core/Gas/Diagnostics/GasJournalAnalyzer.cs`
- Create: `Scrutor.Core/Gas/Diagnostics/GasCounterfactualAnalyzer.cs`
- Modify: `Scrutor.Core/Execution/DivergenceDiagnostics.cs`
- Modify: `Scrutor.Core/Execution/StructuralPatternRules.cs`
- Modify: `Scrutor.EELS.Tests/Conformance/Layer1DiagnosisBridge.cs`
- Modify: `Scrutor.EELS.Tests/Conformance/EelsTaxonomyAnalyzer.cs`
- Modify: `Scrutor.UI/ViewModels/WorkbenchViewModel.cs`
- Modify: `Scrutor.UI/Views/MainWindow.axaml`
- Modify: `Scrutor.UI/Views/ConformanceView.axaml`
- Test: `Scrutor.Tests/Gas/GasJournalAnalyzerTests.cs`
- Test: `Scrutor.Tests/Execution/StructuralPatternRulesTests.cs`
- Test: `Scrutor.EELS.Tests/Conformance/Layer1DiagnosisBridgeTests.cs`

- [ ] **Step 1: Write diagnosis-order and false-positive tests**

Test every classification from the design: application, definition, fork, input, branch, missing/duplicate charge, transfer, refund, exceptional halt, settlement, ambiguous, insufficient evidence, and non-gas. Reproduce the balance-sign and precompile-invalid-success false-positive cases.

- [ ] **Step 2: Build GasTree directly from journal entries**

Remove cost-threshold guesses for warm/cold access and static-base subtraction for memory. Display the recorded component and decision names.

- [ ] **Step 3: Implement recalculation and bounded counterfactuals**

First validate completeness and conservation, then recalculate each entry from recorded inputs. Test one decision change at a time; allow pairs only inside one entry or a directly coupled parent/child pair.

- [ ] **Step 4: Demote legacy heuristics**

Delete `KnownGasConstants` and folder-based `Certain` diagnoses. Structural patterns may summarize journal evidence or return `InsufficientEvidence`; they cannot outrank a reconciled ledger.

- [ ] **Step 5: Wire Case Inspector and commit**

```powershell
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter "FullyQualifiedName~GasJournalAnalyzerTests|FullyQualifiedName~StructuralPatternRulesTests" --nologo
dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj --filter "FullyQualifiedName~Layer1DiagnosisBridgeTests" --nologo
git add Scrutor.Core Scrutor.EELS.Tests/Conformance Scrutor.UI Scrutor.Tests
git commit -m "feat(inspector): diagnose gas failures from execution journal"
```

### Task 11: Enforce the single source of truth and remove migration scaffolding

**Files:**

- Create: `Scrutor.Analyzers/Scrutor.Analyzers.csproj`
- Create: `Scrutor.Analyzers/GasLiteralAnalyzer.cs`
- Create: `Scrutor.Analyzers/GasLiteralCodeFixProvider.cs`
- Modify: `Directory.Packages.props`
- Modify: `Scrutor.sln`
- Modify: `Scrutor.Core/Scrutor.Core.csproj`
- Modify: `Scrutor.Core/Forks/IForkRules.cs`
- Modify: `Scrutor.Core/Forks/ForkRules.cs`
- Modify: `Scrutor.Core/Execution/IntrinsicGas.cs`
- Modify: `Scrutor.Core/Execution/DivergenceDiagnostics.cs`
- Test: `Scrutor.Tests/Gas/GasAuthorityTests.cs`

- [ ] **Step 1: Write a failing authority test**

Scan production syntax for gas-affecting numeric literals and direct gas arithmetic outside `Scrutor.Core/Gas`. Allow only named non-gas constants and tests through a reviewed allowlist with file/line/reason.

- [ ] **Step 2: Add the Roslyn analyzer in warning mode**

Report `SCRGAS001` for new gas literals/formulas outside the gas subsystem and `SCRGAS002` for direct mutation of `GasUsed` or `GasRefundCounter` outside approved application boundaries.

- [ ] **Step 3: Remove legacy gas authority**

Delete migrated members such as `SloadCost`, `SstoreBaseCost`, gas-price properties, forkless `IntrinsicGas` overloads, and the diagnostic constant list. Remove any obsolete wrappers only after the authority test proves no production references remain.

- [ ] **Step 4: Promote analyzer diagnostics to errors and commit**

```powershell
dotnet build Scrutor.sln --nologo
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter "FullyQualifiedName~GasAuthorityTests|FullyQualifiedName~GasCoverageManifestTests" --nologo
git add Scrutor.Analyzers Directory.Packages.props Scrutor.sln Scrutor.Core Scrutor.Tests/Gas
git commit -m "build(gas): enforce schedule as the only gas authority"
```

### Task 12: Prove conformance, mutation resistance, and performance

**Files:**

- Modify: `Scrutor.Benchmarks/GasScheduleBenchmarks.cs`
- Create: `Scrutor.Tests/Gas/GasScheduleMutationTests.cs`
- Modify: `Directory.Packages.props`
- Modify: `Scrutor.sln`
- Modify: `docs/gas/GAS_COVERAGE_MATRIX.md`
- Create: `docs/gas/GAS_SCHEDULE_VERIFICATION.md`

- [ ] **Step 1: Add mutation tests**

Clone test-only schedules and alter one constant, activation fork, branch decision, and transfer amount. Assert that formula vectors, manifest activation, conservation, and diagnosis tests each catch their corresponding mutation.

- [ ] **Step 2: Add representative benchmarks**

Run the same fixed-opcode, memory/copy, SSTORE-heavy, nested-call, precompile, and settlement workloads captured in Task 1 in no-op-journal and recording-journal modes. Do not change workload inputs between the baseline and final comparison.

- [ ] **Step 3: Run the complete verification ladder**

```powershell
dotnet build Scrutor.sln -c Release --nologo
dotnet test Scrutor.Tests/Scrutor.Tests.csproj -c Release --nologo
dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj -c Release --filter "FullyQualifiedName~CancunOpcodeGasConformanceTests|FullyQualifiedName~Layer1DiagnosisBridgeTests" --nologo
dotnet run -c Release --project Scrutor.Benchmarks/Scrutor.Benchmarks.csproj -- --filter "*GasSchedule*"
```

Then run the existing per-fork EELS sweep settings from Frontier through Osaka. Record fixture version, case totals, pass/fail deltas, known non-gas failures, and benchmark ratios.

For Osaka, the migration must not regress the recorded pre-migration checkpoint of 14,471 passing and 45 failing cases out of 14,516. Any change to the 45-case remainder must be attributed by case ID and mismatch category; aggregate pass rate alone is insufficient.

- [ ] **Step 4: Enforce acceptance criteria**

Do not call the migration complete until:

- manifest coverage is 177/177 and all 168 protocol rules are executable where active;
- all gas charges and movements originate from `GasCalculation` results;
- all component and boundary vectors pass;
- nested-frame and transaction conservation pass;
- mutations are caught;
- no conformance regression remains attributable to the migration;
- normal execution overhead is at most 5% on every representative benchmark;
- Case Inspector distinguishes formula, application, fork, branch, transfer, refund, exceptional-halt, settlement, ambiguous, and non-gas outcomes;
- no `Certain` diagnosis depends only on magnitude or fixture folder.

- [ ] **Step 5: Update the matrix and commit the verification record**

Change every matrix cell from discovery status to final implemented/overridden/inactive/diagnostic-only status and link each rule to its formula tests.

```powershell
git add Scrutor.Benchmarks Scrutor.Tests/Gas Directory.Packages.props Scrutor.sln docs/gas
git commit -m "test(gas): verify conformance conservation and performance"
```

---

## Recommended execution boundaries

Implement as four reviewable milestones rather than one long-lived branch:

1. **Foundation:** Tasks 1–3 — clean baseline, types, schedules, manifest.
2. **Protocol migration:** Tasks 4–8 — formulas and canonical settlement.
3. **Evidence pipeline:** Tasks 9–10 — journal, conservation, Inspector.
4. **Enforcement:** Tasks 11–12 — analyzer, deletion, mutation, conformance, performance.

Each milestone must merge only after its focused tests and full unit suite pass. EELS sweeps are required after Tasks 4, 6, 8, 10, and 12 so a bad slice is localized immediately.
