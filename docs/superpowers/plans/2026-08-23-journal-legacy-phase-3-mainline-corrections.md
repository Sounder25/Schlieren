# Phase 3: Mainline Correction Preservation Plan

> Scope: Phase 3 only. Work in `integration/journal-legacy-recovery`; do not modify `main`, the journal source worktree, or begin legacy capability recovery.

## Goal

Prove that the canonical journal-aware execution path preserves the required mainline protocol corrections, especially the EELS system-transaction correction from `3c0a6d8`, and forward-port only behavior shown to be absent.

## Source anchors

- Integrated Phase 2 head: `1c1f0ac6eab99e8ebf4925883f0235b9f74befa4`
- Mainline source: `20005b309b0a99e1997fdec94781f2a9bbc4dcdd`
- Morning journal source: `22193795b4b1fea15a068fc219ea34ca4f7abf6b`
- System-transaction correction: `3c0a6d81b0a76ade7578111a5ac37f500fb4b3ae`

## Task 1: Commit the Phase 3 plan

Commit this document as the Phase 3 boundary before changing production or test code.

## Task 2: Add exact system-transaction regression coverage

Add a focused execution test that reconstructs the program used by both published Osaka fixtures:

- EIP-7002 withdrawal request contract `0x00000961Ef480Eb55e80D19ad83579A64c007002`
- EIP-7251 consolidation request contract `0x0000BBdDc7CE488642fb579F8B00f3A590007251`

The generated program must match the fixture structure: write value `1` to slots `0..1356`, add the fixture's `JUMPDEST` gas padding, then stop. Execute it as `TransactionAuthorization.System` with the EELS system-call gas limit of `30,000,000` and journal capture enabled.

For each contract, assert:

1. no `IntrinsicGasChargedEvent` exists;
2. the root `FrameEnteredEvent.GasLimit` is exactly `30,000,000`;
3. all 1,357 fixture storage slots equal `1`, including slot `1356` written by the final `SSTORE`;
4. exactly 1,357 committed `StorageWriteEvent` records exist;
5. the final storage-write event correlates with an `SSTORE` opcode event.

This is the direct regression for the historic `2 × 1,357 = 2,714` mismatch cluster.

## Task 3: Guard ordinary external intrinsic gas semantics

In the same focused suite, execute an ordinary impersonated transaction with journal capture and assert:

- exactly one `IntrinsicGasChargedEvent` is emitted;
- its amount equals the canonical intrinsic calculator;
- the root frame receives `tx.GasLimit - intrinsicGas`;
- transaction settlement and reported gas remain conserved.

## Task 4: Audit the remaining mainline corrections

Compare the integrated tree with the behavior and focused tests introduced by the mainline correction commits, including:

- `5e0279e` / `1d876d8`: pre-EIP-150 CALL overflow and EIP-7702 account-existence semantics;
- `116b3a3` / `ae7b169`: EIP-3541, refund quotient, CREATE snapshot, and EIP-7825 behavior;
- `581cd7f`: MODEXP saturation and host-exception handling;
- `ed82d2a`: EIP-170 fork gating;
- `2d974be`: protocol exception halt mapping;
- `cfa189c`: state-overlay existence semantics;
- `eb55b89` and `ff0e5b9`: explicit fork capabilities and executable gas schedule.

Record each as one of:

- preserved and covered by an existing focused test;
- preserved but missing focused coverage, with a test added in Phase 3;
- absent, with the smallest canonical-path forward-port and a regression test.

Do not revive duplicate gas-tree execution, diagnostic execution, or simplified intrinsic-calculation paths.

## Task 5: Verification and Phase 3 gate

Run:

1. the new system-transaction regression suite;
2. focused tests associated with every audited mainline correction;
3. journal conservation, nested-frame, and RPC contract suites;
4. EELS focused alignment suites;
5. the full .NET test suite and React build/tests if production code changes.

Produce a Phase 3 report listing:

- exact commits reviewed;
- exact commits or hunks forward-ported (or an explicit statement that none were necessary);
- focused and full test results;
- fixture-directory failures separately from behavioral failures;
- final branch head and worktree status.

Stop for user review. Phase 4 is not authorized by this plan.
