# Avalonia Retirement Contract

Date: 2026-08-30
Status: Frozen
Reference UI: `Schlieren.UI` (do not repair)
Target UI: `schlieren-ui` + `Schlieren.RPC`

## Policy

`Schlieren.UI` is frozen as the migration reference. Do not fix, restyle, or extend it.

Implement missing capabilities in React and RPC. Use the feature-parity matrix as the retirement checklist. Remove `Schlieren.UI` from the solution only when every user-facing Avalonia row is `MIGRATED_AND_WORKING` or formally `SUPERSEDED`.

A component file does not count as migrated unless the user can actually invoke it.

Do not port the Avalonia Harvest / Certify UI. `Schlieren.Harvest` may remain in the repository as internal certification apparatus. It is not a customer-product feature.

## Status

| Item | Capability | Status |
|---|---|---|
| A | Execution | `MIGRATED_AND_WORKING` |
| B | Fixture / state execution | `MIGRATED_AND_WORKING` |
| C | Workbench tools | `MIGRATED_AND_WORKING` |
| D | Conformance suite | `MIGRATED_AND_WORKING` |
| E | Harvest / Certify | `SUPERSEDED_BY_HUNTER` |
| F | OpSec | `MIGRATED_AND_WORKING` |

F is verified. Avalonia dependency cleanup is mechanical:

1. Inventory remaining `Schlieren.UI` compile references (done).
2. Classify: delete Avalonia-only tests, relocate behavioral coverage to RPC/React, replace smoke tools, drop project references (done).
3. Remove `Schlieren.UI` from `Schlieren.sln` while leaving its source frozen in-repo (done).
4. Source deletion is a **separate, later commit** after the clean-solution gate stays green.

## Two properties that must not be confused

| Property | Meaning | Not meaning |
|---|---|---|
| `commit: false` on `schlieren_traceJournal` | Handler policy: run on a `StateOverlay` and do not write this call into the RPC node's canonical `GlobalState`. Journal persistence is `SimulationDiscarded`. | OpSec. User-chosen simulation mode. A guarantee that the RPC process never mutates state. A guarantee of no network. |
| OpSec lockout | Network-exposure control: prohibited outbound connectors must fail while lockout is active. | State persistence. `commit: false`. A React-only toggle. |

The Avalonia OpSec toggle was a scoped `AsyncLocal` flag. Production HTTP/RPC clients do not call `AssertOffline`. Preserve the **requirement**, not that implementation.

## Retirement backlog

### A — Execution — `MIGRATED_AND_WORKING`

React wiring:

- TopNav RUN starts the same path as Workbench EXECUTE
- STOP / cancellation of the in-flight journal request
- Fork selector bound to `config.fork` and sent on the next run (RPC fork names)
- TX drawer mounted and driving from / to / value / gasLimit / calldata

### B — Fixture / state execution — `MIGRATED_AND_WORKING`

`schlieren_traceJournal` is a normalized execution context, not an EELS fixture parser. React adapter produces `LoadedFixture { source, request, expected, identity }`. Expected post-state stays client-side. Prestate rides as overlay + `commit: false`.

### C — Workbench tools — `MIGRATED_AND_WORKING`

- Reset
- Call-topology invocation (FrameTree / Flow)
- Trace JSON export
- Audit report export

### D — Conformance suite — `MIGRATED_AND_WORKING`

RPC start / poll / cancel / read. Open failing fixture in Workbench via the same `LoadedFixture` adapter.

### E — Harvest — `SUPERSEDED_BY_HUNTER`

Harvest / Certify is an internal Schlieren engineering apparatus for proving Schlieren itself against EELS. It does not belong in the customer product.

The product-facing replacement is Hunter:

- user supplies or targets contracts / projects
- Schlieren executes / traces them
- Hunter drives adversarial cases / fuzzing / scenario generation
- divergences, invariants, suspicious execution paths, gas anomalies, revert chains, state changes, and related signals surface as findings
- Workbench is where those findings are inspected and reproduced
- none of the internal campaign manifests, EELS certification ledger, repair orders, or “certify Schlieren” workflow ships to Web3 users

`Schlieren.Harvest` (and related internal projects) may remain in the repository and keep doing campaign / certification work. They are not a product feature.

The React Harvest tab is not a frontend for the certification apparatus. It must not grow into a port of Avalonia Harvest. It should become Hunter later. Do not spend further time porting the Avalonia Harvest UI.

### F — OpSec — RPC authority

The UI may request lockout. **Schlieren.RPC is the authority** that rejects prohibited outbound operations while lockout is active.

RPC surface:

- `schlieren_opsecStatus`
- `schlieren_opsecSet`
- `schlieren_importCode` (the only product `eth_getCode` fetch path; gated by `OpSecGate`)

Process-wide lock: `OpSecGate` in Core. Distinct from `OpSecLockout` (per-async-flow test isolation used by internal Harvest / Workbench tests).

**OPSEC LOCKOUT ON — allowed**

- loopback RPC
- local Schlieren execution
- local fixtures / files
- local EELS
- local Harvest corpus (internal, not shipped)
- local exports

**OPSEC LOCKOUT ON — blocked**

- public RPC providers
- remote `eth_getCode`
- n8n / cloud workflows
- external HTTP fetches
- any connector that can transmit contract or fixture data

A React toggle alone is not sufficient. Journal `commit: false` is not sufficient.

## Removal gate

`Schlieren.UI` is out of `Schlieren.sln`. Keep the source directory frozen until a dedicated retirement commit deletes it.

Compile-time Avalonia packages may remain in `Directory.Packages.props` only because the frozen project still exists on disk. No in-solution project may reference `Schlieren.UI`.

## Detach classification (2026-08-30)

| Reference | Classification |
|---|---|
| Avalonia VM/Harvest/reset/open-workbench tests | deleted |
| Zero-address vs CREATE, AA→BB, Osaka P-256 | relocated to journal RPC tests |
| Fixture parse / audit markdown / hex nonce | relocated to React tests |
| Conformance load progress | kept on EELS harness (no UI) |
| `UiBytecodeSmoke` / `UiVmSmoke` | replaced with in-process RPC/OpSec smoke |
| `Schlieren.Tests` project reference | removed |
| `Schlieren.sln` project entry | removed; source directory frozen |
