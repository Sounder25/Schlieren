# Strategic Campaign Certification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. If the user explicitly authorizes subagents, `superpowers:subagent-driven-development` may be used. Stop after every task for review.

**Goal:** Bring Campaigns 2-7 to six independently certified 50/50 results and issue one same-commit 300/300 umbrella certificate, with Storage Lifecycle renewed as a seventh 50/50 regression prerequisite.

**Architecture:** Repair Harvest's measuring path before changing consensus behavior. Decode fixture transactions into an explicit typed envelope, execute only through canonical `StateTransition`/`EvmMachine`, recluster the unchanged manifests, and repair one journal-supported causal family at a time. Extend certification with content-hashed suite-gate and umbrella records that reconcile exact commit, EELS, fixture, manifest, and run identities.

**Tech stack:** .NET 8, C# 12, xUnit, `System.CommandLine`, `System.Text.Json`, canonical Schlieren EVM, pinned `ethereum-spec-evm` 2.19.0, append-only Harvest ledger.

**Spec:** `docs/superpowers/specs/2026-08-26-strategic-campaign-certification-design.md`

## Starting evidence

- Planning base: `f532259`; approved design commit: `d05e33f`.
- Storage historical result: 50/50 at `cf20f21`, certificate `cert-20260825224015-673d69`.
- Call and Create historical results: 50/50 at `aa491c9`, not certified.
- Return Data: 49 pass / 1 harness error at `2159de2`.
- Self-Destruct: 34 pass / 16 divergences at `aa491c9`.
- Transient Storage: 48 pass / 2 divergences at `aa491c9`.
- Access List/Fee Market: 47 pass / 1 divergence / 2 harness errors at `aa491c9`.
- The six existing 50-case manifests are frozen. Do not edit or replace them.

The certification train uses these exact manifests:

| Campaign | Manifest |
| --- | --- |
| Storage Lifecycle | `harvest/ledger/campaigns/storage-lifecycle-v1/64d1a71f69d31696fc33cd323361cb51439c76ed7988bfaf09d75cb55afb197e/manifest.json` |
| Call Semantics | `harvest/ledger/campaigns/call-semantics-v1/e20d55edb7e1fdd237df690d522cf217e4852d4dd03e10a864329a078b9d29b2/manifest.json` |
| Create Semantics | `harvest/ledger/campaigns/create-semantics-v1/986d34083db2d9d57ca85df71f33ce4b75e09f4864519fc92d755570dccadb6a/manifest.json` |
| Return Data | `harvest/ledger/campaigns/return-data-v1/c2443f285e5f3ab4a6da403c24c1f25c11377d42d4ecb591a83763dd554e8c0b/manifest.json` |
| Self-Destruct | `harvest/ledger/campaigns/selfdestruct-v1/90f041ed06ff6b54891eec791d527c16f4397b9131219fd1aafe56efd947e397/manifest.json` |
| Transient Storage | `harvest/ledger/campaigns/transient-storage-v1/171209fd3a8d54d5189f30c87da5e846a70c066f9ce2c36f469659575a0ec715/manifest.json` |
| Access List/Fee Market | `harvest/ledger/campaigns/access-list-fee-market-v1/ebc6f5d9b4106a1f24de28f6ecb73c84ab0c1f57822a2978cd3f8acf03409ef0/manifest.json` |

## Global execution rules

- Begin every task from a clean tree and record `git rev-parse HEAD`.
- Work red/green: add the listed failing test, capture the failure, make the smallest production correction, rerun focused tests, then run the listed regression gate.
- Commit only the files declared by the task. Stop after the commit and publish a test report in `passed / failed / skipped / total` form.
- Do not start a later task while the current task is dirty, unreviewed, or failing.
- No new blocking acceptance detail may be added after a task starts unless the plan is amended and approved first. A demonstrated false-certificate, consensus-corruption, credential, or data-loss defect is the only emergency exception.
- Do not edit a frozen manifest or finalized ledger artifact. Every run, comparison, repair order, gate, and certificate is append-only.
- Do not use Schlieren output as expected data. Ground truth remains the pinned fixture and EELS oracle.
- Do not fix an EVM discrepancy while a campaign has `HarnessError` or `Aborted` outcomes.
- Never treat timeout, crash, missing fixture, malformed output, quarantine, or skipped execution as a pass.
- Do not add a diagnostic execution path or reimplement consensus logic inside Harvest.
- Do not change existing RPC JSON contracts or React/Avalonia behavior in this plan.
- Any canonical EVM repair must add a focused permanent regression and preserve journal-on/off outcome parity.
- A formerly passing frozen case that fails is an introduced regression and blocks certification.

## Standard commands

Set machine-local paths without committing them:

```powershell
$env:EELS_FIXTURES_ROOT = '<absolute fixture root>'
$env:EELS_EXE = '<absolute ethereum-spec-evm executable>'
$ledger = 'harvest/ledger'
$cli = 'Schlieren.Cli/bin/Release/net8.0/Schlieren.CLI.exe'
```

Build and unit-test gates:

```powershell
dotnet build Schlieren.sln -c Release
dotnet test Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj -c Release --no-build
dotnet test Schlieren.Tests/Schlieren.Tests.csproj -c Release --no-build
```

Campaign run template:

```powershell
& $cli harvest campaign run '<manifest path>' --ledger $ledger --timeout-seconds 120
```

Every campaign command must print the exact manifest hash, Schlieren full commit, EELS version and SHA-256, fixture-root identity, worker identity, timeout policy, and finalized run ID. Short or unknown commit IDs are certification refusals.

---

## Task 0: Freeze the certification-train intake

**Files:**

- Add: `docs/harvest/baselines/2026-08-26-strategic-campaign-intake.md`
- Add: `harvest/ledger/reports/strategic-campaign-train-status.md`

- [ ] **Step 1: Verify immutable inputs**

Hash and record the seven manifest files, EELS executable, fixture-root identity, worker executable, current commit, repository cleanliness, OS, runtime, and processor count. Confirm each new manifest contains exactly 50 ordered cases. Compare the recorded hashes to the historical ledger; stop if any frozen manifest changed.

- [ ] **Step 2: Reconcile historical evidence**

Read the finalized `run.json` and `complete.json` records listed in Starting evidence. Record each status count and exact failing case ID. Do not rerun yet and do not restate an inferred family as confirmed fact.

- [ ] **Step 3: Capture current unit baseline twice**

```powershell
dotnet build Schlieren.sln -c Release
dotnet test Schlieren.sln -c Release --no-build --logger "trx;LogFileName=strategic-intake-1.trx"
dotnet test Schlieren.sln -c Release --no-build --logger "trx;LogFileName=strategic-intake-2.trx"
```

Expected: identical test identities and totals. Record any difference as a blocking apparatus defect.

- [ ] **Step 4: Commit intake evidence**

```powershell
git add docs/harvest/baselines/2026-08-26-strategic-campaign-intake.md harvest/ledger/reports/strategic-campaign-train-status.md
git commit -m "test: record strategic campaign certification intake"
```

**Stop:** review the recorded hashes and failures before Task 1.

---

## Task 1: Make oracle execution observable and correctly configured

**Files:**

- Modify: `Schlieren.Harvest/Execution/EelsProcessOracle.cs`
- Modify: `Schlieren.Harvest/Execution/IReferenceOracle.cs`
- Modify: `Schlieren.Harvest/Campaigns/SubprocessCaseWorker.cs`
- Modify: `Schlieren.Harvest/Domain/HarvestTypes.cs`
- Modify: `Schlieren.Harvest/Ledger/LedgerTypes.cs`
- Modify: `Schlieren.Harvest/Reporting/MarkdownRunReport.cs`
- Modify: `Schlieren.Cli/Commands/HarvestCommand.cs`
- Modify: `Schlieren.Harvest.Tests/Execution/WorkerExitClassifierTests.cs`
- Add: `Schlieren.Harvest.Tests/Execution/EelsProcessOracleTests.cs`
- Add: `Schlieren.Harvest.Tests/Campaigns/SubprocessCaseWorkerTests.cs`
- Modify: `Schlieren.Tests/CLI/HarvestCommandTests.cs`

- [ ] **Step 1: Write failing configuration and evidence tests**

Add tests proving:

- `EELS_EXE` is required or explicitly supplied; no compiled machine path is used;
- the campaign command rejects a missing executable before creating a run;
- timeout and non-zero exit retain exit kind, elapsed time, bounded stdout/stderr digests, executable identity, and a stable reason code;
- cancellation differs from timeout;
- worker timeout/crash/protocol failure produce durable `Aborted`, while oracle failure produces durable `HarnessError`;
- diagnostic text is data, never parsed to decide the status.

Introduce typed evidence equivalent to:

```csharp
public enum ApparatusFailureKind
{
    OracleTimeout, OracleExit, OracleProtocol,
    WorkerTimeout, WorkerCrash, WorkerProtocol, Cancelled
}

public sealed record ExecutionAttemptEvidence(
    ApparatusFailureKind? FailureKind,
    TimeSpan Elapsed,
    int? ExitCode,
    string StdoutSha256,
    string StderrSha256,
    bool DiagnosticRetentionReduced);
```

Persist this as structured case evidence without changing the six-way `CaseStatus` enum.

- [ ] **Step 2: Prove current failures**

```powershell
dotnet test Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj --filter "FullyQualifiedName~EelsProcessOracleTests|FullyQualifiedName~SubprocessCaseWorkerTests" --no-restore
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~HarvestCommandTests --no-restore
```

Expected: the new tests fail because the command hard-codes `C:/projects/eels-venv/...` and case evidence is currently flattened into strings or null snapshots.

- [ ] **Step 3: Implement typed apparatus evidence**

Use argument-safe `ProcessStartInfo.ArgumentList`, await both redirected streams to completion, kill the whole process tree on timeout, and hash retained stream content. Read `EELS_EXE` at the CLI composition root. Preserve the pinned version and executable digest from the manifest; a mismatch refuses execution.

- [ ] **Step 4: Verify**

```powershell
dotnet test Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj --filter "FullyQualifiedName~EelsProcessOracle|FullyQualifiedName~SubprocessCaseWorker|FullyQualifiedName~WorkerExitClassifier|FullyQualifiedName~CampaignRunner" --no-restore
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~HarvestCommandTests --no-restore
dotnet test Schlieren.sln --no-restore
```

- [ ] **Step 5: Commit**

```powershell
git add Schlieren.Harvest Schlieren.Harvest.Tests Schlieren.Cli/Commands/HarvestCommand.cs Schlieren.Tests/CLI/HarvestCommandTests.cs
git commit -m "fix: preserve typed harvest apparatus evidence"
```

**Stop:** confirm no campaign engine result was changed.

---

## Task 2: Execute the exact Return Data timeout case successfully

**Files:**

- Modify: `Schlieren.Harvest/Worker/WorkerProtocol.cs`
- Modify: `Schlieren.Harvest.Worker/Program.cs`
- Modify: `Schlieren.Harvest/Execution/SchlierenCaseExecutor.cs`
- Modify: `Schlieren.Harvest/Campaigns/SubprocessCaseWorker.cs`
- Add: `Schlieren.Harvest.Tests/Execution/DiagnosticRetentionTests.cs`
- Modify: `Schlieren.Harvest.Tests/Campaigns/SubprocessCaseWorkerTests.cs`
- Modify: `harvest/ledger/reports/strategic-campaign-train-status.md`

- [ ] **Step 1: Add a failing bounded-retention contract**

Extend `ExecuteRequest` with an explicit observation policy:

```csharp
public sealed record ExecutionObservationPolicy(
    bool Journal,
    bool TraceSteps,
    bool StackSnapshots,
    bool MemorySnapshots,
    bool StorageSnapshots);
```

Test that disabling diagnostic retention leaves status, gas, refund, return data, logs, and post-state identical. Test that the worker reports the selected policy in its evidence.

- [ ] **Step 2: Reproduce the exact frozen case**

Run only:

```text
tests/frontier/opcodes/test_all_opcodes.py::test_stack_overflow[fork_Berlin-opcode_RETURNDATASIZE-state_test-fails_False]
```

First run it directly through pinned EELS and then through the Harvest case boundary. Record elapsed time and peak working set if available. Expected pre-fix: the frozen case reaches the existing timeout and is `HarnessError`.

- [ ] **Step 3: Implement bounded observation, not altered execution**

Default campaign comparison to journal/trace/snapshot retention off unless the manifest requires journal evidence. Do not alter bytecode, gas, stack limit, prestate, fork, or expected output. Keep required comparison outputs.

- [ ] **Step 4: Verify the exact case and the full frozen manifest**

Run the exact case three times. All three must complete and produce the same oracle and Schlieren outputs. Then run the unchanged Return Data manifest.

Expected: 50 pass, 0 divergence, 0 fixture invalid, 0 harness error, 0 aborted, 0 quarantined. If EELS independently cannot complete, stop; preserve the original campaign as uncertified and open the version-2-manifest decision described by the spec.

- [ ] **Step 5: Commit code and new append-only evidence**

```powershell
git add Schlieren.Harvest Schlieren.Harvest.Worker Schlieren.Harvest.Tests harvest/ledger docs/harvest
git commit -m "fix: bound harvest diagnostic retention for deep cases"
```

**Stop:** Task 2 is accepted only if the exact frozen case succeeds; substitution is forbidden.

---

## Task 3: Resolve EELS/fixture validity authority without guessing

**Files:**

- Modify: `Schlieren.Harvest/Execution/EelsOutputParser.cs`
- Modify: `Schlieren.Harvest/Execution/FixtureSnapshotBuilder.cs`
- Modify: `Schlieren.Harvest/Campaigns/SubprocessCaseWorker.cs`
- Add: `Schlieren.Harvest/Execution/TransactionValidityExpectation.cs`
- Modify: `Schlieren.Harvest.Tests/Execution/EelsOutputParserTests.cs`
- Add: `Schlieren.Harvest.Tests/Execution/FixtureValidityAuthorityTests.cs`
- Modify: `Schlieren.Harvest.Tests/Campaigns/SubprocessCaseWorkerTests.cs`
- Modify: `harvest/ledger/reports/strategic-campaign-train-status.md`

- [ ] **Step 1: Write failing tests from the two frozen access-list cases**

Add minimized fixture samples preserving the type-1 Berlin and type-2 Cancun intrinsic-gas geometry. Assert that `expectException` means transaction invalidity and is not inferred from the existence of a post-state record. Assert that EELS case matching is exact; never fall back to the first returned entry when a case ID was requested.

- [ ] **Step 2: Capture three independent facts**

For each case record:

- raw fixture validity declaration;
- exact matching EELS case result;
- normalized expected validity used by the comparator.

If fixture and EELS truly disagree, stop and report an oracle/fixture conflict. Do not change expected output to match Schlieren.

- [ ] **Step 3: Implement typed validity normalization**

Return `Valid`, `Invalid(reason)`, or `Unavailable(reason)` from the fixture builder. `Unavailable` is `FixtureInvalid`; oracle parse/match failure is `HarnessError`; a proven authority disagreement remains `HarnessError`. Remove first-entry fallback.

- [ ] **Step 4: Verify Access List apparatus outcomes**

Run the two exact cases, then the unchanged Access List/Fee Market manifest. Acceptance for this task is zero `HarnessError` and zero `Aborted`; engine divergences may remain and are carried to Task 5.

- [ ] **Step 5: Commit**

```powershell
git add Schlieren.Harvest Schlieren.Harvest.Tests harvest/ledger docs/harvest
git commit -m "fix: normalize fixture transaction validity"
```

**Stop:** no canonical EVM file is modified in Task 3.

---

## Task 4: Decode explicit typed transaction envelopes

**Files:**

- Add: `Schlieren.Harvest/Execution/FixtureTransactionEnvelope.cs`
- Add: `Schlieren.Harvest/Execution/FixtureTransactionDecoder.cs`
- Modify: `Schlieren.Harvest/Execution/SchlierenCaseExecutor.cs`
- Add: `Schlieren.Harvest.Tests/Execution/FixtureTransactionDecoderTests.cs`
- Modify: `Schlieren.Harvest.Tests/Execution/SchlierenCaseExecutorTests.cs`
- Modify: `Schlieren.Tests/Execution/BlobTransactionFeeTests.cs`
- Add: `Schlieren.Tests/Execution/TypedTransactionEnvelopeTests.cs`

- [ ] **Step 1: Write failing type-presence tests**

Cover explicit types 0, 1, 2, 3, and 4; empty type-1 access list; zero type-2 fee fields; type-3 blob hashes/fee; type-4 authorization list; missing type with uniquely inferable fields; and ambiguous/contradictory combinations.

The decoded model preserves presence independently from value:

```csharp
public sealed record FixtureTransactionEnvelope(
    byte Type,
    bool TypeWasExplicit,
    Optional<BigInteger> GasPrice,
    Optional<BigInteger> MaxFeePerGas,
    Optional<BigInteger> MaxPriorityFeePerGas,
    Optional<IReadOnlyList<AccessListEntry>> AccessList,
    Optional<BigInteger> MaxFeePerBlobGas,
    Optional<IReadOnlyList<byte[]>> BlobVersionedHashes,
    Optional<IReadOnlyList<Eip7702Authorization>> AuthorizationList);
```

Use an equivalent existing optional-value type if one is already canonical; do not use numeric zero as absence.

- [ ] **Step 2: Prove current misclassification**

```powershell
dotnet test Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj --filter "FullyQualifiedName~FixtureTransactionDecoderTests|FullyQualifiedName~SchlierenCaseExecutorTests" --no-restore
```

Expected: empty-list type 1, zero-fee type 2, and type 3/4 cases fail against value-based inference.

- [ ] **Step 3: Implement decoder and executor mapping**

The decoder owns fixture shape interpretation. The executor maps the typed envelope to `Schlieren.Core.State.Transaction` without inventing prices or treating type 3 as type 2. Reject ambiguous input with a stable fixture-invalid reason.

- [ ] **Step 4: Replace the weak type-2 regression**

Keep the historical test name only if useful, but assert exact sender balance, exact gas used, exact effective price, and exact recipient/coinbase changes. Add exact type-1/3/4 counterparts where the fixture model represents them.

- [ ] **Step 5: Verify and commit**

```powershell
dotnet test Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj --filter "FullyQualifiedName~FixtureTransaction|FullyQualifiedName~SchlierenCaseExecutor" --no-restore
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter "FullyQualifiedName~TypedTransactionEnvelope|FullyQualifiedName~BlobTransactionFee" --no-restore
dotnet test Schlieren.sln --no-restore
git add Schlieren.Harvest Schlieren.Harvest.Tests Schlieren.Tests/Execution
git commit -m "fix: decode explicit harvest transaction envelopes"
```

**Stop:** report exact envelope cases and accounting equations proven.

---

## Task 5: Reinspect all six manifests and freeze the post-apparatus family map

**Files:**

- Add: `docs/harvest/baselines/2026-08-26-post-apparatus-reinspection.md`
- Modify: `harvest/ledger/reports/strategic-campaign-train-status.md`
- Add: append-only records under `harvest/ledger/runs/` and `harvest/ledger/comparisons/`

- [ ] **Step 1: Build once and record candidate identity**

```powershell
dotnet build Schlieren.sln -c Release
git status --short
git rev-parse HEAD
```

- [ ] **Step 2: Run all six unchanged manifests**

Run Call, Create, Return Data, Self-Destruct, Transient Storage, and Access List/Fee Market. Do not begin an EVM repair during the run set.

- [ ] **Step 3: Enforce the apparatus gate**

Expected: 300 terminal outcomes, zero `FixtureInvalid`, `HarnessError`, `Aborted`, and `Quarantined`. If any apparatus status exists, stop and amend Tasks 1-3; do not proceed to Task 6.

- [ ] **Step 4: Recluster typed divergences**

Generate new comparisons against historical baselines. Record exact discrepancy fields, forks, first journal-supported causal boundary, affected case IDs, and whether prior hypotheses survived. Open numbered repair orders for every confirmed family.

- [ ] **Step 5: Commit evidence only**

```powershell
git add docs/harvest harvest/ledger
git commit -m "test: record strategic post-apparatus reinspection"
```

**Stop:** user reviews and approves the confirmed repair order. Tasks 6-9 may be amended to match the evidence before their implementation begins.

---

## Task 6: Repair the Self-Destruct account-lifecycle family

**Provisional files; confirm at Task 5 review:**

- Modify: `Schlieren.Core/Opcodes/SystemOpcodes.cs`
- Modify: `Schlieren.Core/Execution/StateTransition.cs`
- Modify: `Schlieren.Core/State/StateOverlay.cs`
- Modify: `Schlieren.Tests/Execution/SelfDestructAccessTests.cs`
- Modify: `Schlieren.Tests/Execution/ContractCreationLifecycleTests.cs`
- Add: `Schlieren.Tests/Execution/SelfDestructReentryLifecycleTests.cs`
- Modify: relevant repair and run records under `harvest/ledger/`

- [ ] **Step 1: Minimize one representative per lifecycle geometry**

Cover pre-existing and same-transaction-created contracts, reentry after self-destruct, repeated self-destruct, beneficiary transfer, child commit, child revert, and ancestor revert across Cancun, Prague, and Osaka.

- [ ] **Step 2: Prove the current account-existence mismatch**

Tests must assert account existence, code, nonce, balance, storage, beneficiary balance, and journal disposition—not only status.

- [ ] **Step 3: Repair canonical lifecycle finalization**

Preserve EIP-6780: transfer occurs, but deletion after Cancun is restricted to accounts created in the same transaction. Track transaction-scoped created/destroyed identities through nested overlays and apply deletion only at successful transaction finalization. Rolled-back frames must not leak deletion or creation marks.

- [ ] **Step 4: Verify**

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter "FullyQualifiedName~SelfDestruct|FullyQualifiedName~ContractCreationLifecycle" --no-restore
dotnet test Schlieren.sln --no-restore
```

Rerun all 50 Self-Destruct cases. Expected: the confirmed account-lifecycle family is eliminated, no formerly passing case regresses, and any separate return-data family remains separately visible.

- [ ] **Step 5: Commit**

```powershell
git add Schlieren.Core Schlieren.Tests/Execution harvest/ledger docs/harvest
git commit -m "fix: preserve selfdestruct transaction lifecycle"
```

**Stop:** close only the account-lifecycle repair order.

---

## Task 7: Repair the Self-Destruct return-data family

**Provisional files; confirm at Task 5 review:**

- Modify: `Schlieren.Core/Opcodes/SystemOpcodes.cs`
- Modify: `Schlieren.Core/Execution/EvmMachine.cs`
- Add: `Schlieren.Tests/Execution/CreateReturnDataLifecycleTests.cs`
- Modify: `Schlieren.Tests/Execution/ContractCreationLifecycleTests.cs`
- Modify: relevant repair and run records under `harvest/ledger/`

- [ ] **Step 1: Reproduce the exact nested create/destroy case**

Assert success flag, created address, caller `LastReturnData`, copied memory, child return data, runtime code, account existence, and dispositions for two creations in one transaction.

- [ ] **Step 2: Locate the earliest ownership error**

Use explicit frame IDs and instruction-linked journal events. Determine whether `0x36` is stale initialization output, child runtime code, or caller return data before changing code.

- [ ] **Step 3: Repair the canonical return-data boundary**

Creation success exposes the created address on stack; creation failure exposes zero. Initialization output becomes runtime code and must not become the parent's call-like return buffer unless the fork rule explicitly requires it. Do not add a trace-only correction.

- [ ] **Step 4: Verify and commit**

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter "FullyQualifiedName~CreateReturnData|FullyQualifiedName~ContractCreationLifecycle|FullyQualifiedName~ReturnData" --no-restore
dotnet test Schlieren.sln --no-restore
```

Rerun all 50 Self-Destruct cases. Acceptance: 50/50 and no apparatus status.

```powershell
git add Schlieren.Core Schlieren.Tests/Execution harvest/ledger docs/harvest
git commit -m "fix: isolate creation return data ownership"
```

**Stop:** Self-Destruct may be certificate-ready but is not certified until Task 11.

---

## Task 8: Repair remaining Transient Storage families

**Provisional files; confirm after Task 5:**

- Modify: `Schlieren.Core/Execution/StateTransition.cs`
- Modify: `Schlieren.Core/Execution/ExecutionContext.cs`
- Modify: `Schlieren.Core/Opcodes/StorageOpcodes.cs`
- Modify: `Schlieren.Tests/Execution/TransientStorageEffectJournalTests.cs`
- Add: `Schlieren.Tests/Execution/TransientStorageLifecycleTests.cs`
- Modify: `Schlieren.Tests/Execution/TransactionValueJournalingTests.cs`
- Modify: relevant repair and run records under `harvest/ledger/`

- [ ] **Step 1: Rebaseline after the envelope fix**

Do not assume both historical divergences remain. Split balance/fee defects from transient-storage ownership defects using typed deltas.

- [ ] **Step 2: Write one failing test per confirmed family**

Cover transaction-wide lifetime, address ownership under `CALL` and `DELEGATECALL`, static-context write rejection, nested commit, nested revert, ancestor revert, and clearing between transactions. For EIP-7702 reentry, assert exact sender balance, gas, delegated code identity, and account existence.

- [ ] **Step 3: Repair the earliest canonical boundary**

Transient storage is transaction-scoped and frame-transactional: child success commits to its parent view, child revert discards writes, and the root clears after the transaction. Storage ownership follows the execution context required by the opcode/call type. Keep journal events observational.

- [ ] **Step 4: Verify and commit**

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter "FullyQualifiedName~TransientStorage|FullyQualifiedName~TransactionValueJournaling" --no-restore
dotnet test Schlieren.sln --no-restore
```

Rerun all 50 Transient Storage cases. Acceptance: 50/50.

```powershell
git add Schlieren.Core Schlieren.Tests/Execution harvest/ledger docs/harvest
git commit -m "fix: preserve transient storage frame semantics"
```

**Stop:** report whether the historical EIP-7702 case was envelope or core behavior.

---

## Task 9: Repair remaining blob/access-list/fee-market families

**Provisional files; confirm after Task 5:**

- Modify: `Schlieren.Core/Execution/StateTransition.cs`
- Modify: `Schlieren.Core/Execution/IntrinsicGas.cs`
- Modify: `Schlieren.Core/Gas/TransactionIntrinsicGasSchedule.cs`
- Modify: `Schlieren.Tests/Execution/BlobTransactionFeeTests.cs`
- Modify: `Schlieren.Tests/Gas/TransactionIntrinsicGasScheduleTests.cs`
- Add: `Schlieren.Tests/Execution/FeeSettlementEquationTests.cs`
- Modify: relevant repair and run records under `harvest/ledger/`

- [ ] **Step 1: Rebaseline after typed-envelope decoding**

If no divergence remains, add no consensus change. Record that the family was eliminated by the apparatus/envelope correction.

- [ ] **Step 2: Prove every remaining exact equation**

For a remaining type-3 case assert:

```text
sender final = sender initial - value - execution gas charge - blob gas charge
recipient final = recipient initial + committed value
coinbase delta = priority fee only
burned = base fee component + blob fee component
```

Also assert failed/reverted value restoration, upfront affordability, intrinsic gas, blob-count limits, versioned-hash validity, and represented post-state slots.

- [ ] **Step 3: Repair only the proven canonical rule**

Do not copy fixture post-state into execution. Keep blob fee separate from executable gas, never credit blob fee to coinbase, and preserve access-list warmness and intrinsic charges.

- [ ] **Step 4: Verify and commit**

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter "FullyQualifiedName~BlobTransactionFee|FullyQualifiedName~FeeSettlementEquation|FullyQualifiedName~TransactionIntrinsicGas" --no-restore
dotnet test Schlieren.sln --no-restore
```

Rerun all 50 Access List/Fee Market cases. Acceptance: 50/50.

```powershell
git add Schlieren.Core Schlieren.Tests harvest/ledger docs/harvest
git commit -m "fix: align typed transaction fee settlement"
```

**Stop:** no further consensus repair begins until all six campaigns are rerun together.

---

## Task 10: Add same-provenance suite gates and umbrella certification

**Files:**

- Add: `Schlieren.Harvest/Certification/SuiteGateRecord.cs`
- Add: `Schlieren.Harvest/Certification/UmbrellaCertificate.cs`
- Modify: `Schlieren.Harvest/Certification/CertificationService.cs`
- Modify: `Schlieren.Harvest/Ledger/LedgerPaths.cs`
- Modify: `Schlieren.Harvest/Serialization/ContentHasher.cs`
- Modify: `Schlieren.Cli/Commands/HarvestCommand.cs`
- Add: `Schlieren.Harvest.Tests/Certification/SuiteGateTests.cs`
- Add: `Schlieren.Harvest.Tests/Certification/UmbrellaCertificationTests.cs`
- Modify: `Schlieren.Harvest.Tests/Certification/CertificationServiceTests.cs`
- Modify: `Schlieren.Tests/CLI/HarvestCommandTests.cs`

- [ ] **Step 1: Write certificate-refusal tests first**

Individual issuance must refuse unknown/short commit, dirty tree, stale suite gate, mismatched manifest, non-50 total, any non-pass outcome, open applicable repair, missing content hash, EELS mismatch, or fixture mismatch.

Umbrella issuance must refuse:

- fewer or more than the six expected campaign IDs;
- duplicate campaign or manifest;
- any individual certificate not 50/50;
- different Schlieren commit, EELS identity, fixture identity, environment, or suite gate;
- aggregate other than exactly 300 pass / 300 total;
- absent or mismatched same-commit Storage certificate;
- stale or unverifiable content hash.

- [ ] **Step 2: Replace boolean suite-gate trust**

The current CLI trusts a JSON boolean named `certificationEligibility`. Replace it with a content-hashed typed record containing three run artifacts, test identities/totals, exact commit, and timestamps. Validate all three suite runs rather than trusting a flag.

- [ ] **Step 3: Implement certificate schemas**

Certificates reference immutable run and gate hashes. Add ledger paths for suite gates and umbrella certificates. Issuance writes atomically and refuses overwrite.

- [ ] **Step 4: Add CLI surfaces**

Provide commands equivalent to:

```powershell
& $cli harvest suite-gate create --ledger $ledger --trx <run1> --trx <run2> --trx <run3>
& $cli harvest certify <run-id> --ledger $ledger --suite-gate <gate.json>
& $cli harvest certify-umbrella --ledger $ledger --certificate <six paths> --storage-certificate <path> --suite-gate <gate.json>
```

- [ ] **Step 5: Verify and commit**

```powershell
dotnet test Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj --filter "FullyQualifiedName~Certification|FullyQualifiedName~SuiteGate" --no-restore
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~HarvestCommandTests --no-restore
dotnet test Schlieren.sln --no-restore
git add Schlieren.Harvest Schlieren.Harvest.Tests Schlieren.Cli Schlieren.Tests/CLI
git commit -m "feat: certify same-provenance harvest campaign trains"
```

**Stop:** review schema and refusal coverage before creating real certificates.

---

## Task 11: Run the final 350-case certification train

**Files:**

- Add: append-only final runs under `harvest/ledger/runs/`
- Add: final comparisons under `harvest/ledger/comparisons/`
- Add: three-run suite gate under `harvest/ledger/suite-gates/`
- Add: seven individual certificates under `harvest/ledger/certificates/`
- Add: umbrella certificate under `harvest/ledger/umbrella-certificates/`
- Add: `docs/harvest/certification/2026-08-26-strategic-campaign-certificate-report.md`
- Modify: `harvest/ledger/reports/strategic-campaign-train-status.md`

- [ ] **Step 1: Freeze the candidate commit**

Build Release, require a clean tree, and record full commit, dependency lock state, worker hash, EELS 2.19.0 executable hash, fixture-root revision, and all seven manifest hashes. No code changes are allowed after this point without restarting Task 11.

- [ ] **Step 2: Run all seven manifests on the same commit**

Run Storage first, then the six strategic campaigns. Required outcomes:

| Campaign | Required |
| --- | ---: |
| Storage Lifecycle | 50/50 |
| Call Semantics | 50/50 |
| Create Semantics | 50/50 |
| Return Data | 50/50 |
| Self-Destruct | 50/50 |
| Transient Storage | 50/50 |
| Access List/Fee Market | 50/50 |

Any non-pass outcome stops issuance.

- [ ] **Step 3: Run the full suite three times**

```powershell
dotnet test Schlieren.sln -c Release --no-build --logger "trx;LogFileName=cert-suite-1.trx"
dotnet test Schlieren.sln -c Release --no-build --logger "trx;LogFileName=cert-suite-2.trx"
dotnet test Schlieren.sln -c Release --no-build --logger "trx;LogFileName=cert-suite-3.trx"
```

Require identical test identities, pass/fail/skip totals, and zero failures. Create and verify the typed suite-gate record.

- [ ] **Step 4: Issue and verify certificates**

Issue a renewed Storage certificate and six individual strategic certificates. Then issue the umbrella certificate. Read every artifact back through the ledger, verify its content hash, and independently reconcile 300/300. Storage must be referenced as a same-commit prerequisite but excluded from the umbrella denominator.

- [ ] **Step 5: Write the human report**

Report the complete before/after family history, exact provenance, seven 50/50 results, suite totals, certificate IDs/hashes, runtime, and any remaining limitations outside the certified scope.

- [ ] **Step 6: Commit final evidence**

```powershell
git add harvest/ledger docs/harvest/certification
git commit -m "test: certify strategic harvest campaign train"
git status --short
```

Expected: clean tree. Do not claim completion until all hashes reread successfully.

---

## Task 12: Update operator documentation and release status

**Files:**

- Modify: `README.md`
- Modify: `CONFORMANCE_STATUS.md`
- Add: `docs/harvest/STRATEGIC_CAMPAIGN_OPERATOR_GUIDE.md`
- Modify: `docs/harvest/HARVEST_LAPTOP_HANDOFF_2026-08-24.md`

- [ ] **Step 1: Document certified scope precisely**

State what 300/300 proves, what the additional Storage prerequisite proves, the pinned EELS/fixture identities, and what remains uncertified. Do not describe Harvest as certifying all EVM behavior.

- [ ] **Step 2: Document reproducible operation**

Include environment setup, manifest verification, campaign execution, apparatus status interpretation, suite-gate creation, individual certification, umbrella certification, and content-hash verification. Use placeholders for machine-local paths and no secrets.

- [ ] **Step 3: Verify commands and links**

```powershell
& $cli harvest --help
& $cli harvest campaign run --help
& $cli harvest certify --help
& $cli harvest certify-umbrella --help
rg -n "C:/projects/eels-venv|unknown|TODO|TBD" README.md CONFORMANCE_STATUS.md docs/harvest Schlieren.Cli Schlieren.Harvest
```

Expected: help matches documentation; no compiled machine-local EELS path or unresolved placeholder appears in production source or final status.

- [ ] **Step 4: Final regression and commit**

```powershell
dotnet test Schlieren.sln -c Release --no-build
git add README.md CONFORMANCE_STATUS.md docs/harvest
git commit -m "docs: publish strategic harvest certification guide"
```

**Completion:** Task 12 closes only after the repository is clean and the final report links to verified ledger artifacts.

## Required per-task report

After each task, stop and report:

| Field | Required value |
| --- | --- |
| Task | Number and name |
| Commit | Full SHA |
| Files changed | Exact list |
| Red evidence | Test names and observed failure |
| Focused verification | passed / failed / skipped / total |
| Full regression | passed / failed / skipped / total |
| Campaign evidence | Run IDs and six status counts, when applicable |
| Ledger changes | New immutable artifact paths and hashes |
| Scope exceptions | None, or approved plan amendment |
| Working tree | Clean/dirty |

No narrative assertion substitutes for commands, test totals, run IDs, or content hashes.
