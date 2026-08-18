# Executable Per-Fork Gas Schedule and Diagnostic Journal

**Date:** 2026-08-11
**Status:** Approved design
**Decision:** Build a typed, executable per-fork gas schedule that is the single source of truth for EVM execution and gas diagnosis. No external execution client or runtime comparison tool is required.

## Purpose

Scrutor's Case Inspector must determine where gas accounting went wrong, not infer a likely EIP from a final balance delta. The foundation will be a complete internal model of every gas-affecting rule and formula for every supported fork. Execution will use that model to charge gas, and diagnostic runs will retain the model's inputs, branch decisions, components, and gas movements as evidence.

This design replaces heuristic-first gas diagnosis with formula-first accounting. Heuristics may summarize evidence, but they may not claim a root cause without a reconciled gas ledger or a violated accounting invariant.

## Goals

- Represent every gas constant, conditional charge, formula, refund, transfer, and exceptional burn for all supported forks.
- Make the gas schedule the only authority used by the EVM to calculate gas.
- Explain every calculated amount as named components with recorded inputs and branch decisions.
- Track gas conservation across transactions and nested call/create frames.
- Determine whether a discrepancy came from a formula, fork selection, input fact, conditional branch, duplicated or missing charge, transfer, return, refund, burn, or fee settlement.
- Produce evidence-backed Case Inspector results with calibrated confidence.
- Prevent gas rules from drifting back into scattered opcode literals.
- Preserve normal execution performance by making the detailed journal opt-in while always using the same formulas.

## Non-goals

- Building a complete executable specification for non-gas EVM semantics in this phase.
- Depending on EELS, Geth, Nethermind, or another client at runtime.
- Defining gas formulas in JSON, YAML, or a custom expression language.
- Claiming a gas root cause for failures that cannot be reconciled from available expected-state evidence.
- Replacing existing EELS fixtures. They remain acceptance inputs and expected-state evidence.

## Current-State Assessment

`IForkRules` and the fork inheritance chain are the correct seed, but they cover only part of gas behavior. Other formulas and constants currently live in opcode implementations, `EvmMemory`, `IntrinsicGas`, precompile classes, call/create handling, and transaction settlement. The diagnostic engine separately contains a list of recognizable gas constants. This creates multiple representations of gas truth and allows execution, tests, and diagnosis to drift.

The new design removes the separate diagnostic constant catalog. Diagnostic names and protocol references come directly from the executable schedule.

## Architectural Decisions

### Typed C# formulas

Gas formulas will be strongly typed C# functions. Formula inputs use rule-specific context records rather than string dictionaries. This provides compile-time validation, safe arithmetic, discoverable dependencies, and direct unit testing.

### Fork overlays

Each fork inherits the prior fork's immutable schedule and replaces only rules changed by that fork. The resolved schedule is complete: consumers do not walk an inheritance chain during execution.

### Single calculation path

Execution and diagnosis never implement the same formula twice. The formula returns a `GasCalculation`; execution consumes its total and side effects, while diagnostic mode also records its explanation.

### Gas transfers are not charges

Forwarded child gas, returned child gas, and settlement refunds are ledger movements. They are represented separately from consumed gas so conservation checks cannot double-count them.

### Explicit uncertainty

If the available expected state cannot determine an expected gas ledger, the Inspector reports that limitation. It does not manufacture gas units by substituting a gas price of one or promote a magnitude match to a root cause.

## Core Model

### Rule identity and metadata

Every rule has a stable `GasRuleId`, category, activation fork, protocol reference, and implementation boundary. Representative categories are:

- Transaction and validation
- Intrinsic gas
- Fixed opcode
- Memory and copy
- Account and storage access
- Storage mutation and refunds
- Calls and creates
- Logs and hashing
- Contract deployment
- Self-destruct
- Precompiles
- Frame and exceptional-halt accounting
- Final transaction settlement

Metadata is part of the rule definition and is used by reports and the Case Inspector. It is not maintained in a separate diagnostic mapping.

### Fork gas schedule

Conceptually, each supported fork resolves to:

```csharp
public sealed record ForkGasSchedule(
    Fork Fork,
    IReadOnlyDictionary<GasRuleId, IGasRule> Rules,
    GasCoverageManifest Coverage);
```

`IForkRules` retains non-gas feature behavior during this phase and exposes the resolved `ForkGasSchedule`. Existing gas properties and methods are migrated into schedule rules and then removed.

Schedule construction uses typed fork overlays:

```text
Frontier
  -> Homestead overlay
  -> Tangerine Whistle overlay
  -> ...
  -> Cancun overlay
  -> Prague overlay
  -> Osaka overlay
```

An overlay may add a rule, replace a formula, change a constant, change activation metadata, or disable a rule. Startup validation rejects missing required rules, duplicate identifiers, invalid fork ordering, or an incomplete coverage manifest.

### Formula contexts

Formula contexts contain only inputs needed by that formula. Examples include:

- `MemoryExpansionContext(currentWords, requestedEnd)`
- `AccountAccessContext(address, isWarm, exists)`
- `SstoreContext(original, current, next, isWarm, gasRemaining)`
- `CallContext(callKind, requestedGas, gasRemaining, transfersValue, targetExists, isWarm, memoryExpansion)`
- `CreateContext(createKind, initcodeLength, memoryExpansion, targetCollision, gasRemaining)`
- `PrecompileContext(address, inputLength, parsedDimensions, inputValidity)`
- `SettlementContext(gasLimit, intrinsicGas, executionGas, refundCounter, effectiveGasPrice, priorityFee)`

Facts such as warmness and account existence are recorded as inputs. The schedule calculates their gas consequences but does not silently query mutable state. This makes incorrect input facts distinguishable from incorrect formulas.

### Calculation result

Every rule returns an immutable result:

```csharp
public sealed record GasCalculation(
    GasRuleId RuleId,
    Fork Fork,
    ulong ChargedGas,
    long RefundCounterDelta,
    GasDisposition Disposition,
    IReadOnlyList<GasComponent> Components,
    IReadOnlyList<GasDecision> Decisions,
    GasRuleMetadata Metadata);
```

A `GasComponent` has a stable identifier, human label, signed amount, and optional arithmetic expression. A `GasDecision` records the tested condition, observed value, selected branch, and alternatives relevant to counterfactual diagnosis.

All arithmetic is checked. Overflow, negative charge totals, invalid divisors, impossible dimensions, or inconsistent component totals produce a typed gas-specification error before state mutation.

## Complete Formula Coverage

The coverage manifest must account for all of the following before migration is complete:

### Transaction entry and intrinsic gas

- Transaction base cost
- Contract-creation base cost
- Zero and non-zero calldata bytes by fork
- Access-list addresses and storage keys
- Initcode word cost and size limits
- Authorization-list processing and refunds
- Calldata floor rules
- Blob gas, blob fee inputs, and relevant caps
- Fork-specific transaction gas-limit validation

### Opcode execution

- Every fixed-cost opcode
- EXP byte-dependent charge
- SHA3 base, word, and memory charges
- All copy word charges
- LOG base, topic, byte, and memory charges
- Memory expansion, including the quadratic term
- Account and code access, including warm/cold behavior
- SLOAD
- Every SSTORE original/current/new-value branch
- SSTORE reentrancy guard and refund deltas
- Transient storage rules
- CALL, CALLCODE, DELEGATECALL, and STATICCALL
- Value-transfer, new-account, access, and memory components
- EIP-150 forwarding and pre-EIP-150 behavior
- Stipend grant and return semantics
- CREATE and CREATE2 base, hashing, memory, forwarding, and failure behavior
- Runtime code-deposit gas
- SELFDESTRUCT base, cold access, and new-account components

### Precompiles

- All active precompile base and length-dependent formulas
- Fork-dependent repricing
- ModExp formulas for each applicable era
- Pairing base and per-element formulas
- BLS multi-scalar discount tables and dimensional validation
- KZG, P256, and future precompile activation and pricing
- Invalid-input gas disposition where the protocol distinguishes it

### Frame exit and settlement

- Normal child gas return
- REVERT return behavior
- Exceptional-halt remaining-gas burn
- Refund-counter propagation or rollback
- Refund quotient and cap by fork
- Unused transaction gas refund
- Max-fee versus effective-price refund
- Priority-fee credit
- Base-fee burn accounting
- Code-deposit out-of-gas behavior

## Gas Accounting Journal

Detailed diagnostic execution writes an append-only `GasJournal`. Normal execution may use a no-op sink, but both modes call the same schedule.

Each `GasJournalEntry` records:

- Transaction identifier and fork
- Frame identifier, parent frame, call type, depth, contract, and code address
- Program counter and opcode when applicable
- Sequence number establishing chronological order
- Rule and formula metadata
- Gas before and after
- Calculation inputs, components, and decisions
- Movement kind: charge, transfer out, transfer in, return, refund-counter delta, burn, or settlement
- Related entry identifier for paired transfers
- Execution outcome and error classification

Parent call/create entries are opened before child execution. Child transfers and execution are nested beneath them, and the parent entry closes after unused gas returns. This preserves chronological and causal ordering.

### Conservation invariants

The journal validates these invariants with typed equations rather than display-only totals:

- Formula charged gas equals the sum of charge components.
- Gas after a direct charge equals gas before minus charged gas.
- Every transfer out has exactly one matching transfer in.
- A child cannot return more gas than it received plus an explicitly granted stipend.
- A frame's opening gas equals direct consumption, exceptional burn, returned gas, and closing gas after paired transfers are reconciled.
- Refund-counter changes equal the sum of committed refund deltas after rollback rules.
- Effective refund does not exceed the active fork's cap.
- Transaction gas limit reconciles with intrinsic gas, execution consumption, exceptional burn, effective refund, and unused gas.
- Sender refund, coinbase credit, and burned fee reconcile with the gas settlement and price inputs.

An invariant failure is itself a determined implementation defect and includes the responsible journal entries.

## Diagnostic Reasoning

### Expected ledger reconstruction

For conformance cases, Scrutor reconstructs the expected transaction ledger from fixture pre-state, expected post-state, transaction value, sender, recipient, coinbase, fee fields, and expected receipt outcome. Account roles are explicit. Sender gas cost is not conflated with coinbase priority fee or recipient value movement.

If gas price is zero or account changes do not uniquely determine expected gas, the expected ledger is marked partial. Partial evidence may still expose internal conservation failures, but it cannot support a certain end-to-end gas determination.

### Reconciliation sequence

The diagnosis engine proceeds in this order:

1. Validate schedule completeness and the journal schema.
2. Check internal conservation invariants.
3. Recalculate each journal entry from its recorded immutable inputs and verify that execution applied the returned calculation unchanged.
4. Reconcile the actual journal with the expected transaction ledger.
5. Locate missing, duplicated, misapplied, or incorrectly settled movements.
6. Evaluate bounded counterfactuals for decisions active near the unexplained residual.
7. Return a determination only when the evidence uniquely supports it.

Counterfactuals are generated by the rule definition, not by a global list of magic constants. Examples include warm versus cold access, existing versus new account, value versus no value, pre- versus post-fork formula, capped versus uncapped refund, normal return versus exceptional burn, and alternative SSTORE branches.

The engine first tests single-decision changes. It may test pairs only within the same journal entry or directly coupled parent/child entries. It does not search arbitrary combinations across an execution because that would create coincidental explanations.

### Diagnosis classifications

- `ChargeApplication`: execution applied an amount that differs from the immutable `GasCalculation` returned for the recorded inputs.
- `FormulaDefinition`: a complete expected ledger uniquely isolates one formula, its inputs and selected branch are independently confirmed, and the schedule's result conflicts with an applicable curated specification vector.
- `ForkSelection`: another supported fork's active rule uniquely reconciles the discrepancy and the selected fork is inconsistent with the case.
- `InputFact`: changing one recorded state fact to the fixture-supported fact uniquely reconciles the discrepancy.
- `BranchSelection`: the correct inputs were recorded, but the wrong formula branch was applied.
- `MissingCharge` or `DuplicateCharge`: the expected ledger and neighboring journal entries identify an absent or repeated component.
- `TransferAccounting`: forwarded, returned, or stipend gas violates a paired-transfer invariant.
- `RefundAccounting`: refund delta, rollback, propagation, cap, or settlement is wrong.
- `ExceptionalHaltAccounting`: remaining gas was returned when it should be burned, or burned when it should be returned.
- `FeeSettlement`: execution gas is correct but sender, coinbase, or burn settlement is not.
- `AmbiguousGasCause`: more than one bounded counterfactual reconciles the evidence.
- `InsufficientGasEvidence`: expected gas cannot be derived and no internal invariant failed.
- `NonGasDivergence`: the mismatch is outside this phase's gas model.

### Confidence rules

- **Certain:** a violated conservation invariant identifies a unique operation, or a complete expected ledger plus one unique counterfactual identifies a single journal decision and all formula vectors for that rule pass.
- **High:** a complete expected ledger uniquely identifies one entry or component, but the incorrect source fact cannot be independently confirmed from fixture state.
- **Medium:** one bounded explanation fits but the expected ledger is partial.
- **Low:** a formula or component is merely correlated with the residual. Low-confidence results are labeled hypotheses, never root-cause determinations.

Confidence ordering uses an explicit rank function, not enum ordinal values.

## Case Inspector Presentation

The Inspector displays five separate sections:

1. **Determination:** concise statement of the proven or best-supported defect.
2. **First broken equation:** expected and actual values with the residual.
3. **Responsible calculation:** frame, PC, opcode, rule, fork, components, inputs, and branch decisions.
4. **Evidence and confidence:** why the classification and confidence were assigned.
5. **Implementation boundary:** exact subsystem and rule identifier to inspect, plus alternative explanations when ambiguity remains.

The Inspector does not present generic category hints above stronger journal evidence. Failure clusters use a gas-cause signature composed of fork, diagnosis classification, rule identifier, decision/component identifier, call type, and residual. Cases with only partial evidence remain separate from proven clusters.

## Verification Strategy

### Formula vectors

Every rule has published input/output vectors encoded as repository tests. Vectors assert both total gas and named components. Fork-changing EIPs require boundary vectors for the last block/fork before activation and the first after activation.

### Coverage manifest

The manifest maps every active opcode, transaction feature, precompile, frame transition, and settlement operation to its gas rule identifiers for every supported fork. Tests fail if an active operation has no rule or an inactive rule is reachable.

### Literal enforcement

A repository analyzer permits protocol gas constants only inside the gas-specification namespace or explicitly allow-listed non-production test vectors. Opcode and settlement code may not introduce unexplained numeric gas literals.

### Property and invariant testing

Tests cover:

- Memory-cost monotonicity and exact word boundaries
- Checked arithmetic and overflow behavior
- EIP-150 forwarding bounds
- Gas conservation through arbitrary nested call trees
- Stipend behavior with success, failure, insufficient balance, and depth failure
- Refund rollback and propagation
- Exceptional-halt burns
- SSTORE state-transition matrices
- Precompile dimensional boundaries
- Settlement conservation across legacy and typed transactions

### Mutation testing

Representative mutations change constants, branch predicates, fork activation, component signs, transfer pairing, and refund caps. The verification suite must fail for each mutation. This demonstrates that formula correctness is independently guarded even though execution and diagnosis share the formula implementation.

### Existing fixtures

EELS state fixtures remain end-to-end conformance tests. The first acceptance goal is no regression from current fork pass rates. The second is that every gas-related failure produces either a supported determination or an explicit insufficient-evidence result—never a false certain claim.

## Error Handling

- Missing or incomplete schedules prevent execution for that fork.
- Formula arithmetic errors produce a typed internal error before the affected state mutation.
- Journal pairing or conservation errors stop diagnostic classification and surface the invariant failure.
- Unknown gas-affecting paths are recorded as uncovered rules and fail conformance diagnostics.
- Unsupported future forks fail closed rather than inheriting the latest known schedule silently.
- Diagnostic serialization preserves full integer precision and stable rule/component identifiers.

## Performance

Resolved schedules are immutable singletons per fork. Formula calls avoid reflection, dynamic expression evaluation, and dictionary-based inputs. Normal execution uses a no-op journal sink and does not allocate explanation strings. Diagnostic execution captures structured inputs and decisions, with display text generated afterward.

Benchmarks must show that normal execution overhead remains within 5% of the pre-migration baseline for representative opcode, call-heavy, storage-heavy, and precompile workloads. Diagnostic mode may cost more but must remain bounded by configured journal-entry and memory limits without dropping accounting entries required for conservation.

## Migration Sequence

1. **Inventory and guardrails:** create the coverage manifest, enumerate existing gas literals/formulas, introduce rule identifiers, and add the literal analyzer in report-only mode.
2. **Core types:** implement schedules, overlays, typed calculations, checked arithmetic, journal sinks, and validation.
3. **Foundational formulas:** migrate fixed opcodes, intrinsic gas, memory, copy, hashing, and logs.
4. **State-dependent formulas:** migrate warm/cold access, SLOAD, the full SSTORE matrix, and refunds.
5. **Frame formulas:** migrate CALL-family, CREATE-family, forwarding, stipends, child returns, code deposit, and exceptional halts.
6. **Precompiles:** migrate every precompile formula and fork activation.
7. **Settlement:** migrate refund caps, sender refunds, fee distribution, and burns.
8. **Diagnosis:** implement expected-ledger reconstruction, reconciliation, bounded counterfactuals, confidence, clustering, and Case Inspector presentation.
9. **Removal:** delete legacy gas methods, the diagnostic magic-constant catalog, and duplicate formula tests; make literal enforcement blocking.

Each migration step runs existing conformance and unit tests. A legacy path is removed only after its replacement has component-level vectors and end-to-end parity.

## Acceptance Criteria

The foundational gas milestone is complete when:

- The coverage manifest reports 100% of gas-affecting operations for every supported fork.
- Production gas constants and formulas exist only in the gas-specification subsystem.
- All execution gas charges, transfers, returns, refunds, burns, and settlements originate from `GasCalculation` results.
- Every formula has component-level vectors and relevant fork-boundary tests.
- Nested frame and transaction conservation properties pass.
- Mutation tests demonstrate detection of representative formula and accounting defects.
- Existing conformance pass rates do not regress.
- The Case Inspector can distinguish determined gas causes, ambiguous causes, insufficient evidence, and non-gas divergences.
- No `Certain` diagnosis is based only on a balance-delta magnitude or fixture-folder name.

## Follow-on Work

After the gas foundation is complete and stable, the same calculation-and-journal pattern can be extended to transaction validation, account lifecycle, storage semantics, return data, logs, and other non-gas behavior. That extension is deliberately outside this milestone so the gas model can become complete and trustworthy first.
