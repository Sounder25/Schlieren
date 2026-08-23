# Manual Bug-Hunt & Fix Session Summary
**Date:** 2026-08-18
**Branch:** `codex/gas-rule-inventory`
**Scope:** Manual source-code correctness audit (not the gas-rule work — that ran in
parallel, in a separate session, on `Schlieren.Core/Opcodes/`, `StateTransition.cs`,
`IntrinsicGas.cs`, `ForkRules.cs`, `Precompiles.cs` gas pricing) + fixes for every
finding, each with tests.

This file is the index. Every fix below cites the exact commit that contains it —
`git show <hash>` reproduces the diff.

---

## How this started

Original ask was to design a REVM/EELS differential test campaign for gas tracing bugs.
Redirected mid-brainstorm to direct manual source auditing instead ("keep scanning and
see what else pops out"), since reading code by hand was already surfacing real bugs
faster than building test infrastructure would have.

Two waves of parallel read-only audits (5 agents, then 7 agents), each given a
distinct subsystem and told to report only high-confidence logic defects with
file:line and a concrete failure scenario — not style/cosmetic observations.

---

## Wave 1 — Detection/, root-cause modules, precompiles, interpreter core, state/tombstone

These were fixed by a **different, parallel session** working the same working tree.
I verified each fix by reading the resulting code directly (not just trusting the
report). All confirmed correct.

| # | Finding | File:line | Verified via |
|---|---|---|---|
| 1 | `DetectCheckedArithmeticRevert` dead code — `OutputData` never populated for REVERT | `Schlieren.Core/Detection/CompilerPatternDetector.cs:192` | `dcced35` |
| 2 | `LibraryGuardDetector` read wrong PUSH32 stack snapshot (pre-exec, not post) | `Schlieren.Core/Detection/LibraryGuardDetector.cs:50`, `Schlieren.UI/Services/LibraryGuardDetector.cs` | `dcced35` |
| 3 | `EvmMachine` exception catch chain only handled OOG; stack/jump exceptions crashed the tx `Task` | `Schlieren.Core/Execution/EvmMachine.cs:99-150` | `2d974be` |
| 4 | `EvmStack.Peek()` silently returned 0 on empty stack instead of throwing (dormant, unused) | `Schlieren.Core/Execution/EvmStack.cs:50` | `2d974be` |
| 5 | `TraceDivergenceLocator`/`ProtocolDiagnosisReport` — inverted warm/cold attribution from two gas-delta sign conventions | `Schlieren.Core/Execution/TraceDivergenceLocator.cs:78-116`, `Inspect/ProtocolDiagnosisReport.cs:297` | `dcced35` |
| 6 | `StateDiffBuilder` — `CodeChanged=true` hardcoded for every Created/Deleted account, even empty-code EOAs | `Schlieren.Core/State/StateDiffBuilder.cs:94` | `dcced35` |
| 7 | README claimed SELFDESTRUCT+CREATE2 redeploy rejected "within block" — actually transaction-scoped; later-tx-same-block CREATE2 redeploy is **correct** metamorphic-contract behavior, not a bug | `Schlieren.Core/State/StateOverlay.cs`, `README.md` | `490a61d` |
| 8 | `ForkingGlobalState` conflated nonexistent vs. empty account on remote fetch | `Schlieren.Core/State/ForkingGlobalState.cs:165` | fixed again more thoroughly in Wave 2, see below |
| 9 | `StateOverlay.Reset(Address)` leaked EIP-6780 deletion mark (dead code at the time) | `Schlieren.Core/State/StateOverlay.cs:149` | re-verified/extended in Wave 2 |
| 10 | `Bn254Pairing` G2 subgroup check — comment said "matches geth" but check is EIP-197/EELS-correct; **not a bug**, comment fixed | `Schlieren.Core/Execution/Bn254Pairing.cs:118,145` | `490a61d` |

**Notable false alarm caught during cross-referencing:** a separate "confirmed gas bugs"
list claimed `AccountDeployability.cs:27` fails open on `StoragePresence.Unknown`. Actual
current file fails closed (`return storage == StoragePresence.Empty`) — fixed 2026-08-15
in commit `d77dfe2`. Root cause of the false alarm: a **stale duplicate copy of the whole
solution** existed at `Scrutor/Scrutor.Core/...` (pre-rename leftover, untracked) with the
old buggy line. Whoever produced that list read the `Scrutor/` mirror, not the canonical
`Schlieren.Core/`. The `Scrutor/` directory was removed during this session (already gone
by the time it was checked).

---

## Wave 2 — RPC, CLI, mempool/mining, UI Workbench/Conformance

Everything below **I fixed and tested myself** this session, in severity order.
Findings that were also part of the fix commit but not originally numbered (found while
verifying/fixing another item) are marked *(bonus)*.

### Critical — silent wrong answers

| Finding | File:line | Fix commit | Test |
|---|---|---|---|
| `eth_call` at a historical block silently executed against **current** state while reporting the historical block's metadata — code comment admitted it | `Schlieren.RPC/Handlers/EthHandlers.cs:1346` (`ResolveEthCallBlockContext`) | `9fca183` | `EthCallRpcTests.EthCall_RejectsHistoricalBlockNumber_InsteadOfSilentlyUsingCurrentState` |
| `debug_traceTransaction` fallback replay used current-head block context (NUMBER/TIMESTAMP/BASEFEE/COINBASE), not the tx's actual block | `Schlieren.RPC/Handlers/EthHandlers.cs:991` | `9fca183` | covered by existing `DebugTraceAdvancedRpcTests` suite (all passing) |

### High — protocol/DoS correctness

| Finding | File:line | Fix commit | Test |
|---|---|---|---|
| TxMempool nonce replacement ("speed-up") silently orphaned the old tx (no gas-price comparison, `_lookup`/`_pendingByAccount` went out of sync) | `Schlieren.Core/State/TxMempool.cs:30` (`Add`) | `9fca183` | `TxMempoolTests.Add_SameNonceHigherPrice_ReplacesAndEvictsOldHash`, `Add_SameNonceLowerOrEqualPrice_IsRejected` |
| `PeekBest` ignored per-sender nonce order → livelock re-queuing an unappliable higher-nonce tx forever, starving the valid lower-nonce one | `Schlieren.Core/State/TxMempool.cs:47` + `MiningService.cs:122` | `9fca183` | `TxMempoolTests.PopBest_RespectsNonceOrder_WithinSameSender` (this **replaced** a pre-existing test that was asserting the buggy behavior) |
| IOCPServer: connection semaphore acquired before `AcceptAsync`; a transient accept exception never released it → permanent connection-pool shrinkage | `Schlieren.RPC/Server/IOCPServer.cs:69` | `9fca183` | build/manual reasoning only (no socket-level test harness in repo) |
| IOCPServer: 1MB payload cap and Content-Length completeness check compared **UTF-16 char count** against **byte** limits — bypassable with multi-byte content; chunk-boundary UTF-8 splitting also corrupted bodies | `Schlieren.RPC/Server/IOCPServer.cs:112-198` | `9fca183` | same as above |
| Content-Length header: unbounded digit string → uncaught `OverflowException` from `int.Parse` | `Schlieren.RPC/Server/IOCPServer.cs:194` | `9fca183` | same as above |
| `WorkbenchFixtureLoader` sender resolution: standard EELS fixtures carry `secretKey`, not `sender` — old code used the raw 32-byte private key as the sender **address** | `Schlieren.UI/Services/WorkbenchFixtureLoader.cs:184` | `b2a24da` | `WorkbenchFixtureLoaderTests.Parse_FixtureWithSecretKeyNoSender_FallsBackToPreAccount_NotRawKey` |
| `ConformanceRunService`: one fixture throwing propagated through `Task.WhenAll` and aborted the **entire** fork sweep — no tally, no results for any other case | `Schlieren.UI/Services/ConformanceRunService.cs:187` | `b2a24da` | `ConformanceLoadProgressTests.RunCasesAsync_OneCaseThrows_OthersStillComplete`, `RunCasesAsync_RealCancellation_StillPropagates` (verified via a real injected crash through a new `RunCasesAsync(cases, executeDelegate, ...)` entry point extracted specifically to make this testable, not just via inspection) |
| `ForkingGlobalState` materialized a local account entry for **any** fetched address, even all-zero/nonexistent ones, via unconditional `SetBalance/SetNonce/SetCode` → `AccountExistsAsync` wrongly `true` after one query | `Schlieren.Core/State/ForkingGlobalState.cs:165` | `cfa189c` | `ForkingStateTests.AccountExistsAsync_ReturnsFalse_ForGenuinelyNonexistentRemoteAccount`, `AccountExistsAsync_ReturnsTrue_ForRemoteAccountWithOnlyNonzeroNonce` |

### Medium

| Finding | File:line | Fix commit | Test |
|---|---|---|---|
| `ParseBlockTag` (eth_getLogs `fromBlock`/`toBlock`) silently defaulted on an unrecognized tag ("safe"/"finalized"), and threw an unhandled `InvalidOperationException` on a non-string JSON value | `Schlieren.RPC/Handlers/EthHandlers.cs:686` | `843b860` | `EthGetLogsRpcTests.GetLogs_UnrecognizedBlockTag_ThrowsInsteadOfSilentlyUsingDefault`, `GetLogs_NonStringBlockTag_ThrowsInvalidParams_NotUnhandledException` |
| Unclamped `BigInteger→ulong` casts (nonce/gas fields, 6 call sites) → uncaught `OverflowException`. Root cause traced deeper than the call sites: `EthereumTypes.FromEthHex` itself threw raw `OverflowException` | `Schlieren.RPC/Handlers/EthHandlers.cs` (6 sites) + `Schlieren.RPC/Models/JsonRpcModels.cs:136` (`FromEthHex`) | `843b860` | `EthCallRpcTests.EthCall_RejectsOversizedNonce_InsteadOfThrowingOverflowException` |
| `to` address in `BuildCallTransaction` unvalidated (unlike `from`) → uncaught exception from `Address.FromHex` on malformed input | `Schlieren.RPC/Handlers/EthHandlers.cs:1374` | `843b860` | `EthCallRpcTests.EthCall_RejectsInvalidToAddress` |
| TxMempool: full pool silently dropped a submission from a `void` method with zero caller feedback; size/duplicate check ran outside the lock (TOCTOU) | `Schlieren.Core/State/TxMempool.cs:34` | `9fca183` (also wired `Add()`'s new `bool` return through `eth_sendRawTransaction`/`eth_sendTransaction` to throw a clean RPC error on rejection) | `TxMempoolTests.Add_DeduplicatesTransactions` |
| `BytecodeExecutionService.ParseOptionalTo` treated **any** all-zero hex — including the full valid 20-byte zero address — as "no recipient" (CREATE signal) | `Schlieren.UI/Services/BytecodeExecutionService.cs:303` | `37e51bf` | `WorkbenchStateTransitionTests.LiteralZeroAddressTo_IsMessageCall_NotCreate`, `ShortFormPlaceholder_StillMeansCreate` (theory, 3 cases) |

### Low / cleanup

| Finding | File:line | Fix commit | Test |
|---|---|---|---|
| `StateOverlay.Reset(Address)` cleared buffer/created/tombstones but not `_accountsMarkedForDeletion` — leaked an EIP-6780 mark through the discard path (dead code in production today) | `Schlieren.Core/State/StateOverlay.cs:149` | `cfa189c` | `OverlayIsolationTests.ResetAddress_ClearsMarkForDeletion_DoesNotLeakThroughCommit` |
| `AuditReportExporter` wrote finding `Description`/`Details`/`LocationText` into Markdown table cells with no `\|`/newline escaping — breaks on the app's own normal reentrancy-finding output (`"Target: X \| step Y"`) | `Schlieren.UI/Services/AuditReportExporter.cs:59` | `b2a24da` | strengthened the existing `AuditReportExporterTests` test (its own fixture data already contained an unescaped `\|` but the test never asserted on it) |
| Discarded `bool` from `WorkbenchQuantity.TryBigInteger`/`TryUlong` — malformed (not just absent) balance/nonce silently became 0 | `Schlieren.UI/Services/WorkbenchFixtureLoader.cs:314` (`ReadAccount`), `Schlieren.UI/Services/WorkbenchPrestateLoader.cs:93` (`ReadUlong`) | `b2a24da` | `WorkbenchFixtureLoaderTests.Parse_MalformedPreAccountBalance_SurfacesError_NotSilentlyZero`, `WorkbenchPrestateLoaderTests.Parse_MalformedNonce_SurfacesError_NotSilentlyZero` — ***(bonus, found while fixing this)*** `WorkbenchPrestateLoader.ReadUlong` only tried decimal `ulong.TryParse` for string nonces, so **any** hex-formatted nonce (`"0x5"`, the normal Ethereum JSON convention) silently became 0, not just malformed ones — covered by `Parse_HexStringNonce_ParsesCorrectly_NotSilentlyZero` |
| `JsonRpcExceptionMiddleware` put raw `ex.Message` into the client-facing JSON-RPC error `Data` field — potential internal-detail leak (dead code today, not wired into any pipeline since `IOCPServer` is raw-socket, not ASP.NET Core) | `Schlieren.RPC/Middleware/JsonRpcExceptionMiddleware.cs:49` | `843b860` | none (dead code; fixed for when it's eventually wired up) |
| CLI `--cors-origins` help text says "comma-separated" but `AllowMultipleArgumentsPerToken` only splits on spaces | `Schlieren.CLI/CommandLineParser.cs:162` | `e1678a9` | verified manually via a throwaway scratch console project (not committed — `Schlieren.Tests` has no project reference to `Schlieren.CLI`, decided not worth adding one for this) |
| `TraceCommand` swallowed **all** `eth_getTransactionReceipt` exceptions with a bare `catch`, silently showing intrinsic-only gas with no indication why | `Schlieren.CLI/Commands/TraceCommand.cs:76` | `e1678a9` | none (CLI integration test would need a live RPC server; out of scope for this pass) |

### Investigated, not a bug

| Item | Resolution |
|---|---|
| `Bn254Pairing` G2 subgroup check | Confirmed correct per EIP-197/EELS (Wave 1, see above) — the doc comment was wrong, not the math |
| CLI scripting host (`ScriptHost.cs`/`SchlierenNode.cs`) full-trust execution, no sandbox | By design — local dev tool, not a restricted surface. One real gap flagged but **not fixed**: unbounded recursion → uncatchable `StackOverflowException` kills the whole process; no execution timeout either |
| `ProxyImplementationUnresolvedDetector`'s fixed 50-step lookback window | Heuristic tradeoff, not a clear inversion — left alone |
| `FailureClusteringService` clustering by fingerprint (which bakes in fork name) | Deliberate — merging cross-fork clusters would hide genuinely fork-specific bugs |
| `AccountDelta.SelfDestructed` never set in `StateDiffBuilder` | Model gap (`Account` has no self-destruct flag), not a diff-builder logic error |

---

## Commits (this session, chronological)

```
2d974be fix(evm): exception halts crash tx Task; Peek() silent underflow                    [Wave 1, verified]
490a61d docs/test: BN254 subgroup check comment + StateOverlay tombstone semantics           [Wave 1, verified]
dcced35 fix(ui): LibraryGuardDetector PUSH32 stack off-by-one + FailureClusteringService     [Wave 1, verified]
9fca183 fix(mempool/rpc): TOCTOU race, nonce-ordering livelock, RPC rejection signal,
         IOCP semaphore leak                                                                 [Wave 2, authored]
37e51bf fix(service): TryAddress pads short input to Address.Zero; stale gas expectation     [Wave 2, authored]
cfa189c fix(state): ForkingGlobalState empty-vs-nonexistent conflation + StateOverlay         [Wave 2, authored]
843b860 fix(rpc): invalid block tags, unclamped casts, to validation, exception middleware   [Wave 2, authored]
e1678a9 fix(cli): CommandLineParser and TraceCommand fixes                                   [Wave 2, authored]
b2a24da fix(ui): AuditReportExporter, ConformanceRunService, WorkbenchFixtureLoader/
         PrestateLoader fixes + tests                                                        [Wave 2, authored]
```

Interleaved with these were unrelated commits from the parallel gas-rule session
(`ed82d2a`, `eb9b016`, etc.) — not covered by this summary.

## Verification

Full suite as of the last commit in this list: **520 passed, 0 failed, 5 skipped**
(`dotnet test Schlieren.Tests/Schlieren.Tests.csproj`). Every fix above has an
individually-run, passing test cited except where noted (dead code, or no test
harness exists for that surface in this repo).

## To find a specific fix later

```bash
git show <hash>                          # full diff + message
git log --oneline -- <path>               # history of one file
git log --all -S "<distinctive string>"   # find the commit that introduced/removed a string
```
