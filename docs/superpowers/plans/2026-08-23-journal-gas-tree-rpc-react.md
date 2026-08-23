# Journal Gas Tree, RPC, and React Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the typed execution journal the authoritative source for conserved gas accounting, frame-aware trace DTOs, the new `schlieren_traceJournal` RPC method, and the React Workbench, while preserving the exact JSON contracts of `debug_inspect` and `debug_traceCall`.

**Architecture:** Canonical execution records explicit gas-component events and immutable opcode-state observations in the existing journal. A pure Core projection builds the gas tree, frame summaries, steps, raw event DTOs, and conservation result exclusively from journal events. RPC exposes that projection through a new method and runs optional bytecode in a discarded state overlay. React consumes only the new method; the legacy RPC assemblers and Avalonia client remain unchanged.

**Tech Stack:** C# 12, .NET 8, xUnit, JSON-RPC, React 19, TypeScript 6, Zustand, Vite, Vitest

**Spec:** `docs/superpowers/specs/2026-08-23-journal-gas-tree-rpc-react-design.md`

## Global constraints

- Preserve EVM execution behavior: no changes to gas arithmetic, forwarding, stipends, refunds, state commits, return data, or exceptions.
- Record gas facts at their mutation sites. Do not infer an unexplained residual or join journal events to legacy `TraceSteps`.
- Ordinary opcode charges and explicit component charges are additive. CALL-family inclusive deltas, allocations, returns, observations, and refund-counter movements are non-additive. Effective refunds are credits and subtract exactly once.
- Every journal-owned opcode belongs to an explicit `FrameId`; nested opcodes never rely on trace depth for ownership.
- Snapshot arrays and dictionaries are copied when recorded and cannot mutate after the event is appended.
- `schlieren_traceJournal` returns stack, memory, and storage by default. Only `disableStack`, `disableMemory`, and `disableStorage` remove them.
- Optional `code` is installed in an execution-only `StateOverlay`; the base state is unchanged whether execution succeeds, reverts, fails, or is cancelled.
- `debug_inspect` and `debug_traceCall` retain byte-for-byte-equivalent JSON shapes. Do not route either endpoint through the new assembler.
- Do not migrate or edit Avalonia views. The React application under `schlieren-ui/` is the only UI consumer in this phase.
- Do not stage or modify the user's unrelated dirty campaign, diagnosis, regression, muscle, Harvest, or `windmill/` files.
- Use red-green-refactor within every task and commit only the files named by that task.

## File map

- Modify `Schlieren.Core/Execution/Journal/ExecutionJournal.cs`: component events and richer opcode observations.
- Modify `Schlieren.Core/Execution/ExecutionContext.cs`: semantically annotated gas mutation helpers and journal snapshot capture.
- Modify `Schlieren.Core/Execution/EvmMachine.cs`: active-opcode context and journal-native step observations.
- Modify `Schlieren.Core/Opcodes/SystemOpcodes.cs`: explicit CALL/CREATE allocation, return, and local-cost events.
- Modify `Schlieren.Core/Execution/StateTransition.cs`: transaction, precompile, create, collision, and calldata-floor components.
- Create `Schlieren.Core/Execution/Journal/JournalGasTree.cs`: exclusive-event tree and conservation projection.
- Create `Schlieren.Core/Execution/Journal/JournalTraceDtos.cs`: stable transport-neutral DTOs.
- Create `Schlieren.Core/Execution/Journal/JournalTraceAssembler.cs`: journal-to-DTO projection and snapshot filtering.
- Modify `Schlieren.RPC/RpcRouter.cs` and `Schlieren.RPC/Handlers/EthHandlers.cs`: new RPC method only.
- Create `Schlieren.RPC/Handlers/JournalTraceRequestParser.cs`: strict single-object request parsing.
- Modify `schlieren-ui/src/engine/rpc.ts` and `store.ts`: new endpoint and journal-native state.
- Modify React Trace, Machine State, Flow, and gas-oriented views to consume journal DTOs.
- Add focused Core, RPC, and React tests plus root/RPC documentation.

---

### Task 1: Extend the journal with explicit component and immutable step data

**Files:**
- Modify: `Schlieren.Core/Execution/Journal/ExecutionJournal.cs`
- Modify: `Schlieren.Core/Execution/ExecutionContext.cs`
- Modify: `Schlieren.Core/Execution/EvmMachine.cs`
- Modify: `Schlieren.Tests/Execution/EvmMachineJournalTests.cs`
- Modify: `Schlieren.Tests/Execution/ExecutionJournalTests.cs`

- [ ] **Step 1: Write failing contract and snapshot tests**

Add tests proving:

```csharp
[Fact]
public void GasComponentEvent_RequiresExplicitScopeComponentAndSemantics()
{
    var e = new GasComponentEvent
    {
        FrameId = 7,
        Scope = GasComponentScope.Opcode,
        Component = GasComponents.CallLocal,
        Amount = 700,
        Semantics = GasSemantics.ExclusiveCharge,
        Pc = 12,
        Opcode = 0xf1,
        OpcodeName = "CALL"
    };

    Assert.Equal(GasComponentScope.Opcode, e.Scope);
    Assert.Equal("call.local", e.Component);
    Assert.Equal(GasSemantics.ExclusiveCharge, e.Semantics);
}

[Fact]
public async Task JournalStep_CapturesStateWhenLegacyTracingIsDisabled()
{
    var context = BuildJournalContext(code: [0x60, 0x2a, 0x60, 0x00, 0x52, 0x00]);
    context.CaptureTrace = false;

    var result = await Machine.ExecuteAsync(context);

    Assert.Empty(result.TraceSteps);
    var mstore = Assert.Single(result.Journal!.Events.OfType<OpcodeGasEvent>(), e => e.Name == "MSTORE");
    Assert.NotEmpty(mstore.Stack);
    Assert.NotEmpty(mstore.Memory);
    Assert.NotNull(mstore.Storage);
}
```

Add an immutability test that records an opcode event, mutates the live stack/memory/storage afterward, and asserts the event's copies are unchanged.

- [ ] **Step 2: Verify RED**

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~EvmMachineJournalTests|FullyQualifiedName~ExecutionJournalTests.GasComponent" --nologo -v minimal
```

Expected: compilation fails because `GasComponentScope`, `GasComponentEvent`, component constants, and opcode snapshots do not exist.

- [ ] **Step 3: Add the journal contract**

Add:

```csharp
public enum GasComponentScope { Transaction, Frame, Opcode }

public static class GasComponents
{
    public const string CallLocal = "call.local";
    public const string CallForwarded = "call.forwarded";
    public const string CallUnusedReturn = "call.unused-return";
    public const string PrecompileExecution = "precompile.execution";
    public const string CreateCodeDeposit = "create.code-deposit";
    public const string CreateExceptionalBurn = "create.exceptional-burn";
    public const string TransactionCalldataFloor = "transaction.calldata-floor";
    public const string TransactionCollisionBurn = "transaction.collision-burn";
}

public sealed record GasComponentEvent : ExecutionJournalEvent
{
    public required GasComponentScope Scope { get; init; }
    public required string Component { get; init; }
    public required ulong Amount { get; init; }
    public required GasSemantics Semantics { get; init; }
    public int? Pc { get; init; }
    public byte? Opcode { get; init; }
    public string? OpcodeName { get; init; }
}
```

Extend `OpcodeGasEvent` with `Depth`, call context addresses/type, immutable `Stack`, `Memory`, `Storage`, and `Output` values. Use `IReadOnlyList<string>` and `IReadOnlyDictionary<string,string>` transport-neutral primitives, copying every collection on construction/recording.

- [ ] **Step 4: Capture pre-opcode state for the journal independently of legacy tracing**

In `EvmMachine`, capture the pre-stack whenever `CaptureTrace || Journal is not null`; continue populating `TraceSteps` only when `CaptureTrace` is true. In `ExecutionContext`, keep the storage mirror updated under the same combined condition. Populate the richer `OpcodeGasEvent` after each opcode without changing its existing gas delta semantics.

- [ ] **Step 5: Verify GREEN and parity**

Run the Task 1 filter, then:

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~StateTransitionJournalTests.Execution_WithAndWithoutJournal_HasIdenticalBehavior" --nologo -v minimal
```

Expected: all selected tests pass and legacy trace parity remains exact.

- [ ] **Step 6: Commit**

Commit only Task 1 files with message `feat(journal): capture explicit gas and machine state`.

---

### Task 2: Annotate CALL-family and CREATE-family gas movements at mutation sites

**Files:**
- Modify: `Schlieren.Core/Execution/ExecutionContext.cs`
- Modify: `Schlieren.Core/Execution/EvmMachine.cs`
- Modify: `Schlieren.Core/Opcodes/SystemOpcodes.cs`
- Create: `Schlieren.Tests/Execution/CallGasComponentJournalTests.cs`

- [ ] **Step 1: Add failing real-CALL tests**

Execute a root contract that performs a real nested `CALL` to an `SSTORE` child. Assert:

```csharp
var call = Assert.Single(events.OfType<OpcodeGasEvent>(), e => e.Name == "CALL");
Assert.Equal(GasSemantics.InclusiveFrameDelta, call.Semantics);

var local = Assert.Single(events.OfType<GasComponentEvent>(),
    e => e.FrameId == call.FrameId && e.Component == GasComponents.CallLocal);
Assert.Equal(GasSemantics.ExclusiveCharge, local.Semantics);

var allocation = Assert.Single(events.OfType<GasComponentEvent>(),
    e => e.Component == GasComponents.CallForwarded);
Assert.Equal(GasSemantics.Allocation, allocation.Semantics);

var returned = Assert.Single(events.OfType<GasComponentEvent>(),
    e => e.Component == GasComponents.CallUnusedReturn);
Assert.Equal(GasSemantics.Return, returned.Semantics);

var child = Assert.Single(events.OfType<FrameEnteredEvent>(), e => e.ParentFrameId == call.FrameId);
Assert.All(events.OfType<OpcodeGasEvent>().Where(e => e.Name == "SSTORE"),
    e => Assert.Equal(child.FrameId, e.FrameId));
```

Add theory cases for `CALL`, `CALLCODE`, `DELEGATECALL`, and `STATICCALL`, and focused CREATE/CREATE2 tests for forwarded gas and returned unused gas.

- [ ] **Step 2: Verify RED**

Run the new test class. Expected: no component events exist.

- [ ] **Step 3: Add semantically annotated gas helpers**

Extend `ConsumeGas` and `RefundGas` with optional metadata while preserving their arithmetic and old call compatibility:

```csharp
public bool ConsumeGas(
    ulong amount,
    GasSemantics semantics = GasSemantics.ExclusiveCharge,
    string? component = null,
    GasComponentScope scope = GasComponentScope.Opcode);

public void RefundGas(
    ulong amount,
    GasSemantics semantics = GasSemantics.Return,
    string? component = null,
    GasComponentScope scope = GasComponentScope.Opcode);
```

Record a `GasComponentEvent` only when `component` is non-null. Attach the active opcode PC/byte/name maintained by `EvmMachine`. This prevents ordinary opcode costs from being double-recorded.

- [ ] **Step 4: Annotate every CALL-family branch**

At all frontier and modern CALL/CALLCODE/DELEGATECALL/STATICCALL paths in `SystemOpcodes.cs`:

- Mark forwarded child gas as `Allocation / call.forwarded`.
- Mark returned unused child gas as `Return / call.unused-return`.
- Mark memory expansion, account access, value transfer, new-account cost, and other locally retained call cost as `ExclusiveCharge / call.local`.
- Preserve stipend arithmetic; stipend is metadata on allocation/return movement and never a second exclusive charge.
- Cover early-success, insufficient-funds, precompile, revert, exceptional-child, and pre-EIP-150 branches.

- [ ] **Step 5: Annotate CREATE and CREATE2 paths**

Apply the same allocation/return semantics to child creation gas. Do not label code-deposit cost here; canonical `StateTransition` owns that component in Task 3.

- [ ] **Step 6: Verify GREEN and behavior parity**

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~CallGasComponentJournalTests|FullyQualifiedName~Lane2_CallSemanticsTests|FullyQualifiedName~ExceptionalChildGasTests|FullyQualifiedName~BasicCallSemanticTests" --nologo -v minimal
```

Expected: all selected tests pass with unchanged execution results.

- [ ] **Step 7: Commit**

Commit Task 2 files with message `feat(journal): record call gas ownership semantics`.

---

### Task 3: Record canonical transaction, precompile, and creation components

**Files:**
- Modify: `Schlieren.Core/Execution/StateTransition.cs`
- Create: `Schlieren.Tests/Execution/StateTransitionGasComponentTests.cs`

- [ ] **Step 1: Add failing component tests**

Add fresh-state tests for:

- a precompile success and precompile OOG, expecting `precompile.execution` exclusive charge or explicit exceptional burn;
- contract creation code deposit, expecting `create.code-deposit` exclusive charge;
- failed code deposit, expecting `create.exceptional-burn` exceptional burn;
- EIP-7610 creation collision, expecting `transaction.collision-burn`;
- Osaka calldata-floor adjustment, expecting only the incremental `transaction.calldata-floor` amount.

Each test must assert the component's scope, frame ownership, amount, and semantics and verify `ExecutionResult.GasUsed` is unchanged from the non-journal run.

- [ ] **Step 2: Verify RED**

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~StateTransitionGasComponentTests" --nologo -v minimal
```

- [ ] **Step 3: Record facts beside existing arithmetic**

Use a single focused helper in `StateTransition`:

```csharp
static void RecordComponent(
    ExecutionJournal? journal, long? frameId, long? parentFrameId,
    GasComponentScope scope, string component, ulong amount,
    GasSemantics semantics);
```

Call it immediately beside the existing gas mutation. Do not recompute amounts later. Transaction-level components have `Scope.Transaction`; precompile and creation execution components have `Scope.Frame`.

- [ ] **Step 4: Verify GREEN and canonical parity**

Run the new class plus `PrecompileGasScheduleTests`, `Eip7610CreateCollisionTests`, `IntrinsicGasScheduleTests`, and journal parity tests.

- [ ] **Step 5: Commit**

Commit with message `feat(journal): record canonical gas components`.

---

### Task 4: Build the conserved gas tree exclusively from journal events

**Files:**
- Create: `Schlieren.Core/Execution/Journal/JournalGasTree.cs`
- Create: `Schlieren.Tests/Execution/JournalGasTreeTests.cs`
- Modify: `Schlieren.Tests/Execution/GasTraceInvariantTests.cs`

- [ ] **Step 1: Convert the two deliberately failing invariants to journal expectations**

Keep their intent and fixtures. Enable journaling, build through `JournalGasTree.Build(result.Journal, result)`, and assert:

```csharp
Assert.True(tree.Conservation.IsConserved, tree.Conservation.Delta);
Assert.Equal(tree.Conservation.SettledGas, tree.Conservation.DerivedGas);

var child = Assert.Single(tree.Root.Children.Where(n => n.FrameId == expectedChildId));
Assert.Contains(child.Children, n => n.Label.Contains("SSTORE"));
```

Add unit cases for a refund credit, exceptional burn, and CALL inclusive delta exclusion.

- [ ] **Step 2: Verify RED for the intended reasons**

Run `GasTraceInvariantTests` and `JournalGasTreeTests`. Expected: missing builder types; do not accept the old 100000/21024 or missing-child failures as the new RED state.

- [ ] **Step 3: Implement journal-native tree types**

Create:

```csharp
public enum JournalGasEffect { None, Charge, Credit }

public sealed record JournalGasNode(
    string Id,
    string Label,
    long? FrameId,
    GasSemantics Semantics,
    ulong Amount,
    JournalGasEffect Effect,
    ulong TotalGas,
    IReadOnlyList<long> EventSequences,
    IReadOnlyList<JournalGasNode> Children);

public sealed record JournalConservation(
    ulong DerivedGas,
    ulong SettledGas,
    string Delta,
    bool IsConserved);

public sealed record JournalGasTreeResult(
    JournalGasNode Root,
    JournalConservation Conservation);
```

- [ ] **Step 4: Implement one-pass frame indexing and exclusive accounting**

Index `FrameEnteredEvent` by explicit ID and parent ID. Assign every event only by `FrameId`. Include charges for intrinsic, ordinary exclusive opcode events, exclusive component events, and exceptional burns. Include effective refund as one credit. Represent allocation, return, inclusive delta, observation, refund counter, and settlement as evidence nodes with `Effect.None`.

Use checked signed arithmetic internally (`decimal` is acceptable) so credit subtraction cannot wrap. `DerivedGas = charges - credits`. For a settled external transaction use `TransactionSettledEvent.ChargedGas`; for internal/non-settled execution use `ExecutionResult.GasUsed`. Serialize `Delta = DerivedGas - SettledGas` as an invariant-culture signed decimal string. Never add a reconciliation node.

- [ ] **Step 5: Verify GREEN**

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~GasTraceInvariantTests|FullyQualifiedName~JournalGasTreeTests" --nologo -v minimal
```

Expected: the former red conservation and nested-frame tests now pass.

- [ ] **Step 6: Commit**

Commit with message `feat(gas): derive conserved tree from journal events`.

---

### Task 5: Define stable journal-derived DTOs and assembler

**Files:**
- Create: `Schlieren.Core/Execution/Journal/JournalTraceDtos.cs`
- Create: `Schlieren.Core/Execution/Journal/JournalTraceAssembler.cs`
- Create: `Schlieren.Tests/Execution/JournalTraceAssemblerTests.cs`

- [ ] **Step 1: Add failing DTO projection tests**

Build a nested journal fixture and assert the assembled shape contains:

- execution success/error/gas/return data;
- raw events with stable `kind` discriminators and sequence/frame identity;
- explicit root/child frame summaries;
- one step per `OpcodeGasEvent`, ordered by sequence;
- the journal gas tree and conservation result;
- stack/memory/storage present by default;
- each disable option removes only its selected snapshot field.

- [ ] **Step 2: Verify RED**

Run `JournalTraceAssemblerTests`; expect missing DTO/assembler types.

- [ ] **Step 3: Add transport-neutral DTO records**

Define explicit records rather than serializing internal polymorphic events:

```csharp
public sealed record JournalTraceOptions(
    bool DisableStack = false,
    bool DisableMemory = false,
    bool DisableStorage = false);

public sealed record JournalTraceDto(
    bool Ok,
    string Fork,
    JournalExecutionDto Execution,
    IReadOnlyList<JournalEventDto> Events,
    IReadOnlyList<JournalFrameDto> Frames,
    IReadOnlyList<JournalStepDto> Steps,
    JournalGasNode GasTree,
    JournalConservation Conservation);
```

Use nullable snapshot properties so disabled fields are omitted by the RPC serializer. Hex-encode byte arrays and large EVM values consistently. Keep gas quantities in the representation approved by existing RPC conventions.

- [ ] **Step 4: Implement a pure assembler**

`JournalTraceAssembler.FromCanonical(fork, result, options)` validates that `result.Journal` exists, builds frames from enter/exit events, maps each event through an exhaustive type switch, builds steps from opcode events, and invokes `JournalGasTree.Build`. It must not inspect `result.TraceSteps`.

- [ ] **Step 5: Verify GREEN and deterministic JSON**

Run assembler tests twice and compare serialized output to prove stable ordering.

- [ ] **Step 6: Commit**

Commit with message `feat(trace): assemble journal-native trace DTOs`.

---

### Task 6: Add `schlieren_traceJournal` with ephemeral-code execution

**Files:**
- Modify: `Schlieren.RPC/RpcRouter.cs`
- Modify: `Schlieren.RPC/Handlers/EthHandlers.cs`
- Create: `Schlieren.RPC/Handlers/JournalTraceRequestParser.cs`
- Create: `Schlieren.Tests/RPC/JournalTraceRpcTests.cs`
- Modify or create: legacy RPC JSON golden tests under `Schlieren.Tests/RPC/`

- [ ] **Step 1: Freeze legacy endpoint JSON before adding the method**

Add golden serialization tests that invoke `debug_inspect` and `debug_traceCall` with fixed fixtures, recursively assert their complete property sets, and compare canonicalized JSON to checked-in expected strings. These tests must pass before production changes.

- [ ] **Step 2: Add failing new-method tests**

Cover:

1. router registration for `schlieren_traceJournal`;
2. missing `code` loads code from `to`;
3. present `code` executes at `to` but base-state code remains unchanged afterward;
4. overlay is discarded after REVERT and exceptional failure;
5. nested CALL response has explicit parent/child IDs and child-owned opcodes;
6. default response contains stack/memory/storage;
7. each disable flag omits only its corresponding field;
8. malformed address, hex, fork, flag, or missing `to` returns JSON-RPC invalid params;
9. conservation and gas tree come from the journal assembler.

- [ ] **Step 3: Verify RED**

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~JournalTraceRpcTests" --nologo -v minimal
```

- [ ] **Step 4: Parse one explicit request object**

Accept exactly one object with fields:

```text
from, to, gas, gasPrice, value, data, code?, fork, nonce,
disableStack, disableMemory, disableStorage
```

Require `to` whenever `code` is supplied. Default flags to false. Reject unknown structural types and malformed hex as invalid params. Keep parsing isolated from legacy debug parsers.

- [ ] **Step 5: Execute canonically in a discarded overlay**

In the new handler:

- create a `StateOverlay` over the current base state;
- if `code` exists, call `overlay.SetCode(to, codeBytes)` only;
- create a simulation transaction with `EnableJournal = true`, `EnableTracing = false`, and simulation authorization;
- invoke canonical `StateTransition` with `commit: false` against the overlay;
- assemble with `JournalTraceAssembler` and return the DTO;
- discard the overlay on every exit path.

- [ ] **Step 6: Register only the new method**

Add the router dispatch and method listing for `schlieren_traceJournal`. Do not edit the dispatch or response builders for `debug_inspect` or `debug_traceCall`.

- [ ] **Step 7: Verify new and legacy contracts**

Run new RPC tests and legacy golden tests. Expected: all pass and the old canonicalized JSON is identical.

- [ ] **Step 8: Commit**

Commit with message `feat(rpc): add journal trace endpoint`.

---

### Task 7: Migrate the React engine to the journal endpoint

**Files:**
- Modify: `schlieren-ui/package.json`
- Modify: `schlieren-ui/package-lock.json`
- Modify: `schlieren-ui/src/engine/store.ts`
- Modify: `schlieren-ui/src/engine/rpc.ts`
- Create: `schlieren-ui/src/engine/rpc.test.ts`
- Create: `schlieren-ui/src/engine/journal.ts`
- Create: `schlieren-ui/src/engine/journal.test.ts`

- [ ] **Step 1: Install Vitest and add the test script**

Add `"test": "vitest run"` and a Vite-compatible Vitest dev dependency. Let the package manager update only the UI lockfile.

- [ ] **Step 2: Write failing RPC client tests**

Mock `fetch`, populate Workbench input in the Zustand store, call `executeTrace`, and assert:

- exactly one JSON-RPC request is issued;
- method is `schlieren_traceJournal`;
- optional bytecode is sent as `code` in the same request;
- no `anvil_setCode` or `debug_traceCall` request occurs;
- disable flags default false;
- returned frames, events, steps, tree, and conservation populate the result.

- [ ] **Step 3: Define frontend journal DTOs**

Replace the struct-log-derived `ExecutionResult` model with DTOs mirroring the new endpoint. Keep `cursor` as an index into `steps`. Include `frameId`, `parentFrameId`, `semantics`, and optional stack/memory/storage on each step.

- [ ] **Step 4: Replace the RPC workflow**

Delete the `anvil_setCode` preflight and `debug_traceCall` mapping. Send one request object to `schlieren_traceJournal` and validate the response with focused runtime guards in `journal.ts` before storing it.

- [ ] **Step 5: Verify GREEN**

```powershell
npm test -- --runInBand
npm run build
```

Run from `schlieren-ui`. If Vitest rejects `--runInBand`, use `npm test`; do not add Jest-only flags to package scripts.

- [ ] **Step 6: Commit**

Commit only Task 7 UI engine/package files with message `feat(ui): consume journal trace RPC`.

---

### Task 8: Migrate React trace, machine, frame, and gas views

**Files:**
- Modify: `schlieren-ui/src/views/Trace/TracePanel.tsx`
- Modify: `schlieren-ui/src/views/MachineState/MachineState.tsx`
- Modify: `schlieren-ui/src/views/Flow/Flow.tsx`
- Modify associated CSS files only as required
- Create: `schlieren-ui/src/engine/journal-view.ts`
- Create: `schlieren-ui/src/engine/journal-view.test.ts`

- [ ] **Step 1: Add failing pure view-model tests**

Test helpers that:

- map the cursor to the correct journal step and frame;
- flatten frame relationships without using depth inference;
- preserve child ownership of nested opcodes;
- map `Charge`, `Credit`, and `None` to distinct gas-tree presentation states;
- expose a visible warning when `conservation.isConserved` is false.

- [ ] **Step 2: Verify RED**

Run `npm test`; expect missing view helpers.

- [ ] **Step 3: Update Trace and Machine State**

Render journal steps directly. Trace rows use `frameId` and semantic gas effect for heat coloring. Machine State reads the selected step's optional stack/memory/storage and displays a clear “not requested” state only when the corresponding field was disabled.

- [ ] **Step 4: Implement frame-aware Flow and gas tree presentation**

Replace placeholder/depth-derived flow content with explicit `frames` parent-child relationships. Render the supplied `gasTree` recursively, including charges, credits, and non-additive evidence. Surface derived gas, settled gas, signed delta, and conservation state.

- [ ] **Step 5: Verify GREEN and build**

```powershell
npm test
npm run lint
npm run build
```

Expected: tests, lint, and production build pass. Do not edit the unrelated dirty Harvest view.

- [ ] **Step 6: Commit**

Commit with message `feat(ui): render frame-aware journal traces`.

---

### Task 9: Document the endpoint and complete regression verification

**Files:**
- Modify: `README.md`
- Create: `docs/rpc/schlieren_traceJournal.md`
- Modify: an existing RPC index document if one exists
- Modify tests only if verification exposes a real defect; never weaken an assertion

- [ ] **Step 1: Document the user-facing workflow**

Update the root README with:

- what the typed journal makes possible;
- how exclusive gas conservation works;
- how explicit frame identity differs from depth inference;
- React Workbench startup and endpoint expectations;
- the compatibility guarantee for Avalonia and legacy debug endpoints.

Document the full request and response, optional ephemeral `code`, default snapshot behavior, disable flags, component names, gas semantics, signed conservation delta, and JSON-RPC error behavior. Include one nested-CALL example and one ephemeral-code example.

- [ ] **Step 2: Run focused backend gates**

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~ExecutionJournal|FullyQualifiedName~GasTraceInvariantTests|FullyQualifiedName~JournalGasTreeTests|FullyQualifiedName~JournalTraceAssemblerTests|FullyQualifiedName~JournalTraceRpcTests" --nologo -v minimal
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~InspectDtoJsonTests|FullyQualifiedName~DebugTrace" --nologo -v minimal
```

- [ ] **Step 3: Run the full backend and frontend gates**

```powershell
dotnet build Schlieren.sln --nologo
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --nologo -v minimal
```

Then from `schlieren-ui`:

```powershell
npm test
npm run lint
npm run build
```

If the repository's full EELS tests still require unavailable fixtures, report that separately with the exact fixture error; do not conflate it with journal failures.

- [ ] **Step 4: Check scope and legacy contracts**

```powershell
git diff --check
git status --short
git diff --name-only HEAD
```

Confirm no unrelated dirty files are staged, no Avalonia view was migrated, the two old endpoint goldens are unchanged, and no `anvil_setCode` or `debug_traceCall` call remains in the React execution path.

- [ ] **Step 5: Commit documentation**

Commit documentation only with message `docs: explain journal trace and gas semantics`.

## Final review checklist

- The old intentionally failing gas conservation and nested-frame tests are green for semantic reasons, not weakened assertions.
- A real nested CALL has explicit parent/child frame IDs, and child opcodes carry the child ID.
- CALL-family opcode events remain explicitly non-additive; ordinary opcode charges remain additive.
- Exceptional frame burns and effective refund credits are represented exactly once.
- Gas-tree totals are derived only from journal events and conserve against canonical settlement.
- No synthetic residual or reconciliation bucket exists.
- `schlieren_traceJournal` supports stored code and optional ephemeral code without persistent state mutation.
- Stack, memory, and storage are present by default and independently suppressible.
- React performs one journal RPC request and renders frame-aware steps, state, flow, gas, and conservation.
- `debug_inspect` and `debug_traceCall` retain their exact existing JSON contracts.
- Avalonia remains compatible and untouched.
- README and RPC documentation describe the functionality and examples.
- All scoped tests/builds pass; external fixture blockers are reported precisely.
