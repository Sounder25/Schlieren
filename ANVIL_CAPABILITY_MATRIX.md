# Anvil Capability Matrix (Scrutor vs Anvil)

This file tracks parity progress against core Anvil developer-node capabilities.

## Legend

- `Implemented`: available and executable in current Scrutor RPC.
- `Partial`: present but behavior or edge-case parity is incomplete.
- `Missing`: not available yet.

## JSON-RPC Capability Parity

| Category | Method / Behavior | Status | Notes |
|---|---|---|---|
| Core Ethereum RPC | `eth_chainId` | Implemented | |
| Core Ethereum RPC | `eth_blockNumber` | Implemented | |
| Core Ethereum RPC | `eth_getBalance` | Implemented | |
| Core Ethereum RPC | `eth_getCode` | Implemented | |
| Core Ethereum RPC | `eth_getStorageAt` | Implemented | |
| Core Ethereum RPC | `eth_accounts` | Implemented | |
| Core Ethereum RPC | `eth_sendRawTransaction` | Partial | typed envelope decode support + ingress signature validation/recovery added; full typed signing-hash parity remains |
| Core Ethereum RPC | `eth_sendTransaction` | Partial | supports impersonated + managed local unlocked accounts, EIP-1559 fee-field validation/defaulting, and deterministic unsigned hash derivation |
| Core Ethereum RPC | `eth_getTransactionCount` | Implemented | includes `pending` nonce path |
| Core Ethereum RPC | `eth_call` | Partial | hardened call-object validation and block-tag semantics (`latest`/`pending`/`earliest`/hex up to head); historical-state overrides remain |
| Core Ethereum RPC | `eth_getBlockByNumber` | Partial | Ethereum-shaped block response implemented with tx hash/full-tx toggle; advanced historical nuances still in progress |
| Core Ethereum RPC | `eth_getTransactionByHash` | Implemented | |
| Core Ethereum RPC | `eth_getTransactionReceipt` | Implemented | |
| Core Ethereum RPC | `eth_getLogs` | Partial | added `blockHash` semantics + range-conflict validation + strict address filter validation; deeper parity validation remains |
| Dev State Control | `anvil_setBalance` | Implemented | |
| Dev State Control | `anvil_setNonce` | Implemented | |
| Dev State Control | `anvil_setCode` | Implemented | |
| Dev State Control | `anvil_setStorageAt` | Implemented | |
| Mining Control | `anvil_mine` / `evm_mine` | Implemented | |
| Mining Control | `anvil_getAutomine` | Implemented | added in Slice 1 |
| Mining Control | `anvil_setAutomine` / `evm_setAutomine` | Implemented | added in Slice 1 |
| Mining Control | Interval mining (`--block-time`) | Implemented | scheduler now enforces block-time cadence before mining pending txs |
| Time Control | `evm_increaseTime` | Implemented | |
| Time Control | `anvil_setNextBlockTimestamp` / `evm_setNextBlockTimestamp` | Implemented | |
| Snapshots | `evm_snapshot` / `evm_revert` | Implemented | |
| Impersonation | `anvil_impersonateAccount` / `evm_impersonateAccount` | Implemented | |
| Impersonation | `anvil_stopImpersonatingAccount` / `evm_stopImpersonatingAccount` | Implemented | |
| Account Introspection | `anvil_showPrivateKey` | Implemented | |
| Account Introspection | `anvil_showMnemonic` | Implemented | |
| Network RPC | `net_version`, `net_listening`, `net_peerCount` | Implemented | added in Slice 2 |
| Client RPC | `web3_clientVersion` | Implemented | added in Slice 2 |
| Debug/Tracing | `debug_traceTransaction` | Partial | dynamic replay trace with nested call-depth/storage snapshots and trace options (`disableStack`/`disableMemory`/`disableStorage`/`limit`) |
| Debug/Tracing | `debug_traceCall` | Partial | dynamic trace with options support and block-selector validation path alignment (`latest`/`pending`/`earliest`/hex head-bound) |
| Debug/Tracing | `debug_traceBlockByNumber`, `debug_traceBlockByHash` | Partial | tx-by-tx replay traces with nested depth propagation and trace options support |
| Fee/Estimate | `eth_estimateGas` | Partial | intrinsic-gas floor, capped binary-search bounds, and optional block-tag parameter handling implemented; final parity pass pending |
| Fee/Estimate | `eth_gasPrice` | Implemented | added in Slice 4 |
| Fee/Estimate | `eth_feeHistory` | Partial | EIP-1559-style output with head-bound newestBlock validation, clamped genesis windowing, and percentile reward calculations |
| Novel Debug Capability | `debug_whyNot` | Implemented | counterfactual failure classifier with reason/evidence/recommendation output |

## Slice Execution Log

### Slice 1: Automine Parity Controls

- Added `anvil_getAutomine`.
- Added `anvil_setAutomine`.
- Added `evm_setAutomine` alias.
- Wired mining loop to honor `IChainState.Automine`.
- Added RPC and mining-loop tests.

### Slice 2: Network + Client RPC and Transaction Tracing

- Added `net_version`, `net_listening`, `net_peerCount`.
- Added `web3_clientVersion`.
- Added `debug_traceTransaction` endpoint.
- Added permanent tests for all new methods and trace error-path handling.

### Slice 3: Gas Estimation

- Added `eth_estimateGas` endpoint.
- Implemented bounded binary-search gas estimation using non-committing state transition execution.
- Added permanent tests for success path, failure-at-cap path, and router integration.

### Slice 4: Gas Price + Fee History

- Added `eth_gasPrice` endpoint (config-driven with base-fee floor).
- Added `eth_feeHistory` endpoint with:
  - block-window selection by tags/hex block,
  - `baseFeePerGas` (N+1 values),
  - `gasUsedRatio`,
  - optional `reward` via weighted percentile tips.
- Added permanent tests for happy path and validation errors.

### Slice 5: Tracing Depth and Trace RPC Surface

- Upgraded trace collection in EVM runtime to capture per-opcode trace steps.
- Added `debug_traceCall`.
- Added `debug_traceBlockByNumber` and `debug_traceBlockByHash`.
- Upgraded `debug_traceTransaction` to use dynamic replay traces.
- Added permanent tests for all new tracing methods.

### Slice 6: Trace Fidelity (Depth + Storage Deltas)

- Propagated call depth across nested internal calls and surfaced it in `structLogs.depth`.
- Enabled nested subcall trace merge for `CALL` / `CALLCODE` / `DELEGATECALL` / `STATICCALL` / `CREATE` / `CREATE2`.
- Added storage snapshots in `structLogs.storage` sourced from `SLOAD` and `SSTORE` activity.
- Added permanent tests validating nested depth (`depth == 2`) and `SSTORE` storage delta visibility.

### Slice 7: Mining Cadence (Interval vs Instant)

- Added explicit chain-state `BlockTimeSeconds` to drive mining mode selection.
- Implemented interval mining cadence in background miner loop while preserving instant automine behavior.
- Hardened CLI parsing so `--block-time` sets interval automine semantics and no longer collides with balance parsing.
- Added permanent tests for manual toggle behavior, interval cadence, and instant automine fast path.

### Slice 8: Typed Transaction Decode Parity

- Added typed transaction decoding support in `Transaction.FromRaw` for envelopes `0x01` (EIP-2930), `0x02` (EIP-1559), and `0x03` (EIP-4844-style envelope).
- Preserved strict rejection for unsupported typed envelopes (e.g., `0x04`) with explicit error messaging.
- Added permanent decode tests for type 1/2/3 and updated RPC error-path test for unknown typed transaction handling.

### Slice 9: `eth_sendTransaction` Parity Hardening

- Expanded sender authorization to accept both impersonated accounts and managed local unlocked accounts.
- Added stricter request validation for addresses, calldata payloads, and mixed fee-model input (`gasPrice` vs EIP-1559 fields).
- Improved unsigned transaction hash derivation input set for deterministic mempool identity.
- Added permanent tests for managed-account send path and fee-field conflict validation.

### Slice 10: `eth_call` Parameter and Block-Tag Hardening

- Added stricter `eth_call` call-object validation (including `from` address checks).
- Implemented explicit block-tag handling for `latest`, `pending`, `earliest`, and hex block numbers up to current head.
- Added deterministic invalid-parameter errors for unsupported tags and future block numbers.
- Added permanent RPC tests covering happy-path call execution and validation failures.

### Slice 11: `eth_getBlockByNumber` Response-Shape Parity

- Replaced raw internal block return with Ethereum-style JSON-RPC block response shape.
- Added support for transaction hash list vs full transaction objects via the second `fullTransactions` parameter.
- Hardened input handling for block tags/hex quantity and deterministic invalid-tag errors.
- Added permanent tests for hash-mode, full-transaction mode, null-on-missing-block, and invalid-tag rejection.

### Slice 12: `eth_getLogs` Edge-Case Parity Hardening

- Implemented `blockHash`-scoped log querying with explicit conflict rejection when mixed with `fromBlock`/`toBlock`.
- Added strict validation for address filter values (single and array forms).
- Preserved deterministic bounded-range controls and stable ordering guarantees.
- Added permanent tests for `blockHash` semantics, conflict errors, invalid address filters, and empty-address-array behavior.

### Slice 13: `debug_trace*` Options and Fidelity Controls

- Added trace options parsing for `debug_traceTransaction`, `debug_traceCall`, `debug_traceBlockByNumber`, and `debug_traceBlockByHash`.
- Implemented support for `disableStack`, `disableMemory`, `disableStorage`, and `limit` in trace response shaping.
- Added permanent tests proving option behavior (field suppression + step limiting) for transaction and block trace endpoints.

### Slice 14: `eth_estimateGas` Math Tightening

- Added intrinsic-gas floor computation in estimation bounds (tx base gas + calldata byte costs + contract creation surcharge).
- Tightened binary-search bounds to `[intrinsicGas, cappedUpperBound]` for deterministic, monotonic estimation behavior.
- Added explicit failure path when provided gas cap is below intrinsic requirement.
- Added permanent tests for calldata intrinsic cost floor and contract-creation intrinsic floor.

### Slice 15: `eth_feeHistory` Edge and Window Semantics

- Added head-bound validation for `newestBlock` (future block requests now fail deterministically).
- Confirmed no-`rewardPercentiles` calls omit `reward` from result payload.
- Added window-clamping verification when requested `blockCount` extends before genesis.
- Added permanent tests for future newest-block rejection, reward omission behavior, and clamped output shape/lengths.

### Slice 16: Close-Out Hardening (`eth_sendTransaction` + `eth_estimateGas`)

- Added `eth_sendTransaction` EIP-1559 fee-model guardrails (`maxPriorityFeePerGas` requires `maxFeePerGas`, and cannot exceed it).
- Improved gas-price defaulting to respect base-fee floor when explicit fee fields are absent.
- Added `eth_estimateGas` optional block-tag parameter handling aligned with `eth_call` block-tag validation.
- Added permanent tests for EIP-1559 fee-field validation/acceptance and estimate-gas block-tag acceptance/rejection.

### Slice 17: Close-Out Hardening (`debug_trace*` + `eth_sendRawTransaction`)

- Added block-selector-aware execution context handling in `debug_traceCall` to align with `eth_call` block-tag validation behavior.
- Updated block trace replay to use the traced block context (number/timestamp/gas/base-fee) instead of always using head context.
- Hardened `eth_sendRawTransaction` ingress with raw-hex validation and signature recovery validation before mempool admission.
- Added permanent tests for future block-tag rejection in `debug_traceCall` and invalid-signature rejection in `eth_sendRawTransaction`.

### Slice 18: New Capability — `debug_whyNot` (Failure Diagnosis)

- Added new RPC endpoint `debug_whyNot` to classify why a transaction/call fails before execution commit.
- Supports both call-object analysis and known transaction-hash replay analysis.
- Returns structured reasons with evidence and actionable recommendations (nonce mismatch, insufficient funds, intrinsic gas too low, out-of-gas, revert, static-mode violation, generic execution errors).
- Added permanent tests for insufficient-funds classification, nonce-too-high classification, and success/no-blocker baseline.
