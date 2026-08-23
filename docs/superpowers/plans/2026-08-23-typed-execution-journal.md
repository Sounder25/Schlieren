# Typed Execution Journal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in typed execution journal with explicit frame identity and gas semantics, instrumented through canonical `StateTransition` and `EvmMachine` execution without changing EVM behavior.

**Architecture:** One `ExecutionJournal` is created at canonical transaction entry and passed through every recursive frame. `StateTransition` records transaction, frame, refund, and settlement facts; `EvmMachine` records opcode deltas and exceptional burns. Existing struct logs, gas calculations, state transitions, RPC shapes, gas tree, and diagnosis remain unchanged.

**Tech Stack:** C# 12, .NET 8, xUnit, Schlieren Core and Tests projects

**Spec:** `docs/superpowers/specs/2026-08-23-typed-execution-journal-design.md`

## Global Constraints

- Collection is opt-in through `Transaction.EnableJournal`; default is `false`.
- Do not couple the journal to `Transaction.EnableTracing`.
- Do not edit `SystemOpcodes`, RPC, UI, gas-tree, or causal-diagnosis files.
- Do not change gas charging, forwarding, stipend, refund, settlement, state, trace ordering, or exception behavior.
- CALL-family opcode deltas are `InclusiveFrameDelta`; ordinary completed opcode deltas are `ExclusiveCharge`.
- Exceptional remaining-gas consumption is represented once as `ExceptionalBurn`.
- The two tests in `GasTraceInvariantTests` remain intentionally red until journal-to-gas-tree migration.
- Use red-green-refactor for each task and commit only that task's files.

## File map

- Create `Schlieren.Core/Execution/Journal/ExecutionJournal.cs`: event records, gas semantics, frame IDs, sequence allocation, read-only storage.
- Create `Schlieren.Tests/Execution/ExecutionJournalTests.cs`: model, interpreter, integration, ordering, and parity tests.
- Modify `Schlieren.Core/State/Models.cs`: opt-in flag.
- Modify `Schlieren.Core/Execution/ExecutionResult.cs`: optional journal output.
- Modify `Schlieren.Core/Execution/ExecutionContext.cs`: current journal/frame identity.
- Modify `Schlieren.Core/Execution/EvmMachine.cs`: opcode and burn events.
- Modify `Schlieren.Core/Execution/StateTransition.cs`: canonical journal lifecycle and propagation.

---

### Task 1: Typed journal model and opt-in plumbing

**Files:**
- Create: `Schlieren.Core/Execution/Journal/ExecutionJournal.cs`
- Create: `Schlieren.Tests/Execution/ExecutionJournalTests.cs`
- Modify: `Schlieren.Core/State/Models.cs:133-141`
- Modify: `Schlieren.Core/Execution/ExecutionResult.cs:23-48`

**Interfaces:**
- Consumes: `Address`, `CallType`, and `EvmError`.
- Produces: `ExecutionJournal`, typed events, `GasSemantics`, `Transaction.EnableJournal`, and `ExecutionResult.Journal`.

- [ ] **Step 1: Write the failing model tests**

Create `ExecutionJournalTests.cs`:

```csharp
using Schlieren.Core.Execution.Journal;

namespace Schlieren.Tests.Execution;

public sealed class ExecutionJournalTests
{
    [Fact]
    public void Journal_AssignsStableFrameIdsAndStrictEventSequence()
    {
        var journal = new ExecutionJournal();
        var root = journal.OpenFrame(null);
        var child = journal.OpenFrame(root);
        journal.Record(new TransactionStartedEvent { GasLimit = 100_000, IsInternal = false });
        journal.Record(new TransactionStartedEvent { GasLimit = 50_000, IsInternal = true });

        Assert.Equal(1, root);
        Assert.Equal(2, child);
        Assert.Equal([0L, 1L], journal.Events.Select(e => e.Sequence));
        Assert.False(journal.Events is List<ExecutionJournalEvent>);
    }

    [Fact]
    public void JournalFlags_DefaultToDisabledAndAbsent()
    {
        Assert.False(new Transaction().EnableJournal);
        Assert.Null(ExecutionResult.Success(0).Journal);
    }
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~ExecutionJournalTests" --nologo -v minimal
```

Expected: compilation fails because the journal namespace and opt-in properties do not exist.

- [ ] **Step 3: Implement the event model and recorder**

Create `ExecutionJournal.cs` with:

```csharp
using System.Collections.ObjectModel;
using Schlieren.Core.Primitives;

namespace Schlieren.Core.Execution.Journal;

public enum GasSemantics
{
    ExclusiveCharge, InclusiveFrameDelta, Allocation, Return,
    RefundCounter, Credit, ExceptionalBurn, Observation
}

public abstract record ExecutionJournalEvent
{
    public long Sequence { get; internal init; }
    public long? FrameId { get; init; }
    public long? ParentFrameId { get; init; }
}

public sealed record TransactionStartedEvent : ExecutionJournalEvent
{
    public required ulong GasLimit { get; init; }
    public required bool IsInternal { get; init; }
}

public sealed record IntrinsicGasChargedEvent : ExecutionJournalEvent
{
    public required ulong Amount { get; init; }
    public GasSemantics Semantics => GasSemantics.ExclusiveCharge;
}

public sealed record FrameEnteredEvent : ExecutionJournalEvent
{
    public required int Depth { get; init; }
    public required CallType CallType { get; init; }
    public required Address ContractAddress { get; init; }
    public Address? CodeAddress { get; init; }
    public required ulong GasLimit { get; init; }
    public GasSemantics Semantics => GasSemantics.Allocation;
}

public sealed record OpcodeGasEvent : ExecutionJournalEvent
{
    public required int Pc { get; init; }
    public required byte Opcode { get; init; }
    public required string Name { get; init; }
    public required ulong GasBefore { get; init; }
    public required ulong GasAfter { get; init; }
    public required ulong Amount { get; init; }
    public required GasSemantics Semantics { get; init; }
}

public sealed record ExceptionalGasBurnedEvent : ExecutionJournalEvent
{
    public required int Pc { get; init; }
    public required string Opcode { get; init; }
    public required ulong Amount { get; init; }
    public required EvmError Error { get; init; }
    public GasSemantics Semantics => GasSemantics.ExceptionalBurn;
}

public sealed record RefundCounterChangedEvent : ExecutionJournalEvent
{
    public required long Previous { get; init; }
    public required long Current { get; init; }
    public long Delta => Current - Previous;
    public GasSemantics Semantics => GasSemantics.RefundCounter;
}

public sealed record FrameExitedEvent : ExecutionJournalEvent
{
    public required int Depth { get; init; }
    public required bool Success { get; init; }
    public required EvmError Error { get; init; }
    public required ulong GasUsed { get; init; }
    public required ulong GasRemaining { get; init; }
    public GasSemantics Semantics => GasSemantics.Return;
}

public sealed record EffectiveGasRefundedEvent : ExecutionJournalEvent
{
    public required ulong GrossGasUsed { get; init; }
    public required ulong RefundCap { get; init; }
    public required ulong Amount { get; init; }
    public GasSemantics Semantics => GasSemantics.Credit;
}

public sealed record TransactionSettledEvent : ExecutionJournalEvent
{
    public required ulong ChargedGas { get; init; }
    public required ulong UnusedGasReturned { get; init; }
}

public sealed class ExecutionJournal
{
    private readonly List<ExecutionJournalEvent> _events = new();
    private readonly ReadOnlyCollection<ExecutionJournalEvent> _view;
    private long _nextFrameId = 1;
    private long _nextSequence;

    public ExecutionJournal() => _view = _events.AsReadOnly();
    public IReadOnlyList<ExecutionJournalEvent> Events => _view;

    internal long OpenFrame(long? parentFrameId)
    {
        _ = parentFrameId;
        return checked(_nextFrameId++);
    }

    internal void Record(ExecutionJournalEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _events.Add(entry with { Sequence = checked(_nextSequence++) });
    }
}
```

- [ ] **Step 4: Add opt-in properties**

Add to `Transaction`:

```csharp
public bool EnableJournal { get; set; }
```

Add to `ExecutionResult` with the journal namespace imported:

```csharp
public ExecutionJournal? Journal { get; init; }
```

Keep existing result factories unchanged; attach journals with record `with` expressions.

- [ ] **Step 5: Verify GREEN and commit**

Run the Task 1 filter again; expect 2 passed. Then:

```powershell
git add -- Schlieren.Core/Execution/Journal/ExecutionJournal.cs Schlieren.Core/State/Models.cs Schlieren.Core/Execution/ExecutionResult.cs Schlieren.Tests/Execution/ExecutionJournalTests.cs
git commit -m "feat(trace): add typed execution journal model"
```

---

### Task 2: EvmMachine opcode and exceptional-burn events

**Files:**
- Modify: `Schlieren.Core/Execution/ExecutionContext.cs:137-153`
- Modify: `Schlieren.Core/Execution/EvmMachine.cs:25-135`
- Modify: `Schlieren.Tests/Execution/ExecutionJournalTests.cs`

**Interfaces:**
- Consumes: Task 1 journal types.
- Produces: context frame identity and interpreter events with explicit semantics.

- [ ] **Step 1: Add failing interpreter tests**

Add tests that execute real components:

```csharp
[Fact]
public async Task EvmMachine_RecordsOrdinaryOpcodeAsExclusiveCharge()
{
    var journal = new ExecutionJournal();
    var frame = journal.OpenFrame(null);
    var context = new ExecutionContext
    {
        Code = [0x60, 0x01], GasLimit = 100,
        Journal = journal, JournalFrameId = frame
    };

    var result = await new EvmMachine([new OpcodePush1()]).ExecuteAsync(context);

    Assert.True(result.IsSuccess);
    var charge = Assert.Single(journal.Events.OfType<OpcodeGasEvent>());
    Assert.Equal(("PUSH1", 3UL, GasSemantics.ExclusiveCharge),
        (charge.Name, charge.Amount, charge.Semantics));
    Assert.Equal(frame, charge.FrameId);
}

[Fact]
public async Task EvmMachine_RecordsUnknownOpcodeAsExceptionalBurn()
{
    var journal = new ExecutionJournal();
    var frame = journal.OpenFrame(null);
    var context = new ExecutionContext
    {
        Code = [0xfe], GasLimit = 65_535,
        Journal = journal, JournalFrameId = frame
    };

    var result = await new EvmMachine([]).ExecuteAsync(context);

    Assert.Equal(EvmError.InvalidOpcode, result.Error);
    var burn = Assert.Single(journal.Events.OfType<ExceptionalGasBurnedEvent>());
    Assert.Equal(65_535UL, burn.Amount);
    Assert.Equal(GasSemantics.ExceptionalBurn, burn.Semantics);
}

[Fact]
public async Task EvmMachine_RecordsThrownOutOfGasAsOneExceptionalBurn()
{
    var journal = new ExecutionJournal();
    var frame = journal.OpenFrame(null);
    var context = new ExecutionContext
    {
        Code = [0x51], GasLimit = 2,
        Journal = journal, JournalFrameId = frame
    };
    context.Stack.Push(0);

    var result = await new EvmMachine([new OpcodeMload()]).ExecuteAsync(context);

    Assert.Equal(EvmError.OutOfGas, result.Error);
    var burn = Assert.Single(journal.Events.OfType<ExceptionalGasBurnedEvent>());
    Assert.Equal(2UL, burn.Amount);
    Assert.Single(journal.Events.OfType<OpcodeGasEvent>(),
        e => e.Semantics == GasSemantics.Observation && e.Amount == 0);
}
```

Add this CALL test and helpers:

```csharp
[Fact]
public async Task EvmMachine_RecordsCallAsInclusiveFrameDelta()
{
    var journal = new ExecutionJournal();
    var frame = journal.OpenFrame(null);
    var callee = Address.FromHex("0x0000000000000000000000000000000000001000");
    var context = new ExecutionContext
    {
        Code = [0xF1],
        ContractAddress = Address.FromHex("0x0000000000000000000000000000000000002000"),
        Caller = Address.Zero,
        GlobalState = new GlobalState(),
        GasLimit = 100_000,
        Block = new BlockContext { Rules = ForkRulesFactory.For("Osaka") },
        SubCall = (_, _, _, _) => Task.FromResult(ExecutionResult.Success(0)),
        Journal = journal,
        JournalFrameId = frame
    };
    context.Access.WarmAddress(callee);
    PushCallArguments(context);

    var result = await new EvmMachine([new OpcodeCall()]).ExecuteAsync(context);

    Assert.True(result.IsSuccess);
    var charge = Assert.Single(journal.Events.OfType<OpcodeGasEvent>());
    Assert.Equal("CALL", charge.Name);
    Assert.Equal(GasSemantics.InclusiveFrameDelta, charge.Semantics);
}

private static void PushCallArguments(ExecutionContext context)
{
    context.Stack.Push(0);
    context.Stack.Push(0);
    context.Stack.Push(0);
    context.Stack.Push(0);
    context.Stack.Push(0);
    context.Stack.Push(0x1000);
    context.Stack.Push(10_000);
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~ExecutionJournalTests.EvmMachine" --nologo -v minimal
```

Expected: compilation fails because context journal properties are missing.

- [ ] **Step 3: Add context properties**

Import the journal namespace and add next to `CaptureTrace`:

```csharp
public ExecutionJournal? Journal { get; init; }
public long? JournalFrameId { get; init; }
public long? JournalParentFrameId { get; init; }
```

- [ ] **Step 4: Add focused recorder helpers to EvmMachine**

Add a journal import, an inclusive-op set, and helpers:

```csharp
private static readonly HashSet<string> InclusiveCallOps =
[
    "CALL", "CALLCODE", "DELEGATECALL", "STATICCALL", "CREATE", "CREATE2"
];

private static void RecordOpcode(ExecutionContext c, int pc, byte code,
    string name, ulong before, ulong after, ulong amount, GasSemantics semantics)
{
    if (c.Journal is null || c.JournalFrameId is not long frame) return;
    c.Journal.Record(new OpcodeGasEvent
    {
        FrameId = frame, ParentFrameId = c.JournalParentFrameId,
        Pc = pc, Opcode = code, Name = name,
        GasBefore = before, GasAfter = after, Amount = amount,
        Semantics = semantics
    });
}

private static void RecordBurn(ExecutionContext c, int pc, string opcode,
    ulong amount, EvmError error)
{
    if (c.Journal is null || c.JournalFrameId is not long frame || amount == 0) return;
    c.Journal.Record(new ExceptionalGasBurnedEvent
    {
        FrameId = frame, ParentFrameId = c.JournalParentFrameId,
        Pc = pc, Opcode = opcode, Amount = amount, Error = error
    });
}
```

- [ ] **Step 5: Instrument interpreter branches without changing results**

After `actualGasUsed` is calculated:

```csharp
RecordOpcode(context, pc, opcodeByte, opcode.Name, gasBefore, gasAfter,
    actualGasUsed, InclusiveCallOps.Contains(opcode.Name)
        ? GasSemantics.InclusiveFrameDelta
        : GasSemantics.ExclusiveCharge);
```

Before returning a non-REVERT opcode failure, call `RecordBurn(..., gasAfter, execResult.Error)`. For an unknown opcode, burn `gasBefore`. In the OOG catch, record an `OpcodeGasEvent` with amount zero and `Observation`, followed by a burn of `gasBefore`. Do not record exceptional burn for REVERT.

- [ ] **Step 6: Verify GREEN and commit**

Run:

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~ExecutionJournalTests.EvmMachine|FullyQualifiedName~ExceptionalChildGasTests" --nologo -v minimal
```

Expect all selected tests to pass. Then commit the context, machine, and test file with message `feat(trace): journal opcode gas and exceptional burns`.

---

### Task 3: Canonical frame lifecycle and ancestry

**Files:**
- Modify: `Schlieren.Core/Execution/StateTransition.cs:21-547, 781-1064`
- Modify: `Schlieren.Tests/Execution/ExecutionJournalTests.cs`

**Interfaces:**
- Consumes: Tasks 1-2 journal and context types.
- Produces: one shared journal, explicit frame ancestry, and frame enter/exit events.

- [ ] **Step 1: Add a failing nested integration test**

Add this real nested fixture helper, then the test:

```csharp
private static (StateTransition transition, GlobalState state, Transaction tx, BlockContext block)
    BuildNestedTransaction(bool enableJournal)
{
    var state = new GlobalState();
    var callee = Address.FromHex("0x4000000000000000000000000000000000000004");
    var caller = Address.FromHex("0x5000000000000000000000000000000000000005");
    var sender = Address.FromHex("0x2000000000000000000000000000000000000002");
    state.SetCode(callee, [0x60, 0x01, 0x60, 0x00, 0x55, 0x00]);

    var callerCode = new List<byte>
    {
        0x60, 0x00, 0x60, 0x00, 0x60, 0x00, 0x60, 0x00, 0x60, 0x00, 0x73
    };
    callerCode.AddRange(callee.Bytes);
    callerCode.AddRange([0x61, 0x27, 0x10, 0xF1, 0x00]);
    state.SetCode(caller, callerCode.ToArray());

    var machine = new EvmMachine([
        new OpcodeStop(), new OpcodePush1(), new OpcodePush2(),
        new OpcodePush20(), new OpcodeSstore(), new OpcodeCall()
    ]);
    var tx = new Transaction
    {
        From = sender, To = caller, GasLimit = 200_000, GasPrice = 1,
        Authorization = TransactionAuthorization.Internal,
        EnableTracing = true, EnableJournal = enableJournal
    };
    var block = new BlockContext
    {
        BaseFeePerGas = 1,
        Rules = ForkRulesFactory.For("Osaka")
    };
    return (new StateTransition(machine), state, tx, block);
}
```

Then add:

```csharp
[Fact]
public async Task StateTransition_JournalOwnsNestedOpcodesByExplicitChildFrame()
{
    var f = BuildNestedTransaction(true);
    var result = await f.transition.ApplyTransactionAsync(f.tx, f.state, f.block, false);

    var journal = Assert.IsType<ExecutionJournal>(result.Journal);
    var enters = journal.Events.OfType<FrameEnteredEvent>().ToList();
    var root = Assert.Single(enters, e => e.ParentFrameId is null);
    var child = Assert.Single(enters, e => e.ParentFrameId == root.FrameId);
    var sstore = Assert.Single(journal.Events.OfType<OpcodeGasEvent>(), e => e.Name == "SSTORE");

    Assert.Equal(child.FrameId, sstore.FrameId);
    Assert.Equal(root.FrameId, sstore.ParentFrameId);
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~ExecutionJournalTests.StateTransition_JournalOwnsNestedOpcodesByExplicitChildFrame" --nologo -v minimal
```

Expected: failure because canonical execution does not create a journal.

- [ ] **Step 3: Create and attach the canonical journal**

At canonical `ApplyTransactionAsync` entry:

```csharp
var journal = tx.EnableJournal ? new ExecutionJournal() : null;
journal?.Record(new TransactionStartedEvent
{
    GasLimit = tx.GasLimit,
    IsInternal = tx.Authorization == TransactionAuthorization.Internal
});
ExecutionResult Finish(ExecutionResult value) =>
    journal is null ? value : value with { Journal = journal };
```

Wrap all early canonical returns with `Finish`, pass `journal` and null parent frame ID to the canonical `ExecuteInternalAsync` call, and change the final return to `Finish(result)`. Do not edit `ApplyTransactionWithFrameAsync`.

- [ ] **Step 4: Open and complete frames inside ExecuteInternalAsync**

Add parameters:

```csharp
ExecutionJournal? journal = null,
long? parentFrameId = null
```

After the depth guard, allocate `frameId`, compute `frameGasLimit`, `frameAddress`, and two distinct call-type values:

```csharp
var frameId = journal?.OpenFrame(parentFrameId);
var frameGasLimit = executionGasLimit ?? tx.GasLimit;
var frameAddress = creationAddress ?? tx.To ?? Address.Zero;
var executionCallType = DetermineCallType(creationAddress, codeAddress, isStatic);
var journalCallType = depth == 0 ? CallType.Root : executionCallType;
```

Record `FrameEnteredEvent` with `journalCallType`. Add a local `CompleteFrame(ExecutionResult value)` that records a net `RefundCounterChangedEvent` when nonzero, records exactly one `FrameExitedEvent`, and returns `value with { Journal = journal }`.

Use these exact exit quantities:

```csharp
GasUsed = value.GasUsed,
GasRemaining = frameGasLimit > value.GasUsed
    ? frameGasLimit - value.GasUsed
    : 0
```

Route the internal-value insufficient-funds return, precompile OOG, precompile success, and final EVM return through `CompleteFrame`.

- [ ] **Step 5: Propagate identity through context and recursion**

Add to the `ExecutionContext` initializer:

```csharp
Journal = journal,
JournalFrameId = frameId,
JournalParentFrameId = parentFrameId,
```

Use `executionCallType` in the existing `SetCallContext` call so legacy trace metadata does not change. Pass `journal: journal` and `parentFrameId: frameId` to recursive `ExecuteInternalAsync`.

- [ ] **Step 6: Verify GREEN and commit**

Run:

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~ExecutionJournalTests.StateTransition_JournalOwnsNestedOpcodesByExplicitChildFrame|FullyQualifiedName~DebugTraceTransaction_IncludesNestedDepthAndStorageDelta|FullyQualifiedName~BasicCallSemanticTests" --nologo -v minimal
```

Expect all selected tests to pass. Commit `StateTransition.cs` and the journal test file with message `feat(trace): journal canonical frame hierarchy`.

---

### Task 4: Intrinsic, effective-refund, and settlement events

**Files:**
- Modify: `Schlieren.Core/Execution/StateTransition.cs:75-105, 453-530`
- Modify: `Schlieren.Tests/Execution/ExecutionJournalTests.cs`

**Interfaces:**
- Consumes: Task 3 canonical journal lifecycle.
- Produces: intrinsic, effective-refund, and final-settlement events from existing canonical values.

- [ ] **Step 1: Add a failing settlement test**

Add this fresh-fixture helper:

```csharp
private static (StateTransition transition, GlobalState state, Transaction tx, BlockContext block)
    BuildSimpleTransaction(bool enableJournal)
{
    var state = new GlobalState();
    var sender = Address.FromHex("0x2000000000000000000000000000000000000002");
    var target = Address.FromHex("0x1000000000000000000000000000000000000001");
    state.SetBalance(sender, 1_000_000_000);
    state.SetCode(target, [0x00]);
    var tx = new Transaction
    {
        From = sender, To = target, Nonce = 0,
        GasLimit = 100_000, GasPrice = 1,
        Authorization = TransactionAuthorization.Impersonated,
        EnableTracing = true, EnableJournal = enableJournal
    };
    var block = new BlockContext
    {
        BaseFeePerGas = 1,
        Rules = ForkRulesFactory.For("Osaka")
    };
    return (new StateTransition(new EvmMachine([new OpcodeStop()])), state, tx, block);
}
```

Use `BuildSimpleTransaction(true)` and assert:

```csharp
var intrinsic = Assert.Single(result.Journal!.Events.OfType<IntrinsicGasChargedEvent>());
var settled = Assert.Single(result.Journal.Events.OfType<TransactionSettledEvent>());
Assert.Equal(21_000UL, intrinsic.Amount);
Assert.Equal(result.GasUsed, settled.ChargedGas);
Assert.Equal(tx.GasLimit - result.GasUsed, settled.UnusedGasReturned);
```

Add a refund-producing test using the same addresses and block:

```csharp
[Fact]
public async Task StateTransition_JournalRecordsRefundCounterAndEffectiveCredit()
{
    var f = BuildSimpleTransaction(true);
    var target = f.tx.To!.Value;
    f.state.SetStorageAt(target, 0, 1);
    f.state.SetCode(target, [0x60, 0x00, 0x60, 0x00, 0x55, 0x00]);
    var transition = new StateTransition(new EvmMachine([
        new OpcodeStop(), new OpcodePush1(), new OpcodeSstore()
    ]));

    var result = await transition.ApplyTransactionAsync(f.tx, f.state, f.block, true);

    Assert.True(result.IsSuccess);
    var counter = Assert.Single(result.Journal!.Events.OfType<RefundCounterChangedEvent>());
    var credit = Assert.Single(result.Journal.Events.OfType<EffectiveGasRefundedEvent>());
    Assert.True(counter.Delta > 0);
    Assert.True(credit.Amount > 0);
    Assert.True(credit.Amount <= credit.RefundCap);
}
```

- [ ] **Step 2: Verify RED**

Run the settlement test alone. Expect missing intrinsic/settlement events.

- [ ] **Step 3: Record accepted intrinsic gas**

After fork-aware intrinsic and calldata-floor validation succeeds:

```csharp
if (intrinsicGas > 0)
    journal?.Record(new IntrinsicGasChargedEvent { Amount = intrinsicGas });
```

- [ ] **Step 4: Record the already-calculated capped refund**

Preserve existing arithmetic while retaining `effectiveRefund`:

```csharp
ulong effectiveRefund = 0;
if (result.GasRefundCounter > 0)
{
    var maxRefund = (long)(totalGasUsed / block.Rules.RefundQuotient);
    var cappedRefund = Math.Min(result.GasRefundCounter, maxRefund);
    effectiveRefund = (ulong)cappedRefund;
    totalGasUsed -= effectiveRefund;
    journal?.Record(new EffectiveGasRefundedEvent
    {
        GrossGasUsed = totalGasUsed + effectiveRefund,
        RefundCap = (ulong)maxRefund,
        Amount = effectiveRefund
    });
}
```

- [ ] **Step 5: Record settlement**

Immediately before the existing final `GasUsed` replacement:

```csharp
journal?.Record(new TransactionSettledEvent
{
    ChargedGas = totalGasUsed,
    UnusedGasReturned = tx.GasLimit > totalGasUsed
        ? tx.GasLimit - totalGasUsed
        : 0
});
```

- [ ] **Step 6: Verify GREEN and commit**

Run the new settlement test plus `ChildRefundJournalTests` and `IntrinsicGasScheduleTests`; expect all selected tests to pass. Commit with message `feat(trace): journal intrinsic and settlement gas`.

---

### Task 5: Execution parity, ordering, and final verification

**Files:**
- Modify: `Schlieren.Tests/Execution/ExecutionJournalTests.cs`
- Modify production files from Tasks 1-4 only if a test exposes an instrumentation defect.

**Interfaces:**
- Consumes: complete journal implementation.
- Produces: proof that journaling is opt-in, behavior-preserving, correctly ordered, and honest on REVERT.

- [ ] **Step 1: Add disabled and parity tests**

Execute equivalent fresh STOP transactions with journaling off and on. Assert equality of `IsSuccess`, `Error`, `GasUsed`, `GasRefundCounter`, `ReturnData`, projected trace steps, sender balance, and sender nonce. Assert the off result has null journal and the on result has a journal.

Project each trace step as:

```csharp
static object TraceProjection(ExecutionTraceStep s) => new
{
    s.Pc, s.Op, s.Gas, s.GasCost, s.Depth,
    Stack = string.Join(",", s.Stack),
    Memory = string.Join(",", s.Memory),
    Storage = string.Join(",", s.Storage.OrderBy(x => x.Key))
};
```

- [ ] **Step 2: Add ordering and REVERT tests**

For the nested fixture, assert parent enter precedes child enter, child enter precedes child exit, and child exit precedes the parent CALL `OpcodeGasEvent`. Assert CALL semantics are inclusive. Execute a real REVERT frame and assert no `ExceptionalGasBurnedEvent` is recorded.

- [ ] **Step 3: Run the complete journal suite**

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~ExecutionJournalTests" --nologo -v minimal
```

Expected: all journal tests pass.

- [ ] **Step 4: Run the existing suite excluding intentional red tests**

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName!~GasTraceInvariantTests" --nologo -v minimal
```

Expected: 0 failures; report fresh pass/skip totals rather than assuming them.

- [ ] **Step 5: Reconfirm the legacy failures are unchanged**

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --filter "FullyQualifiedName~GasTraceInvariantTests" --nologo -v minimal
```

Expected: exactly two failures for the original reasons: charged gas 21,024 versus tree total 100,000, and no SSTORE in the reconstructed child frame.

- [ ] **Step 6: Build and check formatting**

```powershell
dotnet build Schlieren.sln --nologo
git diff --check
```

Expected: build exit 0 and diff check exit 0. Existing warnings may remain; journal files introduce no new warnings.

- [ ] **Step 7: Commit parity tests**

Commit only `ExecutionJournalTests.cs` with message `test(trace): prove journal execution parity`.

## Final review checklist

- Only planned files changed; `SystemOpcodes`, RPC, UI, gas tree, and diagnosis are untouched.
- Every completed test frame has one enter and one exit event.
- Child SSTORE carries the child frame ID and explicit parent ID.
- CALL events are non-additive; ordinary opcode events are additive.
- Journal-disabled execution allocates no journal and exposes null on the result.
- Journal-enabled and disabled execution/state/trace outputs are identical outside the journal.
- No test was skipped, weakened, or rewritten to hide a failure.
