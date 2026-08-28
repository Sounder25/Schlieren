# Certification-Aware Visualization — Architecture & Plan

**Context:** Schlieren has reached 350/350 certification against EELS 2.19.0 at commit `d2f7e1d`. This is a verified baseline. All future visualization and diagnostic work must preserve this baseline and make certification status visible in the UI.

**Objective:** Make certification provenance visible in diagnostic output, enable historical bug replay, and ensure the visualization layer never independently determines correctness.

---

## Part 1: Current Architecture

### 1.1 Evidence Pipeline

```
Fixture → SchlierenCaseExecutor → ExecutionSnapshot → ConformanceComparator → ComparisonResult
                                    ↑                                              ↓
                              EELS Oracle                                     CaseOutcome
                                    ↓                                              ↓
                            ExecutionSnapshot ──────────────────────────> CaseOutcome.metadata.json
```

**Key files:**
- `Schlieren.Harvest/Execution/ExecutionSnapshot.cs` — normalized post-state (IsSuccess, GasUsed, ReturnData, PostState, Logs)
- `Schlieren.Harvest/Comparison/ConformanceComparator.cs` — produces `ComparisonResult(Status, Deltas[], Detail)`
- `Schlieren.Harvest/Domain/Models.cs` — `FieldDelta(Layer, Kind, Expected, Actual)`, `CaseStatus` enum
- `Schlieren.Harvest/Campaigns/CampaignRunner.cs` — orchestrated case execution
- `Schlieren.Harvest/Certification/CertificationService.cs` — certificate issuing
- `harvest/ledger/runs/{run-id}/run.json` — persisted run with outcomes

### 1.2 Trace Pipeline

```
ExecutionResult → ExecutionTraceStep[] → JournalTraceAssembler → JournalTraceDto
                                                            ↘
                                                        TraceDivergenceLocator
```

**Key files:**
- `Schlieren.Core/Execution/ExecutionTraceStep.cs` — step record (Pc, Op, Gas, GasCost, Depth, Stack, Memory)
- `Schlieren.Core/Execution/TraceDivergenceLocator.cs` — step-by-step comparison against reference trace
- `Schlieren.Core/Execution/Journal/JournalTraceDtos.cs` — rich journal output (frames, steps, gas tree, state effects, security findings)
- `Schlieren.Core/Execution/Journal/JournalTraceAssembler.cs` — assembles journal from execution context

### 1.3 UI Layer

- `Schlieren.UI/ViewModels/ConformanceViewModel.cs` — orchestrates conformance runs, shows failure rows
- `Schlieren.UI/ViewModels/ConformanceViewModel.cs:ConformanceFailureRow` — single failure row with Layer 1 diagnosis
- `Schlieren.UI/Views/ConformanceView.axaml` — XAML view for conformance tab
- `Schlieren.UI/Views/MainWindow.axaml` — main app window

### 1.4 Certification Ledger

```
harvest/ledger/
├── campaigns/
│   └── {campaign-id}/
│       └── {manifest-hash}/
│           └── manifest.json
├── runs/
│   └── {run-id}/
│       ├── run.json (outcomes + summary)
│       ├── cases/
│       │   └── {case-id}.json (comparison details)
│       └── complete.json
├── certificates/
│   └── {certificate-id}.md
└── reports/
    └── {rca-id}.md
```

---

## Part 2: What's Missing

### 2.1 No Certification Provenance in ComparisonResult

`ComparisonResult` knows `Pass` vs `Divergence` but does NOT know:
- Whether this case is in the certified corpus
- Which campaign/certificate attests to it
- The certification run ID
- Whether the divergence is new vs a known historical issue

### 2.2 No Historical Bug Replay

The three consensus bugs (EIP-161, Type-3/4 decoding, CREATE returnData) have:
- Root cause analyses in `harvest/ledger/reports/`
- Pre-fix run records with divergences
- Known commit SHAs where they were fixed

But there's no mechanism to:
- Mark a case as "historically divergent at commit X"
- Replay it as a historical diagnostic without failing the current build
- Show the fix commit and RCA link

### 2.3 Visualization Not Connected to Certification

`ConformanceFailureRow` builds detail strings from raw deltas. It cannot:
- Distinguish "new divergence" from "known historical issue"
- Link to the certification evidence (run ID, campaign, certificate)
- Show when Schlieren changed vs when the reference changed

### 2.4 No Certified Baseline Model

There is no `CertifiedBaseline` model that captures:
- The commit SHA known good
- The campaign manifests and their hashes
- The certificate IDs and issue dates
- The list of certified case IDs

---

## Part 3: Proposed Architecture

### 3.1 New Models

#### CertifiedBaseline

```csharp
namespace Schlieren.Harvest.Certification;

public sealed record CertifiedBaseline(
    string         BaselineId,          // e.g., "d2f7e1d-2026-08-28"
    string         CommitSha,           // e.g., "d2f7e1d"
    DateTime       CertifiedUtc,
    IReadOnlyList<CertifiedCampaign> Campaigns,
    IReadOnlyList<HistoricalBug>     HistoricalBugs);

public sealed record CertifiedCampaign(
    string         CampaignId,
    string         ManifestHash,
    string         CertificateId,
    int            PassCount,
    int            TotalCount,
    string         CertificationRunId);

public sealed record HistoricalBug(
    string         BugId,               // e.g., "EIP-161-EMPTY-ACCOUNT"
    string         Description,
    IReadOnlyList<string> AffectedCaseIds,
    string         FixedInCommitSha,
    string         RcaReportPath);
```

#### CertificationProvenance

```csharp
namespace Schlieren.Harvest.Comparison;

public enum CertificationStatus
{
    CertifiedEquivalent,    // Case passed at d2f7e1d, still passes
    NewDivergence,          // Case passed at d2f7e1d, now diverges → Schlieren regressed
    ReferenceChanged,       // Case passed at d2f7e1d, now diverges → reference changed
    NewlyTested,            // Case not in certified corpus
    HistoricalDivergence,   // Case diverged before fix, replayed for diagnostics
    OutsideCertifiedCorpus  // Case ID not in any certified campaign
}

public sealed record CertificationProvenance(
    CertificationStatus Status,
    string?             CertificateId,
    string?             CampaignId,
    string?             CertificationRunId,
    string?             HistoricalBugId,
    string?             DivergenceSinceCommit,
    string?             FixCommitSha);
```

#### Augmented ComparisonResult

```csharp
public sealed record ComparisonResult(
    CaseStatus             Status,
    IReadOnlyList<FieldDelta> Deltas,
    string?                Detail,
    ExecutionAttemptEvidence? AttemptEvidence,
    CertificationProvenance?  Certification);  // ← NEW
```

### 3.2 Certification-Aware Comparator

Extend `ConformanceComparator` to accept a `CertifiedBaseline` and produce `CertificationProvenance`:

```csharp
public static class ConformanceComparator
{
    // Existing
    public static ComparisonResult Compare(ExecutionSnapshot expected, ExecutionSnapshot actual);
    public static ComparisonResult CompareWithOracle(...);

    // NEW: Certification-aware comparison
    public static ComparisonResult CompareWithCertification(
        ExecutionSnapshot expected,
        ExecutionSnapshot actual,
        string caseId,
        CertifiedBaseline baseline,
        IReadOnlyDictionary<string, ComparisonResult> historicalResults); // from pre-fix runs
}
```

Logic:
1. If `caseId` is in a certified campaign:
   - If `Status == Pass` → `CertifiedEquivalent`
   - If `Status == Divergence` → `NewDivergence` (Schlieren changed)
2. If `caseId` matches a historical bug's affected cases:
   - If diverging AND current commit == fixed commit → error (regression!)
   - If diverging AND replaying pre-fix → `HistoricalDivergence`
3. If `caseId` not in any campaign → `OutsideCertifiedCorpus`

### 3.3 Historical Bug Replay Service

```csharp
namespace Schlieren.Harvest.Diagnostics;

public sealed class HistoricalBugReplayService
{
    public async Task<ComparisonResult> ReplayHistoricalDivergence(
        string bugId,
        CertifiedBaseline baseline,
        CancellationToken ct);
}
```

This service:
1. Loads the historical RCA to find affected cases and pre-fix commit
2. Loads the pre-fix run record to get expected deltas
3. Optionally re-runs the case (or loads cached result)
4. Returns `ComparisonResult` with `CertificationProvenance.Status = HistoricalDivergence`

### 3.4 Visualization Contract

The renderer (`ConformanceViewModel`, `ConformanceFailureRow`) receives a `ComparisonResult` with populated `Certification` field. It MUST NOT:
- Set `CertificationStatus` itself
- Guess at certification correctness
- Fabricate provenance

It SHOULD:
- Display certification status badge (Certified ✓, New Divergence ⚠, Historical 📜, etc.)
- Link to certificate/run/campaign when present
- Show historical bug context if applicable
- Distinguish "Schlieren changed" from "Reference changed"

---

## Part 4: Implementation Phases

### Phase A: Data Contract (No UI Work)

**Goal:** Certification pipeline produces provenance; visualization consumes it.

**Tasks:**
1. Create `CertificationProvenance.cs` model
2. Add `CertificationProvenance? Certification` to `ComparisonResult`
3. Create `CertifiedBaseline.cs` with loader from `harvest/ledger/certificates/`
4. Create `HistoricalBug.cs` model with loader from RCAs
5. Add `CompareWithCertification` to `ConformanceComparator`
6. Update `CampaignRunner` to pass baseline to comparator
7. Update `CaseOutcome` serialization to include certification field

**Files to create:**
```
Schlieren.Harvest/Certification/CertifiedBaseline.cs
Schlieren.Harvest/Comparison/CertificationProvenance.cs
Schlieren.Harvest/Diagnostics/HistoricalBugReplayService.cs
```

**Files to modify:**
```
Schlieren.Harvest/Comparison/ConformanceComparator.cs
Schlieren.Harvest/Campaigns/CampaignRunner.cs
Schlieren.Harvest/Domain/Models.cs (ComparisonResult)
```

**Tests:**
- Given a certified case ID → comparator returns `CertifiedEquivalent`
- Given a previously-passing case that now diverges → `NewDivergence`
- Given a historical bug case replay → `HistoricalDivergence`
- Given an unknown case → `OutsideCertifiedCorpus`

### Phase B: Historical Bug Fixtures

**Goal:** Replay consensus bugs as diagnostic fixtures without failing the build.

**Tasks:**
1. Create `HistoricalBugRegistry` with entries for 3 fixed bugs:
   - EIP-161 empty account cleanup (15 cases)
   - Type-3/4 transaction decoding (2 cases)
   - CREATE returnData leak (1 case)
2. Link each to:
   - Affected case IDs
   - Pre-fix commit SHAs
   - Fix commit SHAs
   - RCA report paths
3. Add `ReplayHistoricalDivergence` method
4. Add CLI command: `schlieren diagnostics replay --bug {bugId}`
5. Add test: replay produces `HistoricalDivergence`, not `Divergence`

**Files to create:**
```
Schlieren.Harvest/Diagnostics/HistoricalBugRegistry.cs
harvest/ledger/bugs/eip-161-empty-account.json
harvest/ledger/bugs/type-3-4-tx-decode.json
harvest/ledger/bugs/create-returndata-leak.json
```

### Phase C: Visualization Integration

**Goal:** Render certification provenance in UI.

**Tasks:**
1. Update `ConformanceFailureRow` to accept `CertificationProvenance`
2. Add certification status badge column
3. Add "View Certificate" link when certified
4. Add "View Historical Bug" link for historical divergences
5. Show "Schlieren changed" vs "Reference changed" distinction
6. Update `ConformanceViewModel` to pass provenance through
7. Add filter: "Only show new regressions" (exclude CertifiedEquivalent)

**Files to modify:**
```
Schlieren.UI/ViewModels/ConformanceViewModel.cs
Schlieren.UI/Views/ConformanceView.axaml
```

### Phase D: Certification Baseline Gate

**Goal:** Fail the visualization test suite if certification baseline is violated.

**Tasks:**
1. Load `CertifiedBaseline` at app startup
2. Add CI gate: run all 350 certified cases, verify 350/350 pass
3. On divergence: emit `NewDivergence` with blocking message
4. Add test: `Given_a_certified_case_when_Schlieren_diverges_then_status_is_NewDivergence`
5. Add test: `Given_all_certified_cases_when_all_pass_then_visualization_shows_CertifiedEquivalent`

**Files to create:**
```
Schlieren.Harvest.Tests/Certification/CertifiedBaselineGateTests.cs
```

---

## Part 5: Acceptance Criteria

By the end of Phase D:

1. **Test suite remains green.** No existing tests break.
2. **Certification baseline preserved.** A CI gate verifies 350/350 daily.
3. **Certification-equivalent case renders with provenance.** Clicking a passing case shows "Certified equivalent ✓" with campaign/ certificate links.
4. **New divergence renders first-divergence card.** A case that passed at `d2f7e1d` but now diverges shows warning badge, "Schlieren regressed since d2f7e1d" message, and first delta.
5. **Historical bug can be replayed.** Running `schlieren diagnostics replay --bug eip-161-empty-account` shows the historical divergence with RCA link, marked as `HistoricalDivergence`, not failing the build.
6. **Visualization never claims correctness without evidence.** Every status badge comes from a populated `CertificationProvenance` produced by the comparator, never guessed by the renderer.

---

## Part 6: Non-Goals (For Later)

- Full 14,516 Osaka fixture suite certification
- Performance benchmarking
- Real-time streaming visualization
- Live EVM debugging with breakpoints
- Automated regression bisection

---

## Part 7: Estimated Effort

| Phase | Scope | Estimate |
|---|---|---|
| A | Data contract + comparator | 2-3 hours |
| B | Historical bug fixtures | 1-2 hours |
| C | Visualization integration | 2-3 hours |
| D | Certification baseline gate | 1 hour |
| **Total** | | **6-9 hours** |

---

## Part 8: Risks

1. **Breaking the certification baseline.** Mitigation: don't modify EVM code during this work. All changes are in Harvest and UI layers.
2. **Performance regression.** Mitigation: certification provenance is optional. Historical replay is off by default.
3. **Coupling to specific commit.** Mitigation: baseline is loaded from persisted certificate, not hard-coded. Supports future baselines.
4. **UI complexity.** Mitigation: start with a single column and badge. Deep linking comes later.

---

## Part 9: References

- Certificate: `harvest/ledger/certificates/2026-08-28-strategic-campaign-certificate.md`
- Session rollup: `docs/harvest/certification/2026-08-28-session-rollup.md`
- RCA EIP-161: `harvest/ledger/reports/2026-08-28-selfdestruct-account-existence-rca.md`
- ConformanceComparator: `Schlieren.Harvest/Comparison/ConformanceComparator.cs`
- ConformanceViewModel: `Schlieren.UI/ViewModels/ConformanceViewModel.cs`
