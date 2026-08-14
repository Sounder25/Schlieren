# Fork Gas-Schedule Audit

**Date:** 2026-08-03
**Purpose:** Inventory every hardcoded gas constant in the EVM, its fork of
introduction, and the pre-fork value. This is the checklist for making Schlieren
fork-aware (Frontier through Cancun) and for diagnosing the remaining
`test_all_opcodes` and `test_precompiles` fixture failures.

## Current architecture (why this audit exists)

The execution engine is hardwired to Cancun rules:

- `EvmMachine` uses one static opcode table (`EvmMachine.cs:11-17`); there is no
  per-fork opcode selection.
- `ExecutionContext` carries no fork identifier. `NodeConfiguration.Hardfork`
  (`NodeConfiguration.cs:23`) is parsed but never read by execution.
- Every opcode and precompile hardcodes its gas constants.
- The EELS harness `EELS_REQUIRED_FORK` only selects which fixture `post` map to
  compare (`EelsStateFixtureLoader.cs:148`); the EVM still runs Cancun rules.

Consequence: running Frontier/Berlin/etc. fixtures cannot pass by patching
opcodes one at a time. The fix is a fork parameter threaded into
`ExecutionContext` (derived from the block) plus a per-fork `GasSchedule` that
every opcode and precompile consults.

## Opcode gas schedule (current hardcoded values)

Legend: `Same` = unchanged Frontier→Cancun. Cost columns use `cold/warm` for
EIP-2929 opcodes.

| Opcode | Cost (current code) | Fork introduced at this value | Pre-fork value |
| :--- | :--- | :--- | :--- |
| ADD/SUB | 3 | Same | — |
| MUL | 5 | Same | — |
| DIV/SDIV/MOD/SMOD | 5 | Same | — |
| ADDMOD/MULMOD | 8 | Same | — |
| EXP | 10 + 50/byte | Spurious Dragon (EIP-160) | 10 + 10/byte |
| SIGNEXTEND | 5 | Same | — |
| LT/GT/SLT/SGT/EQ/ISZERO | 3 | Same | — |
| AND/OR/XOR/NOT/BYTE | 3 | Same | — |
| SHL/SHR/SAR | 3 | Constantinople | invalid |
| KECCAK256 | 30 + 6/word | Same | — |
| PUSH/DUP/SWAP | 3 | Same | — |
| JUMP/JUMPI | 8 / 10 | Same | — |
| JUMPDEST | 1 | Same | — |
| PC/GAS/MSIZE | 2 | Same | — |
| PUSH0 | 2 | Shanghai | invalid |
| MLOAD/MSTORE/MSTORE8 | 3 + expansion | Same | — |
| CALLDATALOAD/SIZE/CODECOPY/CODESIZE | 2–3 | Same | — |
| CALLDATACOPY | 3 + 3/word | Same | — |
| RETURNDATACOPY | 3 + 3/word | Byzantium | invalid |
| MCOPY | 3 + 3/word | Cancun | invalid |
| LOG0–LOG4 | 375 + 375/topic + 8/byte | Same | — |
| SLOAD | 2100/100 (EIP-2929) | Berlin | 50 → 200 → 800 |
| SSTORE | EIP-2200 + 2929 + 3529 | Istanbul/Berlin/London | 20000/5000 (Frontier) |
| BALANCE | 2600/100 | Berlin | 20 → 400 → 700 |
| EXTCODESIZE | 2600/100 | Berlin | 20 → 700 |
| EXTCODECOPY | 2600/100 | Berlin | 20 → 700 |
| EXTCODEHASH | 2600/100 | Berlin | 400 (Constantinople) |
| BLOCKHASH | 20 | Same | — |
| COINBASE/TIMESTAMP/NUMBER/GASLIMIT | 2 | Same | — |
| DIFFICULTY/PREVRANDAO | 2 | Same | — |
| CHAINID | 2 | Istanbul | invalid |
| SELFBALANCE | 5 | Istanbul | invalid |
| BASEFEE | 2 | London | invalid |
| BLOBBASEFEE | 2 | Cancun | invalid |
| BLOBHASH | 3 | Cancun | invalid |
| TLOAD/TSTORE | 100 | Cancun | invalid |
| CREATE | 32000 + (EIP-3860 word gas) | Shanghai adds word gas | 32000 |
| CREATE2 | 32000 + 6/word + word gas | Constantinople + Shanghai | 32000 + 6/word |
| CALL/CALLCODE | 2600/100 + 9000(value) + 25000(new acct) | Berlin (access) | 40 → 700 |
| DELEGATECALL/STATICCALL | 2600/100 | Berlin | 40 → 700 |
| RETURN/REVERT/STOP/INVALID | 0 | Same | — |
| SELFDESTRUCT | 5000 + 2600(cold) + 25000(new acct w/ balance) | Berlin/EIP-161 | 0 → 5000 |

Memory expansion formula `3w + w²/512` is unchanged across all forks.

### Files to touch for opcode fork-awareness

`Schlieren.Core/Opcodes/*.cs` — replace `const` costs with lookups from a
`GasSchedule` instance available on `ExecutionContext`. The EIP-2929 opcodes
(EXTCODESIZE/COPY/HASH, BALANCE, SLOAD, CALL family) and EXP are the highest
divergence points.

## Precompile gas schedule

| Address | Precompile | Cost (current code) | Fork introduced | Pre-fork value |
| :--- | :--- | :--- | :--- | :--- |
| 0x01 | ecrecover | 3000 | Same | — |
| 0x02 | sha256 | 60 + 12/word | Same | — |
| 0x03 | ripemd160 | 600 + 120/word | Same | — |
| 0x04 | identity | 15 + 3/word | Same | — |
| 0x05 | modexp | EIP-2565 formula | Berlin | EIP-198 (Byzantium) |
| 0x06 | bnadd | 150 | Istanbul (EIP-1108) | 500 |
| 0x07 | bnmul | 6000 | Istanbul (EIP-1108) | 40000 |
| 0x08 | bnpairing | 45000 + k·34000 | Istanbul (EIP-1108) | 100000 + k·80000 |
| 0x09 | blake2f | rounds | Istanbul (EIP-152) | invalid |
| 0x0A | kzg | 50000 | Cancun (EIP-4844) | invalid |

### Known 0x08 (bnpairing) defects — relevant to `test_precompiles`

1. **Malformed input gas:** `input.Length % 192 != 0` returns `(empty, 0 gas)`
   (`Precompiles.cs:269-270`). EIP-197 requires an OOG exceptional halt that
   consumes **all** gas.
2. **Fail-closed:** valid `k > 0` inputs always return 0 (`Precompiles.cs:284-286`),
   never the real pairing result. The change from empty-array to 32-byte zero is
   the correct *output format* for a failure, but the pairing is not computed.
3. **Fork pricing:** the formula is Istanbul+; Byzantium/Constantinople fixtures
   expect `100000 + k·80000`.

### Duplicate implementation hazard

`SystemOpcodes.cs:841-1137` contains `PrecompileExecutor`, a **second, partial
(0x01–0x05 only) precompile implementation**. It is not on the call path —
`OpcodeCall` (`SystemOpcodes.cs:240`) and `StateTransition`
(`StateTransition.cs:542-557`) dispatch through `Precompiles.cs`. It survives
only because `OperandOverflowTests.cs:230-241` invokes it via reflection.
Any precompile pricing work must treat `Precompiles.cs` as authoritative and
`PrecompileExecutor` as dead code to delete (with the reflection test moved to
`Precompiles.cs`).

## Intrinsic (transaction) gas — two legacy gaps

`IntrinsicGas.cs` hardcodes Berlin/Cancun values:

| Component | Current value | Fork introduced | Pre-fork value |
| :--- | :--- | :--- | :--- |
| Base | 21000 | Same | — |
| Create surcharge | 32000 | Same | — |
| Calldata zero byte | 4 | Same | — |
| Calldata non-zero byte | **16** | Istanbul (EIP-2028) | **68** |
| Access list address/key | 2400 / 1900 | Berlin (EIP-2930) | n/a |
| Initcode word | 2/word | Shanghai (EIP-3860) | n/a |

EIP-2028 (68→16 per non-zero calldata byte) is a common cause of legacy
transaction-gas mismatch and is easy to miss because it is a transaction-level
cost, not an opcode.

## Refund schedule

- SSTORE-clear refund: 4800 (EIP-3529, London). Pre-London: 15000.
- Restore-to-original refunds: 19900 / 2800 (EIP-2200, Istanbul).
- Refund cap: `gasUsed / 5` (EIP-3529). Pre-London: `gasUsed / 2`.
- `OpcodeSstore` (`StorageOpcodes.cs:45-155`) implements the full EIP-2200
  tri-state metering with EIP-3529 values and EIP-2929 cold surcharge.

## Known non-fork bugs to verify while fork work is in progress

1. **CREATE/CREATE2 skip memory-expansion gas for the initcode region.**
   `Memory.Load` auto-expands without charging (`EvmMemory.cs:21-29`), and neither
   CREATE (`SystemOpcodes.cs:37`) nor CREATE2 (`SystemOpcodes.cs:337`) calls
   `CalculateGasCost`/`Expand` first — unlike CALL (`SystemOpcodes.cs:182-184`).
   Genuine undercharge; not currently exercised by the Cancun corpus.
2. **EIP-3860 applied unconditionally** (intrinsic + CREATE). Correct for Cancun,
   wrong pre-Shanghai.
3. **BLOCKHASH returns 0 always** (`StateOpcodes.cs:184-209`) — no block-hash
   table. Correctness gap, not gas.
4. **`ExecutionContext.RefundGas` silently clamps** `GasUsed = 0` on over-refund
   (`ExecutionContext.cs:212-216`), masking refund-accounting bugs.
5. **`StateTransition` has two divergent transaction paths** —
   `ApplyTransactionAsync` (`StateTransition.cs:21`) and
   `ApplyTransactionWithFrameAsync` (`StateTransition.cs:323`). They already
   disagree on EIP-1559 price-cap refund handling.

## Suggested fork-aware refactor order

1. Add `Hardfork` (enum) to `ExecutionContext`, populated from block/chain config.
2. Introduce `GasSchedule` keyed by hardfork; thread it through opcodes that have
   fork-varying costs (EXP, EIP-2929 family, SLOAD/SSTORE, CALL family, precompiles).
3. Parameterize `IntrinsicGas` (EIP-2028, EIP-3860) and `Precompiles` (modexp,
   bnadd/bnmul/bnpairing) by fork.
4. Gate EIP-3860 and refund values by fork.
5. Extend the EELS harness to run the fixture matrix (frontier → cancun) as the
   verification gate.
