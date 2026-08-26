# Strategic Campaign Certification Design

Date: 2026-08-26
Status: Draft for written approval
Depends on: `2026-08-24-harvest-certification-foundation-design.md`
Target: Campaigns 2-7 and the first multi-campaign Harvest certificate

## Purpose

This design defines how Schlieren advances the six strategic EELS campaigns from baseline evidence to honest, reproducible certification. It preserves the vehicle-inspection model established by the Harvest foundation:

1. verify the measuring apparatus;
2. inspect fixed manifests;
3. classify every defect without hiding it;
4. repair one causal family at a time;
5. reinspect the identical manifests;
6. certify only an exact code and oracle configuration.

The goal is not merely to make a test count green. The goal is to prove that Schlieren agrees with the pinned EELS oracle across 300 deliberately selected cases covering call frames, creation, return data, self-destruct, transient storage, and transaction/access-list behavior.

## Definition of 100%

The six new campaigns are 100% only when all of the following are true on one exact Schlieren commit:

- all six original frozen 50-case manifests execute without substitution;
- every campaign records 50 `Pass` outcomes;
- there are zero `Divergence`, `FixtureInvalid`, `HarnessError`, `Aborted`, or `Quarantined` outcomes;
- every required expected value comes from the pinned fixture or EELS authority, never from Schlieren;
- Campaign 1 Storage Lifecycle is rerun and remains 50/50 on the same final commit;
- three consecutive full-suite runs have identical passing, failing, and skipped totals and case identities;
- each new campaign receives an individual certificate;
- one umbrella certificate proves the six new campaigns are collectively 300/300 under the same provenance.

Storage is a prerequisite regression gate, not part of the umbrella denominator. The final release gate therefore executes 350 Harvest cases while the umbrella certificate accurately states 300/300 for Campaigns 2-7.

## Current evidence and starting point

The following records are historical evidence. They do not certify current `main` because they were produced on earlier commits.

| Campaign | Evidence commit | Latest recorded outcome | Current classification |
| --- | --- | ---: | --- |
| Storage Lifecycle | `cf20f21` | 50 pass, 0 divergence, 0 apparatus failure | Historically certified; must be renewed on the final commit |
| Call Semantics | `aa491c9` | 50 pass | Baseline green; not certified |
| Create Semantics | `aa491c9` | 50 pass | Baseline green; not certified |
| Return Data | `2159de2` | 49 pass, 1 harness error | Apparatus blocked |
| Self-Destruct | `aa491c9` | 34 pass, 16 divergences | Engine defects present |
| Transient Storage | `aa491c9` | 48 pass, 2 divergences | Engine/envelope defects present |
| Access List and Fee Market | `aa491c9` | 47 pass, 1 divergence, 2 harness errors | Apparatus and envelope defects present |

The Return Data harness error is an EELS timeout on the frozen case:

`tests/frontier/opcodes/test_all_opcodes.py::test_stack_overflow[fork_Berlin-opcode_RETURNDATASIZE-state_test-fails_False]`

The Access List and Fee Market harness errors are EELS-versus-fixture transaction-status disagreements in one Berlin type-1 intrinsic-gas case and one Cancun type-2 intrinsic-gas case. The remaining divergence is a blob-transaction balance/storage mismatch.

The Self-Destruct failures currently appear as two families:

- fifteen account-existence mismatches across Cancun, Prague, and Osaka involving reentrant transient-storage/self-destruct behavior;
- one incorrect return-data result in `test_create_and_destroy_multiple_contracts_same_tx`.

The Transient Storage failures currently appear as:

- one nested-static-call/revert balance mismatch;
- one EIP-7702 reentry case with gas, account-existence, and balance mismatches.

Current `main` is `f532259`. Its latest executor repair has not been validated by rerunning the campaigns. It also infers transaction type from non-zero values, which is insufficient for exact typed-envelope conformance. No current-head certification exists.

## Frozen-manifest and oracle integrity

The existing six manifests remain immutable throughout this campaign train. Case identities, ordering, fixture checksums, comparison fields, and denominators must not be edited to obtain a passing result.

The following rules are mandatory:

- A slow or difficult case remains part of its original manifest.
- A timeout is an apparatus failure, never a divergence, pass, fixture absence, or permission to remove the case.
- A case may not be replaced by a similar case merely because it is easier to execute.
- Expected results may not be rewritten from Schlieren output.
- A fixture/oracle problem may be declared only with independent evidence and remains visible in the original campaign history.
- No quarantine is permitted in a 100% certificate.

The exact Return Data timeout case must execute successfully for the original campaign to certify. If independent reproduction proves that the pinned EELS release itself cannot complete that case under a correctly bounded environment, the original campaign remains uncertified. A replacement requires a separately reviewed version-2 manifest with a new hash, explicit supersession record, and documented reason; it does not retroactively turn the original run green.

## Certification train

### Gate 1: make the apparatus trustworthy

No EVM semantic repair begins while any campaign still has a `HarnessError` or `Aborted` result. The apparatus work is:

1. Execute the exact Return Data timeout case through a bounded isolation path that retains the comparison outputs but does not retain unnecessary full trace, stack, memory, or storage snapshots.
2. Record wall time, process outcome, EELS standard output/error digest, and timeout classification durably.
3. Reconcile the two access-list status disagreements by independently checking fixture transaction encoding and direct EELS output.
4. Correct fixture decoding, oracle invocation, or status normalization only after the independent authority is established.
5. Rerun the affected frozen manifests until all 50 cases receive valid conformance outcomes and no apparatus status remains.

Resource reduction is allowed only for diagnostic detail not required by the manifest comparison contract. It must not change the transaction, prestate, environment, fork, oracle, expected fields, or Schlieren execution semantics.

### Gate 2: correct the shared transaction envelope

Transaction type must be decoded from explicit envelope structure, not from whether optional numeric values happen to be non-zero. Zero is a valid value; an empty access list is still an explicit access-list field.

The canonical fixture model and executor must preserve field presence separately from field value and distinguish:

- type 0 legacy envelope: no typed-envelope discriminator and no typed-only fields;
- type 1 EIP-2930 envelope: explicit type `0x01`, including an empty access list;
- type 2 EIP-1559 envelope: explicit type `0x02`, including zero-valued fee fields;
- type 3 EIP-4844 envelope: explicit type `0x03`, blob fee and versioned-hash fields, and type-3 validity rules;
- type 4 EIP-7702 envelope: explicit type `0x04`, authorization-list fields, and type-4 validity rules.

If legacy fixtures omit an explicit type marker, inference may occur only from the presence of structurally unique fields and must reject ambiguous combinations. Blob transactions must not be silently treated as type 2.

Acceptance tests must assert exact results, not merely directional changes. They cover:

- exact sender balance after value, execution gas, effective gas price, and blob gas charges;
- exact miner or fee-recipient accounting where required by the fixture contract;
- intrinsic gas and transaction validity for empty and non-empty access lists;
- zero-valued EIP-1559 fee fields;
- base-fee, priority-fee, fee-cap, and insufficient-balance boundaries;
- type-3 blob fee, blob gas, versioned-hash, and validity behavior;
- type-4 authorization-list behavior represented by the installed fixtures;
- exact status, gas, logs, return data, account existence, balance, nonce, code, and represented storage.

The test introduced at `f532259`, which only proves that a type-2 sender balance decreased, is a regression hint but not sufficient acceptance evidence. It must be supplemented by exact accounting assertions.

### Gate 3: reinspect before diagnosing engine families

After Gates 1 and 2 are green, rerun all six original manifests. This establishes a new baseline after apparatus and envelope corrections and prevents repairs from targeting stale symptoms.

Every remaining divergence must be reclustered from typed deltas and journal evidence. Existing cluster counts are hypotheses until this reinspection confirms them.

### Gate 4: repair one causal family at a time

The provisional repair order is:

1. Self-Destruct account-existence lifecycle family.
2. Self-Destruct return-data family.
3. Remaining Transient Storage family or families after envelope correction.
4. Remaining blob/access-list/fee-market family or families after envelope correction.

For each family:

1. open a repair order linked to every affected case;
2. identify the earliest typed causal divergence;
3. write a focused failing regression test using the smallest faithful reproduction;
4. repair the canonical execution path without introducing a diagnostic fallback;
5. pass focused tests and the full regression suite;
6. rerun the entire affected 50-case frozen manifest;
7. compare before and after runs and record eliminated, unchanged, expanded, introduced, and regressed families;
8. close the repair order only when the original affected cases pass and no passing case regresses.

If a repair changes a shared EVM rule, all six new campaigns and Storage must be rerun before another certificate decision. UI or report projections cannot compensate for an incorrect canonical result.

### Gate 5: final same-commit inspection

On the candidate final commit:

1. verify repository and dependency provenance;
2. run Storage Lifecycle and require 50/50;
3. run each of the six new manifests and require 50/50;
4. run the full test suite three consecutive times and require identical outcomes;
5. confirm no open repair order applies to the seven manifests;
6. confirm no campaign artifact reports divergence, invalid fixture, harness error, abort, or quarantine;
7. issue certificates only after all evidence is durable and content-hashed.

Any code change after the first final inspection invalidates the same-commit set and restarts Gate 5.

## Campaign and umbrella certificates

### Individual certificate

Each Campaigns 2-7 certificate records:

- certificate schema version and ID;
- campaign ID, manifest hash, and ordered case count;
- final run ID and run artifact hash;
- exact Schlieren commit and dirty-tree state;
- EELS version and executable digest;
- fixture-root identity and fixture revision/digest;
- runtime and operating-system identity;
- 50 pass and zero of every non-pass terminal status;
- full-suite gate run IDs and totals;
- all applicable repair-order IDs and their closed evidence;
- issue timestamp and certificate content hash.

An individual certificate proves only its named campaign and provenance. A previous green run on a different commit is not reusable.

### Umbrella certificate

The umbrella certificate references the six individual certificates rather than duplicating their case records. It records and verifies:

- exactly six expected campaign IDs;
- six distinct frozen manifest hashes;
- one shared Schlieren commit;
- one shared EELS version and executable digest;
- one shared fixture revision/root identity;
- one shared three-run full-suite gate;
- aggregate result of 300 pass out of 300 cases;
- zero non-pass outcomes across all referenced runs;
- the renewed Storage certificate as a required external regression prerequisite;
- content hashes for every referenced certificate and final umbrella record.

Umbrella issuance fails if any provenance field differs, any individual certificate is missing or stale, Storage is not renewed on the same commit, or aggregate totals do not reconcile exactly.

## Ledger and reporting requirements

All work remains append-only under `harvest/ledger/`. New runs never overwrite baseline evidence. Machine-readable artifacts remain authoritative; Markdown reports are projections.

Every campaign report must show:

- manifest and run identity;
- code, EELS, fixture, and environment provenance;
- counts for every terminal status;
- exact case list and outcome;
- typed discrepancies and cluster membership;
- apparatus failures separately from engine divergences;
- before/after family movement;
- repairs and regression tests linked by commit;
- certification decision and exact refusal reasons when not certified.

The running report must make downstream regressions visible. A formerly passing case that fails after an unrelated repair is `Introduced` and blocks certification; it is never folded into the repaired family's success count.

## Failure handling

- `HarnessError` or `Aborted`: stop engine diagnosis for that campaign and repair the apparatus first.
- `FixtureInvalid`: stop certification and repair admission/decoding; the case stays in the denominator.
- `Divergence`: open or update a typed causal repair family.
- independently proven oracle defect: preserve evidence, leave the original campaign uncertified, and require reviewed versioning for any successor manifest.
- host crash or timeout: emit a finalized non-pass artifact from the isolation boundary.
- inconsistent repeated suite run: reject the certificate candidate and investigate nondeterminism.
- dirty working tree or unknown dependency identity: refuse certificate issuance.

No failure may disappear because a process terminated, a report was regenerated, or a case was inconvenient.

## Testing strategy

Development remains test-first for every apparatus or engine repair.

### Apparatus tests

- exact frozen slow-case execution and bounded timeout classification;
- child-process crash, cancellation, and output capture;
- comparison parity with diagnostic retention enabled and disabled;
- EELS/fixture status-authority reconciliation;
- durable artifact finalization for every terminal status.

### Transaction-envelope tests

- explicit type 0/1/2/3/4 decoding;
- empty type-1 access list and zero-valued type-2 fees;
- ambiguous or contradictory field rejection;
- exact fee and balance accounting;
- intrinsic-gas and validity boundaries;
- blob and authorization-list semantics.

### Engine-family tests

- account existence across create, self-destruct, reentry, commit, and rollback;
- return-data ownership and replacement across nested creation/destruction;
- transient storage ownership, static context, nested revert, and transaction clearing;
- blob/access-list behavior confirmed by the post-envelope baseline.

### Certification tests

- refusal for each non-pass status;
- refusal for manifest, commit, EELS, fixture, or suite-gate mismatch;
- exact aggregate reconciliation;
- stale individual-certificate rejection;
- Storage-prerequisite rejection;
- deterministic content hashing and append-only issuance.

## Scope exclusions

This certification train does not include:

- changing the six frozen manifests to improve scores;
- random or mutation-based campaign generation;
- Hunter contract-vulnerability search;
- customer EELS download/update distribution;
- React visualization changes;
- performance certification beyond preventing apparatus timeouts;
- certification against mainnet receipts without exact prestate;
- cryptographic signing or public trust infrastructure for certificates.

Those may follow after this train establishes reliable conformance evidence.

## Completion criteria

This undertaking is complete only when:

1. all apparatus blockers are eliminated on the original manifests;
2. typed transaction envelopes and exact fee accounting are proven for represented types;
3. all remaining divergences are repaired through canonical EVM paths with focused regression tests;
4. Storage is renewed at 50/50 on the final commit;
5. Call Semantics, Create Semantics, Return Data, Self-Destruct, Transient Storage, and Access List/Fee Market each produce 50/50 final runs on that same commit;
6. three consecutive full-suite runs are identical and green;
7. six individual certificates and one 300/300 umbrella certificate are issued with reconciled provenance;
8. the complete baseline, repair, reinspection, and certification history remains preserved in the ledger.

Until every condition is met, reports must say exactly what is incomplete. A high pass percentage is progress, not certification.
