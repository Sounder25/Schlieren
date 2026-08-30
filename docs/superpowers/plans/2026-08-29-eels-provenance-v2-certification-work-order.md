# EELS Provenance V2 and Recertification Work Order

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` and execute this work order inline, one task at a time. Do not delegate, create subagents, or begin a later task before the current stop gate is approved. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the incomplete EELS launcher-based provenance path with a versioned, semantically bound v2 certification path while preserving every v1 manifest, run, certificate, JSON contract, and historical result unchanged.

**Architecture:** V1 remains a read-compatible, immutable historical format. V2 freezes a complete semantic EELS identity in the manifest, records the independently observed identity in the run, compares the two before execution and certification, and binds the verified canonical identity hash into a v2 certificate. Certification is fail-closed: missing, dirty, incomplete, mismatched, or unrepeatable provenance produces a typed refusal.

**Tech Stack:** .NET 8, C#, System.Text.Json, System.CommandLine, xUnit, Git, Python/EELS 2.19.0, PowerShell.

**Spec:** This work order is controlling. It refines `docs/superpowers/plans/2026-08-26-strategic-campaign-certification.md` and preserves the historical evidence in `harvest/ledger/certificates/2026-08-28-strategic-campaign-certificate.md`.

## Overall goals

1. Preserve the existing 350/350 result as historical v1 conformance evidence; never delete, rewrite, or silently upgrade it.
2. Make new certification depend on semantic EELS provenance rather than a pip-generated launcher hash alone.
3. Keep expected provenance, observed provenance, run evidence, and certificate evidence cryptographically bound.
4. Preserve all existing JSON contracts and canonical v1 hashes.
5. Establish a clean, reproducible EELS 2.19.0 environment and rerun the seven existing 50-case campaigns under v2.
6. Issue v2 certificates only after 350/350, identical provenance, and three retained full-suite runs.
7. Keep BLS12-381 outside this work. It may become a separate future campaign only after v2 recertification is complete and separately authorized.

## Current authoritative facts

- Repository: `C:\projects\Schlieren`
- Branch: `main`
- Current pushed HEAD at intake: `e928b12a198eb8f86154acf82afd4f4e6f2c4699`
- Historical certification commit: `e50593d`
- Historically certified Schlieren implementation: `13fec7bcca76c5b17dde1c19989e368a712a58ca`
- Historical result: 350/350 across seven frozen v1 manifests.
- Existing EELS checkout: `C:\projects\execution-specs`
- Existing EELS source commit: `85aa48c742c38a2d5a876f84ebf8082a50273064`
- Existing EELS checkout is dirty at `src/ethereum_spec_tools/evm_tools/daemon.py` and is not eligible for v2 certification.
- The main working tree already contains uncommitted provenance work, unrelated React CSS work, and an unauthorized untracked `harvest/ledger/campaigns/precompiles-bls12-v1/` directory.

## Global constraints

- Do not run `git reset`, `git clean`, `git checkout --`, or any command that discards existing work.
- Do not stash, move, edit, stage, delete, or commit any `schlieren-ui/` file.
- Do not edit, stage, delete, or commit `harvest/ledger/campaigns/precompiles-bls12-v1/`.
- Do not create or run a BLS12-381 campaign.
- Do not modify EVM execution, gas, state transition, opcode, journal, detector, RPC, or React behavior.
- Do not modify any existing v1 manifest, run, comparison, certificate, or ledger artifact.
- Never use `git add .`, `git add -A`, or broad directory staging. Stage exact authorized files only.
- Do not parse diagnostic strings to determine status or certification eligibility.
- Missing prerequisites, unknown Git state, timeout, cancellation, process failure, absent fixtures, or incomplete evidence never count as Pass.
- A dirty or unprovable EELS checkout is never certification-eligible.
- The launcher SHA-256 is retained as runtime evidence but is not sufficient semantic identity.
- Every behavior change begins with a failing test, records the red result, implements the minimum correction, and records the green result.
- Stop for review after every task. Do not commit or push until Task 7 explicitly authorizes it.

## Authorized file boundary

Production files that may be modified:

- `Schlieren.Harvest/Configuration/EelsSemanticIdentity.cs`
- `Schlieren.Harvest/Configuration/EelsProvenanceProbe.cs`
- `Schlieren.Harvest/Campaigns/CampaignManifest.cs`
- `Schlieren.Harvest/Campaigns/CampaignRunner.cs`
- `Schlieren.Harvest/Ledger/LedgerTypes.cs`
- `Schlieren.Harvest/Certification/CertificationService.cs`
- `Schlieren.CLI/Commands/HarvestCommand.cs`
- `Schlieren.Harvest/Schlieren.Harvest.csproj` only for `InternalsVisibleTo`

Test and package files that may be modified:

- `Schlieren.Harvest.Tests/Configuration/EelsProvenanceProbeTests.cs`
- `Schlieren.Harvest.Tests/Configuration/EelsProvenanceProbeRunPythonTests.cs`
- `Schlieren.Harvest.Tests/Serialization/CanonicalSerializationTests.cs`
- `Schlieren.Harvest.Tests/Campaigns/CampaignRunnerTests.cs`
- `Schlieren.Harvest.Tests/Certification/CertificationServiceTests.cs`
- `Schlieren.Tests/CLI/HarvestCommandTests.cs`
- `Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj` and `Directory.Packages.props` only if an explicit dynamic-skip test dependency is already present and required; do not add another package without approval.

Documentation files that may be added or amended after code acceptance:

- This work order.
- `harvest/ledger/reports/strategic-campaign-train-status.md`
- A new append-only v2 provenance/certification report under `docs/harvest/certification/`.

`Schlieren.Harvest/Campaigns/CampaignFamilyPolicy.cs` is not authorized. Any existing uncommitted change there belongs to the out-of-scope BLS work and must not be staged.

---

### Task 0: Freeze the intake and separate scope

**Files:** No edits.

**Produces:** A written inventory of authorized provenance changes and unrelated changes.

- [ ] **Step 1: Record repository state**

Run:

```powershell
Set-Location C:\projects\Schlieren
git rev-parse HEAD
git status --short
git diff --name-only
git diff --check
```

Expected: HEAD is recorded; unrelated React and BLS paths are explicitly listed as excluded.

- [ ] **Step 2: Record the current compilation state without editing**

Run:

```powershell
dotnet build Schlieren.sln --no-restore
```

Record success or every compiler error exactly. Do not repair an unrelated failure.

- [ ] **Step 3: Stop and report**

Report the authorized modified files, excluded modified files, build result, and exact next task. Make no code change before approval.

---

### Task 1: Define complete canonical EELS semantic identity

**Files:**

- Modify: `Schlieren.Harvest/Configuration/EelsSemanticIdentity.cs`
- Test: `Schlieren.Harvest.Tests/Configuration/EelsProvenanceProbeTests.cs`

**Consumes:** Raw semantic facts produced by the probe.

**Produces:** `EelsSemanticIdentity.ValidateForCertification()` and `EelsSemanticIdentity.CanonicalHash`.

- [ ] **Step 1: Write failing identity tests**

Add tests with these exact contracts:

```csharp
[Fact] public void CanonicalHash_DependencyInsertionOrder_DoesNotChangeHash();
[Fact] public void CanonicalHash_DependencyVersionChange_ChangesHash();
[Fact] public void CanonicalHash_EveryCertificationFieldChange_ChangesHash();
[Fact] public void ValidateForCertification_EmptyRequiredFields_ReturnsTheirNames();
[Fact] public void ValidateForCertification_DirtyCheckout_ReturnsIsCleanCheckout();
[Fact] public void Constructor_RequiresExplicitCleanlinessAndLockHashes();
```

The “every field” test must cover package name/version, source repository, source commit, source-tree hash, EVM-tools hash, `uv.lock` hash, `pyproject.toml` hash, Python implementation/version, runtime platform, installation mode, distribution artifact or RECORD hash, launcher hash, dependency versions, and clean state.

- [ ] **Step 2: Run the identity tests and capture RED**

```powershell
dotnet test Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj --no-restore --filter "FullyQualifiedName~EelsProvenanceProbeTests"
```

Expected: the new completeness and canonical-hash tests fail against the current implementation.

- [ ] **Step 3: Implement the identity contract**

The public interface must be equivalent to:

```csharp
public sealed class EelsSemanticIdentity
{
    public string PackageName { get; }
    public string PackageVersion { get; }
    public string SourceRepository { get; }
    public string SourceCommit { get; }
    public string SourceTreeSha256 { get; }
    public string EvmToolsSha256 { get; }
    public string UvLockSha256 { get; }
    public string PyprojectTomlSha256 { get; }
    public string PythonImplementation { get; }
    public string PythonVersion { get; }
    public string RuntimePlatform { get; }
    public string InstallMode { get; }
    public string DistributionArtifactSha256 { get; }
    public string LauncherSha256 { get; }
    public IReadOnlyDictionary<string,string> DependencyVersions { get; }
    public bool IsCleanCheckout { get; }
    public string CanonicalHash { get; }
    public IReadOnlyList<string> ValidateForCertification();
}
```

Constructor parameters for cleanliness and all hashes must have no defaults. `CanonicalHash` must hash a deterministic representation containing every field above except `CanonicalHash` itself. Sort dependency keys with `StringComparer.Ordinal`. Do not add a comment or behavior allowing dependency updates without recertification.

`IsSemanticEquivalent` may remain only as a diagnostic source-equivalence helper. Certification must use exact canonical-hash equality.

- [ ] **Step 4: Run identity tests and capture GREEN**

Run the Task 1 filter again. Expected: all identity tests pass.

- [ ] **Step 5: Stop and report**

Report the exact canonical fields and test totals. Do not begin the probe changes before approval.

---

### Task 2: Make the provenance probe deterministic and fail-closed

**Files:**

- Modify: `Schlieren.Harvest/Configuration/EelsProvenanceProbe.cs`
- Modify: `Schlieren.Harvest/Schlieren.Harvest.csproj` only if needed for test visibility
- Test: `Schlieren.Harvest.Tests/Configuration/EelsProvenanceProbeTests.cs`
- Test: `Schlieren.Harvest.Tests/Configuration/EelsProvenanceProbeRunPythonTests.cs`

**Produces:** `EelsProvenanceProbe.Probe(...)` returning a complete observed identity or a typed failure.

- [ ] **Step 1: Write failing process and probe tests**

Required deterministic unit tests:

```csharp
[Fact] public void PythonStartInfo_PreservesSeparateArguments();
[Fact] public void PythonStartInfo_PreservesSpacesQuotesBackslashesAndNewlines();
[Fact] public void Probe_UnknownGitStatus_IsNotClean();
[Fact] public void Probe_MissingLockfile_IsIncomplete();
[Fact] public void SourceTreeHash_IncludesRelativePathsAndFileBytes();
[Fact] public void SourceTreeHash_RenameChangesHash();
```

Required integration tests:

```csharp
[Fact] public void RunPython_MultilineScriptAndSpacedPath_RoundTrips();
[Fact] public void RunPython_Timeout_IsTyped();
[Fact] public void RunPython_Cancellation_IsDistinctFromTimeout();
[Fact] public void RunPython_LargeStdoutAndStderr_DoNotDeadlock();
```

If Python is unavailable, integration tests must be reported as explicit xUnit skips. They must never `return` and appear as passes. Unit tests must not require Python.

- [ ] **Step 2: Capture RED**

```powershell
dotnet test Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj --no-restore --filter "FullyQualifiedName~EelsProvenanceProbeRunPythonTests|FullyQualifiedName~EelsProvenanceProbeTests"
```

- [ ] **Step 3: Implement deterministic process execution**

Use `ProcessStartInfo.ArgumentList`; never manually quote or escape a combined argument string. Start stdout and stderr drains immediately. Use a linked timeout/caller token with `WaitForExitAsync`. On timeout or cancellation, kill the entire process tree, await termination, finish draining both streams, and return distinct typed outcomes. A Git failure or timeout must not produce `IsCleanCheckout=true`.

Source-tree hashes must feed SHA-256 with normalized relative path, byte length, and file bytes for every admitted source file in ordinal path order. Concatenating file bytes without paths is prohibited.

Read `direct_url.json` or installed metadata to record installation mode. Record the repository remote URL and a distribution artifact or canonical installed-distribution digest. Do not use the current working directory to infer the EELS commit.

- [ ] **Step 4: Capture GREEN**

Run the Task 2 filter again and record pass/skip totals separately.

- [ ] **Step 5: Probe the current EELS checkout honestly**

Probe:

```text
Executable: C:\projects\execution-specs\.venv\Scripts\ethereum-spec-evm.exe
Source root: C:\projects\execution-specs
```

Expected: version 2.19.0 is observed, source commit `85aa48c...` is observed, and certification validation refuses because the checkout is dirty. A refusal is the correct result.

- [ ] **Step 6: Stop and report**

Do not clean or rebuild EELS in this task.

---

### Task 3: Preserve v1 and introduce an explicit v2 manifest

**Files:**

- Modify: `Schlieren.Harvest/Campaigns/CampaignManifest.cs`
- Test: `Schlieren.Harvest.Tests/Serialization/CanonicalSerializationTests.cs`

**Produces:** Legacy `Freeze(...)` behavior for v1 and explicit `FreezeV2(...)` for new certification manifests.

- [ ] **Step 1: Add v1 golden compatibility tests**

Required tests:

```csharp
[Fact] public void V1Manifest_SerializesByteForByteWithoutV2Fields();
[Fact] public void V1Manifest_KnownHistoricalHash_RemainsUnchanged();
[Fact] public void ExistingV1Manifest_DeserializeSerialize_DoesNotAddNullV2Fields();
```

Use a checked-in historical v1 manifest as the golden input. Assert that neither `eelsProvenance` nor `semanticIdentityHash` appears in v1 JSON.

- [ ] **Step 2: Add v2 red tests**

```csharp
[Fact] public void FreezeV2_RequiresCompleteCleanProvenance();
[Fact] public void FreezeV2_RequiresCampaignVersionTwo();
[Fact] public void FreezeV2_StoresCanonicalSemanticHash();
[Fact] public void FreezeV2_ThinIdentityMustBindToObservedRuntime();
[Fact] public void V2ManifestHash_ChangesWhenAnyProvenanceFieldChanges();
```

- [ ] **Step 3: Capture RED**

```powershell
dotnet test Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj --no-restore --filter "FullyQualifiedName~CanonicalSerializationTests"
```

- [ ] **Step 4: Implement explicit versioning**

Keep existing v1 creation and hashing behavior unchanged. Add an explicit v2 entry point equivalent to:

```csharp
public static CampaignManifest FreezeV2(
    IReadOnlyList<FixtureCaseMetadata> cases,
    string campaignId,
    DateTime createdUtc,
    EelsIdentity runtimeIdentity,
    EelsSemanticIdentity semanticIdentity,
    string fixtureRootSha256,
    string familyName,
    IReadOnlyList<string> comparisonFields);
```

V2 fields must use `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` so v1 output omits them. Do not select schema version merely because an optional parameter happens to be null or non-null. `FreezeV2` must set schema version `2` and campaign version `2` explicitly.

- [ ] **Step 5: Capture GREEN and stop**

Run the Task 3 filter. Report the historical manifest and hash used by the golden test.

---

### Task 4: Persist observed provenance in the run

**Files:**

- Modify: `Schlieren.Harvest/Ledger/LedgerTypes.cs`
- Modify: `Schlieren.Harvest/Campaigns/CampaignRunner.cs`
- Test: `Schlieren.Harvest.Tests/Campaigns/CampaignRunnerTests.cs`
- Test: `Schlieren.Harvest.Tests/Serialization/CanonicalSerializationTests.cs`

**Produces:** Optional legacy-compatible run fields `EelsProvenance` and `EelsSemanticIdentityHash` populated for v2 runs.

- [ ] **Step 1: Write failing persistence and mismatch tests**

```csharp
[Fact] public void V1Run_NullProvenanceFieldsAreOmitted();
[Fact] public void V2Run_PersistsObservedProvenanceAndCanonicalHash();
[Fact] public void V2Run_ExpectedAndObservedProvenanceMismatch_RefusesBeforeFirstCase();
[Fact] public void V2Run_DirtyObservedProvenance_RefusesBeforeFirstCase();
```

- [ ] **Step 2: Capture RED**

Run filters for `CampaignRunnerTests|CanonicalSerializationTests`.

- [ ] **Step 3: Implement run binding**

Add nullable, omit-when-null v2 fields to `RunRecord`. The runner must receive observed provenance from the composition root; it must not copy the manifest value and call it observed. Before executing any case, compare the observed canonical hash to the frozen manifest canonical hash and validate cleanliness/completeness. On failure, create no case results and return a typed apparatus refusal.

- [ ] **Step 4: Capture GREEN and stop**

Report v1 JSON compatibility and the exact pre-execution refusal behavior.

---

### Task 5: Bind certification to manifest and run evidence

**Files:**

- Modify: `Schlieren.Harvest/Certification/CertificationService.cs`
- Test: `Schlieren.Harvest.Tests/Certification/CertificationServiceTests.cs`

**Produces:** A v2 certificate containing `EelsSemanticIdentityHash` and typed refusal reasons.

- [ ] **Step 1: Write failing refusal tests**

Add these exact tests:

```csharp
[Fact] public void CertifyV2_V1Manifest_Refuses();
[Fact] public void CertifyV2_MissingManifestProvenance_Refuses();
[Fact] public void CertifyV2_MissingRunProvenance_Refuses();
[Fact] public void CertifyV2_IncompleteProvenance_Refuses();
[Fact] public void CertifyV2_DirtyProvenance_Refuses();
[Fact] public void CertifyV2_ManifestAndRunCanonicalHashMismatch_Refuses();
[Fact] public void CertifyV2_ThinRuntimeIdentityMismatch_Refuses();
[Fact] public void CertifyV2_StoredSemanticHashMismatch_Refuses();
[Fact] public void CertifyV2_AllGatesGreen_IssuesBoundCertificate();
```

The all-green fixture must use one internally consistent version, launcher hash, source commit, manifest identity, run identity, and semantic hash. Arbitrary unrelated “clean” objects are prohibited.

- [ ] **Step 2: Capture RED**

```powershell
dotnet test Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj --no-restore --filter "FullyQualifiedName~CertificationServiceTests"
```

- [ ] **Step 3: Implement certificate binding**

The v2 API must consume the actual manifest and run, not a free provenance parameter:

```csharp
public CertificationResult CertifyV2(
    RunRecord run,
    CampaignManifest manifest,
    string runContentHash,
    SuiteGateRecord suiteGate,
    bool repositoryClean,
    bool hasOpenRepairOrders,
    bool hasRegressions);
```

If `SuiteGateRecord` is not yet implemented on the current branch, retain the existing typed suite-gate input and do not invent a boolean replacement in this task; record that limitation for the later suite-gate task.

The certificate must store the manifest hash, run content hash, full Schlieren commit, schema version, EELS semantic canonical hash, and runtime launcher hash. Preserve the existing v1 certificate type and artifacts unchanged; introduce a versioned v2 certificate type or nullable omit-when-null additions proven compatible by golden tests.

- [ ] **Step 4: Capture GREEN and stop**

Report every refusal reason and the certificate fields asserted by the all-green test.

---

### Task 6: Wire CLI creation and execution without legacy fallbacks

**Files:**

- Modify: `Schlieren.CLI/Commands/HarvestCommand.cs`
- Test: `Schlieren.Tests/CLI/HarvestCommandTests.cs`

**Produces:** Explicit v1 discovery compatibility and v2 certification creation/run paths.

- [ ] **Step 1: Write failing CLI tests**

```csharp
[Fact] public async Task CampaignCreateV2_UsesProbeIdentityWithoutEelsVersionFlag();
[Fact] public async Task CampaignCreateV2_DirtyEelsRefuses();
[Fact] public async Task CampaignRunV2_ObservedIdentityMismatchRefusesBeforeLedgerRun();
[Fact] public async Task CampaignRunV2_ClearsPythonPathForEelsOnly();
[Fact] public async Task CampaignV1_CannotEnterV2Certification();
```

- [ ] **Step 2: Capture RED**

Run the `HarvestCommandTests` filter.

- [ ] **Step 3: Implement CLI composition**

V2 creation derives package version and semantic identity from the probe. It must not require the legacy `--eels-version` flag. V2 execution probes the actual configured EELS environment, compares it to the manifest before ledger creation, and passes the observed identity to the runner. Remove `PYTHONPATH` only from the EELS child-process environment; do not mutate the parent process globally.

Retain v1 commands only for historical inspection/discovery compatibility. They must not issue v2 certificates.

- [ ] **Step 4: Capture GREEN and stop**

Report CLI exit codes and stable typed refusal codes.

---

### Task 7: Full verification, scoped commit, and handoff

**Files:** Only files authorized above.

- [ ] **Step 1: Run focused suites**

```powershell
dotnet test Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj --no-restore --filter "FullyQualifiedName~EelsProvenance|FullyQualifiedName~CanonicalSerialization|FullyQualifiedName~CampaignRunner|FullyQualifiedName~CertificationService"
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --no-restore --filter "FullyQualifiedName~HarvestCommandTests"
```

- [ ] **Step 2: Run full affected suites**

```powershell
dotnet test Schlieren.Harvest.Tests/Schlieren.Harvest.Tests.csproj --no-restore
dotnet test Schlieren.Tests/Schlieren.Tests.csproj --no-restore
dotnet build Schlieren.sln -c Release --no-restore
```

Report pass, fail, and skip separately. Do not call fixture-unavailable or interrupted aggregate runs green.

- [ ] **Step 3: Verify historical compatibility**

Deserialize and reserialize at least one checked-in v1 manifest and v1 run. Assert byte-for-byte canonical output and unchanged content hashes. Run an existing historical ledger comparison and verify that it does not rewrite artifacts.

- [ ] **Step 4: Review exact scope**

```powershell
git diff --check
git status --short
git diff --name-only
```

The report must explicitly list every excluded React/BLS file still dirty and confirm none is staged.

- [ ] **Step 5: Stop for approval before commit**

Provide:

- exact test totals;
- exact modified authorized files;
- exact excluded files;
- v1 golden compatibility result;
- current dirty EELS refusal result;
- proposed commit message: `fix: bind v2 certification to semantic EELS provenance`.

Do not stage, commit, or push until approval.

- [ ] **Step 6: Commit only after approval**

Use explicit file paths. Never stage `CampaignFamilyPolicy.cs`, `schlieren-ui/`, or `precompiles-bls12-v1/`. Push to `origin/main`, verify the online SHA, and leave a written handoff.

---

### Task 8: Establish clean EELS and recertify the existing seven campaigns

**Authorization:** This task requires a separate approval after Task 7 is pushed. Do not begin automatically.

**Goal:** Produce new v2 evidence without modifying v1 evidence.

- [ ] **Step 1: Create an isolated clean EELS source checkout**

Preserve `C:\projects\execution-specs` and its dirty `daemon.py` change. Create a separate clean checkout/worktree at the verified EELS 2.19.0 source commit. Confirm clean status and record the remote, full commit, tag/version declaration, `uv.lock`, and `pyproject.toml` hashes.

- [ ] **Step 2: Build and install a reproducible distribution**

Build a wheel using the locked dependency set, hash the wheel, install it in a dedicated certification environment, and probe it. Certification validation must return no incomplete fields and `IsCleanCheckout=true`.

- [ ] **Step 3: Create seven v2 manifests**

Use exactly the same ordered 50 case IDs and fixture hashes as the seven historical v1 campaigns. Each v2 manifest records its v1 parent manifest hash. Do not add, remove, substitute, or reorder cases.

- [ ] **Step 4: Freeze one Schlieren candidate commit**

Require a clean tree and Release build. Record full commit, worker binary hash, all seven v2 manifest hashes, fixture identity, semantic EELS identity hash, and environment.

- [ ] **Step 5: Run the seven campaigns without code changes**

Required result:

```text
Storage Lifecycle             50/50
Call Semantics                50/50
Create Semantics              50/50
Return Data                   50/50
Self-Destruct                 50/50
Transient Storage            50/50
Access List/Fee Market        50/50
Total                        350/350
```

Any non-Pass outcome stops issuance. Diagnose; do not repair during the inspection set.

- [ ] **Step 6: Run the full Release suite three times**

Retain three distinct TRX files and their SHA-256 hashes. Require identical test identities/totals and zero failures. Missing fixtures and skips must be identical and explicitly accounted for.

- [ ] **Step 7: Issue v2 certificates**

Issue seven individual certificates and one umbrella certificate, all bound to the same Schlieren commit, semantic EELS hash, fixture identity, environment, and suite gate. Append evidence; never overwrite v1 artifacts.

- [ ] **Step 8: Commit, push, and close**

Commit only new v2 manifests, runs, reports, suite-gate artifacts, and certificates. Push and verify the online SHA.

---

## Final acceptance criteria

The work is complete only when all statements below are true:

- Existing v1 JSON and hashes are unchanged.
- Existing v1 manifests, runs, comparisons, and certificates remain present and untouched.
- V2 manifests contain complete semantic provenance and a matching canonical identity hash.
- V2 runs contain independently observed provenance and refuse mismatch before case execution.
- V2 certification consumes manifest and run evidence rather than an unattached caller object.
- Dirty, incomplete, missing, or mismatched provenance produces typed refusal.
- Dependency changes alter the semantic canonical hash.
- Python absence is a visible skip or failure, never a false pass.
- No React, BLS, EVM, journal, RPC, detector, or unrelated file is included in the provenance commit.
- A clean EELS environment is used for final v2 evidence.
- All seven campaigns pass 350/350 on one unchanged commit and provenance set.
- Three full Release suite runs are retained and identical.
- GitHub contains the verified commits and the working tree handoff identifies all remaining unrelated changes.
