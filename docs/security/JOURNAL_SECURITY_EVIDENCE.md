# Journal Security Evidence

Schlieren's security findings are deterministic statements about one observed canonical execution. They are not source-code guesses and are not claims that every input is exploitable.

## Evidence source

`JournalSecurityAnalyzer` accepts a validated `JournalAnalysis`. It uses explicit frame IDs, parent IDs, call type, storage owner, code owner, typed state effects, and frame/transaction disposition. It never reconstructs frames from depth or parses stack/storage display strings.

The removed flat-trace batch and live detectors are not fallback paths. Avalonia, regression checks, RPC, and React now consume the same analyzer.

## Rules

Current rule IDs are defined in the production `SecurityRuleId` enum and the detectors below. The rules listed here match the production code as of the commit that introduced this document's last update.

`SEC.REENTRANCY.REENTRY` requires an explicit `CALL` or `CALLCODE` frame whose storage context equals an active ancestor's storage context and whose frame contains a typed persistent storage write.

`SEC.REENTRANCY.POST_WRITE` records an ancestor write after the re-entry evidence and raises the survived-path severity.

`SEC.STORAGE.DELEGATE_COLLISION` requires explicit `DELEGATECALL` or `CALLCODE` geometry, distinct code and storage owners, and a typed write to slot zero or an EIP-1967 implementation, admin, or beacon slot.

## Active detectors

- `CompilerPatternDetector` — classifies compiler-emitted patterns (dispatcher, proxy stub, guard) from typed bytecode facts.
- `LibraryGuardDetector` — identifies library guard patterns from storage ownership and call geometry.
- `ProxyDelegationDetector` — identifies proxy delegation patterns from `DELEGATECALL` geometry and storage slot writes.

Call-family semantics (`CALL`, `CALLCODE`, `DELEGATECALL`, `STATICCALL`) are encoded in the production `CallType` enum. The journal frame tree records these explicitly; the analyzer does not infer call type from depth or stack.

## Grade and disposition

Each finding includes a deterministic ID, rule, category, server-assigned severity and fact grade, primary frame/instruction, supporting event sequences, complete ancestry, affected addresses/slots, execution disposition, persistence disposition, summary, and limitation.

A finding whose evidence was rolled back is retained for forensic inspection but clamped to `Info`, with execution `Reverted` and persistence `NotApplicable`. A successful simulation is `Survived` and `SimulationDiscarded`; it is not mislabeled as an EVM revert.

## UI responsibility

`schlieren_traceJournal` returns the authoritative recursive `frameTree`. Each node already lists its direct state-effect and security-finding IDs. React may expand, collapse, and navigate this supplied tree, but it does not regroup flat frames, derive ancestry, or recalculate severity.

## Operational credential note

Two JWTs previously committed in `Schlieren.UI/Services/HarvestService.cs` (lines 16–17 at commit `d43c8c5`) have been removed from tracked source as part of Task 3 (commit: `security: externalize harvest credentials`). **Removing credentials from source does not revoke them.** Both tokens must be rotated externally. Rotation is an operational action and must not be reported as complete without independent evidence outside this repository.
