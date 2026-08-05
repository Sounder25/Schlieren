---
name: eels-balance-auditor
description: >
  For every failing EELS fixture case, reconstructs expected sender AND coinbase
  balances using the 5-term gas ledger equation and EIP-1559 priority fee routing.
  Flags whether discrepancies stem from gas deduction, unused refund, EIP-3529 storage refund,
  value-transfer, or coinbase priority fee routing.
---

# Skill: eels-balance-auditor

## Purpose
Reconstructs both sender and coinbase account balances from normative EVM rules to isolate sender gas ledger bugs vs coinbase fee-routing discrepancies.

## Reconstructed Equations

### 1. Sender Balance Equation (5 Terms)
$$\text{expected\_sender} = \text{pre\_balance} - \text{upfront\_gas} - \text{value} + \text{unused\_gas\_refund} + \text{EIP-3529\_refund} + \text{value\_restore\_on\_revert}$$

### 2. Coinbase Balance Equation (EIP-1559 Fee Routing)
$$\text{expected\_coinbase} = \text{pre\_coinbase} + (\text{totalGasUsedAfterRefund} \times \text{priorityFeePerGas})$$
$$\text{priorityFeePerGas} = \min(\text{maxPriorityFeePerGas}, \text{maxFeePerGas} - \text{baseFee})$$

## Diagnosis Categories
- **Term 1 Fault**: Upfront gas deduction off by gas units $\times$ price.
- **Term 2 Fault**: Value transfer skipped or mis-credited.
- **Term 3 Fault**: Unused gas refund miscalculated or omitted.
- **Term 4 Fault**: EIP-3529 storage refund ($\min(\text{counter}, \text{gasUsed}/5) \times \text{price}$) omitted or double-counted.
- **Term 5 Fault**: Revert value restoration omitted.
- **Coinbase Routing Fault**: Sender delta $= -X$ and Coinbase delta $= +X$ indicates `baseFee` vs `effectiveGasPrice` fee split bug.

## Usage

```powershell
$env:EELS_FIXTURES_ROOT  = "C:/projects/Scrutor/fixtures/state_tests/cancun"
$env:EELS_INCLUDE_SUBDIRS = "1"
$env:EELS_MAX_CASES      = "9999"
dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj --filter "EelsBalanceAudit"
```
