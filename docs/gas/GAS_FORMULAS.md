# Schlieren Gas Formulas

Reconciled **2026-08-15** against production under `Schlieren.Core/`.

This is the usable formula book: every protocol gas effect, the exact expression, when it applies, and what kind of movement it is. Rule IDs match `GAS_RULE_INVENTORY.md`. Fork cells and remaining defects live in `GAS_COVERAGE_MATRIX.md`.

**Notation**

- `words(n) = ceil(n / 32)`
- `memory_cost(w) = 3w + floor(w² / 512)`
- `Δmem(newBytes) = memory_cost(words(newBytes)) − memory_cost(words(oldBytes))`
- Warm/cold exists only Berlin+ (EIP-2929) unless a row says otherwise
- Movements: **Charge** (parent pays), **TransferOut** (reserved for child), **TransferIn** (stipend, not charged to parent), **Return** (unused child gas), **Burn** (frame allocation consumed), **RefundCounterDelta**, **Settlement**, **Validation**

Where **Current** differs from **Protocol**, the current engine formula is listed first and the protocol requirement is called out.

---

## 1. Transaction entry and intrinsic

| ID | Protocol formula | Fork | Movement |
|---|---|---|---|
| `TX.BASE` | `21000` | all | Charge |
| `TX.CREATE_SURCHARGE` | Protocol: `0` Frontier, `32000` Homestead+ when `tx.To == null`. **Current: `32000` on every fork.** | Homestead+ | Charge |
| `TX.CALLDATA_ZERO` | `4` per zero byte of `tx.Data` | all | Charge |
| `TX.CALLDATA_NONZERO` | `68` per nonzero byte Frontier–Constantinople; `16` Istanbul+ (EIP-2028) | Istanbul override | Charge |
| `TX.ACCESS_LIST_ADDRESS` | `2400` per access-list address | Berlin+ | Charge |
| `TX.ACCESS_LIST_STORAGE_KEY` | `1900` per access-list storage key | Berlin+ | Charge |
| `TX.INITCODE_WORD` | `2 * words(tx.Data.Length)` when `tx.To == null` | Shanghai+ | Charge |
| `TX.AUTHORIZATION_COST` | `25000` per type-4 authorization entry | Prague+ | Charge |
| `TX.AUTHORIZATION_REFUND` | `+12500` refund counter per valid authorization whose authority already exists | Prague+ | RefundCounterDelta |
| `TX.CALLDATA_FLOOR` | tokens = `1` per zero byte + `4` per nonzero; floor = `21000 + 10 * tokens`. Pre-exec: reject if `gasLimit < floor`. Post-refund: `used = max(used, floor)` | Prague+ | Validation + Charge |
| `TX.MAX_GAS_LIMIT` | reject if `tx.GasLimit > 2^24` (`16777216`); equality is valid. Internal txs skip. | Osaka+ | Validation |

Intrinsic total (canonical path, `IntrinsicGas.Compute`):

```
21000
+ (to == null ? 32000 : 0)                  // not Frontier-gated today
+ (to == null && Shanghai+ ? 2*words(data) : 0)
+ sum(byte == 0 ? zeroCost : nonzeroCost)
+ Berlin+ access-list 2400/address + 1900/key
+ Prague+ type-4: 25000 * authCount
```

---

## 2. Fixed opcode charges

These are constants. After activation they do not depend on operands. Inactive opcodes must burn the frame (`HALT.OPCODE_ACTIVATION`).

| ID | Opcode | Gas | Activation |
|---|---|---|---|
| `OP.STOP` | STOP `0x00` | 0 | all |
| `OP.JUMPDEST` | JUMPDEST `0x5B` | 1 | all |
| `OP.ADD` / `OP.SUB` | ADD `0x01` / SUB `0x03` | 3 | all |
| `OP.MUL` / `OP.DIV` / `OP.SDIV` / `OP.MOD` / `OP.SMOD` / `OP.SIGNEXTEND` | `0x02` / `0x04` / **`0x05`** / **`0x06`** / `0x07` / `0x0B` | 5 | all |
| `OP.ADDMOD` / `OP.MULMOD` | `0x08` / `0x09` | 8 | all |
| `OP.LT` `OP.GT` `OP.SLT` `OP.SGT` `OP.EQ` `OP.ISZERO` | `0x10`–`0x15` | 3 | all |
| `OP.AND` `OP.OR` `OP.XOR` `OP.NOT` `OP.BYTE` | `0x16`–`0x1A` | 3 | all |
| `OP.SHL` `OP.SHR` `OP.SAR` | `0x1B`–`0x1D` | 3 | Constantinople+ (local gate) |
| `OP.CLZ` | CLZ `0x1E` | 5 | Osaka+ (local gate) |
| `OP.POP` | POP `0x50` | 2 | all |
| `OP.PUSH0` | PUSH0 `0x5F` | 2 | Shanghai+ (local gate) |
| `OP.PUSH1_32` | PUSH1–32 `0x60`–`0x7F` | 3 | all |
| `OP.DUP1_16` | DUP1–16 `0x80`–`0x8F` | 3 | all |
| `OP.SWAP1_16` | SWAP1–16 `0x90`–`0x9F` | 3 | all |
| `OP.ADDRESS` `OP.ORIGIN` `OP.CALLER` `OP.CALLVALUE` `OP.CALLDATASIZE` `OP.CODESIZE` `OP.GASPRICE` | `0x30` `0x32` `0x33` `0x34` `0x36` `0x38` `0x3A` | 2 | all |
| `OP.CALLDATALOAD` | CALLDATALOAD `0x35` | 3 | all |
| `OP.RETURNDATASIZE` | RETURNDATASIZE `0x3D` | 2 | Byzantium+ (**no local gate**) |
| `OP.BLOBHASH` | BLOBHASH `0x49` | 3 | Cancun+ (local gate) |
| `OP.GAS` | GAS `0x5A` | 2; pushed value is remaining **after** this 2 | all |
| `OP.BLOCKHASH` | BLOCKHASH `0x40` | 20 | all |
| `OP.COINBASE` `OP.TIMESTAMP` `OP.NUMBER` `OP.DIFFICULTY` `OP.GASLIMIT` | `0x41`–`0x45` | 2 | all (`0x44` returns prevrandao Paris+) |
| `OP.CHAINID` | CHAINID `0x46` | 2 | Istanbul+ (**no local gate**) |
| `OP.SELFBALANCE` | SELFBALANCE `0x47` | 5 | Istanbul+ (local gate) |
| `OP.BASEFEE` | BASEFEE `0x48` | 2 | London+ (local gate) |
| `OP.BLOBBASEFEE` | BLOBBASEFEE `0x4A` | 2 | Cancun+ (local gate) |
| `OP.JUMP` | JUMP `0x56` | 8 | all |
| `OP.JUMPI` | JUMPI `0x57` | 10 (taken or not) | all |
| `OP.PC` | PC `0x58` | 2 | all |
| `OP.MSIZE` | MSIZE `0x59` | 2 | all |

---

## 3. Dynamic opcode, memory, copy, hash, log

| ID | Formula | Fork | Movement |
|---|---|---|---|
| `MEMORY.EXPANSION` | `Δmem(requestedEnd)`. Host: signed-int clamp + **16 MiB** hard cap (non-protocol OOG). | all | Charge / host halt |
| `OP.EXP` | Protocol: `10 + 10*byte_count(exp)` Frontier–Tangerine; `10 + 50*byte_count(exp)` Spurious Dragon+ (EIP-160). `byte_count(0)=0`, else `ceil(bitlen/8)`. **Current: always `10 + 50*bytes`.** | EIP-160 | Charge |
| `OP.MLOAD` / `OP.MSTORE` | `3 + Δmem(offset+32)` | all | Charge |
| `OP.MSTORE8` | `3 + Δmem(offset+1)` | all | Charge |
| `OP.MCOPY` | `length==0 → 3`; else `3 + 3*words(length) + Δmem(max(dst+len, src+len))` | Cancun+ | Charge |
| `OP.CALLDATACOPY` / `OP.CODECOPY` / `OP.RETURNDATACOPY` | `3 + 3*words(length) + Δmem(dest+length)` | RETURNDATACOPY Byzantium+ | Charge |
| `OP.EXTCODECOPY` | `ExtAccountCost(warm) + 3*words(length) + Δmem(dest+length)` | see access table | Charge |
| `OP.KECCAK256` | `30 + 6*words(length) + Δmem(offset+length)` | all | Charge |
| `OP.LOG0`–`OP.LOG4` | `375 + 375*topicCount + 8*dataLength + Δmem(offset+length)`; static context must halt | all | Charge |
| `OP.RETURN` | `Δmem(offset+length)` (base 0) | all | Charge |
| `OP.REVERT` | `Δmem(offset+length)` (base 0); unused frame gas **returns** | Byzantium+ (**no local gate**) | Charge + Return |

---

## 4. Account and storage access

| ID | Formula | Fork | Movement |
|---|---|---|---|
| `ACCESS.INITIAL_WARM_SET` | Warm, no charge: sender; `to` or CREATE address; active precompiles (`1..PrecompileCount`); Osaka+ `0x0100`; access-list addresses/keys; **Shanghai+** coinbase (EIP-3651). | Berlin+ / Shanghai coinbase / Osaka P256 | Access mutation |
| `ACCESS.EIP7702_AUTHORITY_WARM` | After chain-id / max-nonce / signature, warm `auth.Signer` even if later nonce/code checks fail | Prague+ | Access mutation |
| `ACCESS.BALANCE` | Protocol BALANCE: F–H `20`; Tangerine–Constantinople **`400`**; Istanbul `700`; Berlin+ warm `100` / cold `2600`. **Current: Tangerine–Istanbul share `ExtAccountCost` = 700, so Tangerine–Constantinople overcharge +300.** | see fork | Charge |
| `ACCESS.EXTCODESIZE` | F–H `20`; Tangerine–Istanbul `700`; Berlin+ `100`/`2600` | | Charge |
| `ACCESS.EXTCODEHASH` | Inactive pre-Constantinople (**no local gate**). Constantinople `400`; Istanbul `700`; Berlin+ `100`/`2600` | Constantinople+ | Charge |
| `ACCESS.SLOAD` | F–H `50`; Tangerine–Constantinople `200`; Istanbul `800`; Berlin+ warm `100` / cold `2100` | | Charge |
| `ACCESS.TLOAD` | `100` | Cancun+ (local gate) | Charge |

`ExtAccountCost` / `SloadCost` / `ExtCodeHashCost` live on `IForkRules`.

---

## 5. SSTORE

Composition (Berlin+): `total = (cold ? 2100 : 0) + base(original, current, new)`.

`SSTORE.REENTRANCY_GUARD` (Istanbul+): if `gas_left ≤ 2300` before work, OOG-burn the frame.

### `SSTORE.FORMULA_FRONTIER` (Frontier–Constantinople)

```
cost   = (new != 0 && current == 0) ? 20000 : 5000
refund = (new == 0 && current != 0) ? +15000 : 0
```

### `SSTORE.FORMULA_ISTANBUL` (Istanbul only)

`SET=20000`, `RESET=5000`, `NOOP=800`, `CLEAR=15000`.

- `current == new` → `(800, 0)`
- clean (`original == current`):
  - `original == 0` → `(20000, 0)`
  - `new == 0` → `(5000, +15000)`
  - else → `(5000, 0)`
- dirty: base `800`; if `original != 0`, `−15000` when `current == 0`, `+15000` when `new == 0`; if `new == original`, add `19200` (`original==0`) or `4200`

### `SSTORE.FORMULA_BERLIN` (Berlin only)

Same branches. `SET=20000`, `RESET=2900`, `NOOP=100`, `CLEAR=15000`. Dirty restore-to-original: `+19900` (`original==0`) or `+2800`. Caller adds cold `2100`.

### `SSTORE.FORMULA_LONDON` (London–Osaka)

Berlin charges. `CLEAR=4800` (EIP-3529). Restore-to-original additions unchanged.

`SSTORE.TSTORE`: `100`, no refund. Cancun+ (local gate). Static context fails first.

---

## 6. CALL-family movements

Do not collapse these into one opcode charge.

| ID | Formula | Fork | Movement |
|---|---|---|---|
| `CALL.MEMORY_EXPANSION` | `Δmem(max(argsOff+argsLen, retOff+retLen))` | all | Charge |
| `CALL.DEPTH_LIMIT` | fail push `0` if child would exceed 1024; no TransferOut. **Current CALL uses `depth > 1024` vs CREATE `depth >= 1024`.** | all | Validation |
| `CALL.ACCESS_COST` | F–H `40`; Tangerine–Istanbul `700`; Berlin+ `CallBaseCost=0` so warm `100` / cold `2600` | | Charge |
| `CALL.EIP7702_DELEGATION_ACCESS` | if code is `0xEF0100 ‖ delegate`, add `ExtAccountCost(delegateWarm)` | Prague+ | Charge |
| `CALL.VALUE_TRANSFER_COST` | `value > 0 ? 9000 : 0` (CALL/CALLCODE only) | all | Charge |
| `CALL.NEW_ACCOUNT_COST` | Protocol: F–Tangerine `25000` if target nonexistent (incl. zero value); Spurious+ `25000` if `value>0` and target empty/dead. **Current: Spurious predicate on Tangerine too.** | | Charge |
| `CALL.PRE_EIP150_CHARGE` | `parentCost = 40 + requested + valueCost + newAccount`; child gets `requested + stipend`; no 63/64 | Frontier–Homestead | Charge + TransferOut |
| `CALL.EIP150_FORWARDING` | `avail = max(remaining − extras, 0)`; `maxForward = avail − floor(avail/64)`; `forwarded = min(requested, maxForward)`; parent consumes `extras + forwarded` | Tangerine+ | Charge + TransferOut |
| `CALL.STIPEND_GRANT` | `value > 0 ? 2300 : 0` added to child limit, **not** charged to parent | CALL/CALLCODE | TransferIn |
| `CALL.INSUF_BALANCE_EARLY_EXIT` | return `forwarded + stipend` (or legacy `requested + stipend`); extras stay charged | | Return |
| `CALL.CHILD_GAS_RETURN` | `return = childLimit − min(childUsed, childLimit)` | | Return |
| `CALL.EXCEPTIONAL_CHILD_BURN` | non-REVERT child failure: `childUsed = childLimit` | | Burn |
| `CALL.REFUND_COUNTER_PROPAGATION` | add child refund counter only if child succeeded | | RefundCounterDelta |
| `STATICCALL.FORWARDING` | access first, then 63/64 of remaining | Byzantium+ (**no local gate**) | TransferOut |
| `DELEGATECALL.FORWARDING` | Protocol: invalid Frontier; Homestead exact requested; Tangerine+ 63/64. **Current: always 63/64, all forks.** | Homestead+ | TransferOut |
| `CALL.PRECOMPILE_DISPATCH` | if target is active precompile, run in child budget; unused returns | fork-dependent set | Charge in child |

---

## 7. CREATE-family

| ID | Formula | Fork | Movement |
|---|---|---|---|
| `CREATE.BASE` | `32000` + Shanghai+ `2*words(initcode)` | all / Shanghai words | Charge |
| `CREATE2.BASE` | `32000 + 6*words(initcode)` + Shanghai+ `2*words`. **No `HasCreate2` dispatch gate.** | Constantinople+ | Charge |
| `CREATE.INITCODE_SIZE_LIMIT` | OOG if `length > 2*24576` | Shanghai+ (gated) | Halt |
| `CREATE.MEMORY_EXPANSION` | `Δmem(offset+length)` before load | all (now charged) | Charge |
| `CREATE.EIP150_FORWARDING` | Protocol: F–H all remaining; Tangerine+ `remaining − floor(remaining/64)`. **Current CREATE always 63/64.** | Tangerine+ | TransferOut |
| `CREATE.PRE_CHECK_NO_TRANSFER` | if `balance < value` or nonce `u64::MAX` or `depth >= 1024`: push 0, do not consume forwarded gas | all | no TransferOut |
| `CREATE.WARMING` | `WarmAddress(new)` after pre-check | Berlin+ relevant | Access mutation |
| `CREATE.COLLISION_BURN` | if dest nonce≠0 or code nonempty or storage nonempty: no return of forwarded gas. **Current: `Unknown` remote storage is treated deployable.** | EIP-7610 | Burn |
| `CREATE.CODE_DEPOSIT` | `200 * runtime.length` from child remaining | all | Charge (child) |
| `CREATE.CODE_SIZE_LIMIT` | fail if runtime `> 24576` (EIP-170). Opcode and top-level both check. **No Spurious gate: Frontier–Tangerine also reject.** | Spurious+ | Halt |
| `CREATE.DEPOSIT_OOG` | Protocol: Frontier succeeds with empty code if unaffordable; Homestead+ exceptional fail. **Current: always halt; overlay discarded; created account reverted; warmth kept.** | Homestead+ | Burn |
| `CREATE.EF_PREFIX_BURN` | London+ reject runtime starting `0xEF`; consume child allocation | London+ | Burn |
| `CREATE.CHILD_GAS_RETURN` | unused init gas after deposit | | Return |
| `CREATE.REFUND_COUNTER_PROPAGATION` | only after successful deposit | | RefundCounterDelta |
| `CREATE.TOP_LEVEL_DEPOSIT` | same deposit/size/prefix rules on `tx.To == null` | same as opcode | Charge / halt |

---

## 8. SELFDESTRUCT

| ID | Formula | Fork | Movement |
|---|---|---|---|
| `SELFDESTRUCT.BASE` | `0` Frontier–Homestead; `5000` Tangerine+ | | Charge |
| `SELFDESTRUCT.NEW_ACCOUNT` | Tangerine: `25000` if beneficiary nonexistent (any balance). Spurious+: `25000` if originator balance > 0 and beneficiary empty. | Tangerine+ | Charge |
| `SELFDESTRUCT.COLD_ACCESS` | Berlin+ cold beneficiary `+2600` | Berlin+ | Charge |
| `SELFDESTRUCT.REFUND` | Protocol: `+24000` first SELFDESTRUCT of that account, Frontier–Berlin. **Current: never credited.** | Frontier–Berlin | RefundCounterDelta |

EIP-6780 (Cancun+) restricts *deletion*, not these gas rows.

---

## 9. Precompiles

Charged inside the applicable budget (top-level: `tx.GasLimit − intrinsic`; nested: forwarded child gas). `null` output → OOG the whole budget. Unused nested gas returns.

Active set: `0x01–0x04` Frontier; `+0x05–0x08` Byzantium; `+0x09` Istanbul; `+0x0A` Cancun; `+0x0B–0x11` Prague; `+0x0100` Osaka.

| ID | Address | Formula | Fork |
|---|---|---|---|
| `PRECOMPILE.ECRECOVER` | `0x01` | `3000` | Frontier+ |
| `PRECOMPILE.SHA256` | `0x02` | `60 + 12*words(len)` | Frontier+ |
| `PRECOMPILE.RIPEMD160` | `0x03` | `600 + 120*words(len)` | Frontier+ |
| `PRECOMPILE.IDENTITY` | `0x04` | `15 + 3*words(len)` | Frontier+ |
| `PRECOMPILE.MODEXP_EIP198` | `0x05` | `floor(multComplexity(maxLen) * iterationCount / 20)` no floor. Complexity: `x²` (`x≤64`); `x²/4 + 96x − 3072` (`x≤1024`); else `x²/16 + 480x − 199680`. **Current saturates at `10_000_000_000`.** | Byzantium–Istanbul |
| `PRECOMPILE.MODEXP_EIP2565` | `0x05` | `max(200, floor(words8(maxLen)² * iterationCount / 3))`; extra exp bytes ×8. **Same 10B saturate.** | Berlin–Prague |
| `PRECOMPILE.MODEXP_EIP7883` | `0x05` | `max(500, complexity * iterationCount)`; complexity `16` if `maxLen≤32` else `2*words8(maxLen)²`; extra exp bytes ×16. **Same 10B saturate.** | Osaka+ |
| `PRECOMPILE.MODEXP_LENGTH_LIMIT` | `0x05` | Protocol unbounded pre-Osaka; Osaka+ each length ≤ `1024` (EIP-7823). **Current also host-caps pre-Osaka at 8192.** | Osaka protocol |
| `PRECOMPILE.BN254_ADD` | `0x06` | `500` Byzantium–Constantinople; `150` Istanbul+ | |
| `PRECOMPILE.BN254_MUL` | `0x07` | `40000` / `6000` | |
| `PRECOMPILE.BN254_PAIRING` | `0x08` | `100000 + 80000*k` / `45000 + 34000*k`, `k = len/192` | |
| `PRECOMPILE.BLAKE2F` | `0x09` | gas = BE `rounds` (4 bytes); input 213 bytes | Istanbul+ |
| `PRECOMPILE.KZG_POINT_EVAL` | `0x0A` | `50000`; input 192 bytes | Cancun+ |
| `PRECOMPILE.BLS_G1ADD` | `0x0B` | `375` | Prague+ |
| `PRECOMPILE.BLS_G1MSM` | `0x0C` | `floor(k*12000*G1Discount[min(k,128)−1]/1000)`, `k=len/160` | Prague+ |
| `PRECOMPILE.BLS_G2ADD` | `0x0D` | `600` | Prague+ |
| `PRECOMPILE.BLS_G2MSM` | `0x0E` | `floor(k*22500*G2Discount[min(k,128)−1]/1000)`, `k=len/288` | Prague+ |
| `PRECOMPILE.BLS_PAIRING` | `0x0F` | `37700 + 32600*k`, `k=len/384` | Prague+ |
| `PRECOMPILE.BLS_MAP_FP_G1` | `0x10` | `5500` | Prague+ |
| `PRECOMPILE.BLS_MAP_FP2_G2` | `0x11` | `23800` | Prague+ |
| `PRECOMPILE.P256VERIFY` | `0x0100` | `6900` (valid or invalid input); short budget is OOG | Osaka+ |

---

## 10. Halt, refund cap, settlement

| ID | Formula | Fork | Movement |
|---|---|---|---|
| `HALT.OPCODE_ACTIVATION` | inactive opcode byte → invalid opcode, `GasUsed = frame.GasLimit` | per opcode table | Burn |
| `HALT.OOG_BURN` | OOG: `GasUsed = frame.GasLimit` | all | Burn |
| `HALT.REVERT_RETURN` | REVERT: `GasUsed = gas consumed so far`; unused returns | Byzantium+ | Return |
| `HALT.EXCEPTIONAL_BURN` | any non-REVERT failure: `GasUsed = frame.GasLimit` | all | Burn |
| `HALT.EVM_GAS_CAP` | `evmUsed = min(result.GasUsed, executionGasLimit)` | all | Settlement |
| `SETTLE.REFUND_CAP` | `capped = min(counter, floor(used / RefundQuotient))`; `used -= capped`. Quotient `2` Frontier–Berlin (50%); `5` London+ (20%) | | Refund |
| `SETTLE.CALLDATA_FLOOR_POST` | `used = max(used, TX.CALLDATA_FLOOR)` | Prague+ | Charge |
| `SETTLE.EFFECTIVE_GAS_PRICE` | type 0/1: `gasPrice`. type 2/3/4: `min(maxFee, baseFee + maxPriority)` | London+ dynamic | input |
| `SETTLE.SENDER_REFUND` | upfront `gasLimit * effective`; return `(gasLimit − used) * effective`; failed exec also restores `tx.Value` | all | Settlement |
| `SETTLE.COINBASE_CREDIT` | `priority = max(effective − baseFee, 0)`; `miner = used * priority`. Pre-London `baseFee=0` so full fee. | | Settlement |
| `SETTLE.BLOB_FEE` | `blobGas = 131072 * blobCount`; `blobBaseFee = FakeExponential(1, excessBlobGas, 3338477)`; `fee = blobGas * blobBaseFee` (not refundable) | Cancun+ type 3 | Settlement |

---

## 11. Host policies (not protocol formulas)

Named so diagnosis does not treat them as Yellow Paper gas.

| Policy | Effect |
|---|---|
| 16 MiB EVM memory | `EvmMemory` OOG regardless of remaining protocol gas |
| Pre-Osaka ModExp 8192-byte length cap | host reject; protocol has no cap |
| ModExp `10_000_000_000` saturate | undercharges huge accepted inputs |
| `Unknown` remote storage deployable | CREATE2 can skip EIP-7610 collision burn |

---

## 12. Still open vs protocol (canonical path, 2026-08-15)

Closed since the `806dd2d` discovery (do not re-fix):

- SDIV=`0x05`, MOD=`0x06`
- CREATE/CREATE2 charge `Δmem` and gate EIP-3860 word gas + size
- LOG* reject static context
- Coinbase pre-warm only Shanghai+; Osaka warms `0x0100`
- Local activation: SHL/SHR/SAR, CLZ, PUSH0, SELFBALANCE, BASEFEE, BLOBBASEFEE, BLOBHASH, MCOPY, TLOAD/TSTORE
- Osaka tx gas cap on `ApplyTransactionAsync`; Osaka suite 14,516/14,516

Closed during 2026-08-15 session (commits `63977c2`–`5c7191e`):

- SELFDESTRUCT 24000 refund Frontier–Berlin (`HasSelfdestructRefund` flag, `LondonRules` = false)
- EIP-3651 coinbase pre-warm fork-gated to Shanghai+ (`HasEip3651WarmCoinbase`)
- EXTCODEHASH/CHAINID/CREATE2/RETURNDATASIZE/RETURNDATACOPY local activation gates
- BALANCE correct pricing: Frontier/Homestead=20, TangerineWhistle–Constantinople=400, Istanbul+=700 (`BalanceCost()`)
- CREATE/CREATE2 gas forwarding: Frontier/Homestead forward all remaining gas; TangerineWhistle+ 63/64
- SELFDESTRUCT self-send balance zeroing: pre-EIP-6780 always zeroes originator; Cancun+ only if same-tx

Still wrong or missing on the **canonical** path:

1. Frontier CREATE tx surcharge `32000` (should be 0)
2. EXP always 50/byte (should be 10/byte before Spurious Dragon)
3. No interpreter-wide activation: REVERT, STATICCALL, DELEGATECALL (CHAINID, RETURNDATA*, CREATE2, EXTCODEHASH now gated)
4. EIP-170 has no Spurious gate (too early on Frontier–Tangerine); unknown remote storage is deployable; CREATE deposit-OOG never uses Frontier empty-code success; warmth not un-warmed on failed create
5. Tangerine CALL new-account predicate; pre-EIP-150 CALL overflow
6. ModExp 10B saturate + pre-Osaka 8192 host cap
7. Diagnostic `ApplyTransactionWithFrameAsync` / gas-tree still a second lifecycle

## 13. Diagnostic IDs (not protocol charges)

These must consume the journal + this book. They must not keep a second constant list.

| ID | What it does today | Defect |
|---|---|---|
| `DIAG.BALANCE_TO_GAS` | `(actual−expected)/effectiveGasPrice` (truncating; 0-price → 1) | Coinbase divisor is priority fee; value/blob residuals are not gas |
| `DIAG.BALANCE_DIRECTION` | Core: `deltaGas>0` = undercharge. Bridge treats `deltaWei<0` as undercharge | Sign reversed in the bridge |
| `DIAG.KNOWN_CONSTANT_MATCH` | First exact match in a flat list; ×2–32 if constant ≥500 | Fork-blind, ambiguous (500, 5000) |
| `DIAG.FORK_CONTEXT` | Only `IsOsakaOrLater` / `IsPragueOrLater` plus folder names | Cannot exclude Frontier–Cancun impossibles |
| `DIAG.REFUND_CAP` | `floor(used/5)` always | Wrong on Frontier–Berlin (quotient 2) |
| `DIAG.GAS_TREE_INTRINSIC` | Calldata always `4*zero+16*nonzero` | Wrong pre-Istanbul; drops CREATE/AL/init/auth |
| `DIAG.GAS_TREE_EXECUTION` | `ApplyTransactionWithFrameAsync` second lifecycle | Omits 7825, 3860, auth, modern precompile warm, etc. |
| `DIAG.GAS_TREE_ACCESS_CLASS` | cold if opcode total ≥2100 | Warm SSTORE reset is 2900; cold no-op is 2200 |
| `DIAG.GAS_TREE_MEMORY_LEDGER` | copy base ≈3; unused mixed into consumption | Mislabel + no conservation |

Also named above but easy to miss: `SSTORE.COLD_SURCHARGE` = Berlin+ `+2100` on first cold slot, added to the era formula. `PRECOMPILE.DISPATCH_BUDGET` = top-level `gasLimit − intrinsic`, nested = forwarded child gas. `OP.LOG1`, `OP.LOG2`, and `OP.LOG3` use the LOG0–LOG4 formula with `topicCount` 1–3.
