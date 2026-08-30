# BLS G1ADD Campaign 8 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Work inline unless Erick explicitly authorizes delegation. Stop after every task for review.

**Goal:** Create, run, repair, reinspect, and eventually certify one immutable 50-case campaign for EIP-2537 BLS12-381 G1ADD behavior without reopening or weakening the seven completed campaigns.

**Architecture:** Extend the existing campaign-family selector with optional deterministic strata. Existing campaign families retain their current greedy selection behavior. Campaign 8 uses explicit strata and quotas so its frozen manifest truthfully covers G1ADD valid inputs, invalid encodings, call geometry, gas boundaries, and pre-Prague activation.

**Tech Stack:** .NET 8, C# 12, xUnit, `System.CommandLine`, EELS state-test fixtures, existing Harvest append-only ledger.

**Governing doctrine:** `docs/superpowers/specs/2026-08-24-harvest-certification-foundation-design.md`

## Global constraints

- The seven completed campaigns and their certificates are historical evidence. Do not edit, replace, or recertify them as part of Campaign 8.
- Preserve the existing broad `precompiles-bls12-v1` draft manifest. It is not Campaign 8 certification evidence because its 50 cases are not representative of its broad label.
- Campaign 8 family ID is `precompiles-bls12-g1add`; campaign ID is `precompiles-bls12-g1add-v1`.
- The frozen denominator is exactly 50 cases.
- Selection strata are exact: 15 valid, 18 invalid, 12 call-type, 4 gas-boundary, and 1 pre-fork activation case.
- Prague and Osaka are balanced inside each stratum whenever both forks exist. The single activation case is Cancun G1ADD-before-fork.
- Selection is deterministic and independent of fixture enumeration order.
- No case may satisfy two strata in the same manifest.
- If a stratum lacks its quota, creation fails with an insufficiency report; the selector must not borrow from another stratum.
- Ground truth remains the frozen fixture and EELS oracle. Schlieren output never becomes expected data.
- Discovery may run against an operationally identified EELS 2.19.0 installation. Certification requires the repository's existing certification gates and clean provenance; discovery results must not be called certified.
- Every apparatus failure remains distinct from an engine divergence. No timeout, crash, missing fixture, malformed output, abort, quarantine, or skip counts as a pass.
- Engine repairs must use the canonical EVM path, begin with a focused failing regression, and preserve journal-on/off outcome parity.
- Ledger evidence is append-only. A new run never overwrites a prior run or manifest.
- Do not modify React, Avalonia, RPC JSON contracts, the typed journal contract, or unrelated UI files.

## Task 0: Baseline and corpus intake

**Files:**

- Add: `docs/harvest/baselines/2026-08-30-bls-g1add-intake.md`

- [x] Record the starting commit and isolated worktree state.
- [x] Build `Schlieren.sln` in Release.
- [x] Run `Schlieren.Harvest.Tests` in Release.
- [x] Inventory the BLS fixture corpus by operation, test kind, and fork.
- [ ] Record the corpus counts, the broad-draft skew, and the exact Campaign 8 strata in the intake document.
- [ ] Commit only the work order and intake evidence.

Acceptance: build has zero errors; Harvest baseline is 233 passed / 0 failed / 0 skipped / 233 total; the working tree contains no unrelated change.

## Task 1: Add deterministic stratified selection

**Files:**

- Modify: `Schlieren.Harvest/Campaigns/CampaignFamilyPolicy.cs`
- Modify: `Schlieren.Harvest.Tests/Campaigns/CampaignFamilyPolicyTests.cs`

- [ ] Write a failing test proving an input dominated by one category still returns the exact requested quota from every stratum.
- [ ] Write a failing test proving shuffled fixture enumeration produces the same ordered case IDs.
- [ ] Write a failing test proving insufficient capacity in one stratum fails instead of borrowing cases from another.
- [ ] Capture the red failures before changing production code.
- [ ] Add `CampaignSelectionStratum` with a name, quota, required case/path keywords, and excluded keywords.
- [ ] Select evenly across ordinal-sorted candidates within each stratum, including both ends when the quota is greater than one.
- [ ] Reject overlapping selections and quota totals that differ from the requested campaign count.
- [ ] Preserve the current greedy selector unchanged when a family has no strata.
- [ ] Run focused family-policy tests and the complete Harvest suite.
- [ ] Commit only the selector and its tests.

Acceptance: exact quotas, stable ordering, fail-closed insufficiency, no behavioral change for the original seven family policies.

## Task 2: Define the G1ADD family and truthful manifest metadata

**Files:**

- Modify: `Schlieren.Harvest/Campaigns/CampaignFamilyPolicy.cs`
- Modify: `Schlieren.Harvest/Campaigns/CampaignManifest.cs`
- Modify: `Schlieren.CLI/Commands/HarvestCommand.cs`
- Modify: `Schlieren.Harvest.Tests/Campaigns/CampaignFamilyPolicyTests.cs`
- Modify: `Schlieren.Harvest.Tests/Campaigns/CampaignManifestTests.cs`
- Modify: `Schlieren.Tests/CLI/HarvestCommandTests.cs`

- [ ] Write failing tests for the five exact G1ADD strata and their fork allocation.
- [ ] Write a failing test proving `CampaignManifest.Freeze` records the selected family and selection-policy version instead of storage-lifecycle constants.
- [ ] Capture red before implementation.
- [ ] Register `precompiles-bls12-g1add` v1 with these quotas: valid 15, invalid 18, call types 12, gas 4, before-fork 1.
- [ ] Pass the family name and policy version explicitly through campaign creation and manifest freezing.
- [ ] Keep existing call sites source-compatible where required, but prevent a new non-storage campaign from being mislabeled.
- [ ] Run focused tests, all Harvest tests, and CLI tests.
- [ ] Commit only the family, manifest, CLI wiring, and tests.

Acceptance: the policy selects 50 unique cases with exact strata and the manifest labels itself `precompiles-bls12-g1add-v1` using the new selection-policy identity.

## Task 3: Freeze and audit Campaign 8

**Files:**

- Add: `harvest/ledger/campaigns/precompiles-bls12-g1add-v1/<manifest-hash>/manifest.json`
- Modify: `docs/harvest/baselines/2026-08-30-bls-g1add-intake.md`

- [ ] Build Release binaries.
- [ ] Create the campaign from `EELS_FIXTURES_ROOT` using `EELS_EXE` and EELS version `2.19.0`.
- [ ] Recompute and verify the manifest content hash.
- [ ] Verify exactly 50 unique case IDs and exact stratum totals.
- [ ] Verify every referenced fixture exists and its SHA-256 matches.
- [ ] Verify the manifest has no case outside G1ADD or its G1ADD pre-fork activation check.
- [ ] Record the exact manifest path, hash, EELS identity, fixture identity, and case distribution.
- [ ] Commit the immutable manifest and intake update.

Acceptance: all audit checks pass. Do not edit this manifest after the commit.

## Task 4: Run discovery and create the defect taxonomy

**Files:**

- Add: append-only run artifacts under `harvest/ledger/runs/`
- Add: `docs/harvest/reports/2026-08-30-bls-g1add-discovery.md`
- Add as needed: append-only comparison and repair-order artifacts under `harvest/ledger/`

- [ ] Record the exact Schlieren commit and dirty-tree state before execution.
- [ ] Run the frozen Campaign 8 manifest with a 120-second per-case timeout.
- [ ] Record all six terminal counts: pass, divergence, fixture invalid, harness error, aborted, quarantined.
- [ ] If any harness error or abort occurs, stop engine diagnosis and open an apparatus repair order.
- [ ] If apparatus is clean, cluster divergences by typed delta and earliest journal-supported causal difference.
- [ ] Record cases outside the current repair family without hiding or fixing them opportunistically.
- [ ] Commit only append-only evidence and the discovery report.

Acceptance: every one of the 50 cases has a durable terminal outcome; discovery is not described as certification.

## Task 5: Repair, reinspect, and certify

Repeat for one causal family at a time:

- [ ] Open a repair order linked to every affected case.
- [ ] Write and capture a minimal failing canonical-EVM regression.
- [ ] Make the smallest canonical execution correction.
- [ ] Pass focused tests and the full regression suite.
- [ ] Rerun the identical frozen 50-case manifest.
- [ ] Record eliminated, unchanged, introduced, expanded, and regressed families.
- [ ] Close the repair order only when its original cases pass and no formerly passing case regresses.

Certification begins only after a clean 50/50 discovery successor run. Apply the existing repository certification gates without weakening them. Any code change after the candidate run invalidates that candidate and requires another identical-manifest run.

## Required checkpoint report

After each task report: task name, full commit SHA, exact files changed, red evidence, focused and full test totals, campaign run ID and six terminal counts when applicable, new ledger paths and hashes, scope exceptions, and working-tree cleanliness.
