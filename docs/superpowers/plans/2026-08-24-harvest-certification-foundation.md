# Harvest Certification Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first honest, repeatable Harvest certification cycle: repair and calibrate the measuring apparatus, freeze a deterministic 50-case storage campaign, execute Schlieren and pinned EELS independently, retain typed before/after evidence, and certify only an exact all-green commit.

**Architecture:** Add `Schlieren.Harvest` as a production application layer over the canonical `Schlieren.Core` EVM. Keep fixture cataloging, oracle execution, comparison, clustering, ledger persistence, and certification separate behind typed interfaces. The CLI composes those services; neither UI nor test assemblies contain certification behavior. Every fixture and engine execution occurs in a child process so a stack overflow, timeout, or killed host becomes a durable `Aborted` result.

**Tech Stack:** .NET 8, C# 12, `System.CommandLine`, `System.Text.Json`, xUnit, the canonical `StateTransition`/`EvmMachine`, and a pinned `ethereum-spec-evm` executable supplied by explicit local configuration.

**Spec:** `docs/superpowers/specs/2026-08-24-harvest-certification-foundation-design.md`

## Global Constraints

- Work test-first: add one failing contract test, run it and record the expected failure, make the smallest production change, then rerun it.
- Do not change EVM behavior merely to match a fixture. Confirm every consensus repair against the fork rule and add a focused permanent regression.
- Do not use `Schlieren.EELS.Tests`, `Schlieren.Tests`, Python scripts, React, Avalonia, or trace-derived reconstruction as production certification authorities.
- Do not use the old `EelsExecutionHarness.ParseOutput`: it guesses success, hard-codes intrinsic gas, and does not return state or logs.
- Do not parse human-readable mismatch strings. All comparison and clustering inputs are typed facts.
- Do not alter existing RPC JSON contracts.
- Never put a credential, machine-specific executable path, or downloaded fixture in tracked source.
- Never mutate a frozen manifest or finalized ledger record. Corrections create a new version or record.
- Run each case with a fresh EVM, state, OpSec scope, worker process, and ledger staging directory.
- A missing oracle, malformed fixture, timeout, crash, cancellation, partial artifact, or worker termination is never a pass.
- Commit after every completed task using the commit text given below. Do not begin the next task with a dirty tree.

### Acceptance and change control (effective from Task 2)

- A task is accepted or rejected only against this plan, the approved specification, and correctness properties necessarily implied by the code being changed. Reviewers must cite the exact written requirement or demonstrate a concrete correctness, security, data-loss, consensus, or false-certification defect.
- A reviewer preference, additional hardening idea, broader refactor, or newly imagined test is advisory unless it was written here before the task began. Advisory findings go into the next planned task or a documented backlog; they do not block the current task.
- New blocking requirements require a committed plan amendment before implementation of the affected task starts. The amendment must name the affected task, files, test, command, expected result, and scope boundary.
- Do not apply new acceptance criteria retroactively. The only emergency exception is a demonstrated risk of credential exposure, unrecoverable data loss, consensus corruption, or a false conformance certificate. Record and approve that exception explicitly before further implementation.
- Reviews may require correction without a plan amendment when committed code fails an existing test, does not compile, contradicts an explicit interface or status rule, changes an out-of-scope contract, or does not implement a listed step.
- Test reports use `passed / failed / skipped / total` and identify the projects actually executed. A discovery-only or zero-case EELS run is never labeled a conformance run.

### Deterministic concurrent-test contract (effective from Task 2)

- Do not use `Thread.Sleep`, `Task.Delay`, elapsed-time assumptions, or scheduler luck to establish ordering. Timeouts may guard against deadlock but may not decide which action happens first.
- Coordinate participants with explicit signals. For task-based tests, use `TaskCompletionSource` with `TaskCreationOptions.RunContinuationsAsynchronously` and a two-way handshake that proves the asserted states overlap.
- Bound every coordination wait in unit tests with `WaitAsync(TimeSpan.FromSeconds(5))` so a regression fails instead of hanging the test host.
- Release or fault every dependent signal from `finally`/`catch` paths using `TrySetResult`, `TrySetException`, or cancellation. A failed assertion or constructor must not orphan a companion task.
- A regression test must be capable of failing against the replaced implementation. If the old implementation would pass because the asserted states did not overlap, the test is not evidence.
- These rules govern new or modified concurrent tests from Task 2 forward. Task 1 voluntarily satisfies them at commit `4c730c3`; Task 1 is closed and is not reopened by this amendment.

### Amendment log

- **2026-08-24 — Amendment 1:** Added prospective acceptance/change-control rules, deterministic concurrent-test requirements, and the Task 2 review boundary before Task 2 implementation began.
- **2026-08-24 — Amendment 2:** Expanded Task 3's declared file list and composition contract before Task 3 began so external configuration reaches `HarvestService`, `HarvestViewModel`, and `MainWindow` without hidden constructors or hard-coded corpus paths.

---

## Task 0: Capture the pre-repair intake baseline

**Files:**

- Add: `docs/harvest/baselines/2026-08-24-pre-repair-intake.md`

- [ ] **Step 1: Record immutable intake identity**

Start from a clean tree. Record `git rev-parse HEAD`, `git status --short`, `dotnet --info`, OS/architecture, processor count, and whether `EELS_FIXTURES_ROOT` and an EELS executable are available. Record paths only in normalized/redacted form; never copy environment values that may be credentials.

- [ ] **Step 2: Take the first full-suite measurement twice**

```powershell
dotnet build Schlieren.sln -c Release
dotnet test Schlieren.sln -c Release --no-build --logger "trx;LogFileName=intake-suite-1.trx"
dotnet test Schlieren.sln -c Release --no-build --logger "trx;LogFileName=intake-suite-2.trx"
```

Record total, pass, fail, skip, aborted-host behavior, elapsed time, and differing test identities. Do not describe absent fixture directories as conformance passes. If a process terminates, record the last completed test and process exit classification as observed evidence.

- [ ] **Step 3: Run known-defect probes without changing code**

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~OpSecLockoutTests --no-build
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~OverlayIsolationTests --no-build
dotnet test Schlieren.EELS.Tests/Schlieren.EELS.Tests.csproj --filter FullyQualifiedName~EelsTaxonomyDrill --no-build
```

Bound the taxonomy probe externally to 15 minutes. If it crashes, hangs, or lacks fixtures, stop the child test process and record `Aborted` or `FixtureUnavailable`; do not infer a mismatch count. Search tracked files for JWT-shaped strings and literal API-key/token assignments, recording file/line and a redacted fingerprint only.

- [ ] **Step 4: Write and commit the intake report**

The report separates apparatus observations, engine-test observations, fixture availability, credential findings, and limitations. It includes exact command lines and artifact hashes for retained TRX files, but the ignored TRX files themselves remain local.

```powershell
git add docs/harvest/baselines/2026-08-24-pre-repair-intake.md
git commit -m "test: record harvest pre-repair intake baseline"
```

---

## Task 1: Stabilize OpSec isolation

**Files:**

- Modify: `Schlieren.Core/Security/OpSecLockout.cs`
- Modify: `Schlieren.UI/ViewModels/WorkbenchViewModel.cs`
- Modify: `Schlieren.Tests/Security/OpSecLockoutTests.cs`
- Add: `Schlieren.Tests/Security/OpSecConcurrencyTests.cs`
- Add: `Schlieren.Tests/UI/WorkbenchOpSecIsolationTests.cs`

- [ ] **Step 1: Write the failing concurrency contracts**

Add tests that start two tasks behind a barrier: one enters `ExecuteIsolatedAsync`, the other remains outside. Assert the inner task sees `IsEnabled == true`, the outer task remains false, nested scopes restore correctly, and an exception restores the caller's state. Add a Workbench test proving constructing and disposing a default `WorkbenchViewModel` does not change another execution context's OpSec state.

The public contract becomes:

```csharp
public static bool IsEnabled { get; }
public static IDisposable EnterScope();
public static void ExecuteIsolated(Action action);
public static T ExecuteIsolated<T>(Func<T> func);
public static Task ExecuteIsolatedAsync(Func<Task> action);
public static Task<T> ExecuteIsolatedAsync<T>(Func<Task<T>> func);
```

Use `AsyncLocal<int>` for scope depth. Remove the public setter; session preferences stay in the Workbench instance. `AssertOffline` reads only the current async flow.

- [ ] **Step 2: Prove the current race**

Run:

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter "FullyQualifiedName~OpSecConcurrencyTests|FullyQualifiedName~WorkbenchOpSecIsolationTests" --no-restore
```

Expected: at least one new assertion fails because `IsEnabled` is process-global or the Workbench constructor changes it.

- [ ] **Step 3: Implement scoped OpSec and remove Workbench global writes**

Implement `EnterScope` with an idempotent private scope object. Rewrite all four execution helpers in terms of `using var scope = EnterScope()`. In `WorkbenchViewModel`, keep `OpSecEnabled` as local UI state and wrap only the selected execution operation when it is true. `ApplyOpSec` may update only labels and local state.

- [ ] **Step 4: Verify focused and regression tests**

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter "FullyQualifiedName~OpSec|FullyQualifiedName~Workbench" --no-restore
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --no-restore
```

- [ ] **Step 5: Commit**

```powershell
git add Schlieren.Core/Security/OpSecLockout.cs Schlieren.UI/ViewModels/WorkbenchViewModel.cs Schlieren.Tests/Security/OpSecLockoutTests.cs Schlieren.Tests/Security/OpSecConcurrencyTests.cs Schlieren.Tests/UI/WorkbenchOpSecIsolationTests.cs
git commit -m "fix: isolate opsec state per execution flow"
```

---

## Task 2: Make overlay storage traversal bounded and non-recursive

**Files:**

- Modify: `Schlieren.Core/State/StateOverlay.cs`
- Modify: `Schlieren.Tests/State/OverlayIsolationTests.cs`

**Task 2 acceptance boundary:**

- Required production scope is `StateOverlay.GetStorageAtAsync` and only the private helpers necessary to make that method iterative, cancellation-aware, and cycle-safe.
- Required behavior is nearest-overlay precedence, tombstone-as-zero, inherited-value lookup through 2,048 overlays, overridden-value lookup through 2,048 overlays, cancellation on traversal, unchanged shallow-chain behavior, and the exact cycle exception named below.
- Do not change `StateTransition`, `EvmMachine`, journal types, gas semantics, other `IGlobalState` implementations, taxonomy reporting, or EELS fixture parsing in Task 2.
- `GetStorageKeysAsync`, `GetStoragePresenceAsync`, balance, nonce, code, account-existence, commit, snapshot, and restore behavior are outside Task 2 unless an existing focused test proves the `GetStorageAtAsync` change broke them.
- A newly noticed deep-recursion risk in another getter is recorded for a separately approved task; it is not grounds to expand or block Task 2.
- Review gates are exactly the focused tests and commands written in Steps 1–3 plus compilation and a clean-tree check. Additional stress depths, benchmarks, or taxonomy sweeps are advisory.

- [ ] **Step 1: Reproduce deep parent traversal without crashing the test host**

Add tests that construct 2,048 valid nested overlays over one `GlobalState` and read both an inherited storage value and an overridden value without `StackOverflowException`. Add a cancellation test and a shallow-chain behavior test. This is a deterministic probe of the recursive parent walk that previously killed the taxonomy host.

- [ ] **Step 2: Replace recursive overlay-to-overlay lookup with iteration**

Walk consecutive `StateOverlay` instances in a loop, checking tombstones and buffered values at each level, then await the first non-overlay parent's `GetStorageAtAsync`. Check cancellation on each hop. Preserve existing shadowing and tombstone semantics. Add a private reference-identity set as a defensive cycle guard and throw `InvalidOperationException("StateOverlay parent cycle detected.")` if a corrupted graph repeats.

- [ ] **Step 3: Verify focused and taxonomy-adjacent storage tests**

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~OverlayIsolationTests --no-restore
dotnet test Schlieren.EELS.Tests/Schlieren.EELS.Tests.csproj --filter "FullyQualifiedName~EelsPublishedStorageTests|FullyQualifiedName~EelsSelfDestructRevertTests" --no-restore
```

- [ ] **Step 4: Commit**

```powershell
git add Schlieren.Core/State/StateOverlay.cs Schlieren.Tests/State/OverlayIsolationTests.cs
git commit -m "fix: make overlay storage traversal non-recursive"
```

---

## Task 3: Remove operational credentials and synchronize evidence docs

**Files:**

- Modify: `Schlieren.UI/Services/HarvestService.cs`
- Add: `Schlieren.UI/Services/HarvestServiceOptions.cs`
- Modify: `Schlieren.UI/ViewModels/HarvestViewModel.cs`
- Modify: `Schlieren.UI/Views/MainWindow.axaml.cs`
- Modify: `Schlieren.UI/App.axaml.cs`
- Add: `Schlieren.Tests/UI/HarvestServiceConfigurationTests.cs`
- Add: `tools/verify_no_tracked_secrets.ps1`
- Modify: `docs/security/JOURNAL_SECURITY_EVIDENCE.md`
- Modify: `CONFORMANCE_STATUS.md`

**Task 3 acceptance boundary:**

- `App.OnFrameworkInitializationCompleted` is the composition root. It loads `HarvestServiceOptions` from the four named environment keys, constructs one `HarvestService`, constructs one `HarvestViewModel`, and passes that ViewModel into `MainWindow`.
- `MainWindow` must not construct `HarvestViewModel`. `HarvestViewModel` must not construct `HarvestService`. Constructors receive these dependencies explicitly.
- `HarvestService` owns its `HttpClient` and consumes `HarvestServiceOptions`; `HarvestViewModel` consumes the service and options. App shutdown disposes the ViewModel/service once.
- Remove every operational JWT and the compiled `C:\projects\Schlieren\muscle\corpus` path from tracked C# source, including `HarvestViewModel.ClearAllAsync`.
- The literal loopback default `http://localhost:5678` is allowed because it is a non-secret service default, but an environment value overrides it. No credential receives a compiled default.
- If `SCHLIEREN_N8N_API_KEY` is absent, status/poll calls do not send `X-N8N-API-KEY` and the UI reports the n8n integration as unconfigured. If `SCHLIEREN_MCP_TOKEN` is absent, workflow execution sends no bearer header and returns an explicit disabled result. If `SCHLIEREN_HARVEST_CORPUS` is absent, corpus read/clear operations use no fallback directory and report corpus integration as unconfigured.
- Rotation is an external operational action. This task must document both exposed token fingerprints as requiring rotation; it must not claim rotation occurred without independent evidence.
- Do not redesign the Harvest UI, n8n workflow protocol, polling interval, corpus JSON schema, or workflow identifiers in Task 3.

- [ ] **Step 1: Add failing configuration and tracked-secret checks**

Tests must prove the service constructor accepts `HarvestServiceOptions`, sends no authorization header when the API key is absent, and never falls back to a compiled token or hard-coded corpus path. The scan script must inspect tracked text from `git ls-files`, reject JWT-shaped strings and known secret names assigned to literals, and print file plus line without printing a complete secret.

Use this configuration record:

```csharp
public sealed record HarvestServiceOptions(
    Uri N8nBaseUri,
    string? N8nApiKey,
    string? McpToken,
    string? CorpusDirectory)
{
    public static HarvestServiceOptions FromEnvironment(Func<string, string?> read);
}
```

`FromEnvironment` reads only `SCHLIEREN_N8N_BASE_URL`, `SCHLIEREN_N8N_API_KEY`, `SCHLIEREN_MCP_TOKEN`, and `SCHLIEREN_HARVEST_CORPUS`. It validates an absolute HTTP/HTTPS base URI, trims blank credentials to `null`, canonicalizes a nonblank corpus path with `Path.GetFullPath`, and leaves a missing corpus path as `null`.

The construction contracts are:

```csharp
public HarvestService(HarvestServiceOptions options, HttpMessageHandler? handler = null);
public HarvestViewModel(HarvestService service, HarvestServiceOptions options);
public MainWindow(WorkbenchViewModel viewModel, HarvestViewModel harvestViewModel);
```

The optional handler exists only to make request headers and destinations observable in tests; `HarvestService` still creates and owns the `HttpClient`.

- [ ] **Step 2: Externalize configuration and remove all embedded values**

Resolve local settings at the application composition root from environment/configuration keys `SCHLIEREN_N8N_BASE_URL`, `SCHLIEREN_N8N_API_KEY`, `SCHLIEREN_MCP_TOKEN`, and `SCHLIEREN_HARVEST_CORPUS`. Missing optional integration credentials disable the integration with a visible status; they do not crash core execution.

Update `HarvestViewModel.ClearAllAsync` to use the configured corpus directory. When it is absent, do not write a file and set `StatusMessage` to `"Harvest corpus is not configured"`. Ensure the application exit handler disposes the injected Harvest ViewModel alongside the Workbench.

- [ ] **Step 3: Update evidence documentation**

Document current typed journal rule IDs and call-family semantics from the production enums and detectors. Remove branch-state claims and clearly label historical test counts with commit hashes. Add an explicit operational note that the exposed credentials must be rotated outside Git; deleting source does not revoke them.

- [ ] **Step 4: Verify**

```powershell
pwsh -File tools/verify_no_tracked_secrets.ps1
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~HarvestServiceConfigurationTests --no-restore
dotnet build Schlieren.sln --no-restore
```

Expected: secret scan exits 0 without printing credential material; configuration tests pass with no live network dependency; solution build exits 0. Also run `git diff --check` and require a clean working tree after the task commit.

- [ ] **Step 5: Commit**

```powershell
git add Schlieren.UI/Services/HarvestService.cs Schlieren.UI/Services/HarvestServiceOptions.cs Schlieren.UI/ViewModels/HarvestViewModel.cs Schlieren.UI/Views/MainWindow.axaml.cs Schlieren.UI/App.axaml.cs Schlieren.Tests/UI/HarvestServiceConfigurationTests.cs tools/verify_no_tracked_secrets.ps1 docs/security/JOURNAL_SECURITY_EVIDENCE.md CONFORMANCE_STATUS.md
git commit -m "security: externalize harvest credentials"
```

---

## Task 4: Establish the production Harvest domain and canonical serialization

**Files:**

- Add: `Schlieren.Harvest/Schlieren.Harvest.csproj`
- Add: `Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj`
- Add: `Schlieren.Harvest.Worker/Schlieren.Harvest.Worker.csproj`
- Add: `Schlieren.Harvest.Worker/Program.cs`
- Modify: `Schlieren.sln`
- Modify: `Schlieren.CLI/Schlieren.CLI.csproj`
- Add: `Schlieren.Harvest/Domain/HarvestTypes.cs`
- Add: `Schlieren.Harvest/Serialization/HarvestJson.cs`
- Add: `Schlieren.Harvest/Serialization/ContentHasher.cs`
- Add: `Schlieren.Harvest.Tests/Serialization/CanonicalSerializationTests.cs`

- [ ] **Step 1: Scaffold projects and references**

`Schlieren.Harvest` references only `Schlieren.Core`. `Schlieren.Harvest.Tests` references Harvest and Core with the repository's existing xUnit packages. CLI references Harvest. Worker references Harvest and Core. Add all three projects to the solution. The worker program initially rejects every operation with a typed protocol-error response; execution operations arrive in Task 6.

- [ ] **Step 2: Define the stable domain vocabulary**

Create exact enums:

```csharp
public enum CaseStatus { Pass, Divergence, FixtureInvalid, HarnessError, Aborted, Quarantined }
public enum DiscrepancyLayer { Validity, Receipt, Gas, ReturnData, Logs, Account, Storage, Journal }
public enum DiscrepancyKind { Status, GasUsed, RefundCounter, ReturnData, LogCount, LogAddress, LogTopics, LogData, AccountExistence, Nonce, Balance, Code, StorageValue, JournalConservation }
public enum RunKind { Calibration, Inspection, Reinspection }
public enum RunState { Staging, ApparatusFailed, InspectionFailed, Completed, Certified }
```

Define immutable records for `ExpectedValue<T>`, `ActualValue<T>`, `FieldDelta`, `CaseOutcome`, `EnvironmentIdentity`, `ToolIdentity`, and `ContentEnvelope<T>`. Every persisted record carries `SchemaVersion`, `CreatedUtc`, and `ContentHash`.

- [ ] **Step 3: Test canonical JSON and hashes**

Canonical serialization must use UTF-8, camelCase, enum strings, UTC round-trip timestamps, lexicographically sorted dictionary keys, and no indentation. `ContentHash` is lowercase SHA-256 over canonical JSON with the `contentHash` field omitted. Tests prove semantically identical dictionary insertion orders hash identically and a one-field change changes the hash.

- [ ] **Step 4: Verify**

```powershell
dotnet build Schlieren.sln
dotnet test Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj --filter FullyQualifiedName~CanonicalSerializationTests --no-build
```

- [ ] **Step 5: Commit**

```powershell
git add Schlieren.sln Schlieren.CLI/Schlieren.CLI.csproj Schlieren.Harvest Schlieren.Harvest.Tests Schlieren.Harvest.Worker
git commit -m "feat: establish typed harvest domain"
```

---

## Task 5: Build fixture admission and immutable manifests

**Files:**

- Add: `Schlieren.Harvest/Fixtures/FixtureCatalog.cs`
- Add: `Schlieren.Harvest/Fixtures/EelsFixtureReader.cs`
- Add: `Schlieren.Harvest/Fixtures/FixtureAdmission.cs`
- Add: `Schlieren.Harvest/Campaigns/StorageLifecyclePolicy.cs`
- Add: `Schlieren.Harvest/Campaigns/CampaignSelector.cs`
- Add: `Schlieren.Harvest/Campaigns/CampaignManifest.cs`
- Add: `Schlieren.Harvest.Tests/Fixtures/FixtureCatalogTests.cs`
- Add: `Schlieren.Harvest.Tests/Campaigns/CampaignSelectorTests.cs`
- Add: `Schlieren.Harvest.Tests/Fixtures/Samples/`

- [ ] **Step 1: Define admission reason codes and metadata**

```csharp
public enum AdmissionReasonCode
{
    Admitted, MissingRoot, OutsideRoot, MalformedJson, DuplicateCaseId,
    UnsupportedFormat, UnsupportedFork, MissingPreState, MissingPostState,
    MissingStatusAuthority, MissingGasAuthority, MissingLogsAuthority,
    AmbiguousVariant, ChecksumMismatch
}

public sealed record FixtureCaseMetadata(
    string CaseId,
    string RelativePath,
    string SourceSha256,
    string Fork,
    IReadOnlySet<StorageDimension> Dimensions,
    AdmissionReasonCode Admission,
    string? Detail);
```

Paths must be resolved with `Path.GetFullPath`, compared against the canonical root using the platform-appropriate comparison, and persisted only as slash-normalized relative paths.

- [ ] **Step 2: Write admission tests**

Cover missing root, traversal outside root, malformed JSON, duplicate IDs, unsupported fork, missing required authority, valid legacy fixture, valid published fixture, and deterministic catalog order. Samples are minimal data-only JSON documents.

- [ ] **Step 3: Implement the storage selection policy**

Define `StorageDimension` for SLOAD/SSTORE, warm/cold, four value transitions, repeated/unchanged writes, root/nested, four call types, child commit/rollback, ancestor rollback, simulation discard, refund, and fork-sensitive behavior. Selection uses a deterministic greedy set-cover score with ordinal case-ID tie-breaking. It must return exactly the requested count or an `InsufficientCoverageReport`; no random seed or unrelated fill is allowed.

- [ ] **Step 4: Freeze and verify manifest identity**

`CampaignManifest` includes every field required by the spec and an ordered `ManifestCase` list. Recreating against unchanged inputs must produce the same case order and manifest hash; timestamps are supplied by an injected `TimeProvider` and excluded from semantic selection but included in the final frozen content.

- [ ] **Step 5: Verify**

```powershell
dotnet test Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj --filter "FullyQualifiedName~FixtureCatalogTests|FullyQualifiedName~CampaignSelectorTests" --no-build
```

- [ ] **Step 6: Commit**

```powershell
git add Schlieren.Harvest/Fixtures Schlieren.Harvest/Campaigns Schlieren.Harvest.Tests/Fixtures Schlieren.Harvest.Tests/Campaigns
git commit -m "feat: admit eels fixtures and freeze campaigns"
```

---

## Task 6: Implement independent EELS and canonical Schlieren execution

**Files:**

- Add: `Schlieren.Harvest/Execution/IReferenceOracle.cs`
- Add: `Schlieren.Harvest/Execution/EelsProcessOracle.cs`
- Add: `Schlieren.Harvest/Execution/EelsOutputParser.cs`
- Add: `Schlieren.Harvest/Execution/SchlierenCaseExecutor.cs`
- Add: `Schlieren.Harvest/Execution/ExecutionSnapshot.cs`
- Add: `Schlieren.Harvest/Execution/WorkerExitClassifier.cs`
- Modify: `Schlieren.Harvest.Worker/Program.cs`
- Add: `Schlieren.Harvest.Tests/Execution/EelsOutputParserTests.cs`
- Add: `Schlieren.Harvest.Tests/Execution/SchlierenCaseExecutorTests.cs`
- Add: `Schlieren.Harvest.Tests/Execution/JournalParityTests.cs`
- Add: `Schlieren.Harvest.Tests/Execution/WorkerExitClassifierTests.cs`

- [ ] **Step 1: Define one normalized execution snapshot**

`ExecutionSnapshot` contains transaction validity/status, gas used, optional refund counter, return data, ordered typed logs, and a complete represented post-state of typed accounts and storage slots. It also carries optional journal evidence separately. Every expected field includes its authority (`FixturePostState`, `EelsExecutable`, or `FixtureMetadata`).

- [ ] **Step 2: Pin and probe the EELS executable**

`EelsProcessOracle` receives an absolute executable path, expected version string, working directory, and timeout through `EelsOracleOptions`. It invokes `ethereum-spec-evm statetest --json <fixture>` with `UseShellExecute=false`, redirects both streams, kills the process tree on timeout, records executable SHA-256 and reported version, and rejects a version mismatch before case execution.

- [ ] **Step 3: Parse real EELS output without guessed defaults**

Golden parser tests must cover success, EVM failure, invalid transaction, refund, return bytes, zero/multiple logs, post-state, malformed NDJSON, nonzero exit, and missing required fields. No `catch { }`, default-success, hard-coded 21,000 intrinsic gas, or empty collection may stand in for absent output. Missing required evidence returns a typed apparatus failure.

- [ ] **Step 4: Execute Schlieren through only the canonical path**

Adapt the production-safe parts of `EelsStateFixtureLoader` into `EelsFixtureReader`; do not reference the test assembly. `SchlierenCaseExecutor` builds a fresh `GlobalState`, fresh opcode catalog, `EvmMachine`, and `StateTransition`, then calls `ApplyTransactionAsync` once. Snapshot output comes directly from `ExecutionResult` and committed state. Journal on/off changes observation only.

- [ ] **Step 5: Prove journal parity**

For success, revert, nested call, and storage rollback cases, run the same input with journal enabled and disabled. Assert identical status, gas, refund, return data, logs, and post-state; only journal evidence may differ.

- [ ] **Step 6: Move case execution behind the worker**

Define `WorkerTerminationKind` as `Completed`, `TimedOut`, `Cancelled`, `Crashed`, and `ProtocolError`. Tests classify a zero exit with a valid response, timeout, caller cancellation, nonzero/terminated process, and zero exit with a missing or invalid response. The parent writes a request containing a manifest hash, case ID, canonical fixture path, source checksum, fork, mode, and options. The worker validates all identities, invokes exactly one executor, writes one response atomically, and exits. The parent converts every termination class into a non-pass case artifact. Add a `calibration-crash` operation that deliberately terminates only the worker process, proving the parent can persist `Aborted` evidence.

- [ ] **Step 7: Verify**

```powershell
dotnet test Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj --filter "FullyQualifiedName~EelsOutputParserTests|FullyQualifiedName~SchlierenCaseExecutorTests|FullyQualifiedName~JournalParityTests|FullyQualifiedName~WorkerExitClassifierTests" --no-build
```

- [ ] **Step 8: Commit**

```powershell
git add Schlieren.Harvest/Execution Schlieren.Harvest.Worker Schlieren.Harvest.Tests/Execution
git commit -m "feat: execute harvest cases against independent eels"
```

---

## Task 7: Build the typed comparator and six-signal calibration gate

**Files:**

- Add: `Schlieren.Harvest/Comparison/ConformanceComparator.cs`
- Add: `Schlieren.Harvest/Calibration/CalibrationSuite.cs`
- Add: `Schlieren.Harvest/Calibration/CalibrationRecord.cs`
- Add: `Schlieren.Harvest.Tests/Comparison/ConformanceComparatorTests.cs`
- Add: `Schlieren.Harvest.Tests/Calibration/CalibrationSuiteTests.cs`

- [ ] **Step 1: Test every comparison field independently**

The comparator accumulates all deltas in stable order: status, gas, refund, return data, logs by index/address/topics/data, accounts by address, then nonce/balance/code/storage by slot. Tests prove a case with three mismatches returns three deltas, a missing expected authority is `FixtureInvalid` or `HarnessError` rather than `Pass`, and journal evidence cannot satisfy an absent EELS expectation.

- [ ] **Step 2: Implement exact terminal-status rules**

Completed comparison with no deltas is `Pass`; completed comparison with any required delta is `Divergence`; admission defects are `FixtureInvalid`; Harvest protocol/parser faults are `HarnessError`; timeout/cancel/crash/termination is `Aborted`; only an explicit signed-off quarantine record produces `Quarantined`.

- [ ] **Step 3: Implement six hand-authored calibration probes**

Create fixed inputs for exact match, gas mismatch, status mismatch, storage mismatch, malformed fixture, and killed worker. Expected classifications live in test data and are not produced by comparator code. `CalibrationSuite.RunAsync` returns all six outcomes plus apparatus gate status.

- [ ] **Step 4: Verify**

```powershell
dotnet test Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj --filter "FullyQualifiedName~ConformanceComparatorTests|FullyQualifiedName~CalibrationSuiteTests" --no-build
```

- [ ] **Step 5: Commit**

```powershell
git add Schlieren.Harvest/Comparison Schlieren.Harvest/Calibration Schlieren.Harvest.Tests/Comparison Schlieren.Harvest.Tests/Calibration
git commit -m "feat: calibrate typed harvest comparison"
```

---

## Task 8: Cluster typed causal failure families

**Files:**

- Add: `Schlieren.Harvest/Clustering/FailureFingerprint.cs`
- Add: `Schlieren.Harvest/Clustering/FailureFamilyClusterer.cs`
- Modify: `Schlieren.Core/Execution/Causal/FailureClusteringService.cs`
- Add: `Schlieren.Harvest.Tests/Clustering/FailureFamilyClustererTests.cs`

- [ ] **Step 1: Define the stable fingerprint**

The key contains fork, discrepancy layer/kind, expected/actual status geometry, first divergent frame/call/ancestry, instruction/opcode/PC, gas rule and delta, state-effect owner/slot/disposition, halt category, and conservation state. Serialize typed nullable fields canonically and hash them; never include title, rendered detail, test name, or source path.

- [ ] **Step 2: Test stability and separation**

Prove input ordering does not change families, summaries do not affect keys, identical geometry clusters, different forks remain separate, and different storage slots can cluster only when their causal owner/rule geometry matches according to the explicit fingerprint policy.

- [ ] **Step 3: Reuse canonical diagnosis without string parsing**

Add a typed adapter from Harvest evidence into `FailureClusteringService` or extract its typed clustering primitive into Core. Preserve existing callers and tests. The first causal journal evidence may enrich the fingerprint but must not decide expected consensus output.

- [ ] **Step 4: Verify and commit**

```powershell
dotnet test Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj --filter FullyQualifiedName~FailureFamilyClustererTests --no-build
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~FailureClustering --no-restore
git add Schlieren.Harvest/Clustering Schlieren.Harvest.Tests/Clustering Schlieren.Core/Execution/Causal/FailureClusteringService.cs
git commit -m "feat: cluster harvest failures by causal geometry"
```

---

## Task 9: Persist an atomic append-only run ledger

**Files:**

- Add: `Schlieren.Harvest/Ledger/IRunLedger.cs`
- Add: `Schlieren.Harvest/Ledger/FileRunLedger.cs`
- Add: `Schlieren.Harvest/Ledger/LedgerPaths.cs`
- Add: `Schlieren.Harvest/Ledger/LedgerRecords.cs`
- Add: `Schlieren.Harvest/Reporting/MarkdownRunReport.cs`
- Add: `Schlieren.Harvest.Tests/Ledger/FileRunLedgerTests.cs`
- Modify: `.gitignore`
- Add: `harvest/ledger/.gitkeep`

- [ ] **Step 1: Test append-only and atomic guarantees**

Tests must prove writes use a sibling staging directory, finalization fails when any declared case is absent, detailed artifacts exist for all non-pass cases, finalized paths cannot be overwritten, interrupted staging is not discoverable as a run, filenames reject traversal/invalid identifiers, and the final marker is written last.

- [ ] **Step 2: Implement the exact ledger layout**

Use the layout from the spec. Add `staging/<run-id>/` under the ledger root and move it atomically to `runs/<run-id>/` only on the same volume after hashes and counts verify. The marker is `complete.json`, containing run ID, run content hash, expected case count, actual case count, and finalization timestamp.

- [ ] **Step 3: Generate Markdown only from finalized JSON**

`MarkdownRunReport` reloads and verifies machine records, then renders apparatus status, conformance totals, failure families, case statuses, provenance, limitations, and open repair orders. It cannot accept ad hoc counters.

- [ ] **Step 4: Verify and commit**

```powershell
dotnet test Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj --filter FullyQualifiedName~FileRunLedgerTests --no-build
git add .gitignore harvest/ledger Schlieren.Harvest/Ledger Schlieren.Harvest/Reporting Schlieren.Harvest.Tests/Ledger
git commit -m "feat: persist append-only harvest evidence"
```

---

## Task 10: Orchestrate campaigns, repairs, comparisons, and certification

**Files:**

- Add: `Schlieren.Harvest/Campaigns/CampaignRunner.cs`
- Add: `Schlieren.Harvest/Comparison/RunComparator.cs`
- Add: `Schlieren.Harvest/Repairs/RepairOrderService.cs`
- Add: `Schlieren.Harvest/Certification/CertificationService.cs`
- Add: `Schlieren.Harvest/Regression/RegressionPromoter.cs`
- Add: `Schlieren.Harvest.Tests/Campaigns/CampaignRunnerTests.cs`
- Add: `Schlieren.Harvest.Tests/Comparison/RunComparatorTests.cs`
- Add: `Schlieren.Harvest.Tests/Repairs/RepairOrderServiceTests.cs`
- Add: `Schlieren.Harvest.Tests/Certification/CertificationServiceTests.cs`

- [ ] **Step 1: Test runner completeness and isolation**

Use fake workers and a temporary ledger to prove exactly the manifest's ordered cases run, each reaches one terminal status, cancellation finalizes an apparatus-failed record without a certificate, and one worker's crash cannot suppress later case records.

- [ ] **Step 2: Implement before/after classification**

`RunComparator` first requires identical manifest hashes. It reports eliminated, reduced, expanded, introduced, unchanged, and regressed families plus runtime/throughput deltas. A formerly passing case that becomes anything else is a regression.

- [ ] **Step 3: Implement repair-order lifecycle**

Opening requires a finalized divergence cluster. Closing requires a commit SHA, permanent test reference, identical-manifest reinspection ID, and proof that the family is eliminated or an explicit non-fixed disposition. Records are append-only revisions; do not edit the open record.

- [ ] **Step 4: Implement certification refusals before success**

Test one refusal per gate: calibration failure, manifest mismatch, incomplete count, each non-pass status, open repair, downstream regression, dirty repository, missing three-run suite gate, missing EELS identity, and unverified content hash. Only all apparatus gates plus 50/50 exact pass issues a certificate bound to commit, manifest, EELS executable/revision, environment, and run hashes.

- [ ] **Step 5: Implement provenance-only regression promotion**

Promotion copies a minimized data fixture only after a human-approved repair order identifies its representative. It records source run, case, family, repair, and commit. It never changes expected values or approves a fix automatically.

- [ ] **Step 6: Verify and commit**

```powershell
dotnet test Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj --filter "FullyQualifiedName~CampaignRunnerTests|FullyQualifiedName~RunComparatorTests|FullyQualifiedName~RepairOrderServiceTests|FullyQualifiedName~CertificationServiceTests" --no-build
git add Schlieren.Harvest/Campaigns/CampaignRunner.cs Schlieren.Harvest/Comparison/RunComparator.cs Schlieren.Harvest/Repairs Schlieren.Harvest/Certification Schlieren.Harvest/Regression Schlieren.Harvest.Tests
git commit -m "feat: enforce harvest inspection lifecycle"
```

---

## Task 11: Add the internal `schlieren harvest` CLI

**Files:**

- Add: `Schlieren.CLI/Commands/HarvestCommand.cs`
- Modify: `Schlieren.CLI/Program.cs`
- Add: `Schlieren.Tests/CLI/HarvestCommandTests.cs`

- [ ] **Step 1: Build parser tests for the full command tree**

Test help and validation for:

```text
schlieren harvest calibrate
schlieren harvest catalog --fixtures <root> --eels <exe> --eels-version <version>
schlieren harvest campaign create storage-lifecycle --count 50 --fixtures <root> --eels <exe> --eels-version <version>
schlieren harvest campaign run <manifest> --ledger <root> --timeout-seconds <n>
schlieren harvest compare <before-run> <after-run> --ledger <root>
schlieren harvest repair open <family-id> --run <run-id> --ledger <root>
schlieren harvest repair close <repair-id> --commit <sha> --run <run-id> --test <test-name> --ledger <root>
schlieren harvest certify <run-id> --ledger <root> --suite-gate <gate-record>
```

All paths are explicit options or environment-backed defaults; none are compiled machine paths. Use exit code 0 for completed requested operation, 2 for invalid input, 3 for apparatus failure, 4 for conformance divergence, and 5 for certification refusal.

- [ ] **Step 2: Compose production services**

`HarvestCommand` is only composition and presentation. It calls Harvest contracts, prints artifact paths and concise totals, and returns typed exit codes. It does not compare, cluster, or write JSON itself.

- [ ] **Step 3: Verify and commit**

```powershell
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --filter FullyQualifiedName~HarvestCommandTests --no-restore
dotnet run --project Schlieren.CLI -- harvest --help
git add Schlieren.CLI/Commands/HarvestCommand.cs Schlieren.CLI/Program.cs Schlieren.Tests/CLI/HarvestCommandTests.cs
git commit -m "feat: expose internal harvest certification cli"
```

---

## Task 12: Run calibration and freeze Campaign 1

**Files:**

- Add: `harvest/ledger/calibrations/<calibration-id>.json`
- Add: `harvest/ledger/campaigns/storage-lifecycle-v1/<manifest-hash>/manifest.json`
- Add: `harvest/ledger/reports/<calibration-id>.md`
- Modify: `CONFORMANCE_STATUS.md`

- [ ] **Step 1: Record exact inputs before execution**

Capture clean Schlieren commit, `dotnet --info`, OS/architecture, EELS executable version and SHA-256, EELS fixture revision, canonical fixture-root identity, worker version, and credential-scan result. Refuse to proceed if the tree is dirty, EELS is unpinned, or the fixture revision is unknown.

- [ ] **Step 2: Run Phase 0 calibration**

```powershell
dotnet run --project Schlieren.CLI -- harvest calibrate --ledger harvest/ledger --eels $env:EELS_EXE --eels-version $env:EELS_VERSION
```

Expected: six controlled signals classified exactly; apparatus gate green. Commit the immutable calibration record before campaign creation.

- [ ] **Step 3: Run the full suite three times from one build**

```powershell
dotnet build Schlieren.sln -c Release
dotnet test Schlieren.sln -c Release --no-build --logger "trx;LogFileName=full-suite-1.trx"
dotnet test Schlieren.sln -c Release --no-build --logger "trx;LogFileName=full-suite-2.trx"
dotnet test Schlieren.sln -c Release --no-build --logger "trx;LogFileName=full-suite-3.trx"
```

Parse the three TRX files into a hashed suite-gate record. Require identical test identities, outcomes, totals, skips, and failures—not merely equal pass counts.

- [ ] **Step 4: Catalog and freeze exactly 50 storage cases**

```powershell
dotnet run --project Schlieren.CLI -- harvest catalog --fixtures $env:EELS_FIXTURES_ROOT --eels $env:EELS_EXE --eels-version $env:EELS_VERSION
dotnet run --project Schlieren.CLI -- harvest campaign create storage-lifecycle --count 50 --fixtures $env:EELS_FIXTURES_ROOT --eels $env:EELS_EXE --eels-version $env:EELS_VERSION --ledger harvest/ledger
```

If admission yields fewer than 50, stop with the insufficiency artifact. Do not weaken the policy.

- [ ] **Step 5: Commit calibration and manifest**

```powershell
git add harvest/ledger docs/superpowers/specs/2026-08-24-harvest-certification-foundation-design.md CONFORMANCE_STATUS.md
git commit -m "test: freeze harvest storage certification campaign"
```

---

## Task 13: Perform baseline inspection and the repair loop

**Files:**

- Add: `harvest/ledger/runs/<run-id>/`
- Add: `harvest/ledger/reports/<run-id>.md`
- Conditionally add: `harvest/ledger/repairs/<repair-order-id>.json`
- Conditionally add: focused regression fixture/test in the owning subsystem
- Conditionally modify: the confirmed consensus implementation file
- Modify: `CONFORMANCE_STATUS.md`

- [ ] **Step 1: Execute the frozen manifest**

```powershell
dotnet run --project Schlieren.CLI -- harvest campaign run harvest/ledger/campaigns/storage-lifecycle-v1/<manifest-hash>/manifest.json --ledger harvest/ledger --timeout-seconds 120
```

Replace `<manifest-hash>` with the hash printed by Task 12. Inspect the finalized JSON, case totals, non-pass artifacts, clusters, and Markdown projection. Confirm every one of the 50 manifest cases has exactly one durable terminal outcome.

- [ ] **Step 2: If the run contains apparatus failures, repair the apparatus first**

Do not open EVM repair orders for `FixtureInvalid`, `HarnessError`, or `Aborted`. Add a reproducer, repair Harvest or the worker, rerun calibration, create a new run against the identical manifest, and retain the failed run.

- [ ] **Step 3: If the run contains divergences, open one repair order per family**

```powershell
dotnet run --project Schlieren.CLI -- harvest repair open <family-id> --run <run-id> --ledger harvest/ledger
```

For the first family only: select a representative, confirm the governing Ethereum/EELS rule, add a failing permanent regression, apply the smallest canonical EVM repair, run focused tests, then run the full suite. Never edit the expected Harvest result.

- [ ] **Step 4: Reinspect the identical manifest**

Run the same manifest path again, compare runs, and close the repair only with evidence:

```powershell
dotnet run --project Schlieren.CLI -- harvest compare <before-run> <after-run> --ledger harvest/ledger
dotnet run --project Schlieren.CLI -- harvest repair close <repair-id> --commit <repair-sha> --run <after-run> --test <fully-qualified-test-name> --ledger harvest/ledger
```

If Campaign 1 has no Schlieren divergence, record that no artificial defect was introduced; the lifecycle criterion is conditionally satisfied exactly as the approved spec states.

- [ ] **Step 5: Attempt certification honestly**

```powershell
dotnet run --project Schlieren.CLI -- harvest certify <final-run-id> --ledger harvest/ledger --suite-gate <suite-gate-record>
```

Expected outcome is either a certificate with 50/50 exact passes and every gate green, or a typed refusal listing every unmet gate. Both are valid historical outcomes; only the first is certification.

- [ ] **Step 6: Final verification**

```powershell
pwsh -File tools/verify_no_tracked_secrets.ps1
dotnet build Schlieren.sln -c Release
dotnet test Schlieren.sln -c Release --no-build
git status --short
```

Require a clean tree after committing the immutable evidence, no credential findings, and no unaccounted test failure.

- [ ] **Step 7: Commit final evidence**

```powershell
git add harvest/ledger CONFORMANCE_STATUS.md
git commit -m "test: record first harvest storage inspection"
```

---

## Completion Audit

- [ ] Every approved Phase 0 defect has a reproducer, repair, focused verification, and calibration-log entry.
- [ ] The six calibration signals classify exactly as authored.
- [ ] Three consecutive full-suite runs have identical test identities and outcomes.
- [ ] Journal on/off parity covers consensus outputs and post-state.
- [ ] The EELS executable and fixture corpus are independently pinned and hashed.
- [ ] The storage manifest contains exactly 50 admitted official cases and is immutable.
- [ ] Every case has one durable terminal status; no infrastructure condition appears as a pass.
- [ ] Every divergence carries typed deltas and a typed causal-family fingerprint.
- [ ] Before/after comparison rejects different manifests and detects regressions.
- [ ] Repair orders preserve the complete inspection/reinspection chain.
- [ ] Certification refuses every incomplete or non-pass condition.
- [ ] Existing RPC JSON contracts and both UIs remain behaviorally unchanged except credential externalization.
- [ ] `harvest/ledger/` and the final report contain exact commit, environment, EELS, manifest, and artifact hashes.
- [ ] Full solution build, full tests, secret scan, and repository cleanliness are verified from fresh commands.
