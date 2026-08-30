# `schlieren_traceJournal`

`schlieren_traceJournal` executes one non-committing call through the canonical state-transition path and returns a typed, frame-aware execution journal. It is the data source for the React workbench. It does not replace or alter `debug_inspect` or `debug_traceCall`.

## Request

The method accepts exactly one **normalized execution-context object**. It does not parse EELS/state-test fixtures, Foundry traces, or raw 7702 signatures. Clients convert those formats before calling RPC.

Two encodings are accepted. Nested mode is **presence of the `transaction` property**, not a comparison of JSON element identity.

1. **Flat** (legacy workbench): transaction fields on the root object. `to` is required. Missing `to` is invalid params. `code` without `to` is invalid.
2. **Nested**: `transaction` object present (even `{}`). Omitted or JSON `null` `to` means CREATE.

Do not collapse these into one rule. Flat `to` required / nested CREATE is intentional backward compatibility.

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "schlieren_traceJournal",
  "params": [{
    "fork": "Osaka",
    "transaction": {
      "type": "0x2",
      "from": "0x0000000000000000000000000000000000000001",
      "to": "0x00000000000000000000000000000000000000aa",
      "nonce": "0x0",
      "gasLimit": "0x989680",
      "gasPrice": "0x3b9aca00",
      "maxFeePerGas": "0x3b9aca00",
      "maxPriorityFeePerGas": "0x1",
      "value": "0x0",
      "data": "0x",
      "accessList": [],
      "authorizationList": []
    },
    "preState": [],
    "blockContext": {},
    "options": {
      "disableStack": false,
      "disableMemory": false,
      "disableStorage": false
    }
  }]
}
```

Flat form remains valid:

```json
{
  "from": "0x0000000000000000000000000000000000000001",
  "to": "0x00000000000000000000000000000000000000aa",
  "gas": "0x989680",
  "gasPrice": "0x0",
  "value": "0x0",
  "data": "0x",
  "fork": "Osaka"
}
```

### Scalar encoding

| Kind | JSON type | Encoding |
|---|---|---|
| Addresses, fork labels, `code`, `data`, blob hashes | **string only** | Hex strings (`0x…`). JSON numbers are invalid. |
| Quantities (`gasLimit`, `nonce`, `value`, fees, balances, storage keys/values, block numbers) | **string only** | Unsigned Ethereum quantity: `0x`-hex or decimal digits. JSON numbers are invalid. Negative strings are invalid. |
| Flags | boolean | JSON `true` / `false` |

Limits:

- `gasLimit`, `gas`, `nonce`, `type`, block `number`/`timestamp`/`gasLimit`/`baseFee`/`chainId`/`excessBlobGas`, authorization `nonce`/`chainId`: **uint64** (`0` … `2^64-1`). Larger values return `-32602`.
- `value`, `gasPrice`, `maxFeePerGas`, `maxPriorityFeePerGas`, `maxFeePerBlobGas`, account `balance`, storage keys/values, `prevRandao`: **uint256** (`0` … `2^256-1`). Larger values return `-32602`.

Fixture-shaped arrays (`"data": ["0x"]`, `"gasLimit": ["0x1"]`) are invalid. Those belong in a client adapter.

### Field presence vs value

Omitted fee fields are not the same as `0x0`.

- Missing `maxFeePerGas` on a type-2+ transaction: inherit `gasPrice` (or `0` if that is also missing).
- `"maxFeePerGas": "0x0"`: explicit zero. It does **not** inherit `gasPrice`.
- Type inference uses **presence** of `maxFeePerGas` / `maxPriorityFeePerGas`, not whether the value is zero. Explicit `"maxFeePerGas": "0x0"` implies type 2 unless `type` is set.

Explicit `type` / `txType` (`0`–`4`) wins. Otherwise: authorization list → 4, blob hashes → 3, fee-field presence → 2, access list → 1, else 0.

### EIP-7702 normalized authorizations

`transaction.authorizationList[]` is **normalized semantics**, not a signed tuple. This method does not recover `signer` from `yParity`/`r`/`s`.

| Field | Meaning |
|---|---|
| `address` or `delegate` | Delegation target (code the authority will point at). Required when `valid` is true. |
| `signer` | Already-recovered authority EOA. Required when `valid` is true. |
| `nonce` | Authority nonce expected at processing time (uint64 quantity). |
| `chainId` | Scoping chain id; `0` means any chain. |
| `valid` | Caller-supplied validity. If omitted, true only when `signer` is a non-zero address. |

`yParity`/`r`/`s` without `signer` is `-32602` (“does not decode 7702 signatures”). `"valid": false` may omit `signer`.

### Isolation

`preState` is applied only to a per-call overlay. The call always runs with `commit: false`. `_globalState` is not written. When `preState` is non-empty the overlay parent is a fresh empty state so node accounts do not leak into the fixture.

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

The result has additive journal-derived sections:

```json
{
  "ok": true,
  "fork": "Osaka",
  "execution": { "success": true, "error": null, "gasUsed": 22106, "gasRefundCounter": 0, "returnData": "0x" },
  "events": [],
  "frames": [],
  "steps": [],
  "gasTree": {},
  "conservation": { "derivedGas": 22106, "settledGas": 22106, "delta": "0", "isConserved": true },
  "stateEffects": [],
  "securityFindings": [],
  "frameTree": null
}
```

- `events` is the stable typed journal projection, ordered by `sequence`.
- `frames` contains explicit `id`, `parentId`, depth, call type, addresses, gas limit, result, and remaining gas. A real nested CALL creates a child frame; its opcodes carry that child ID.
- `steps` contains opcode observations with frame IDs, PC/opcode, gas before/after/cost, call context, output, and enabled snapshots.
- `gasTree` is rebuilt exclusively from journal events. It never infers child gas by subtracting legacy inclusive traces.
- `conservation.delta` is signed decimal `derivedGas - settledGas`. `isConserved` is true only when it is zero.
- `stateEffects` contains analyzed typed effects with effect/instruction/frame identity and execution/persistence dispositions.
- `securityFindings` is the proof-linked finding collection. Each finding includes the server-assigned rule/category/severity/grade, primary frame and instruction, event sequences, complete frame ancestry, execution and persistence dispositions, affected addresses/slots, summary, and limitation.
- `frameTree` is the authoritative server-built hierarchy. Nodes contain ordered ancestors, direct effect/finding IDs, and recursive children. React never rebuilds ancestry from `frames`.

## Gas semantics

Only exclusive charges and exceptional burns add gas to the tree. Effective refunds are credits and subtract once. Inclusive frame deltas, forwarded allocations, unused returns, refund-counter changes, and settlement observations are evidence and do not add again.

CALL-family opcodes therefore expose distinct components:

- `call.local` — the caller-owned exclusive charge
- `call.forwarded` — non-additive gas allocation to the child
- `call.unused-return` — non-additive gas returned from the child

Other explicit components are `precompile.execution`, `create.code-deposit`, `create.exceptional-burn`, `transaction.calldata-floor`, and `transaction.collision-burn`. Exceptional frame burns are also emitted as their own event kind.

## EELS alignment

`JournalEelsAlignment.Project` maps journal steps to EIP-3155 fields: `pc`, `op`, `gas`, `gasCost`, `depth`, `stack`, `memory`, and `storage`. The comparer returns the first mismatching field plus journal `frameId`, sequence, PC, and opcode. The React Conformance view performs the same deterministic comparison against pasted EELS `structLogs`; the RPC server does not start or embed Python.

## Security evidence

Security analysis consumes validated `JournalAnalysis`, never a reconstructed depth stack. Reentrancy requires an explicit `CALL`/`CALLCODE` frame that re-enters an ancestor storage context plus a typed write. Delegate collision requires explicit `DELEGATECALL`/`CALLCODE` geometry, separate code/storage owners, and a typed write to slot zero or an EIP-1967 implementation/admin/beacon slot. Findings describe the observed path only; they do not claim universal exploitability.

## Errors and compatibility

Malformed addresses, quantities, byte strings, flags, the wrong parameter count, missing flat `to`, JSON numbers in string/quantity fields, negative quantities, uint64/uint256 overflow, fixture-shaped arrays, or raw 7702 signatures without `signer` return JSON-RPC invalid params (`-32602`). Existing `debug_inspect` and `debug_traceCall` serializers and response shapes are unchanged.
