# Journal Security Evidence

Schlieren's security findings are deterministic statements about one observed canonical execution. They are not source-code guesses and are not claims that every input is exploitable.

## Evidence source

`JournalSecurityAnalyzer` accepts a validated `JournalAnalysis`. It uses explicit frame IDs, parent IDs, call type, storage owner, code owner, typed state effects, and frame/transaction disposition. It never reconstructs frames from depth or parses stack/storage display strings.

The removed flat-trace batch and live detectors are not fallback paths. Avalonia, regression checks, RPC, and React now consume the same analyzer.

## Rules

`SEC.REENTRANCY.REENTRY` requires an explicit `CALL` or `CALLCODE` frame whose storage context equals an active ancestor's storage context and whose frame contains a typed persistent storage write.

`SEC.REENTRANCY.POST_WRITE` records an ancestor write after the re-entry evidence and raises the survived-path severity.

`SEC.STORAGE.DELEGATE_COLLISION` requires explicit `DELEGATECALL` or `CALLCODE` geometry, distinct code and storage owners, and a typed write to slot zero or an EIP-1967 implementation, admin, or beacon slot.

## Grade and disposition

Each finding includes a deterministic ID, rule, category, server-assigned severity and fact grade, primary frame/instruction, supporting event sequences, complete ancestry, affected addresses/slots, execution disposition, persistence disposition, summary, and limitation.

A finding whose evidence was rolled back is retained for forensic inspection but clamped to `Info`, with execution `Reverted` and persistence `NotApplicable`. A successful simulation is `Survived` and `SimulationDiscarded`; it is not mislabeled as an EVM revert.

## UI responsibility

`schlieren_traceJournal` returns the authoritative recursive `frameTree`. Each node already lists its direct state-effect and security-finding IDs. React may expand, collapse, and navigate this supplied tree, but it does not regroup flat frames, derive ancestry, or recalculate severity.
