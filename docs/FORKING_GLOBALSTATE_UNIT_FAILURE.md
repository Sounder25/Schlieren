# Known unit failure: ForkingGlobalState unfetched remote storage

**Frozen with:** workbench `StateTransition` cutover (2026-08-15)  
**Suite:** `Schlieren.Tests` — **348 passed, 1 failed** (349 total)

## Test

| | |
|---|---|
| **Name** | `Schlieren.Tests.Execution.SelfDestructAccessTests.ForkingGlobalState_UnfetchedRemoteStorage_ReturnsUnknownPresence` |
| **File** | `Schlieren.Tests/Execution/SelfDestructAccessTests.cs` (around line 448) |
| **Deterministic?** | **Yes.** Same assertion every run: CREATE2 pops `240896453012664701504956954585511235825729922169` instead of `0`. That integer is the derived CREATE2 address, not a random fault. |

## What the test expects

1. `ForkingGlobalState.GetStoragePresenceAsync` returns `StoragePresence.Unknown` when a mock `IForkProvider` is attached (no remote key enumeration).
2. CREATE2 must **fail closed** on `Unknown`: treat the destination as a collision and **push `0`**.

`ForkingGlobalState` still returns `Unknown` for that provider. The second half fails because `AccountDeployability.IsDeployableAsync` now allows deploy when storage is **not** `NonEmpty`:

```csharp
// Empty or Unknown-without-writes: only NonEmpty blocks deployment.
return storage != StoragePresence.NonEmpty;
```

So CREATE2 proceeds and pushes the new address. The test’s fail-closed contract is stale relative to that deployability rule.

## Why it is unrelated to the workbench / EELS freeze

| Path | Uses `ForkingGlobalState`? |
|---|---|
| Workbench `BytecodeExecutionService` → `StateTransition.ApplyTransactionAsync` | **No.** Seeds a local `GlobalState`. |
| EELS / Conformance (`EelsStateFixtureExecutor`) | **No.** Local `GlobalState` from fixture JSON. |
| Call graph, P256 wrapper, contract-to-contract CALL smoke | **No.** |

This test only exercises the **RPC archive-fork** state wrapper (`IForkProvider`), used when a node is pointed at a remote parent chain. It is not on the conformant fixture pipeline or the workbench execution path frozen here.

## Does it affect any conformant engine path?

**Not the EELS / workbench path.** Fixtures always have known local storage presence (`Empty` / `NonEmpty`). Osaka 14,516/14,516 and the workbench 12/12 smokes do not go through `ForkingGlobalState`.

**It does record a real gap on the fork-RPC product path:** EIP-7610 collision-burn for *unknown remote storage* is incomplete. Inventory already flags that: `CREATE.COLLISION_BURN` in `docs/gas/GAS_RULE_INVENTORY.md` and `docs/gas/GAS_COVERAGE_MATRIX.md`. Resolution is Task 1 of `docs/superpowers/plans/2026-08-14-executable-gas-schedule-completion.md` (unknown remote storage must not be treated as deployable). That is a separate track from this UI/engine-unification freeze.

## Do not “fix” it in this freeze

Changing `AccountDeployability` here would retune CREATE2 collision for every local path without an EIP-7610 vector set. Leave the one red test documented until the fork-RPC / unknown-storage task lands.
