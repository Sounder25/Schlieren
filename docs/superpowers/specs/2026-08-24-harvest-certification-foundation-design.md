# Harvest Certification Foundation Design

Date: 2026-08-24
Status: Approved 2026-08-24
Target: First internal Harvest certification cycle

## Purpose

Harvest is a product for finding defects in Schlieren by replaying independently grounded Ethereum cases and comparing canonical outputs. It is not the future Web3 Hunter. Hunter searches for harmful contract behavior; Harvest measures whether Schlieren agrees with Ethereum and EELS.

This first delivery establishes a vehicle-inspection-style certification process:

1. calibrate the measuring apparatus;
2. repair known apparatus defects;
3. inspect a fixed 50-case campaign;
4. submit every conformance defect as a repair order;
5. repair one failure family at a time;
6. reinspect the identical campaign;
7. certify only an exact Schlieren commit, EELS revision, and campaign manifest.

## Product boundary

Two products will eventually share Schlieren's canonical EVM:

- **Harvest** finds bugs in Schlieren. Its ground truth is EELS or an Ethereum receipt plus exact prestate and expected post-state. Its output is a typed conformance delta and certification history.
- **Hunter** finds bugs in third-party contracts. Its ground truth is an explicit harm invariant. Its output is a reproducible harmful call sequence or profit path.

Hunter is outside this design. Harvest must not classify a security pattern as an exploit, search calldata, or mutate contract behavior.

The future customer-facing **Conformance Verifier** is also outside this implementation. The verifier will eventually consume signed, versioned campaign packs and shared report schemas. Internal fixture selection, mutation, clustering intelligence, minimization, and repair workflow remain part of Harvest.

## Scope

### Included

- Phase 0 known-defect repair and calibration.
- A production `Schlieren.Harvest` application layer.
- An internal Harvest CLI surface.
- Immutable campaign manifests.
- Deterministic selection of 50 complete storage-focused EELS cases.
- Fixture admission validation before a manifest is frozen.
- Canonical Schlieren execution against the selected cases.
- Typed comparison of status, gas, logs, return data where specified, and post-state.
- Failure-family clustering.
- Append-only run records, repair orders, before/after comparisons, and certification logs.
- First baseline inspection of Campaign 1.
- At least one complete divergence-to-reinspection lifecycle if the campaign exposes a Schlieren defect.

### Deferred

- Customer-facing Conformance Verifier.
- EELS network download/update support and signed packs.
- React Harvest UI changes.
- Real-transaction acquisition or receipt-only replay.
- Generated mutation campaigns.
- Coverage-guided selection.
- Hosted services, databases, authentication, billing, and telemetry.
- Hunter.

## Phase 0: apparatus calibration

Harvest cannot certify Schlieren until its own measuring path is trustworthy. Phase 0 is a hard gate and must complete before Campaign 1 may issue a conformance result.

### Known defects to resolve

1. The process-global `OpSecLockout.IsEnabled` race exposed by concurrent Workbench and async OpSec tests.
2. The `StateOverlay.GetStorageAtAsync` recursion/stack-overflow path that can abort taxonomy runs.
3. Embedded Harvest/n8n credentials. Secrets must be removed from tracked source, rotated outside the repository, and loaded through explicit external configuration.
4. Taxonomy execution that can terminate the test host without emitting a durable aborted-run artifact.
5. Security/conformance documentation that describes superseded journal rules or obsolete branch state.

Each defect requires a reproducing test or deterministic probe before repair, a focused verification after repair, and an entry in the initial certification log.

### Calibration signals

The apparatus must correctly recognize six controlled cases:

- known exact match;
- deliberate gas mismatch;
- deliberate status mismatch;
- deliberate state mismatch;
- malformed fixture;
- aborted execution.

The expected classification is hand-authored and independent of the comparator under test.

### Phase 0 acceptance

- All six calibration signals receive the correct status and typed evidence.
- Known crashes and shared-state races are fixed.
- Three consecutive full test-suite runs produce identical totals and case outcomes.
- Journal-enabled and journal-disabled canonical executions retain outcome, gas, return data, logs, and post-state parity.
- No tracked source contains operational credentials.
- A crash, timeout, or malformed fixture produces a durable non-pass artifact.
- The baseline report identifies the exact Schlieren commit, runtime environment, EELS revision, and remaining limitations.

## Architecture

Create a production library, `Schlieren.Harvest`, rather than orchestrating test assemblies or growing the existing Python scripts into a second implementation. Existing campaigns and harnesses may be adapted, but production behavior must move behind shared application contracts.

### Components

#### `FixtureCatalog`

- Indexes locally installed EELS fixture roots.
- Records EELS release/commit identity and fixture checksums.
- Exposes typed fixture metadata without executing cases.
- Rejects missing, malformed, incomplete, ambiguous, or unsupported fixtures with stable reason codes.

#### `CampaignSelector`

- Applies a versioned storage-family selection policy.
- Selects exactly 50 admitted cases deterministically.
- Balances declared dimensions rather than sampling random opcodes.
- Emits a complete manifest whose case IDs and source checksums cannot change after the first run.

#### `CampaignRunner`

- Executes only admitted manifest cases.
- Uses the canonical Schlieren `StateTransition`/`EvmMachine` path.
- Does not re-execute through a diagnostic engine or trace-derived fallback.
- Places each case behind an execution boundary that can report timeout, crash, cancellation, and host termination distinctly.

#### `ConformanceComparator`

- Compares normalized expected and actual outcomes field by field.
- Produces typed deltas; it never parses human-readable mismatch strings.
- Preserves multiple discrepancies on one case while identifying the earliest available causal evidence.

#### `FailureFamilyClusterer`

- Reuses or advances the canonical causal fingerprint model.
- Groups cases by first causal divergence geometry, not final message text or test name.
- Keeps fork-specific failures separate unless an explicit cross-fork relationship is proven.

#### `RunLedger`

- Persists append-only manifests, runs, case artifacts, cluster records, repair orders, comparisons, and certificates.
- Writes atomically so interrupted runs cannot masquerade as complete runs.
- Never modifies or deletes a prior inspection record.

#### `RunComparator`

- Compares two runs of the same manifest.
- Reports eliminated, reduced, expanded, introduced, unchanged, and regressed families.
- Rejects comparisons whose manifest hashes differ.

#### `RegressionPromoter`

- Converts a fixed representative into a small permanent regression fixture and test.
- Records the source campaign, repair order, and repair commit.
- Does not automatically approve or edit expected results.

## Campaign 1: storage lifecycle

Campaign 1 uses 50 complete official EELS fixtures with independent prestate and expected post-state. Synthetic and real-transaction cases are deferred.

The selection policy must cover as many of these declared dimensions as the installed corpus supports:

- `SLOAD` and `SSTORE`;
- warm and cold storage access;
- zero-to-nonzero, nonzero-to-zero, and nonzero-to-different-nonzero transitions;
- repeated writes and unchanged writes;
- root and nested frame ownership;
- `CALL`, `STATICCALL`, `DELEGATECALL`, and `CALLCODE` geometry where applicable;
- child commit and child rollback;
- ancestor rollback;
- committed execution and simulation-discarded execution where the harness supports both;
- gas refund effects;
- fork-sensitive storage behavior represented by the available EELS corpus.

Selection must be deterministic. If fewer than 50 complete cases satisfy the policy, manifest creation fails with an insufficiency report; it must not silently relax admission rules or fill the batch with unrelated cases.

## Immutable campaign manifest

The manifest is the inspected object's identity. It includes:

- schema version;
- campaign ID and version;
- family name and batch size;
- selection-policy version;
- EELS release/commit identity;
- fixture root identity;
- ordered case IDs, source paths, and checksums;
- fork and declared coverage dimensions for each case;
- required comparison fields;
- creation timestamp and tool version;
- canonical manifest hash.

After the first run, the manifest is immutable. Replacing or reclassifying a case requires a new campaign version and manifest, not an edit.

## Run and case statuses

Apparatus integrity and engine conformance are separate measurements.

Each case receives exactly one terminal status:

- `Pass`: Schlieren matches all required EELS outputs.
- `Divergence`: execution completed but at least one required output differs.
- `FixtureInvalid`: fixture failed admission or runtime validation.
- `HarnessError`: Harvest failed to perform the comparison correctly.
- `Aborted`: crash, timeout, cancellation, or host termination prevented a valid result.
- `Quarantined`: independent evidence proves an oracle or fixture defect.

Only `Pass` contributes positively to conformance. `FixtureInvalid`, `HarnessError`, `Aborted`, and `Quarantined` are never silently removed from denominators or presented as passes.

Because manifests are validated before freezing, a runtime `FixtureInvalid` indicates an apparatus/admission defect and invalidates certification for that run.

## Comparison contract

### Ground-truth authority

Every required comparison field names its independent authority in the manifest:

- fixture post-state supplies account existence, nonce, balance, code, storage, logs hash, and transaction validity/status when the fixture defines them;
- the pinned EELS executable oracle supplies exact gas, refund, return data, logs, and other runtime outputs required by the campaign;
- fixture metadata supplies the fork and block environment.

Schlieren output is never used to fill a missing expected value. If the declared authority cannot produce a required field, fixture admission fails before the manifest is frozen. Campaign 1 requires exact status, gas, logs, and represented post-state, so all 50 admitted cases must have independent authority for those fields.

For every applicable field, persist expected value, actual value, equality, and a stable discrepancy kind:

- transaction validity/status;
- total gas used;
- refund counter when specified;
- return data when specified;
- ordered logs including address, topics, and data;
- account existence;
- nonce;
- balance;
- code;
- storage slots and values represented by the expected post-state.

Journal-only facts such as frame IDs, gas semantics, and dispositions are supporting evidence. They help locate the cause but cannot replace independent EELS output as ground truth.

## Failure-family fingerprints

A fingerprint uses typed facts when available:

- fork;
- discrepancy layer and kind;
- expected/actual status geometry;
- first divergent frame ID, call type, and ancestry;
- first divergent instruction ID, opcode, and PC;
- gas-rule/component identity and delta;
- state-effect kind, owner, slot, and disposition;
- exception/halt category;
- journal conservation state.

Human summaries are generated from fingerprints but are never cluster keys.

## Inspection, repair, and reinspection

Campaign states are explicit:

`Draft → Calibrating → ReadyForInspection → Inspecting → InspectionFailed → UnderRepair → ReadyForReinspection → Reinspecting → Certified`

An aborted or invalid run transitions to an apparatus-failure state and cannot advance to inspection failure or certification.

Each divergence cluster creates a numbered repair order containing:

- cluster fingerprint and affected cases;
- expected and actual output facts;
- journal evidence links;
- initial diagnosis and confidence;
- root cause once confirmed;
- repair commit;
- tests added;
- reinspection run ID;
- final disposition.

Repairs address one failure family at a time. The identical manifest is rerun after repair.

Reinspection reports:

- clusters eliminated;
- clusters reduced or expanded;
- new clusters introduced;
- previously passing cases regressed;
- unchanged failures;
- runtime and throughput changes.

Certification requires apparatus integrity, 50 completed valid cases, 50 exact matches, no open campaign repair orders, no downstream regression, and a clean full-suite gate.

## Append-only certification log

The first delivery stores the compact, version-controlled ledger at `harvest/ledger/`:

```text
harvest/ledger/
  campaigns/<campaign-id>/<manifest-hash>/manifest.json
  runs/<run-id>/run.json
  runs/<run-id>/cases/<case-id>.json
  runs/<run-id>/clusters/<family-id>.json
  repairs/<repair-order-id>.json
  comparisons/<before-run>--<after-run>.json
  certificates/<certificate-id>.json
  reports/<run-id>.md
```

Every artifact includes a schema version and content hash. Passing cases remain compact entries in `run.json`; detailed case files are required for every divergence, invalid, harness-error, aborted, or quarantined outcome. Run completion uses an atomic finalization marker written only after every declared case and summary artifact is durable.

The human-readable Markdown report is a projection of machine-readable records, not the authoritative source.

## Internal CLI

The first delivery extends the existing Schlieren CLI with these commands:

- `schlieren harvest calibrate` — execute Phase 0 calibration signals and baseline gates;
- `schlieren harvest catalog` — validate and summarize an EELS fixture root;
- `schlieren harvest campaign create storage-lifecycle --count 50` — freeze a manifest;
- `schlieren harvest campaign run <manifest>` — execute an inspection or reinspection;
- `schlieren harvest compare <before-run> <after-run>` — produce the before/after record;
- `schlieren harvest repair open <family-id>` — create a repair order from a cluster;
- `schlieren harvest repair close <repair-id> --commit <sha> --run <run-id>` — close only with reinspection evidence;
- `schlieren harvest certify <run-id>` — validate all gates and issue a certificate or a typed refusal.

## Failure handling and triage

When a storage campaign exposes another subsystem:

- a shared EVM correctness defect blocks the campaign and is repaired immediately;
- a downstream mismatch caused by storage behavior stays in the current cluster;
- an independently proven fixture/oracle defect is quarantined with evidence;
- an unrelated UI, tooling, or documentation issue is preserved in the ledger and may be deferred with an explicit reason;
- a host crash or timeout is `Aborted`, never a pass or fixture absence.

Deferral means preserved with status, reason, and ownership. No discovered defect disappears from history.

## Security and operational constraints

- No operational token, API key, or bearer credential may be committed.
- Phase 1 operates entirely on a local EELS corpus after installation.
- Fixture paths are canonicalized and constrained to the declared root.
- Artifacts are data only; Harvest does not execute scripts supplied by fixtures.
- Output filenames use validated identifiers, not raw external names.
- Partial writes are isolated from finalized runs.
- Concurrency must not share mutable EVM, state, OpSec, or ledger writer state between cases.

## Testing strategy

Development follows test-first red/green cycles.

### Unit contracts

- fixture validation and stable rejection codes;
- deterministic 50-case selection;
- manifest canonicalization and hashing;
- typed comparison for every output kind;
- cluster stability under ordering changes;
- atomic ledger finalization;
- incompatible-run comparison refusal;
- certification refusal for every non-pass status.

### Integration contracts

- controlled calibration signals;
- canonical EELS fixture execution;
- host crash/timeout captured as `Aborted` in a subprocess boundary;
- journal on/off parity;
- before/after comparison using the identical manifest;
- regression promotion provenance.

### System gates

- three identical consecutive full-suite runs;
- complete Campaign 1 baseline;
- focused repairs plus full regression suite;
- no tracked credential scan findings;
- final repository status and exact commit provenance.

## Success criteria for the first delivery

The delivery succeeds when:

1. Phase 0 is green and produces its first immutable calibration record.
2. A 50-case storage manifest is deterministically created from complete EELS fixtures.
3. All 50 cases receive durable, honest terminal outcomes.
4. Divergences are clustered by typed causal fingerprints.
5. Baseline Markdown and JSON reports are generated from the ledger.
6. If a Schlieren defect is found, at least one cluster completes repair and identical-manifest reinspection with no new regression.
7. Certification is issued only if all 50 match and all gates pass; otherwise Harvest emits a typed certification refusal.
8. The full before/after history remains available without overwriting prior evidence.

## Future verifier boundary

The future customer verifier will share manifest, result, and certificate schemas plus the safe local campaign runner. It will not include internal selection policy, mutation, clustering intelligence, minimization, or repair automation. It will never modify EVM semantics to make a campaign pass.
