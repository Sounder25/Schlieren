# Execution Visualization Architecture — Certified Baseline Integration

**What Schlieren Is:** An EVM diagnostic instrument. It executes Ethereum bytecode and reveals *why* execution produced the result it did — gas flows, control paths, storage mutations, call frames.

**What the Visualization Does:** Renders execution traces so a human can understand what happened. Not a dashboard. An execution microscope.

**Why Certification Matters:** The visualization without certification is just pretty pictures about Schlieren's output. With certification, it's pictures about *Ethereum's* behavior — because we know Schlieren matches the reference. The baseline is what makes the microscope trustworthy.

---

## The Core Loop

```
User wants to understand an execution:
  1. Load fixture or bytecode
  2. Execute through Schlieren (with journal/tracing enabled)
  3. Compare against EELS reference (deterministic)
  4. Visualization renders:
       - Full execution trace (steps, gas, frames, state)
       - First divergence point (if any)
       - Propagation path (how divergence cascaded)
       - Downstream consequences (where execution "snapped")
       - Evidence links (case, delta, reference fixture)
```

The user isn't looking at a "test result." They're looking at an execution — and the certification baseline is what lets us say "what you're seeing is what Ethereum does."

---

## What the Visualization Must Render

### For a Certified-Equivalent Execution

When Schlieren and EELS agree (350/350 baseline):

- **Normal execution is visually compact.** Don't flood the user with noise. Show structure: call tree topography, gas allocation by frame, key state transitions.
- **The trace is navigable.** User can drill into any frame, step through opcodes, see stack/memory/storage at each point.
- **Certification is background, not foreground.** A small badge: "Certified equivalent ✓" — not the focus. The focus is the execution itself.

### For a Divergent Execution

When Schlieren and EELS disagree:

- **First divergence card.** The exact step where execution split — opcode, PC, depth, gas remaining, what Schlieren did vs what Ethereum expected.
- **The "why" is visible.** Not just "gas mismatch" but "EIP-2929 cold account access (2600) charged incorrectly" — the comparison pipeline produces this, not the renderer.
- **Propagation visualization.** Show how that first divergence rippled:
  - Stack state diverged → subsequent opcodes saw wrong values
  - Gas went negative → execution halted early
  - Storage slot written differently → downstream SLOAD returned wrong value
- **The "snap" point.** Where the divergence became unrecoverable — REVERT instead of STOP, out-of-gas that shouldn't happen, invalid opcode that shouldn't be reached.
- **Evidence chain.** Link back to:
  - Case ID
  - Campaign that discovered it
  - Reference fixture
  - Comparison deltas
  - Certification baseline comparison (did this pass before?)

### For a Historical Bug Replay

The three consensus bugs are now fixed. But they're valuable:

- **They're canonical examples of real divergences.** The visualization must be able to show them.
- **Replay mode.** Load the pre-fix execution, render the first divergence, show the propagation — but mark clearly:"Historical. Fixed in commit X. This is a diagnostic replay, not a current failure."
- **RCA link.** Connect to the root-cause analysis document.

---

## Architecture: Minimal, Evidence-Driven

### Principle: Renderer Consumes Facts

The visualization layer NEVER determines correctness. It renders what the execution/comparison pipeline discovered.

Correctness is determined by:
- EELS comparison (ConformanceComparator)
- Trace divergence (TraceDivergenceLocator)
- Certification baseline (CertifiedBaseline)

The renderer's job:
- Take `JournalTraceDto` → render execution steps, frames, gas tree
- Take `TraceDivergence` → render first divergence card with context
- Take `ComparisonResult` → render deltas, link to fixture
- Take `CertificationProvenance` → show certification status, historical context

### What Flows to the Renderer

```csharp
public sealed class ExecutionDiagnosticBundle
{
    // Core execution state
    public JournalTraceDto Trace { get; init; }         // Full execution trace
    public ExecutionSnapshot SchlierenFinalState { get; init; }
    public ExecutionSnapshot? ReferenceFinalState { get; init; }  // EELS, if available
    
    // Comparison evidence
    public ComparisonResult Comparison { get; init; }  // Deltas, status
    public TraceDivergence? FirstDivergence { get; init; }  // First step-level split
    
    // Certification context
    public CertificationProvenance Certification { get; init; }  // Certified? Historical? New?
    
    // Navigation helpers
    public IReadOnlyList<PropagationStep> PropagationPath { get; init; }
    public DivergenceSnapPoint? SnapPoint { get; init; }
}
```

The renderer receives ONE bundle. It does not call the EVM, the comparator, or the certification service. It renders what it's given.

---

## New Models Required

### CertificationProvenance (Harvest Layer)

Already defined in the first plan. Key statuses:

| Status | Meaning |
|---|---|
| `CertifiedEquivalent` | This case passes at baseline commit — Schlieren matches Ethereum |
| `NewDivergence` | This case passed at baseline, now diverges — something regressed |
| `HistoricalDivergence` | This case diverged before fix — replaying for diagnostics |
| `OutsideCertifiedCorpus` | This case wasn't in the baseline — unknown territory |

### PropagationStep (Visualization Layer)

```csharp
public sealed record PropagationStep(
    int StepIndex,
    string Opcode,
    string DivergenceType,        // "Stack", "Gas", "Storage", "ControlFlow"
    string ExpectedState,
    string ActualState,
    string ConsequenceDescription);
```

### DivergenceSnapPoint (Visualization Layer)

```csharp
public sealed record DivergenceSnapPoint(
    int StepIndex,
    string Opcode,
    string SnapKind,              // "OutOfGas", "Revert", "InvalidOpcode", "_STACKUnderflow"
    string ExpectedOutcome,
    string ActualOutcome);
```

---

## The Three Historical Bugs as Diagnostic Fixtures

These are now built-in to Schlieren's test corpus. They represent real execution disagreements with known root causes.

### Bug 1: EIP-161 Empty Account Cleanup

- **What happened:** SELFDESTRUCT to 0-balance beneficiary left ghost accounts
- **Families:** 15 cases in `test_reentrant_selfdestructing_call`
- **Fix commit:** `5868d80`
- **Diagnostic value:** Shows account-existence divergence propagation — downstream CALL fails because account persists when it should be gone

### Bug 2: Type-3/4 Transaction Decoding

- **What happened:** Blob hashes and authorization lists weren't parsed from fixtures
- **Families:** 3 cases (Transient Storage, Access List)
- **Fix commit:** `7143dae`
- **Diagnostic value:** Shows gas-only divergence — transaction-level fee mismatch without execution divergence — different failure signature

### Bug 3: CREATE ReturnData Leak

- **What happened:** Top-level CREATE tx exposed init code output as returnData
- **Family:** 1 case `test_create_and_destroy_multiple_contracts_same_tx`
- **Fix commit:** `13fec7b`
- **Diagnostic value:** Shows return-data buffer contamination — created contract's init code leaks into caller's return data

These three should be **first-class fixtures** in the visualization test corpus. When the visualization is tested, it must be able to render all three correctly.

---

## Implementation Phases (Revised)

### Phase A: Execution Diagnostic Bundle

**Goal:** Create the data contract that flows to the renderer.

1. Define `ExecutionDiagnosticBundle`
2. Define `PropagationStep` and `DivergenceSnapPoint`
3. Add builder that assembles bundle from:
   - Journal trace
   - Comparison result
   - Trace divergence
   - Certification provenance

**Files:**
```
Schlieren.Harvest/Diagnostics/ExecutionDiagnosticBundle.cs
Schlieren.Harvest/Diagnostics/DiagnosticBundleBuilder.cs
```

### Phase B: Propagation Path Tracer

**Goal:** Given a first divergence, trace its downstream effects.

1. Walk from first divergence step to end of trace
2. Detect stack corruption propagation (wrong value used by subsequent opcode)
3. Detect gas propagation (negative gas, early halt)
4. Detect storage propagation (wrong slot read)
5. Detect control flow propagation (wrong JUMP target)
6. Identify snap point where execution becomes unrecoverable

**Files:**
```
Schlieren.Core/Execution/PropagationTracer.cs
```

### Phase C: Renderer Contract

**Goal:** Define what the renderer receives and how it renders.

1. Renderer takes `ExecutionDiagnosticBundle`
2. Renderers:
   - `TraceSummaryRenderer` — execution overview, pass/fail, gas summary
   - `FrameTopologyRenderer` — call tree, depth, frame boundaries
   - `DivergenceCardRenderer` — first divergence, context, gas delta, subsystem hint
   - `PropagationRenderer` — cascade visualization, step-by-step propagation
   - `SnapPointRenderer` — where execution became unrecoverable
3. Each renderer produces data for the UI layer (Avalonia), not UI elements directly

**Files:**
```
Schlieren.Visualization/Rendering/TraceSummaryRenderer.cs
Schlieren.Visualization/Rendering/DivergenceCardRenderer.cs
Schlieren.Visualization/Rendering/PropagationRenderer.cs
etc.
```

### Phase D: Historical Bug Replay

**Goal:** The three bugs are renderable as diagnostic examples.

1. Create `HistoricalBugFixture` for each bug
2. Store pre-fix execution traces
3. `HistoricalBugReplayService` loads trace, produces `ExecutionDiagnosticBundle`
4. Renderer shows historical divergence with "Fixed in commit X" marker

**Files:**
```
Schlieren.Harvest/Diagnostics/HistoricalBugFixture.cs
Schlieren.Harvest/Diagnostics/HistoricalBugReplayService.cs
harvest/ledger/bugs/eip-161-empty-account.json (metadata)
harvest/ledger/bugs/type-3-4-decode.json
harvest/ledger/bugs/create-returndata.json
```

### Phase E: Visualization Test Suite

**Goal:** Prove the visualization renders correctly.

1. Test: Certified-equivalent execution renders compact trace without divergence card
2. Test: Divergent execution renders first divergence card with correct gas delta
3. Test: Propagation path is correctly traced for stack corruption case
4. Test: Propagation path is correctly traced for gas mismatch case
5. Test: Snap point is correctly identified for REVERT case
6. Test: Historical bug replay renders with "Fixed" marker and RCA link
7. Test: Visualization never shows a status that wasn't provided by the comparison pipeline

**Files:**
```
Schlieren.Visualization.Tests/RenderingTests.cs
Schlieren.Visualization.Tests/PropagationTests.cs
Schlieren.Visualization.Tests/HistoricalBugTests.cs
```

### Phase F: Certification Baseline Gate

**Goal:** CI gate ensures baseline remains 350/350.

Already covered by existing certification tests. Add visualization-specific gate:

1. Load `CertifiedBaseline`
2. Run all 350 certified cases through DiagnosticBundleBuilder
3. Assert all produce `CertificationProvenance.Status = CertifiedEquivalent`
4. On failure: fail CI with "Schlieren regressed from certified baseline"

---

## How This Differs From the First Plan

| First Plan (Wrong) | Revised Plan (Right) |
|---|---|
| "Make certification status visible in diagnostic output" | Certification is background context for execution visualization |
| "Add status badge to ConformanceFailureRow" | Render execution traces, divesrgence cards, propagation paths |
| "Conformance view shows pass/fail with certificate link" | Visualization is an execution microscope, not a test results dashboard |
| "Add certification column to failure list" | The failure list is secondary. The trace viewer is primary. |

---

## Acceptance Criteria (Revised)

By the end of Phase F:

1. **Execution trace is renderable.** Given any fixture, the visualization shows steps, frames, gas, state — not just pass/fail.
2. **First divergence is visible.** When Schlieren and EELS split, the exact step is shown with context.
3. **Propagation is traced.** The cascade from first divergence to downstream consequences is visible.
4. **Snap point is identified.** Where execution became unrecoverable is marked.
5. **Historical bugs are replayable.** All three consensus bugs render correctly with "Fixed" markers.
6. **Certification is background.** Certified executions show a "Certified equivalent" badge but don't dominate the view.
7. **Renderer never guesses.** All status, divergence, and propagation comes from the diagnostic pipeline, not the renderer.
8. **Baseline gate passes.** CI confirms 350/350 on every run.

---

## Estimated Effort (Revised)

| Phase | Scope | Estimate |
|---|---|---|
| A | Execution Diagnostic Bundle | 2-3 hours |
| B | Propagation Path Tracer | 3-4 hours |
| C | Renderer Contract | 2-3 hours |
| D | Historical Bug Replay | 2 hours |
| E | Visualization Test Suite | 2-3 hours |
| F | Certification Baseline Gate | 1 hour |
| **Total** | | **12-16 hours** |

Higher than before because we're not adding a badge to a table — we're building the execution visualization layer that the product is for.

---

## What the User Actually Sees

Not a list of test results. An execution:

```
┌─ Execution ─────────────────────────────────────────────┐
│ CREATE tx to 0x8fdf... • Gas used: 285,488 (refund: 0)  │
│ Certified equivalent ✓ • Run: selfdestruct-v1_xxxx      │
├─────────────────────────────────────────────────────────┤
│ [Topology tab] [Steps tab] [Gas tree tab] [State tab]  │
├─────────────────────────────────────────────────────────┤
│ Frame 0 (root) • 0x081f... • 27 steps • 285,488 gas    │
│   ├─ Frame 1 (CREATE child) • 0x22d... • 8 steps       │
│   ├─ Frame 2 (CALL selector=1) • depth 1 • 5 steps      │
│   └─ Frame 3 (CALL selector=2) • depth 1 • 3 steps      │
└─────────────────────────────────────────────────────────┘
```

And when there's a divergence:

```
┌─ Divergence Card ───────────────────────────────────────┐
│ First split at step 14 (PC=0x12, depth=1)               │
│ Opcode: SSTORE                                          │
│ Expected gas remaining: 284,200                         │
│ Actual gas remaining: 281,600                           │
│ Delta: -2,600 (Schlieren overcharged)                  │
│                                                         │
│ Subsystem: EIP-2929 cold storage read (SLOAD pricing)  │
│ Likely cause: Cold account access cost incorrect        │
│                                                         │
│ [View full comparison] [View propagation path]          │
└─────────────────────────────────────────────────────────┘
```

This is what Schlieren is for.
