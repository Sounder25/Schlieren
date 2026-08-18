# debug_inspect JSON contract (frozen)

Do not rename fields without updating Agent A tests, Agent B tests, and this file.

## Method

`debug_inspect`

Params: one object in the JSON-RPC `params` array.

```json
[{
  "from": "0x0000000000000000000000000000000000000001",
  "to": null,
  "data": "0x6000",
  "gas": "0x186a0",
  "value": "0x0",
  "gasPrice": "0xa",
  "fork": "Frontier",
  "mismatches": [
    "balance mismatch for 0xf6c3a9edc1afa0ad5b720e4d42e1437c43d3b3ff: expected=0xf4240, actual=0xa6040"
  ],
  "expectException": null,
  "expectedReceiptSuccess": true,
  "disableStack": false,
  "disableMemory": false,
  "disableStorage": false
}]
```

`mismatches` optional. Without it, diagnosis grade is at most `STRONG`.

## Result

See `2026-08-15-inspect-rpc-backend.md` Task 0 example.

Required top-level keys: `ok`, `fork`, `execution`, `trace`, `gasTree`, `diagnosis`.

`execution`: `success`, `error`, `gasUsed`, `gasLimit`, `refundCounter`, `returnValue`.

`trace.structLogs[]`: `pc`, `op`, `gas`, `gasCost`, `gasCostDec`, `depth`, `stack`, `memory`, `storage`, `contract`, `caller`, `callType`, `output`.

`gasTree`: `label`, `gas`, `totalGas`, `children` (same shape, recursive).

`diagnosis`: `fingerprint`, `firstPhase`, `root`, `candidates`.

`diagnosis.root` / `candidates[]`: `ruleId`, `title`, `grade` (`PROVEN`|`STRONG`|`POSSIBLE`), `score`, `phase`, `why`, `proof`, `consequences`, `likelyFix`, `codeBoundary`, `protocolRule`, `gasDelta`.

## Geth traces

`debug_traceTransaction` / `debug_traceCall` keep `gas`, `failed`, `returnValue`, `structLogs`.  
Additive only: `gasCostDec`, `contract`, `caller`, `callType`, `output`.
