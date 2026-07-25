# EVM Operand Overflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert oversized consensus-visible EVM memory operands into fork-correct exceptional halts without disguising interpreter defects as EVM results.

**Architecture:** `OperandValidation.TryResolveMemoryRange` is the single boundary between 256-bit EVM operands and Scrutor's `int`-indexed `EvmMemory`. The seven affected opcode implementations use it before gas calculation, host allocation, or memory access; source-data offsets retain their opcode-specific zero-padding or bounds semantics. MODEXP keeps a separate wide-integer decode/gas/allocation path.

**Tech Stack:** C# 12, .NET 8, xUnit, Scrutor.Core EVM interpreter

## Global Constraints

- Oversized nonzero memory ranges return `ExecutionResult.Failure(EvmError.OutOfGas)`, never `InternalError`.
- Zero-length memory ranges do not validate or expand the memory offset.
- `InternalError` remains reserved for implementation invariant failures.
- Unexpected interpreter exceptions are logged with execution context and rethrown during development.
- MODEXP length words are not passed through the ordinary memory-range helper.

---

### Task 1: Pin Down the Shared Range Contract

**Files:**
- Modify: `Scrutor.Core/Execution/OperandValidation.cs`
- Create: `Scrutor.Tests/Opcodes/OperandOverflowTests.cs`

**Interfaces:**
- Consumes: `UInt256`, Scrutor's `int`-indexed `EvmMemory`
- Produces: `internal static bool TryResolveMemoryRange(UInt256 offset, UInt256 length, out int offsetInt, out int lengthInt, out ulong endExclusive)`

- [ ] **Step 1: Write failing theory tests for the full offset/length matrix**

Cover `0`, `Int32.MaxValue`, `Int32.MaxValue + 1`, and `UInt256.MaxValue` offsets against `0`, `1`, `Int32.MaxValue`, `Int32.MaxValue + 1`, and `UInt256.MaxValue` lengths. Assert that all zero-length rows succeed with zeroed outputs, valid nonzero sums at or below `Int32.MaxValue` succeed, and every oversized operand or sum fails.

- [ ] **Step 2: Run the focused helper tests and verify the current implementation's visibility or contract failures**

Run: `dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter FullyQualifiedName~OperandOverflowTests`

Expected: FAIL before exposing the internal helper to the test assembly and before all matrix expectations are represented.

- [ ] **Step 3: Implement the minimal centralized helper contract**

Keep zero-length handling first, reject operands above `Int32.MaxValue`, compute `endExclusive` in a checked unsigned expression, and reject ends above `Int32.MaxValue`. Remove unrelated MODEXP and jump conversion helpers from this memory-range component.

- [ ] **Step 4: Run the focused tests**

Run: `dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter FullyQualifiedName~OperandOverflowTests`

Expected: PASS.

### Task 2: Apply the Contract to the Seven Memory-Range Opcodes

**Files:**
- Modify: `Scrutor.Core/Opcodes/ControlFlowOpcodes.cs`
- Modify: `Scrutor.Core/Opcodes/ExecutionOpcodes.cs`
- Modify: `Scrutor.Core/Opcodes/MemoryCopyOpcode.cs`
- Modify: `Scrutor.Core/Opcodes/StateOpcodes.cs`
- Modify: `Scrutor.Tests/Opcodes/OperandOverflowTests.cs`

**Interfaces:**
- Consumes: `OperandValidation.TryResolveMemoryRange`
- Produces: guarded implementations of `RETURN`, `REVERT`, `CALLDATACOPY`, `CODECOPY`, `RETURNDATACOPY`, `MCOPY`, and `KECCAK256`

- [ ] **Step 1: Add failing opcode matrix tests**

Execute each affected opcode across the required operand matrix. Assert fork-correct zero-length behavior, `OutOfGas` for invalid nonzero memory ranges, normal execution for small valid ranges, `RETURNDATACOPY`'s EIP-211 source bounds, and zero-padding for huge `CALLDATACOPY`/`CODECOPY` source offsets.

- [ ] **Step 2: Run the focused opcode tests and verify expected failures**

Run: `dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter FullyQualifiedName~OperandOverflowTests`

Expected: FAIL on duplicated guards, unchecked range sums, source-offset handling, or return-data edge semantics.

- [ ] **Step 3: Replace per-opcode narrowing with the shared helper**

Use `endExclusive` for memory gas calculation. Preserve opcode-specific base gas and termination behavior. Do not validate calldata/code source offsets as memory offsets; compare them as wide integers before any source cast. For `RETURNDATACOPY`, validate `offset + length <= returndatasize`, including the zero-length boundary rule.

- [ ] **Step 4: Run the focused tests**

Run: `dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter FullyQualifiedName~OperandOverflowTests`

Expected: PASS.

### Task 3: Preserve Nested CALL Failure Semantics and Internal Diagnostics

**Files:**
- Modify: `Scrutor.Core/Execution/EvmMachine.cs`
- Modify: `Scrutor.Tests/Opcodes/OperandOverflowTests.cs`

**Interfaces:**
- Consumes: child `ExecutionResult.Failure(EvmError.OutOfGas)`
- Produces: CALL result `1`/`0` stack behavior without converting unexpected host exceptions into consensus failures

- [ ] **Step 1: Add failing nested-execution and exception-boundary tests**

Execute a parent CALL whose child performs an oversized nonzero memory operation. Assert the child halts with OOG, CALL pushes `0`, and the parent reaches its following opcode. Add a deliberately throwing test opcode and assert the interpreter rethrows after logging rather than returning `InvalidOpcode` or another EVM result.

- [ ] **Step 2: Run the focused tests and verify expected failures**

Run: `dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter FullyQualifiedName~OperandOverflowTests`

Expected: FAIL because the interpreter catch-all currently converts unexpected exceptions into `InvalidOpcode`.

- [ ] **Step 3: Log full internal context and rethrow unexpected exceptions**

Keep `EvmOutOfGasException` as the explicit consensus mapping and `OperationCanceledException` propagation. For all other exceptions, log depth, program counter, opcode byte/name, gas, addresses, and stack state where safely available, then `throw`.

- [ ] **Step 4: Run the focused tests**

Run: `dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter FullyQualifiedName~OperandOverflowTests`

Expected: PASS.

### Task 4: Review MODEXP Independently and Verify the Repository

**Files:**
- Modify: `Scrutor.Core/Opcodes/SystemOpcodes.cs`
- Modify: `Scrutor.Tests/Opcodes/OperandOverflowTests.cs`

**Interfaces:**
- Consumes: three 256-bit MODEXP length words and available precompile gas
- Produces: wide length decoding and gas-first rejection before bounded host allocation

- [ ] **Step 1: Add MODEXP tests for truncated length words, minimum gas, zero padding, enormous declared lengths, and allocation avoidance**

Assert no `OverflowException` or `OutOfMemoryException`, correct output padding for small truncated inputs, minimum-gas enforcement, and OOG before allocation for declared lengths above host limits.

- [ ] **Step 2: Run MODEXP-focused tests and verify expected failures**

Run: `dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter FullyQualifiedName~ModExp`

Expected: FAIL where 256-bit length decoding is currently truncated to `ulong` or host bounds are checked before a wide gas decision.

- [ ] **Step 3: Implement wide decode/gas-first MODEXP validation**

Decode all 32 bytes into a wide unsigned integer, compute the fork gas formula without narrowing, compare gas before allocation, and only then convert bounded slice/allocation sizes. Do not use `TryResolveMemoryRange`.

- [ ] **Step 4: Run focused and full verification**

Run: `dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter "FullyQualifiedName~OperandOverflowTests|FullyQualifiedName~ModExp"`

Run: `dotnet test Scrutor.sln`

Expected: PASS with zero failures.
