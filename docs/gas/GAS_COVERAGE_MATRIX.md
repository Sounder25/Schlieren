# Scrutor Per-Fork Gas Coverage Matrix

## Legend

- `D` — defined directly for this fork
- `I` — inherited unchanged from the previous fork
- `O` — overridden by this fork
- `N/A` — operation or feature is inactive on this fork
- `S` — implemented through scattered production logic rather than the fork schedule
- `M` — missing or not demonstrably represented

`M` on a pre-activation fork means the required invalid-opcode or validation behavior is missing, not that the later opcode's price should be active. Diagnostic rows are marked `S` because they are hand-maintained outside the protocol schedule and currently run for every fork.

## Coverage Matrix

| Rule ID | Category | Frontier | Homestead | TangerineWhistle | SpuriousDragon | Byzantium | Constantinople | Istanbul | Berlin | London | Paris | Shanghai | Cancun | Prague | Osaka | Evidence |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| TX.BASE | Transaction Entry/Intrinsic | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Execution/IntrinsicGas.cs:13,59` - constant `TxBase = 21_000`. |
| TX.CREATE_SURCHARGE | Transaction Entry/Intrinsic | M | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Execution/IntrinsicGas.cs:14,62-64` - hardcoded 32,000 whenever `tx.To == null`; the required Homestead activation gate is absent, so Frontier is overcharged. |
| TX.CALLDATA_ZERO | Transaction Entry/Intrinsic | D | I | I | I | I | I | I | I | I | I | I | I | I | I | `Scrutor.Core/Forks/ForkRules.cs:55`; `Scrutor.Core/Execution/IntrinsicGas.cs:71-72` - schedule property is defined as 4 and inherited unchanged. |
| TX.CALLDATA_NONZERO | Transaction Entry/Intrinsic | D | I | I | I | I | I | O | I | I | I | I | I | I | I | `Scrutor.Core/Forks/ForkRules.cs:56,200`; `Scrutor.Core/Execution/IntrinsicGas.cs:71-72` - base 68, overridden to 16 in Istanbul. |
| TX.ACCESS_LIST_ADDRESS | Transaction Entry/Intrinsic | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | S | S | S | S | S | S | `Scrutor.Core/Execution/IntrinsicGas.cs:17,74-80` - hardcoded 2,400, gated by `HasEip2930AccessLists`. |
| TX.ACCESS_LIST_STORAGE_KEY | Transaction Entry/Intrinsic | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | S | S | S | S | S | S | `Scrutor.Core/Execution/IntrinsicGas.cs:18,75-80` - hardcoded 1,900 per key, gated by `HasEip2930AccessLists`. |
| TX.INITCODE_WORD | Transaction Entry/Intrinsic | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | S | S | S | `Scrutor.Core/Execution/IntrinsicGas.cs:65-67` - hardcoded 2 per word behind `HasEip3860InitcodeLimit`. |
| TX.AUTHORIZATION_COST | Transaction Entry/Intrinsic | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | S | `Scrutor.Core/Execution/IntrinsicGas.cs:21,84-86` - hardcoded 25,000 per type-4 authorization behind the Prague flag. |
| TX.AUTHORIZATION_REFUND | Transaction Entry/Intrinsic | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | M | M | `Scrutor.Core/Execution/StateTransition.cs:290-350,380-382` - main path adds 12,500, but existing-empty accounts are misclassified and the gas-tree path omits authorization processing/refund. |
| TX.CALLDATA_FLOOR | Transaction Entry/Intrinsic | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | S | `Scrutor.Core/Execution/IntrinsicGas.cs:24-39`; `Scrutor.Core/Execution/StateTransition.cs:95-101,461-469` - shared formula with separate pre-execution and settlement enforcement sites. |
| TX.MAX_GAS_LIMIT | Transaction Entry/Intrinsic | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | M | `Scrutor.Core/Execution/StateTransition.cs:75-81`; `Scrutor.Core/Forks/ForkRules.cs:51-52,407` - the main Osaka path validates the cap, but the gas-tree transaction path omits it. |
| OP.STOP | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ControlFlowOpcodes.cs:15` |
| OP.JUMPDEST | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ControlFlowOpcodes.cs:87` |
| OP.ADD | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ArithmeticOpcodes.cs:21` |
| OP.SUB | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ArithmeticOpcodes.cs:56` |
| OP.MUL | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ArithmeticOpcodes.cs:38` |
| OP.DIV | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ArithmeticOpcodes.cs:77` |
| OP.SDIV | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ArithmeticOpcodes.cs:155` |
| OP.MOD | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ArithmeticOpcodes.cs:98` |
| OP.SMOD | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ArithmeticOpcodes.cs:192` |
| OP.ADDMOD | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ArithmeticOpcodes.cs:215` |
| OP.MULMOD | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ArithmeticOpcodes.cs:238` |
| OP.SIGNEXTEND | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ArithmeticOpcodes.cs:311` |
| OP.LT | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ComparisonOpcodes.cs:21` |
| OP.GT | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ComparisonOpcodes.cs:36` |
| OP.SLT | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ComparisonOpcodes.cs:54` |
| OP.SGT | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ComparisonOpcodes.cs:72` |
| OP.EQ | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ComparisonOpcodes.cs:87` |
| OP.ISZERO | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ComparisonOpcodes.cs:102` |
| OP.AND | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/BitwiseOpcodes.cs:21` |
| OP.OR | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/BitwiseOpcodes.cs:38` |
| OP.XOR | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/BitwiseOpcodes.cs:55` |
| OP.NOT | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/BitwiseOpcodes.cs:74` |
| OP.BYTE | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/BitwiseOpcodes.cs:101` |
| OP.SHL | Fixed Opcode Gas | M | M | M | M | M | S | S | S | S | S | S | S | S | S | Missing pre-Constantinople gate; price at `Scrutor.Core/Opcodes/BitwiseOpcodes.cs:126` |
| OP.SHR | Fixed Opcode Gas | M | M | M | M | M | S | S | S | S | S | S | S | S | S | Missing pre-Constantinople gate; price at `Scrutor.Core/Opcodes/BitwiseOpcodes.cs:149` |
| OP.SAR | Fixed Opcode Gas | M | M | M | M | M | S | S | S | S | S | S | S | S | S | Missing pre-Constantinople gate; price at `Scrutor.Core/Opcodes/BitwiseOpcodes.cs:183` |
| OP.CLZ | Fixed Opcode Gas | M | M | M | M | M | M | M | M | M | M | M | M | M | S | No schedule flag; price at `Scrutor.Core/Opcodes/BitwiseOpcodes.cs:216` |
| OP.POP | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/StackOpcodes.cs:22` |
| OP.PUSH0 | Fixed Opcode Gas | M | M | M | M | M | M | M | M | M | M | S | S | S | S | Unused `HasPush0`; price at `Scrutor.Core/Opcodes/StackOpcodes.cs:39` |
| OP.PUSH1_32 | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/StackOpcodes.cs:77` |
| OP.DUP1_16 | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/StackOpcodes.cs:126` |
| OP.SWAP1_16 | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/StackOpcodes.cs:162` |
| OP.ADDRESS | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ExecutionOpcodes.cs:42` |
| OP.ORIGIN | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ExecutionOpcodes.cs:23` |
| OP.CALLER | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ExecutionOpcodes.cs:189` |
| OP.CALLVALUE | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ExecutionOpcodes.cs:207` |
| OP.CALLDATALOAD | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ExecutionOpcodes.cs:237` |
| OP.CALLDATASIZE | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ExecutionOpcodes.cs:255` |
| OP.CODESIZE | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ExecutionOpcodes.cs:309` |
| OP.GASPRICE | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ExecutionOpcodes.cs:96` |
| OP.RETURNDATASIZE | Fixed Opcode Gas | M | M | M | M | S | S | S | S | S | S | S | S | S | S | Unused `HasReturnDataOps`; `Scrutor.Core/Opcodes/ExecutionOpcodes.cs:353-363` |
| OP.BLOBHASH | Fixed Opcode Gas | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | S | S | Gate at `Scrutor.Core/Opcodes/ExecutionOpcodes.cs:135-140`; hardcoded price at `:169-170` |
| OP.GAS | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ExecutionOpcodes.cs:111-118` |
| OP.BLOCKHASH | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/StateOpcodes.cs:201` |
| OP.COINBASE | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/StateOpcodes.cs:220` |
| OP.TIMESTAMP | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/StateOpcodes.cs:238` |
| OP.NUMBER | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/StateOpcodes.cs:256` |
| OP.DIFFICULTY | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/StateOpcodes.cs:274` |
| OP.GASLIMIT | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/StateOpcodes.cs:292` |
| OP.CHAINID | Fixed Opcode Gas | M | M | M | M | M | M | S | S | S | S | S | S | S | S | Unused `HasChainId`; `Scrutor.Core/Opcodes/StateOpcodes.cs:47-57` |
| OP.SELFBALANCE | Fixed Opcode Gas | M | M | M | M | M | M | S | S | S | S | S | S | S | S | Unused `HasSelfBalance`; hardcoded price at `Scrutor.Core/Opcodes/StateOpcodes.cs:80` |
| OP.BASEFEE | Fixed Opcode Gas | M | M | M | M | M | M | M | M | S | S | S | S | S | S | Missing pre-London gate; `Scrutor.Core/Opcodes/StateOpcodes.cs:300-310` |
| OP.BLOBBASEFEE | Fixed Opcode Gas | M | M | M | M | M | M | M | M | M | M | M | S | S | S | Missing pre-Cancun gate; `Scrutor.Core/Opcodes/StateOpcodes.cs:318-328` |
| OP.JUMP | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ControlFlowOpcodes.cs:37` |
| OP.JUMPI | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ControlFlowOpcodes.cs:52,62` |
| OP.PC | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ControlFlowOpcodes.cs:76` |
| OP.MSIZE | Fixed Opcode Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/MemoryOpcodes.cs:103` |
| MEMORY.EXPANSION | Dynamic Opcode/Memory/Copy/Hash/Log | S | S | S | S | S | S | S | S | S | S | S | S | S | S | Formula and host cap at `Scrutor.Core/Execution/EvmMemory.cs:8,51-86` |
| OP.EXP | Dynamic Opcode/Memory/Copy/Hash/Log | M | M | M | S | S | S | S | S | S | S | S | S | S | S | Missing 10-per-byte era; hardcoded 50-per-byte at `Scrutor.Core/Opcodes/ArithmeticOpcodes.cs:253-265` |
| OP.MLOAD | Dynamic Opcode/Memory/Copy/Hash/Log | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/MemoryOpcodes.cs:20-23` |
| OP.MSTORE | Dynamic Opcode/Memory/Copy/Hash/Log | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/MemoryOpcodes.cs:47-50` |
| OP.MSTORE8 | Dynamic Opcode/Memory/Copy/Hash/Log | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/MemoryOpcodes.cs:81-84` |
| OP.MCOPY | Dynamic Opcode/Memory/Copy/Hash/Log | M | M | M | M | M | M | M | M | M | M | M | S | S | S | Unused `HasMcopy`; `Scrutor.Core/Opcodes/MemoryCopyOpcode.cs:12-56` |
| OP.CALLDATACOPY | Dynamic Opcode/Memory/Copy/Hash/Log | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ExecutionOpcodes.cs:270-291` |
| OP.CODECOPY | Dynamic Opcode/Memory/Copy/Hash/Log | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ExecutionOpcodes.cs:324-345` |
| OP.RETURNDATACOPY | Dynamic Opcode/Memory/Copy/Hash/Log | M | M | M | M | S | S | S | S | S | S | S | S | S | S | Unused `HasReturnDataOps`; `Scrutor.Core/Opcodes/ExecutionOpcodes.cs:371-403` |
| OP.EXTCODECOPY | Dynamic Opcode/Memory/Copy/Hash/Log | D | I | O | I | I | I | I | O | I | I | I | I | I | I | Access schedule at `Scrutor.Core/Forks/ForkRules.cs:60,133,257`; invariant copy logic at `Scrutor.Core/Opcodes/StateOpcodes.cs:131-158` |
| OP.KECCAK256 | Dynamic Opcode/Memory/Copy/Hash/Log | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/StateOpcodes.cs:27-39` |
| OP.LOG0 | Dynamic Opcode/Memory/Copy/Hash/Log | S | S | S | S | S | S | S | S | S | S | S | S | S | S | Shared hardcoded formula and missing static branch at `Scrutor.Core/Opcodes/LoggingOpcodes.cs:15-49` |
| OP.LOG1 | Dynamic Opcode/Memory/Copy/Hash/Log | S | S | S | S | S | S | S | S | S | S | S | S | S | S | Shared hardcoded formula and missing static branch at `Scrutor.Core/Opcodes/LoggingOpcodes.cs:15-54` |
| OP.LOG2 | Dynamic Opcode/Memory/Copy/Hash/Log | S | S | S | S | S | S | S | S | S | S | S | S | S | S | Shared hardcoded formula and missing static branch at `Scrutor.Core/Opcodes/LoggingOpcodes.cs:15-55` |
| OP.LOG3 | Dynamic Opcode/Memory/Copy/Hash/Log | S | S | S | S | S | S | S | S | S | S | S | S | S | S | Shared hardcoded formula and missing static branch at `Scrutor.Core/Opcodes/LoggingOpcodes.cs:15-56` |
| OP.LOG4 | Dynamic Opcode/Memory/Copy/Hash/Log | S | S | S | S | S | S | S | S | S | S | S | S | S | S | Shared hardcoded formula and missing static branch at `Scrutor.Core/Opcodes/LoggingOpcodes.cs:15-57` |
| OP.RETURN | Dynamic Opcode/Memory/Copy/Hash/Log | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Opcodes/ControlFlowOpcodes.cs:98-108` |
| OP.REVERT | Dynamic Opcode/Memory/Copy/Hash/Log | M | M | M | M | S | S | S | S | S | S | S | S | S | S | Unused `HasRevert`; `Scrutor.Core/Opcodes/ControlFlowOpcodes.cs:111-128` |
| ACCESS.INITIAL_WARM_SET | Account and Storage Access | N/A | N/A | N/A | N/A | N/A | N/A | N/A | M | M | M | S | M | M | M | Primary and duplicate initialization paths: `Scrutor.Core/Execution/StateTransition.cs:247-287`; fork counts at `Scrutor.Core/Forks/ForkRules.cs:98,167,202,379,395,405` |
| ACCESS.EIP7702_AUTHORITY_WARM | Account and Storage Access | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | O | I | Gate and warm transition at `Scrutor.Core/Execution/StateTransition.cs:290-321`; `Scrutor.Core/Forks/ForkRules.cs:48,391` |
| ACCESS.BALANCE | Account and Storage Access | D | I | M | M | M | M | I | O | I | I | I | I | I | I | Wrong shared Tangerine price at `Scrutor.Core/Forks/ForkRules.cs:60,133`; Berlin override at `:257` |
| ACCESS.EXTCODESIZE | Account and Storage Access | D | I | O | I | I | I | I | O | I | I | I | I | I | I | `Scrutor.Core/Forks/ForkRules.cs:60,133,257`; use at `Scrutor.Core/Opcodes/StateOpcodes.cs:100-101` |
| ACCESS.EXTCODEHASH | Account and Storage Access | M | M | M | M | M | O | O | O | I | I | I | I | I | I | Missing pre-Constantinople gate; pricing overrides at `Scrutor.Core/Forks/ForkRules.cs:183,204,258` |
| ACCESS.SLOAD | Account and Storage Access | D | I | O | I | I | I | O | O | I | I | I | I | I | I | `Scrutor.Core/Forks/ForkRules.cs:18,131,197,255`; use at `Scrutor.Core/Opcodes/StorageOpcodes.cs:19-27` |
| ACCESS.TLOAD | Account and Storage Access | M | M | M | M | M | M | M | M | M | M | M | S | S | S | Unused `HasTloadTstore`; hardcoded price at `Scrutor.Core/Opcodes/StorageOpcodes.cs:93-107` |
| SSTORE.REENTRANCY_GUARD | SSTORE | N/A | N/A | N/A | N/A | N/A | N/A | O | I | I | I | I | I | I | I | Activation at `Scrutor.Core/Forks/ForkRules.cs:21,201`; threshold at `Scrutor.Core/Opcodes/StorageOpcodes.cs:56-62` |
| SSTORE.COLD_SURCHARGE | SSTORE | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | S | S | S | S | S | S | Fork flag at `Scrutor.Core/Forks/ForkRules.cs:22,252`; hardcoded 2100 at `Scrutor.Core/Opcodes/StorageOpcodes.cs:78-83` |
| SSTORE.FORMULA_FRONTIER | SSTORE | D | I | I | I | I | I | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | `Scrutor.Core/Forks/ForkRules.cs:26-36` |
| SSTORE.FORMULA_ISTANBUL | SSTORE | N/A | N/A | N/A | N/A | N/A | N/A | O | N/A | N/A | N/A | N/A | N/A | N/A | N/A | `Scrutor.Core/Forks/ForkRules.cs:211-241` |
| SSTORE.FORMULA_BERLIN | SSTORE | N/A | N/A | N/A | N/A | N/A | N/A | N/A | O | N/A | N/A | N/A | N/A | N/A | N/A | `Scrutor.Core/Forks/ForkRules.cs:266-295` |
| SSTORE.FORMULA_LONDON | SSTORE | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | O | I | I | I | I | I | `Scrutor.Core/Forks/ForkRules.cs:311-340` |
| SSTORE.TSTORE | SSTORE | M | M | M | M | M | M | M | M | M | M | M | S | S | S | Unused `HasTloadTstore`; hardcoded price at `Scrutor.Core/Opcodes/StorageOpcodes.cs:111-125` |
| CALL.MEMORY_EXPANSION | CALL-Family | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `SystemOpcodes.cs:222-230,679-685,801-807,947-953` |
| CALL.DEPTH_LIMIT | CALL-Family | M | M | M | M | M | M | M | M | M | M | M | M | M | M | Off-by-one recursion gate: `StateTransition.cs:775-806,953,978-996` |
| CALL.ACCESS_COST | CALL-Family | D | I | O | I | I | I | I | O | I | I | I | I | I | I | `ForkRules.cs:64,136,257-261`; opcode composition in `SystemOpcodes.cs:239-244,691-695,814-817,960-963` |
| CALL.EIP7702_DELEGATION_ACCESS | CALL-Family | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | S | Prague flag `ForkRules.cs:390`; scattered access logic `SystemOpcodes.cs:246-258,699-708,818-830,965-976` |
| CALL.VALUE_TRANSFER_COST | CALL-Family | S | S | S | S | S | S | S | S | S | S | S | S | S | S | Hardcoded 9000 at `SystemOpcodes.cs:259,833` |
| CALL.NEW_ACCOUNT_COST | CALL-Family | M | M | M | S | S | S | S | S | S | S | S | S | S | S | Incorrect legacy existence/Tangerine predicate at `SystemOpcodes.cs:261-290` |
| CALL.PRE_EIP150_CHARGE | CALL-Family | M | M | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | Missing complete legacy behavior and unchecked overflow at `SystemOpcodes.cs:274-364`; `ExecutionContext.cs:226-230` |
| CALL.EIP150_FORWARDING | CALL-Family | N/A | N/A | S | S | S | S | S | S | S | S | S | S | S | S | Scattered 63/64 paths at `SystemOpcodes.cs:366-379,712-718,837-846,980-986` |
| CALL.STIPEND_GRANT | CALL-Family | S | S | S | S | S | S | S | S | S | S | S | S | S | S | Hardcoded 2300 at `SystemOpcodes.cs:297,384,856,865` |
| CALL.INSUF_BALANCE_EARLY_EXIT | CALL-Family | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `SystemOpcodes.cs:300-310,386-398,848-860` |
| CALL.CHILD_GAS_RETURN | CALL-Family | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `SystemOpcodes.cs:355-358,447-451,746-747,894-897,1018-1019` |
| CALL.EXCEPTIONAL_CHILD_BURN | CALL-Family | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `EvmMachine.cs:69-79,89-97`; caller return calculation `SystemOpcodes.cs:447-451` |
| CALL.REFUND_COUNTER_PROPAGATION | CALL-Family | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `SystemOpcodes.cs:358,452-455,748-751,898-901,1020-1023` |
| STATICCALL.FORWARDING | CALL-Family | N/A | N/A | N/A | N/A | S | S | S | S | S | S | S | S | S | S | Active formula `SystemOpcodes.cs:650-767`; missing pre-Byzantium dispatch gate is covered by `HALT.OPCODE_ACTIVATION` |
| DELEGATECALL.FORWARDING | CALL-Family | N/A | M | S | S | S | S | S | S | S | S | S | S | S | S | Homestead legacy forwarding missing; current path `SystemOpcodes.cs:918-1037` |
| CALL.PRECOMPILE_DISPATCH | CALL-Family | S | S | M | S | S | S | S | S | S | S | S | S | S | S | Tangerine legacy touch omission in direct branch `SystemOpcodes.cs:313-318,405-421` |
| CREATE.BASE | CREATE-Family | M | M | M | M | M | M | M | M | M | M | S | S | S | S | Hardcoded base and prematurely active word charge `SystemOpcodes.cs:25-35` |
| CREATE.INITCODE_SIZE_LIMIT | CREATE-Family | M | M | M | M | M | M | M | M | M | M | S | S | S | S | Missing pre-Shanghai inactivity gate at `SystemOpcodes.cs:25-27,491-493`; activation flag `ForkRules.cs:362` |
| CREATE.MEMORY_EXPANSION | CREATE-Family | M | M | M | M | M | M | M | M | M | M | M | M | M | M | Required charge absent before `Memory.Load`: `SystemOpcodes.cs:37,504`; `EvmMemory.cs:23-45` |
| CREATE.EIP150_FORWARDING | CREATE-Family | M | M | S | S | S | S | S | S | S | S | S | S | S | S | CREATE incorrectly caps legacy forks; `SystemOpcodes.cs:53-54,71,519-520,540` |
| CREATE.PRE_CHECK_NO_TRANSFER | CREATE-Family | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `SystemOpcodes.cs:43-62,515-527` |
| CREATE.WARMING | CREATE-Family | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | S | S | S | S | S | S | Berlin+ access-state mutation `SystemOpcodes.cs:64-65,530-531`; activation `ForkRules.cs:252` |
| CREATE.COLLISION_BURN | CREATE-Family | M | M | M | M | M | M | M | M | M | M | M | M | M | M | EIP-7610 predicate incomplete for unknown remote storage: `AccountDeployability.cs:11-30`; `ForkingGlobalState.cs:83-99` |
| CREATE.CHILD_GAS_RETURN | CREATE-Family | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `SystemOpcodes.cs:107-108,169-180,574-575,631-642` |
| CREATE.CODE_DEPOSIT | CREATE-Family | S | S | S | S | S | S | S | S | S | S | S | S | S | S | Hardcoded 200 and duplicated paths: `SystemOpcodes.cs:112-116,161-162,579-583,623-624`; `StateTransition.cs:404-418,662-669` |
| CREATE.CODE_SIZE_LIMIT | CREATE-Family | N/A | N/A | N/A | M | M | M | M | M | M | M | M | M | M | M | Internal EIP-170 check absent; top-level-only checks `StateTransition.cs:386-391,655-660` |
| CREATE.DEPOSIT_OOG | CREATE-Family | M | M | M | M | M | M | M | M | M | M | M | M | M | M | Frontier semantics missing and successful initcode commits before validation: `SystemOpcodes.cs:99,116-136,566,583-600`; `StateTransition.cs:978-1009` |
| CREATE.EF_PREFIX_BURN | CREATE-Family | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | M | M | M | M | M | M | London gate exists, but rollback is incomplete: `SystemOpcodes.cs:139-158,604-620`; `StateTransition.cs:978-1009` |
| CREATE2.BASE | CREATE-Family | N/A | N/A | N/A | N/A | N/A | S | S | S | S | S | S | S | S | S | Hardcoded 32000/6/2 word components at `SystemOpcodes.cs:498-502`; missing activation is covered by `HALT.OPCODE_ACTIVATION` |
| CREATE.REFUND_COUNTER_PROPAGATION | CREATE-Family | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `SystemOpcodes.cs:167,629` |
| CREATE.TOP_LEVEL_DEPOSIT | CREATE-Family | M | M | M | M | M | M | M | M | M | M | M | M | M | M | Duplicated, drifted fork handling at `StateTransition.cs:386-424,655-671` |
| SELFDESTRUCT.BASE | SELFDESTRUCT | D | I | O | I | I | I | I | I | I | I | I | I | I | I | `ForkRules.cs:88,140`; consumed at `SystemOpcodes.cs:1061-1064` |
| SELFDESTRUCT.NEW_ACCOUNT | SELFDESTRUCT | N/A | N/A | M | S | S | S | S | S | S | S | S | S | S | S | Direct constant `ForkRules.cs:89,141`; Tangerine predicate defect `SystemOpcodes.cs:1066-1081` |
| SELFDESTRUCT.COLD_ACCESS | SELFDESTRUCT | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | S | S | S | S | S | S | Hardcoded 2600 and Berlin flag at `SystemOpcodes.cs:1063-1064`; `ForkRules.cs:252` |
| SELFDESTRUCT.REFUND | SELFDESTRUCT | M | M | M | M | M | M | M | M | N/A | N/A | N/A | N/A | N/A | N/A | No production credit or schedule property; scoped opcode `SystemOpcodes.cs:1040-1110` |
| PRECOMPILE.DISPATCH_BUDGET | Precompile Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Execution/StateTransition.cs:243-245,818-842`; `Scrutor.Core/Opcodes/SystemOpcodes.cs:313-318,405-421,721-723,869-872,989-991` - top-level and nested budget handling is split across dispatch paths. |
| PRECOMPILE.ECRECOVER | Precompile Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Execution/Precompiles.cs:139-168` - hardcoded 3,000; active from Frontier through `PrecompileCount`. |
| PRECOMPILE.SHA256 | Precompile Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Execution/Precompiles.cs:172-177` - hardcoded `60 + 12 * ceil(len/32)`. |
| PRECOMPILE.RIPEMD160 | Precompile Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Execution/Precompiles.cs:180-193` - hardcoded `600 + 120 * ceil(len/32)`. |
| PRECOMPILE.IDENTITY | Precompile Gas | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `Scrutor.Core/Execution/Precompiles.cs:196-201` - hardcoded `15 + 3 * ceil(len/32)`. |
| PRECOMPILE.MODEXP_EIP198 | Precompile Gas | N/A | N/A | N/A | N/A | M | M | M | N/A | N/A | N/A | N/A | N/A | N/A | N/A | `Scrutor.Core/Execution/Precompiles.cs:264-350`; `Scrutor.Core/Forks/ForkRules.cs:167` - the EIP-198 branch has a non-protocol 10,000,000,000 saturation that can undercharge accepted inputs. |
| PRECOMPILE.MODEXP_EIP2565 | Precompile Gas | N/A | N/A | N/A | N/A | N/A | N/A | N/A | M | M | M | M | M | M | N/A | `Scrutor.Core/Execution/Precompiles.cs:284-289,301-349`; `Scrutor.Core/Forks/ForkRules.cs:254` - the EIP-2565 branch has the same reachable non-protocol gas saturation. |
| PRECOMPILE.MODEXP_EIP7883 | Precompile Gas | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | `Scrutor.Core/Execution/Precompiles.cs:270-283,322-335`; `Scrutor.Core/Forks/ForkRules.cs:406` - hardcoded Osaka formula and 500 floor. |
| PRECOMPILE.MODEXP_LENGTH_LIMIT | Precompile Gas | N/A | N/A | N/A | N/A | M | M | M | M | M | M | M | M | M | S | `Scrutor.Core/Execution/Precompiles.cs:212-218` - non-protocol 8,192-byte cap is present before Osaka; Osaka's EIP-7823 1,024-byte limit is implemented. |
| PRECOMPILE.BN254_ADD | Precompile Gas | N/A | N/A | N/A | N/A | D | I | O | I | I | I | I | I | I | I | `Scrutor.Core/Forks/ForkRules.cs:92,206`; `Scrutor.Core/Execution/Precompiles.cs:82,364-382` - 500 base schedule, overridden to 150 in Istanbul. |
| PRECOMPILE.BN254_MUL | Precompile Gas | N/A | N/A | N/A | N/A | D | I | O | I | I | I | I | I | I | I | `Scrutor.Core/Forks/ForkRules.cs:93,207`; `Scrutor.Core/Execution/Precompiles.cs:83,387-404` - 40,000 base schedule, overridden to 6,000 in Istanbul. |
| PRECOMPILE.BN254_PAIRING | Precompile Gas | N/A | N/A | N/A | N/A | D | I | O | I | I | I | I | I | I | I | `Scrutor.Core/Forks/ForkRules.cs:94-95,208-209`; `Scrutor.Core/Execution/Precompiles.cs:84,416-429` - base/per-pair values are overridden in Istanbul. |
| PRECOMPILE.BLAKE2F | Precompile Gas | N/A | N/A | N/A | N/A | N/A | N/A | S | S | S | S | S | S | S | S | `Scrutor.Core/Execution/Precompiles.cs:434-465`; `Scrutor.Core/Forks/ForkRules.cs:202` - rounds-based gas and validation are hardcoded; activation is by count. |
| PRECOMPILE.KZG_POINT_EVAL | Precompile Gas | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | M | M | M | `Scrutor.Core/Execution/Precompiles.cs:495-542`; `Scrutor.Core/Forks/ForkRules.cs:379` - gas and proof checks are hardcoded; trusted-setup load failure escapes as a host exception instead of the required budget-burning result. |
| PRECOMPILE.BLS_G1ADD | Precompile Gas | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | S | `Scrutor.Core/Execution/Bls12381Precompiles.cs:19,85-98`; `Scrutor.Core/Forks/ForkRules.cs:392-395` - hardcoded 375. |
| PRECOMPILE.BLS_G1MSM | Precompile Gas | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | S | `Scrutor.Core/Execution/Bls12381Precompiles.cs:20,30-40,51-53,104-140` - hardcoded multiplier, discount table/cap, and floor division. |
| PRECOMPILE.BLS_G2ADD | Precompile Gas | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | S | `Scrutor.Core/Execution/Bls12381Precompiles.cs:22,144-157`; `Scrutor.Core/Forks/ForkRules.cs:392-395` - hardcoded 600. |
| PRECOMPILE.BLS_G2MSM | Precompile Gas | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | S | `Scrutor.Core/Execution/Bls12381Precompiles.cs:23,41-53,163-198` - hardcoded multiplier, discount table/cap, and floor division. |
| PRECOMPILE.BLS_PAIRING | Precompile Gas | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | S | `Scrutor.Core/Execution/Bls12381Precompiles.cs:26-28,202-245` - hardcoded `37,700 + 32,600*k`; empty input is rejected before the identity branch. |
| PRECOMPILE.BLS_MAP_FP_G1 | Precompile Gas | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | S | `Scrutor.Core/Execution/Bls12381Precompiles.cs:21,249-264` - hardcoded 5,500. |
| PRECOMPILE.BLS_MAP_FP2_G2 | Precompile Gas | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | S | `Scrutor.Core/Execution/Bls12381Precompiles.cs:24,268-284` - hardcoded 23,800. |
| PRECOMPILE.P256VERIFY | Precompile Gas | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | `Scrutor.Core/Execution/Precompiles.cs:16-27,69-74,685-759`; `Scrutor.Core/Forks/ForkRules.cs:405` - separate address 0x0100, hardcoded 6,900. |
| HALT.OPCODE_ACTIVATION | Exceptional Halt | M | M | M | M | M | M | M | M | M | M | M | M | M | M | Global registration and ungated dispatch: `ServiceCollectionExtensions.cs:95-105`; `EvmMachine.cs:34-79` |
| HALT.OOG_BURN | Exceptional Halt | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `EvmMachine.cs:63-79,89-97` |
| HALT.REVERT_RETURN | Exceptional Halt | N/A | N/A | N/A | N/A | S | S | S | S | S | S | S | S | S | S | `EvmMachine.cs:69-79`; missing preactivation dispatch is represented by `HALT.OPCODE_ACTIVATION` |
| HALT.EXCEPTIONAL_BURN | Exceptional Halt | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `EvmMachine.cs:42-50,69-79` |
| HALT.EVM_GAS_CAP | Exceptional Halt | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `StateTransition.cs:446-451` |
| SETTLE.REFUND_CAP | Settlement | D | I | I | I | I | I | I | I | O | I | I | I | I | I | `ForkRules.cs:38,309`; application `StateTransition.cs:453-459` |
| SETTLE.CALLDATA_FLOOR_POST | Settlement | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | S | Prague flag `ForkRules.cs:391`; scattered application `StateTransition.cs:461-469` |
| SETTLE.SENDER_REFUND | Settlement | S | S | S | S | S | S | S | S | S | S | S | S | S | S | Primary and gas-tree paths `StateTransition.cs:230-237,471-490,692-699` |
| SETTLE.COINBASE_CREDIT | Settlement | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `StateTransition.cs:492-510` |
| SETTLE.EFFECTIVE_GAS_PRICE | Settlement | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `StateTransition.cs:60-73` |
| SETTLE.BLOB_FEE | Settlement | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | S | S | S | Hardcoded Cancun constants and settlement logic `StateTransition.cs:150-161,175-179,230-237,721-743` |
| DIAG.BALANCE_TO_GAS | Diagnostic | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `DivergenceDiagnostics.cs:74–82`; `Layer1DiagnosisBridge.cs:31,62–97,250–266` |
| DIAG.BALANCE_DIRECTION | Diagnostic | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `DivergenceDiagnostics.cs:86–96`; `Layer1DiagnosisBridge.cs:76–79` |
| DIAG.KNOWN_CONSTANT_MATCH | Diagnostic | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `DivergenceDiagnostics.cs:25–70,83–116` |
| DIAG.FORK_CONTEXT | Diagnostic | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `MismatchContext.cs`; `Layer1DiagnosisBridge.cs:169–205` |
| DIAG.REFUND_CAP | Diagnostic | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `StructuralPatternRules.cs:208–228`; hardcoded quotient 5 |
| DIAG.GAS_TREE_INTRINSIC | Diagnostic | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `StateTransition.cs:546–569`; `TraceCommand.cs:67–73` |
| DIAG.GAS_TREE_EXECUTION | Diagnostic | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `StateTransition.cs:572–719`; duplicated transaction evaluator |
| DIAG.GAS_TREE_ACCESS_CLASS | Diagnostic | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `GasTree.cs:118–129`; aggregate-cost heuristic |
| DIAG.GAS_TREE_MEMORY_LEDGER | Diagnostic | S | S | S | S | S | S | S | S | S | S | S | S | S | S | `GasTree.cs:42–70,136–159`; approximate component split |

## Fork Transition Changes

### Homestead

- EIP-2 activates the 32000 transaction-creation surcharge and exceptional failure for unaffordable code deposit.
- EIP-7 activates DELEGATECALL; current global opcode registration fails to enforce the Frontier boundary.

### Tangerine Whistle

- EIP-150 reprices IO-heavy account/code access, SELFDESTRUCT, and CALL-family base cost.
- EIP-150 activates the 63/64 CALL/CREATE forwarding cap. CALLCODE/DELEGATECALL/CREATE contain pre-boundary drift documented in the inventory.

### Spurious Dragon

- EIP-160 raises EXP per-byte cost from 10 to 50.
- EIP-161 changes dead/empty-account predicates and removes the SELFDESTRUCT new-account charge when no value is transferred.
- EIP-170 activates the 24576-byte deployed-code limit; internal CREATE paths currently omit it.

### Byzantium

- EIP-140/211/214 activate REVERT, RETURNDATA operations, and STATICCALL.
- EIP-196/197/198 activate ModExp and BN254 precompiles 0x05–0x08 with their original prices.
- Current opcode dispatch does not enforce the activation boundary.

### Constantinople

- EIP-145 activates SHL/SHR/SAR at 3 gas.
- EIP-1014 activates CREATE2 with `32000 + 6·ceil(initcodeBytes/32)` before memory/deposit charges.
- EIP-1052 activates EXTCODEHASH at 400 gas.
- Current opcode dispatch does not enforce these boundaries.

### Istanbul

- EIP-2028 reduces nonzero calldata from 68 to 16.
- EIP-1884 raises BALANCE/EXTCODEHASH to 700 and SLOAD to 800, and activates SELFBALANCE/CHAINID pricing.
- EIP-2200 activates tri-state SSTORE and the 2300-gas reentrancy guard.
- EIP-1108 reduces BN254 prices; BLAKE2F becomes active.

### Berlin

- EIP-2929 introduces warm/cold address and slot access, adjusted SSTORE constants, and transaction initial warmth.
- EIP-2930 adds 2400-per-address and 1900-per-slot access-list intrinsic charges.
- EIP-2565 replaces the ModExp formula and adds its 200 minimum.

### London

- EIP-3529 changes the refund quotient from 2 to 5, reduces the SSTORE clear refund to 4800, and removes the SELFDESTRUCT refund.
- EIP-1559 changes effective price/coinbase settlement; EIP-3198 activates BASEFEE.
- EIP-3541 activates EF-prefixed runtime-code rejection.

### Paris

- No gas-price or gas-formula transition was found. The 0x44 environmental opcode changes semantic meaning to PREVRANDAO without changing its 2-gas price.

### Shanghai

- EIP-3855 activates PUSH0 at 2 gas.
- EIP-3860 activates the 49152-byte initcode limit and `2·ceil(initcodeBytes/32)` charge.
- EIP-3651 adds coinbase to the initial warm set. Current code warms it from Berlin onward.

### Cancun

- EIP-1153 activates TLOAD/TSTORE at 100.
- EIP-5656 activates MCOPY; EIP-4844 activates blob transaction settlement, BLOBHASH/BLOBBASEFEE, and KZG point evaluation.
- EIP-6780 restricts SELFDESTRUCT deletion to contracts created in the same transaction.
- Most Cancun opcode activations are not enforced by the interpreter.

### Prague

- EIP-2537 activates BLS12-381 precompiles 0x0B–0x11.
- EIP-7623 activates the calldata token floor before execution and after refund application.
- EIP-7702 activates type-4 authorization intrinsic charges, existing-authority refunds, authority warming, and delegation-designator access.

### Osaka

- EIP-7825 caps transaction gas at 16777216.
- EIP-7823 limits ModExp lengths to 1024 bytes; EIP-7883 raises ModExp pricing and its floor to 500.
- EIP-7939 activates CLZ at 5; EIP-7951 activates P256Verify at 6900.

## Missing Coverage by Severity

### Critical

- `HALT.OPCODE_ACTIVATION`: pre-activation opcodes execute instead of invalid-opcode frame burn.
- `DIAG.GAS_TREE_EXECUTION`: diagnostic re-execution can apply a different transaction/fork lifecycle than canonical execution.
- `CREATE.DEPOSIT_OOG` and `CREATE.EF_PREFIX_BURN`: failed deployment can leak state and access warmth.
- `CREATE.MEMORY_EXPANSION`: CREATE/CREATE2 omit required expansion gas.
- `CALL.PRE_EIP150_CHARGE`: unchecked narrowing/addition can wrap an oversized gas argument into an undercharge.

### High

- `ACCESS.INITIAL_WARM_SET`: coinbase is warmed too early; modern precompile warming differs in the diagnostic path.
- `TX.AUTHORIZATION_REFUND`: existing-empty-account detection can lose 12500 per valid authorization.
- `ACCESS.BALANCE`: Tangerine Whistle–Constantinople is overcharged by 300.
- `SELFDESTRUCT.REFUND`: the pre-London 24000 refund is absent.
- `PRECOMPILE.MODEXP_EIP198`, `PRECOMPILE.MODEXP_EIP2565`, and `PRECOMPILE.MODEXP_EIP7883`: raw gas is saturated at a non-protocol 10-billion ceiling.
- `CALL.DEPTH_LIMIT`: CALL-family recursion permits depth 1025.

### Medium

- `MEMORY.EXPANSION`: signed-int arithmetic and the 16 MiB host limit can replace protocol gas reachability.
- `PRECOMPILE.MODEXP_LENGTH_LIMIT`: a non-protocol pre-Osaka 8192-byte cap rejects valid inputs.
- `PRECOMPILE.KZG_POINT_EVAL`: trusted-setup load failure escapes as a host exception.
- `OP.LOG0`–`OP.LOG4`: static-mode violation is missing and boundary addition can overflow.
- `DIAG.KNOWN_CONSTANT_MATCH`, `DIAG.REFUND_CAP`, and `DIAG.BALANCE_TO_GAS`: fork-blind or algebraically unsafe root-cause inference.

### Low

- Stale `IForkRules` precompile-count comments and incomplete source/test citations do not change current canonical execution but make migration and review less reliable.

## Proposed Schedule Overlay Order

1. `Frontier` defines all baseline fixed/dynamic costs, memory, CALL/CREATE movements, precompiles 0x01–0x04, settlement, and legacy refunds.
2. Each later fork inherits the previous immutable schedule and overrides only its transition list above.
3. Activation belongs in the same schedule as price/formula data; an inactive rule cannot be dispatched merely because an implementation class exists.
4. Host policies are explicit, separately typed, and never silently substituted for protocol gas formulas.
5. Diagnostic metadata is generated from the resolved schedule and execution journal rather than maintained as a second constant catalog.

## Validation Summary

- Inventory rule IDs: 177 (168 protocol + 9 diagnostic)
- Matrix rule IDs: 177
- Unique IDs in each document: 177; missing, extra, and duplicate IDs: 0
- Every matrix row contains exactly 14 valid fork-status cells
- Cell counts: `D=13`, `I=148`, `O=24`, `N/A=416`, `S=1581`, `M=296`
- Placeholder search: no matches
- Markdown whitespace validation: passed
