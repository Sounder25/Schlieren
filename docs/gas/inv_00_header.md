# Scrutor Gas Rule Inventory

## Method and Scope

Produced by exhaustive read of every `.cs` file under `Scrutor.Core/` that contains gas-affecting logic.
Each row records the exact current formula, all inputs, fork-dependent behavior, source path with line
number, existing tests, and findings. No external EVM client was consulted. No production code was
modified.

Search strategy: overlapping keyword sets (`Gas`, `ConsumeGas`, `RefundGas`, `GasUsed`, `GasLimit`,
`Stipend`, `Warm`, `Cold`, and numeric literals ≥100) across all production source, followed by manual
verification of every candidate line.

Exclusions: `DivergenceDiagnostics.cs` and `StructuralPatternRules.cs` hold diagnostic constants only —
not executed during gas charging. `GasTree.cs` is reporting infrastructure. `NodeConfiguration.cs` and
`ConfigurationLoader.cs` contain RPC gas caps, not EVM protocol rules.

## Supported Forks

| Enum | Fork Name | Key Gas EIPs |
|---:|---|---|
| 0 | Frontier | Yellow Paper baseline |
| 1 | Homestead | EIP-7: DELEGATECALL available |
| 2 | TangerineWhistle | EIP-150: IO-op repricing, 63/64 forwarding rule |
| 3 | SpuriousDragon | EIP-155/161: no gas-constant change |
| 4 | Byzantium | EIP-196/197/198: BN254 + MODEXP precompiles |
| 5 | Constantinople | EIP-145/1014/1052: SHL/SHR/SAR, CREATE2, EXTCODEHASH |
| 6 | Istanbul | EIP-1108/1884/2028/2200: BN254 reprice, SLOAD=800, calldata non-zero=16, SSTORE net metering |
| 7 | Berlin | EIP-2565/2929/2930: ModExp reprice, warm/cold access lists |
| 8 | London | EIP-1559/3529/3541: base fee, refund cap 20%, EF-prefix reject |
| 9 | Paris | EIP-3675: PoS merge, no gas-constant change |
| 10 | Shanghai | EIP-3855/3860: PUSH0 (2 gas), initcode word cost + size limit |
| 11 | Cancun | EIP-1153/4844/5656/6780: TLOAD/TSTORE, blob tx, MCOPY, SELFDESTRUCT restriction |
| 12 | Prague | EIP-2537/7623/7702: BLS12-381 precompiles, calldata floor, set-code tx |
| 13 | Osaka | EIP-7825/7883/7939/7951: tx gas cap, ModExp increase, CLZ opcode, P256Verify |

Source: `Scrutor.Core/Forks/Fork.cs:1-24`, `Scrutor.Core/Forks/ForkRules.cs:1-433`

## Inventory Summary

| Category | Rules |
|---|---:|
| Transaction Entry and Intrinsic Gas | 9 |
| Fixed Opcode Gas | 14 |
| Dynamic Opcode, Memory, Copy, Hash, and Log Gas | 9 |
| Account and Storage Access Gas | 5 |
| SSTORE Charges and Refunds | 4 |
| CALL-Family Gas and Frame Transfers | 20 |
| CREATE-Family and Code-Deposit Gas | 12 |
| SELFDESTRUCT Gas | 3 |
| Precompile Gas | 18 |
| Exceptional Halt, Refund Cap, and Settlement | 9 |
| **Total** | **103** |
