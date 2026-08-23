# `schlieren_traceJournal`

`schlieren_traceJournal` executes one non-committing call through the canonical state-transition path and returns a typed, frame-aware execution journal. It is the data source for the React workbench. It does not replace or alter `debug_inspect` or `debug_traceCall`.

## Request

The method accepts exactly one object:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "schlieren_traceJournal",
  "params": [{
    "from": "0x0000000000000000000000000000000000000001",
    "to": "0x00000000000000000000000000000000000000aa",
    "gas": "0x989680",
    "gasPrice": "0x0",
    "value": "0x0",
    "data": "0x",
    "fork": "Osaka"
  }]
}
```

`from`, `gas`, `gasPrice`, `value`, `data`, `fork`, and `nonce` are optional. `to` is required. Quantities use Ethereum hex quantity syntax.

### Ephemeral bytecode

Set optional `code` to execute pasted bytecode at `to` in a discarded state overlay:

```json
{"to":"0x00000000000000000000000000000000000000aa","code":"0x600160005500","gas":"0x186a0"}
```

The overlay is used only for this call. The account code and every execution state change are discarded afterward.

### Snapshot controls

Opcode steps include `stack`, `memory`, and `storage` by default. Callers that only need events or gas accounting may set any of these to `true`:

- `disableStack`
- `disableMemory`
- `disableStorage`

Disabled properties are omitted from step JSON rather than returned as `null`.

## Response

The result has seven journal-derived sections:

```json
{
  "ok": true,
  "fork": "Osaka",
  "execution": { "success": true, "error": null, "gasUsed": 22106, "gasRefundCounter": 0, "returnData": "0x" },
  "events": [],
  "frames": [],
  "steps": [],
  "gasTree": {},
  "conservation": { "derivedGas": 22106, "settledGas": 22106, "delta": "0", "isConserved": true }
}
```

- `events` is the stable typed journal projection, ordered by `sequence`.
- `frames` contains explicit `id`, `parentId`, depth, call type, addresses, gas limit, result, and remaining gas. A real nested CALL creates a child frame; its opcodes carry that child ID.
- `steps` contains opcode observations with frame IDs, PC/opcode, gas before/after/cost, call context, output, and enabled snapshots.
- `gasTree` is rebuilt exclusively from journal events. It never infers child gas by subtracting legacy inclusive traces.
- `conservation.delta` is signed decimal `derivedGas - settledGas`. `isConserved` is true only when it is zero.

## Gas semantics

Only exclusive charges and exceptional burns add gas to the tree. Effective refunds are credits and subtract once. Inclusive frame deltas, forwarded allocations, unused returns, refund-counter changes, and settlement observations are evidence and do not add again.

CALL-family opcodes therefore expose distinct components:

- `call.local` — the caller-owned exclusive charge
- `call.forwarded` — non-additive gas allocation to the child
- `call.unused-return` — non-additive gas returned from the child

Other explicit components are `precompile.execution`, `create.code-deposit`, `create.exceptional-burn`, `transaction.calldata-floor`, and `transaction.collision-burn`. Exceptional frame burns are also emitted as their own event kind.

## EELS alignment

`JournalEelsAlignment.Project` maps journal steps to EIP-3155 fields: `pc`, `op`, `gas`, `gasCost`, `depth`, `stack`, `memory`, and `storage`. The comparer returns the first mismatching field plus journal `frameId`, sequence, PC, and opcode. The React Conformance view performs the same deterministic comparison against pasted EELS `structLogs`; the RPC server does not start or embed Python.

## Errors and compatibility

Malformed addresses, quantities, byte strings, flags, unknown forks, the wrong parameter count, or missing `to` return JSON-RPC invalid params (`-32602`). Existing `debug_inspect` and `debug_traceCall` serializers and response shapes are unchanged.
