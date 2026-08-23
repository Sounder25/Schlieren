# Journal Security Reentrancy Recovery Design

Date: 2026-08-23  
Phase: 4A of journal/legacy recovery  
Branch: `integration/journal-legacy-recovery`

## Purpose

Recover and advance Schlieren's reentrancy capability without restoring trace-depth heuristics or a second execution path. The server will derive findings from explicit journal frame ancestry, typed state effects, frame resolution, and transaction persistence. React will render and navigate the server's findings without recomputing ancestry, disposition, or severity.

This slice is end-to-end: canonical journal evidence enters the analyzer, becomes a proof-linked security finding in `schlieren_traceJournal`, and appears as an interactive finding in the React Workbench.

## Scope

Phase 4A includes:

- frame-level lifecycle metadata in `JournalAnalysis` needed by security rules;
- reentrancy detection using explicit frame identity and ancestry;
- calibrated reentrancy rule IDs and severities;
- exact evidence links to frames, instructions, and journal sequences;
- unchanged transport shape for existing debug RPC methods;
- React rendering and navigation of server-derived findings;
- focused synthetic and real-execution tests, including rollback and false-positive cases.

Phase 4A does not include:

- restoring `ReentrancyDetector`, `LiveReentrancyDetector`, or `ExecutionContext.OnStep` security callbacks;
- changing storage-collision rules, beyond allowing the generic React renderer to display findings already returned by the server;
- unchecked-call, gas-griefing, access-control, oracle, or exploitability detectors;
- WebSocket or incremental streaming during EVM execution;
- gas-tree presentation changes;
- legacy synthetic trace generation;
- Phase 4B or later capability recovery.

## Existing Problem

The current `JournalSecurityAnalyzer` is already frame-aware, but its reentrancy rule requires a `StorageWriteEvent` inside the re-entered frame before it emits any finding. That misses an important class: a re-entered frame can read stale state and return without writing, followed by a write in the original frame after the external interaction.

The current critical rule also compares the ancestor write to the first child write, not to explicit child-frame completion. The ordering happens to work in simple synchronous traces, but it does not state or prove the intended condition: the ancestor wrote after the re-entrant call returned.

The React Workbench parses `securityFindings` but its Diagnostics panel always displays “No findings in this execution.” The evidence already crosses the RPC boundary and is discarded by the view.

## Design Principles

1. Frame topology is authoritative. Depth changes and address-string scans are not evidence.
2. State-effect events are authoritative. Opcode storage snapshots are not used for detection.
3. Resolution and persistence are explicit. A reverted pattern is observable evidence, not a persisted vulnerability.
4. Pattern certainty and exploitability are different. A finding may prove that reentry occurred while explicitly limiting what it claims about exploitability.
5. The server owns rule classification and severity. React only renders and navigates the result.
6. One canonical execution remains. No detector may trigger another evaluation.

## Journal Analysis Extension

`JournalAnalysis` will expose sufficient immutable frame lifecycle facts for downstream rules. Each `JournalFrameAnalysis` will include:

- frame entry sequence;
- frame resolution sequence;
- explicit resolution (`Commit` or `Rollback`);
- effective execution disposition;
- effective persistence disposition;
- existing parent and ordered ancestor IDs.

Effective frame execution disposition is `Reverted` when the frame or any ancestor resolves with rollback. Otherwise it is `Survived`.

Effective frame persistence disposition is:

- `NotApplicable` when effective execution disposition is reverted;
- `CommittedToState` when the frame survived and the transaction committed;
- `SimulationDiscarded` when the frame survived and the transaction was a discarded simulation.

These values use the same lifecycle evidence and definitions already applied to typed state effects. They are internal analysis facts; existing debug RPC contracts do not change.

`FrameStateResolvedEvent.Sequence` is the authoritative return boundary. A parent effect is “after the re-entrant call returned” only when its sequence is greater than the child frame's resolution sequence.

## Reentrancy Candidate Rule

A frame is a reentrancy candidate when all of the following are true:

1. its call type is `Call`, `CallCode`, or `StaticCall`;
2. an active ancestor has the same storage-owner address (`ContractAddress`);
3. the frame is not ordinary `DelegateCall` execution;
4. the nearest matching ancestor is selected as the original entry.

One candidate is produced per re-entered frame. If several ancestors have the same storage owner, the nearest matching ancestor is used so nested reentry does not generate combinatorial duplicate findings.

The immediate parent frame identifies the calling context. Finding addresses include the re-entered storage owner and, when different, the immediate caller's contract or code address.

## Finding Rules and Severity

### `SEC.REENTRANCY.OBSERVED`

Emitted when a frame re-enters a storage owner active in an ancestor but the re-entered frame has no typed storage read or write for that owner.

- survived execution: `Info`;
- reverted execution: `Info`.

This proves frame reentry only. It does not claim state exposure or exploitability.

### `SEC.REENTRANCY.STATE_CONTACT`

Emitted instead of `OBSERVED` when the re-entered frame contains a `StorageReadEvent` or `StorageWriteEvent` whose `StorageAddress` is the re-entered storage owner.

- survived execution: `Medium`;
- reverted execution: `Info`.

The finding lists the distinct contacted slots and the exact effect sequences. This proves that re-entered execution touched the original storage context.

### `SEC.REENTRANCY.POST_WRITE`

Emitted in addition to the base finding when the nearest matching ancestor performs one or more `StorageWriteEvent` operations after the re-entered frame's `FrameStateResolvedEvent`.

- survived write: `Critical`;
- reverted write or reverted candidate path: `Info`.

The finding aggregates the distinct post-return slots for that reentry into one result. It links the re-entered frame entry, its resolution boundary, and the ancestor write events. This proves the observed checks/interactions/effects ordering pattern; it does not prove that an attacker can profit from it.

## Evidence Model

Every finding retains the existing `SecurityFinding` transport fields:

- primary frame ID;
- optional primary instruction ID;
- supporting journal event sequences;
- frame ancestry;
- execution and persistence dispositions;
- addresses and storage slots;
- summary and limitation.

For `OBSERVED`, the primary frame is the re-entered frame, the primary instruction may be absent, and the frame-entry sequence is the minimum evidence.

For `STATE_CONTACT`, the primary instruction is the first typed storage effect's instruction and supporting evidence includes the entry plus all relevant storage-effect sequences.

For `POST_WRITE`, the primary instruction is the first post-return ancestor write. Supporting evidence includes the child entry sequence, child resolution sequence, and every aggregated post-return write sequence.

Finding IDs remain deterministic: rule ID plus the re-entered frame ID and the primary evidence sequence. Repeated analysis of the same journal produces identical IDs and ordering.

`FactGrade` remains `Proven` because the reported execution pattern and ordering are directly evidenced. The limitation text must state that observed-path proof is not universal exploitability proof.

## Analyzer Data Flow

1. `StateTransition` and `EvmMachine` produce the existing canonical journal.
2. `JournalAnalysis.Build` validates frame lifecycle and derives frame/effect dispositions.
3. `JournalSecurityAnalyzer` walks frames in entry order and selects the nearest matching ancestor from pre-built ancestry.
4. Typed storage effects are grouped by frame and ordered by journal sequence.
5. Base and post-return rules produce deterministic `SecurityFinding` records.
6. `JournalTraceAssembler` maps findings into the existing `JournalSecurityFindingDto` shape.
7. `schlieren_traceJournal` returns findings and the pre-built frame tree.
8. React renders findings and uses their evidence IDs to navigate existing events, steps, and frames.

No TypeScript code computes frame ancestry, rollback survival, persistence, rule IDs, or severity.

## RPC Compatibility

`debug_inspect` and `debug_traceCall` retain their existing JSON shape and behavior.

`schlieren_traceJournal` continues using its existing fields:

- `securityFindings`;
- `events`;
- `steps`;
- `frameTree`.

Phase 4A requires no new top-level or finding fields. React can navigate an instruction by locating the journal event with `primaryInstructionId`, then selecting the opcode step with the corresponding opcode-event sequence. When no primary instruction exists, React selects the first step in `primaryFrameId`.

## React Workbench Behavior

The Diagnostics security section will render the `securityFindings` array received from the server.

Each card shows:

- server-provided severity and category;
- server-provided summary and limitation;
- primary frame ID;
- execution and persistence disposition;
- involved addresses and storage slots when present;
- count of linked evidence events.

Selecting a card navigates to the primary instruction when available. Otherwise it navigates to the first step in the primary frame. Navigation is UI traversal of the server-provided relationships, not ancestry reconstruction.

The empty state appears only when `securityFindings.length == 0`. Its copy names only detectors actually provided by the server. Existing mock text claiming unchecked-return and gas-griefing detection will be removed until those rules exist.

The renderer is category-agnostic, so current storage-collision findings can also appear. Phase 4A does not change their classification.

## Determinism and Deduplication

Findings are ordered by:

1. re-entered frame entry sequence;
2. rule order: `OBSERVED` or `STATE_CONTACT`, then `POST_WRITE`;
3. primary evidence sequence.

The analyzer emits exactly one base finding per re-entered frame and at most one aggregated post-write finding for that frame. Storage slots and evidence sequences are distinct and sorted.

No mutable analyzer state survives between executions.

## Error Handling

Malformed journals continue to fail in `JournalAnalysis.Build` with typed `JournalAnalysisException` errors. The security analyzer assumes validated analysis and does not silently repair missing parents, lifecycle events, or dispositions.

React treats a missing `securityFindings` field as an empty list for compatibility with older journal servers, as it does today. Invalid finding contents are not used to infer replacement severity or ancestry.

## Test Strategy

Implementation follows test-driven development. Tests are written and observed failing before production changes.

### Journal analysis tests

- committed frame derives survived/committed disposition;
- rolled-back child derives reverted/not-applicable disposition;
- surviving frame in simulation derives simulation-discarded disposition;
- ancestor rollback propagates to descendants;
- entry and resolution sequences are exact.

### Analyzer unit tests

- same-owner nested `CALL` produces `OBSERVED` without storage effects;
- same-owner nested `STATICCALL` with storage read produces informational read-only state contact;
- surviving same-owner call with storage contact produces `STATE_CONTACT`;
- ancestor write after child resolution produces one critical `POST_WRITE` finding;
- ancestor write before child entry does not produce `POST_WRITE`;
- different contract does not produce reentrancy;
- `DELEGATECALL` to different code with the same storage owner does not produce reentrancy;
- rolled-back reentry and rolled-back post-write are informational and non-persistent;
- nested repeated reentry selects the nearest matching ancestor and avoids duplicate pairs;
- finding IDs, evidence ordering, slots, and addresses are deterministic.

### Real execution tests

- canonical bytecode creates a real `A → B → A` call tree and the child opcodes belong to explicit frame IDs;
- a real re-entry with storage contact produces the expected server finding;
- a real parent post-return `SSTORE` produces the critical rule;
- a real reverted child keeps the pattern visible but informational;
- journal enabled and disabled execution remain behaviorally identical.

### RPC tests

- `schlieren_traceJournal` returns findings with frame and evidence links;
- frame-tree nodes reference their finding IDs;
- existing `debug_inspect` and `debug_traceCall` contract snapshots remain identical.

### React tests

- no-result and zero-finding empty states are accurate;
- findings render server-provided severity, summary, disposition, addresses, and slots;
- selecting a finding focuses the linked opcode step;
- a finding without an instruction focuses the first step in its primary frame;
- React does not contain severity or ancestry classification tables.

## Legacy Recovery Boundary

The following files remain disconnected and are not restored:

- `Schlieren.Core/Security/ReentrancyDetector.cs`;
- `Schlieren.Core/Security/LiveReentrancyDetector.cs`;
- their trace-derived call-stack logic;
- `ExecutionContext.OnStep` security callbacks;
- `WorkbenchExecutionService` synthetic security traces.

Their user-visible intent—detect reentry, identify post-call mutation, and navigate a finding—is recovered through typed canonical evidence. Their heuristic implementation is not.

## Acceptance Criteria

Phase 4A is complete only when:

1. the analyzer detects typed `OBSERVED`, `STATE_CONTACT`, and `POST_WRITE` cases as specified;
2. rollback and simulation dispositions are correct and visible;
3. ordinary delegate execution and different-contract calls remain false-positive free;
4. findings link to exact journal sequences and deterministic frame IDs;
5. React renders and navigates the server findings without classifying them;
6. existing debug RPC JSON contracts remain unchanged;
7. canonical execution parity and gas conservation tests still pass;
8. the old trace and live detector paths remain absent from production;
9. focused and full validation results are recorded;
10. the user reviews the slice results before Phase 4B begins.

## Approval Boundary

Approval of this specification authorizes writing a detailed implementation plan only. It does not authorize Phase 4B, storage-collision redesign, gas-tree presentation recovery, legacy Workbench scenarios, mainline merge, or deletion of recovery history.
