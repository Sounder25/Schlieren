# Journal Security Reentrancy Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recover and advance reentrancy detection from canonical typed journal evidence and make the resulting proof-linked findings interactive in the React Workbench.

**Architecture:** Extend `JournalAnalysis` with immutable frame lifecycle/disposition facts, then rewrite only the reentrancy portion of `JournalSecurityAnalyzer` around explicit ancestry, typed storage effects, and resolution ordering. Preserve the existing journal RPC DTO shape; React renders server classifications and performs evidence navigation only.

**Tech Stack:** C# 12, .NET 8, xUnit, ASP.NET JSON-RPC, TypeScript 6, React 19, Zustand, Vitest 4, Vite 8.

**Spec:** `docs/superpowers/specs/2026-08-23-journal-security-reentrancy-recovery-design.md`

## Global Constraints

- Execute inline in `C:\projects\Schlieren\.worktrees\journal-legacy-recovery`; do not dispatch subagents.
- Do not modify the dirty `main` worktree or `C:\projects\Schlieren\.worktrees\journal-gas-tree-rpc-react`.
- Do not restore `ReentrancyDetector`, `LiveReentrancyDetector`, `ExecutionContext.OnStep` security callbacks, or `WorkbenchExecutionService`.
- Do not change `debug_inspect` or `debug_traceCall` JSON contracts.
- Do not add reentrancy ancestry, rollback, persistence, rule, or severity classification to TypeScript.
- Do not change storage-collision classification in Phase 4A.
- Keep one canonical `StateTransition` execution path and one journal-derived DTO assembler.
- Write and observe focused test failures before each production behavior change.
- Commit each completed task independently and stop after Phase 4A validation.

## File Structure

### Create

- `Schlieren.Tests/Execution/ReentrancyJournalFixture.cs` — deterministic real `A → B → A` bytecode/state fixture shared by execution and RPC tests.
- `Schlieren.Tests/Execution/ReentrancyJournalExecutionTests.cs` — real canonical-EVM reentrancy, post-write, rollback, and parity proofs.
- `schlieren-ui/src/views/Workbench/Diagnostics.test.tsx` — server-rendered Diagnostics finding/empty-state assertions.

### Modify

- `Schlieren.Core/Execution/Journal/JournalAnalysis.cs` — frame entry/resolution sequences and frame-level execution/persistence disposition.
- `Schlieren.Core/Execution/Journal/JournalSecurityAnalyzer.cs` — journal-native observed/state-contact/post-write rules.
- `Schlieren.Tests/Execution/JournalDispositionTests.cs` — lifecycle/disposition red-green tests.
- `Schlieren.Tests/Security/JournalSecurityAnalyzerTests.cs` — rule, severity, evidence, rollback, deduplication, and false-positive tests.
- `Schlieren.Tests/RPC/JournalTraceRpcTests.cs` — end-to-end RPC finding and frame-tree links.
- `schlieren-ui/src/engine/journal-view.ts` — evidence-to-step navigation helper; no classification.
- `schlieren-ui/src/engine/journal-view.test.ts` — navigation and server-link traversal tests.
- `schlieren-ui/src/views/Workbench/Diagnostics.tsx` — finding cards and seek behavior.
- `schlieren-ui/src/views/Workbench/Diagnostics.css` — severity-neutral layout plus server-severity visual classes.
- `Schlieren.Tests/Execution/CanonicalExecutionArchitectureTests.cs` — legacy detector disconnection checks if not already covered.

---

### Task 1: Frame Lifecycle Facts in Journal Analysis

**Files:**
- Modify: `Schlieren.Tests/Execution/JournalDispositionTests.cs`
- Modify: `Schlieren.Core/Execution/Journal/JournalAnalysis.cs`

**Interfaces:**
- Consumes: `FrameEnteredEvent.Sequence`, `FrameStateResolvedEvent.Sequence`, `TransactionPersistenceEvent.Outcome`, existing frame ancestry.
- Produces: `JournalFrameAnalysis.EntrySequence`, `ResolutionSequence`, `ExecutionDisposition`, and `PersistenceDisposition`.

- [ ] **Step 1: Add failing frame-lifecycle assertions**

Extend the existing tests and add the ancestor-rollback case:

```csharp
[Fact]
public void ParentRollback_PropagatesFrameDispositionAndPreservesLifecycleSequences()
{
    var journal = new ExecutionJournal();
    Enter(journal, 1, null, 0);
    Enter(journal, 2, 1, 1);
    Resolve(journal, 2, 1, FrameStateResolution.Commit);
    Resolve(journal, 1, null, FrameStateResolution.Rollback);
    Persist(journal, TransactionPersistenceOutcome.CommittedToState);

    var analysis = JournalAnalysis.Build(journal);
    var root = analysis.Frames[1];
    var child = analysis.Frames[2];

    Assert.True(root.EntrySequence < child.EntrySequence);
    Assert.True(child.EntrySequence < child.ResolutionSequence);
    Assert.Equal(ExecutionDisposition.Reverted, root.ExecutionDisposition);
    Assert.Equal(PersistenceDisposition.NotApplicable, root.PersistenceDisposition);
    Assert.Equal(ExecutionDisposition.Reverted, child.ExecutionDisposition);
    Assert.Equal(PersistenceDisposition.NotApplicable, child.PersistenceDisposition);
}
```

In `DryRunExecution_EmitsCompleteLifecycleAndDiscardedPersistence`, add:

```csharp
Assert.Equal(ExecutionDisposition.Survived, frame.ExecutionDisposition);
Assert.Equal(PersistenceDisposition.SimulationDiscarded, frame.PersistenceDisposition);
Assert.True(frame.EntrySequence < frame.ResolutionSequence);
```

- [ ] **Step 2: Run the focused tests and verify the compile failure**

Run:

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --no-restore --filter FullyQualifiedName~JournalDispositionTests
```

Expected: compilation fails because the four new `JournalFrameAnalysis` properties do not exist.

- [ ] **Step 3: Extend the immutable frame analysis model**

Change the record signature to:

```csharp
public sealed record JournalFrameAnalysis(
    long Id,
    long? ParentId,
    int Depth,
    CallType CallType,
    Address ContractAddress,
    Address? CodeAddress,
    FrameStateResolution Resolution,
    long EntrySequence,
    long ResolutionSequence,
    ExecutionDisposition ExecutionDisposition,
    PersistenceDisposition PersistenceDisposition,
    IReadOnlyList<long> AncestorIds);
```

Store the complete resolution event instead of only its enum:

```csharp
var resolutions = new Dictionary<long, FrameStateResolvedEvent>();
// ...
case FrameStateResolvedEvent resolved:
    var resolvedId = RequireFrameId(resolved, "ResolutionWithoutFrame");
    if (!resolutions.TryAdd(resolvedId, resolved))
        throw Error("DuplicateFrameResolution", $"Frame {resolvedId} has multiple resolutions.");
    break;
```

Add one local disposition function and use it for every frame:

```csharp
ExecutionDisposition FrameExecutionDisposition(long frameId)
{
    long? cursor = frameId;
    while (cursor.HasValue)
    {
        if (resolutions[cursor.Value].Resolution == FrameStateResolution.Rollback)
            return ExecutionDisposition.Reverted;
        cursor = entered[cursor.Value].ParentFrameId;
    }
    return ExecutionDisposition.Survived;
}

PersistenceDisposition FramePersistenceDisposition(long frameId)
{
    if (FrameExecutionDisposition(frameId) == ExecutionDisposition.Reverted)
        return PersistenceDisposition.NotApplicable;
    return persistence == TransactionPersistenceOutcome.CommittedToState
        ? PersistenceDisposition.CommittedToState
        : PersistenceDisposition.SimulationDiscarded;
}
```

Populate the new properties from `pair.Value.Sequence` and `resolutions[pair.Key].Sequence`. Update effect analysis to read `.Resolution` from the stored event and preserve its current behavior.

- [ ] **Step 4: Run lifecycle and invariant tests**

Run:

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --no-restore --filter "FullyQualifiedName~JournalDispositionTests|FullyQualifiedName~JournalAnalysisInvariantTests"
```

Expected: all selected tests pass.

- [ ] **Step 5: Commit Task 1**

```powershell
git add -- Schlieren.Core/Execution/Journal/JournalAnalysis.cs Schlieren.Tests/Execution/JournalDispositionTests.cs
git commit -m "feat(journal): derive frame lifecycle dispositions"
```

---

### Task 2: Typed Reentrancy Rules

**Files:**
- Modify: `Schlieren.Tests/Security/JournalSecurityAnalyzerTests.cs`
- Modify: `Schlieren.Core/Execution/Journal/JournalSecurityAnalyzer.cs`

**Interfaces:**
- Consumes: Task 1 `JournalFrameAnalysis` lifecycle properties and `JournalAnalysis.StateEffects`.
- Produces: deterministic `SecurityFinding` records with rule IDs `SEC.REENTRANCY.OBSERVED`, `SEC.REENTRANCY.STATE_CONTACT`, and `SEC.REENTRANCY.POST_WRITE`.

- [ ] **Step 1: Replace the old reentry assertion and add failing rule tests**

Update the existing reentry expectation from `SEC.REENTRANCY.REENTRY` to `SEC.REENTRANCY.STATE_CONTACT`. Add table-driven journal builders so each test controls call type, child resolution, transaction persistence, storage effects, and event order.

Add these exact behavioral tests:

```csharp
[Fact]
public void SameOwnerCallWithoutStateContact_IsObservedInformational()
{
    var journal = BuildReentryJournal(CallType.Call, childEffect: null,
        postWrite: null, childResolution: FrameStateResolution.Commit);

    var finding = Assert.Single(JournalSecurityAnalyzer.Analyze(JournalAnalysis.Build(journal)));

    Assert.Equal("SEC.REENTRANCY.OBSERVED", finding.RuleId);
    Assert.Equal(SecuritySeverity.Info, finding.Severity);
    Assert.Equal(2, finding.PrimaryFrameId);
    Assert.Null(finding.PrimaryInstructionId);
}

[Fact]
public void SameOwnerCallWithStorageRead_IsStateContactMedium()
{
    var journal = BuildReentryJournal(CallType.Call,
        childEffect: NewRead(frameId: 2, slot: 7), postWrite: null,
        childResolution: FrameStateResolution.Commit);

    var finding = Assert.Single(JournalSecurityAnalyzer.Analyze(JournalAnalysis.Build(journal)));

    Assert.Equal("SEC.REENTRANCY.STATE_CONTACT", finding.RuleId);
    Assert.Equal(SecuritySeverity.Medium, finding.Severity);
    Assert.Equal([new BigInteger(7)], finding.StorageSlots);
}

[Fact]
public void StaticCallStateContact_RemainsInformational()
{
    var journal = BuildReentryJournal(CallType.StaticCall,
        childEffect: NewRead(frameId: 2, slot: 7), postWrite: null,
        childResolution: FrameStateResolution.Commit);

    var finding = Assert.Single(JournalSecurityAnalyzer.Analyze(JournalAnalysis.Build(journal)));

    Assert.Equal("SEC.REENTRANCY.STATE_CONTACT", finding.RuleId);
    Assert.Equal(SecuritySeverity.Info, finding.Severity);
}

[Theory]
[InlineData(CallType.DelegateCall)]
[InlineData(CallType.CallCode)]
public void SharedStorageExecution_IsNotClassifiedAsReentrancy(CallType callType)
{
    var journal = BuildReentryJournal(callType,
        childEffect: NewWrite(frameId: 2, slot: 0), postWrite: null,
        childResolution: FrameStateResolution.Commit);

    Assert.Empty(JournalSecurityAnalyzer.Analyze(JournalAnalysis.Build(journal))
        .Where(finding => finding.Category == SecurityCategory.Reentrancy));
}

[Fact]
public void AncestorWriteAfterChildResolution_ProducesOneCriticalPostWrite()
{
    var journal = BuildReentryJournal(CallType.Call,
        childEffect: NewRead(frameId: 2, slot: 0),
        postWrite: NewWrite(frameId: 1, slot: 1),
        childResolution: FrameStateResolution.Commit);

    var findings = JournalSecurityAnalyzer.Analyze(JournalAnalysis.Build(journal));
    var critical = Assert.Single(findings, finding =>
        finding.RuleId == "SEC.REENTRANCY.POST_WRITE");

    Assert.Equal(SecuritySeverity.Critical, critical.Severity);
    Assert.Equal(1, critical.PrimaryFrameId);
    Assert.Equal([BigInteger.One], critical.StorageSlots);
    Assert.Equal(critical.SupportingEventSequences.Order(), critical.SupportingEventSequences);
}
```

Use these typed helpers for the test journals:

```csharp
private static readonly Address Target =
    Address.FromHex("0x9100000000000000000000000000000000000001");
private static readonly Address Other =
    Address.FromHex("0x9200000000000000000000000000000000000002");

private static ExecutionJournal BuildReentryJournal(
    CallType childType,
    StateEffectEvent? childEffect,
    StorageWriteEvent? postWrite,
    FrameStateResolution childResolution,
    bool postWriteBeforeChild = false,
    bool differentChildAddress = false,
    TransactionPersistenceOutcome persistence = TransactionPersistenceOutcome.CommittedToState)
{
    var journal = new ExecutionJournal();
    Enter(journal, 1, null, 0, CallType.Root, Target);
    if (postWriteBeforeChild && postWrite is not null)
        journal.Record(postWrite);
    Enter(journal, 2, 1, 1, childType, differentChildAddress ? Other : Target);
    if (childEffect is not null)
        journal.Record(childEffect);
    Resolve(journal, 2, 1, childResolution);
    if (!postWriteBeforeChild && postWrite is not null)
        journal.Record(postWrite);
    Resolve(journal, 1, null, FrameStateResolution.Commit);
    journal.Record(new TransactionPersistenceEvent { Outcome = persistence });
    return journal;
}

private static StorageReadEvent NewRead(long frameId, BigInteger slot) => new()
{
    Scope = StateEffectScope.Frame,
    FrameId = frameId,
    ParentFrameId = frameId == 1 ? null : 1,
    InstructionId = 10 + frameId,
    Pc = 3,
    Opcode = 0x54,
    StorageAddress = Target,
    Slot = slot,
    Value = 1,
    IsWarm = true
};

private static StorageWriteEvent NewWrite(long frameId, BigInteger slot) => new()
{
    Scope = StateEffectScope.Frame,
    FrameId = frameId,
    ParentFrameId = frameId == 1 ? null : 1,
    InstructionId = 20 + frameId,
    Pc = 4,
    Opcode = 0x55,
    StorageAddress = Target,
    Slot = slot,
    OriginalValue = 0,
    PreviousValue = 0,
    Value = 1,
    IsWarm = true
};

private static void Enter(
    ExecutionJournal journal, long id, long? parentId, int depth,
    CallType callType, Address contractAddress)
{
    journal.Record(new FrameEnteredEvent
    {
        FrameId = id, ParentFrameId = parentId, Depth = depth,
        CallType = callType, ContractAddress = contractAddress, GasLimit = 100_000
    });
    journal.Record(new FrameStateCheckpointEvent { FrameId = id, ParentFrameId = parentId });
}

private static void Resolve(
    ExecutionJournal journal, long id, long? parentId, FrameStateResolution resolution) =>
    journal.Record(new FrameStateResolvedEvent
    {
        FrameId = id, ParentFrameId = parentId, Resolution = resolution
    });
```

Add explicit tests using these helpers for a write before child entry, reverted child, simulation persistence, and a different child contract. Build a four-frame journal directly with `Enter` for `A(root) → B → A → A`; assert frames 3 and 4 each produce one base finding and frame 4 selects frame 3 as its nearest matching ancestor. Add two root writes after frame 2 resolves and assert one `POST_WRITE` finding with two sorted slots. Analyze the same journal twice and assert the complete finding arrays are equal.

- [ ] **Step 2: Run analyzer tests and verify failures**

Run:

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --no-restore --filter FullyQualifiedName~JournalSecurityAnalyzerTests
```

Expected: failures show the old `REENTRY` rule, missing observed/read cases, incorrect CALLCODE handling, and missing resolution-bound post-write behavior.

- [ ] **Step 3: Implement candidate selection from explicit ancestry**

Replace reentry candidate gating with:

```csharp
if (frame.CallType is not (CallType.Call or CallType.StaticCall))
    return;

var matchingAncestor = frame.AncestorIds
    .Reverse()
    .Select(id => analysis.Frames[id])
    .FirstOrDefault(ancestor => ancestor.ContractAddress.Equals(frame.ContractAddress));
if (matchingAncestor is null)
    return;
```

Order analyzed frames by `EntrySequence`, not numeric frame ID. Group direct state effects by `FrameId` and order them by `Effect.Sequence`.

- [ ] **Step 4: Implement the base finding**

Select typed storage contact only when its storage owner matches the re-entered frame:

```csharp
var contacts = frameEffects
    .Where(effect => effect.Effect switch
    {
        StorageReadEvent read => read.StorageAddress.Equals(frame.ContractAddress),
        StorageWriteEvent write => write.StorageAddress.Equals(frame.ContractAddress),
        _ => false
    })
    .ToArray();

var baseRule = contacts.Length == 0
    ? "SEC.REENTRANCY.OBSERVED"
    : "SEC.REENTRANCY.STATE_CONTACT";
var baseSeverity = frame.ExecutionDisposition == ExecutionDisposition.Reverted ||
                   frame.CallType == CallType.StaticCall ||
                   contacts.Length == 0
    ? SecuritySeverity.Info
    : SecuritySeverity.Medium;
```

Build deterministic evidence as `frame.EntrySequence` plus distinct sorted contact sequences. Use the first contact instruction when present. Collect distinct sorted slots from `StorageReadEvent` and `StorageWriteEvent`.

- [ ] **Step 5: Implement resolution-bound post-write analysis**

Select only direct effects of the nearest matching ancestor after the child resolution:

```csharp
var postWrites = ancestorEffects
    .Where(effect => effect.Effect is StorageWriteEvent write &&
                     write.StorageAddress.Equals(matchingAncestor.ContractAddress) &&
                     effect.Effect.Sequence > frame.ResolutionSequence)
    .ToArray();
```

Emit at most one `SEC.REENTRANCY.POST_WRITE` finding. Its primary frame is `matchingAncestor`; its primary instruction is the first post-write instruction; its evidence is child entry, child resolution, and distinct sorted write sequences. Severity is `Critical` only when both the candidate frame and the post-write effects survived; otherwise it is `Info`.

- [ ] **Step 6: Replace the effect-only finding factory**

Use a factory that accepts explicit proof facts:

```csharp
private static SecurityFinding CreateFinding(
    string id,
    string ruleId,
    SecurityCategory category,
    SecuritySeverity severity,
    JournalFrameAnalysis primaryFrame,
    long? instructionId,
    IEnumerable<long> evidenceSequences,
    ExecutionDisposition executionDisposition,
    PersistenceDisposition persistenceDisposition,
    IEnumerable<Address> addresses,
    IEnumerable<BigInteger> slots,
    string summary)
```

The caller supplies the complete deterministic `id`; the factory sorts and deduplicates evidence and slots. It deduplicates addresses and sorts them with `OrderBy(address => address.ToString(), StringComparer.Ordinal)`. Reentrancy callers construct IDs as:

```csharp
$"{ruleId}:frame-{reenteredFrame.Id}:event-{primaryEvidenceSequence}"
```

Keep `DiagnosisGrade.Proven` and the observed-path exploitability limitation from the specification. Adapt the storage-collision call explicitly as follows, preserving its current severity downgrade through the analyzed effect dispositions:

```csharp
var severity = effect.ExecutionDisposition == ExecutionDisposition.Reverted
    ? SecuritySeverity.Info
    : SecuritySeverity.Critical;
findings.Add(CreateFinding(
    $"SEC.STORAGE.DELEGATE_COLLISION:{effect.Effect.Sequence}",
    "SEC.STORAGE.DELEGATE_COLLISION",
    SecurityCategory.StorageCollision,
    severity,
    frame,
    effect.Effect.InstructionId,
    [effect.Effect.Sequence],
    effect.ExecutionDisposition,
    effect.PersistenceDisposition,
    [frame.ContractAddress, codeAddress],
    [write.Slot],
    $"Code at {codeAddress} wrote reserved slot 0x{write.Slot:x} in storage owned by {frame.ContractAddress}."));
```

- [ ] **Step 7: Run analyzer, lifecycle, and storage-collision tests**

Run:

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --no-restore --filter "FullyQualifiedName~JournalSecurityAnalyzerTests|FullyQualifiedName~JournalDispositionTests|FullyQualifiedName~StorageEffectJournalTests"
```

Expected: all selected tests pass and storage-collision tests remain unchanged.

- [ ] **Step 8: Commit Task 2**

```powershell
git add -- Schlieren.Core/Execution/Journal/JournalSecurityAnalyzer.cs Schlieren.Tests/Security/JournalSecurityAnalyzerTests.cs
git commit -m "feat(security): derive reentrancy proofs from journal frames"
```

---

### Task 3: Real Canonical EVM Reentrancy Proofs

**Files:**
- Create: `Schlieren.Tests/Execution/ReentrancyJournalFixture.cs`
- Create: `Schlieren.Tests/Execution/ReentrancyJournalExecutionTests.cs`

**Interfaces:**
- Produces: `ReentrancyJournalFixture.Install(GlobalState state, bool attackerReverts)` and `ReentrancyJournalFixture.Opcodes()` for Task 4.
- Verifies: canonical `StateTransition` produces the exact typed evidence consumed by Task 2.

- [ ] **Step 1: Create the shared bytecode fixture**

Use fixed addresses and build bytecode without an assembler dependency:

```csharp
internal static class ReentrancyJournalFixture
{
    internal static readonly Address Sender = Address.FromHex("0x1000000000000000000000000000000000000001");
    internal static readonly Address Target = Address.FromHex("0xa00000000000000000000000000000000000000a");
    internal static readonly Address Attacker = Address.FromHex("0xb00000000000000000000000000000000000000b");

    internal static void Install(GlobalState state, bool attackerReverts)
    {
        state.SetBalance(Sender, 10_000_000);
        state.SetCode(Target, BuildTargetCode());
        state.SetCode(Attacker, BuildCallerCode(Target, attackerReverts));
    }

    internal static IReadOnlyList<IOpcode> Opcodes() =>
    [
        new OpcodeStop(), new OpcodePush1(), new OpcodePush2(), new OpcodePush20(),
        new OpcodeSload(), new OpcodeSstore(), new OpcodePop(), new OpcodeJumpi(),
        new OpcodeJumpDest(), new OpcodeCall(), new OpcodeRevert(), new OpcodeMstore()
    ];

    private static byte[] BuildTargetCode()
    {
        var code = new List<byte> { 0x60, 0x00, 0x54, 0x60, 0x00, 0x57 };
        code.AddRange([0x60, 0x01, 0x60, 0x00, 0x55]);
        AddCall(code, Attacker);
        code.AddRange([0x60, 0x01, 0x60, 0x01, 0x55, 0x00]);
        code[4] = checked((byte)code.Count);
        code.AddRange([0x5b, 0x60, 0x00, 0x54, 0x50, 0x00]);
        return code.ToArray();
    }

    private static byte[] BuildCallerCode(Address target, bool revert)
    {
        var code = new List<byte>();
        AddCall(code, target);
        code.AddRange(revert
            ? [0x60, 0x00, 0x60, 0x00, 0xfd]
            : [0x00]);
        return code.ToArray();
    }

    private static void AddCall(List<byte> code, Address to)
    {
        code.AddRange([0x60, 0x00, 0x60, 0x00, 0x60, 0x00, 0x60, 0x00, 0x60, 0x00, 0x73]);
        code.AddRange(to.Bytes);
        code.AddRange([0x61, 0xc3, 0x50, 0xf1]);
    }
}
```

- [ ] **Step 2: Add real execution tests**

Create this helper that runs an impersonated Osaka transaction, then add the tests:

```csharp
private static async Task<(
    ExecutionResult Result,
    BigInteger Slot0,
    BigInteger Slot1)> Run(bool attackerReverts, bool enableJournal)
{
    var state = new GlobalState();
    ReentrancyJournalFixture.Install(state, attackerReverts);
    var result = await new StateTransition(new EvmMachine(ReentrancyJournalFixture.Opcodes()))
        .ApplyTransactionAsync(
            new Transaction
            {
                From = ReentrancyJournalFixture.Sender,
                To = ReentrancyJournalFixture.Target,
                GasLimit = 500_000,
                GasPrice = 1,
                Authorization = TransactionAuthorization.Impersonated,
                EnableJournal = enableJournal
            },
            state,
            new BlockContext { BaseFeePerGas = 1, Rules = ForkRulesFactory.For("Osaka") });
    return (
        result,
        await state.GetStorageAtAsync(ReentrancyJournalFixture.Target, 0),
        await state.GetStorageAtAsync(ReentrancyJournalFixture.Target, 1));
}
```

```csharp
[Fact]
public async Task RealAtoBtoA_ProducesStateContactAndCriticalPostWrite()
{
    var run = await Run(attackerReverts: false, enableJournal: true);
    var result = run.Result;
    var journal = Assert.IsType<ExecutionJournal>(result.Journal);
    var analysis = JournalAnalysis.Build(journal);
    var findings = JournalSecurityAnalyzer.Analyze(analysis);

    Assert.True(result.IsSuccess);
    Assert.Equal(3, analysis.Frames.Count);
    var reentered = Assert.Single(analysis.Frames.Values,
        frame => frame.Depth == 2 && frame.ContractAddress.Equals(ReentrancyJournalFixture.Target));
    Assert.Equal(CallType.Call, reentered.CallType);
    Assert.Contains(findings, finding =>
        finding.RuleId == "SEC.REENTRANCY.STATE_CONTACT" &&
        finding.PrimaryFrameId == reentered.Id);
    Assert.Contains(findings, finding =>
        finding.RuleId == "SEC.REENTRANCY.POST_WRITE" &&
        finding.Severity == SecuritySeverity.Critical &&
        finding.StorageSlots.Contains(BigInteger.One));
}

[Fact]
public async Task RevertedAttackerPath_IsVisibleButInformational()
{
    var run = await Run(attackerReverts: true, enableJournal: true);
    var result = run.Result;
    var findings = JournalSecurityAnalyzer.Analyze(JournalAnalysis.Build(result.Journal!));

    Assert.True(result.IsSuccess);
    Assert.All(findings.Where(finding => finding.Category == SecurityCategory.Reentrancy),
        finding => Assert.Equal(SecuritySeverity.Info, finding.Severity));
    Assert.Contains(findings, finding =>
        finding.ExecutionDisposition == ExecutionDisposition.Reverted);
}
```

Add journal-on/off parity by running the same successful fixture twice and comparing `Result.IsSuccess`, `Result.Error`, `Result.GasUsed`, `Result.ReturnData`, `Slot0`, and `Slot1`.

- [ ] **Step 3: Run the real execution tests**

Run:

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --no-restore --filter FullyQualifiedName~ReentrancyJournalExecutionTests
```

Expected: all real execution tests pass. If a test fails, fix only canonical instrumentation or Task 2 analysis; do not add a trace-derived fallback.

- [ ] **Step 4: Run nested-frame and conservation guards**

Run:

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --no-build --filter "FullyQualifiedName~ExplicitCallTypeJournalTests|FullyQualifiedName~GasTraceInvariantTests|FullyQualifiedName~StateTransitionJournalTests"
```

Expected: all selected tests pass.

- [ ] **Step 5: Commit Task 3**

```powershell
git add -- Schlieren.Tests/Execution/ReentrancyJournalFixture.cs Schlieren.Tests/Execution/ReentrancyJournalExecutionTests.cs
git commit -m "test(security): prove reentrancy on canonical EVM frames"
```

---

### Task 4: RPC Evidence and Frame-Tree Links

**Files:**
- Modify: `Schlieren.Tests/RPC/JournalTraceRpcTests.cs`

**Interfaces:**
- Consumes: Task 2 findings and Task 3 fixture.
- Preserves: `JournalSecurityFindingDto` and `JournalTraceDto` property sets.

- [ ] **Step 1: Add a failing end-to-end RPC test**

Expand `BuildFixture` to construct its machine from `ReentrancyJournalFixture.Opcodes()`. Preserve every opcode required by existing RPC tests. Add:

```csharp
[Fact]
public async Task RealReentry_ReturnsProofLinkedFindingsInPrebuiltFrameTree()
{
    var (state, router) = BuildFixture();
    ReentrancyJournalFixture.Install(state, attackerReverts: false);

    var response = await router.ProcessRequest("""
        {"jsonrpc":"2.0","id":5,"method":"schlieren_traceJournal","params":[{
          "from":"0x1000000000000000000000000000000000000001",
          "to":"0xa00000000000000000000000000000000000000a",
          "gas":"0x7a120",
          "fork":"Osaka"
        }]}
        """);

    using var document = JsonDocument.Parse(response);
    var result = document.RootElement.GetProperty("result");
    var findings = result.GetProperty("securityFindings").EnumerateArray().ToArray();
    var contact = Assert.Single(findings, finding =>
        finding.GetProperty("ruleId").GetString() == "SEC.REENTRANCY.STATE_CONTACT");
    var critical = Assert.Single(findings, finding =>
        finding.GetProperty("ruleId").GetString() == "SEC.REENTRANCY.POST_WRITE");

    Assert.Equal("critical", critical.GetProperty("severity").GetString());
    Assert.NotEmpty(contact.GetProperty("supportingEventSequences").EnumerateArray());
    Assert.True(FrameTreeContainsFinding(
        result.GetProperty("frameTree"),
        contact.GetProperty("id").GetString()!));
    Assert.True(FrameTreeContainsFinding(
        result.GetProperty("frameTree"),
        critical.GetProperty("id").GetString()!));
}
```

Implement `FrameTreeContainsFinding(JsonElement node, string id)` as recursive traversal of `securityFindingIds` and `children`; it must not reconstruct parents.

- [ ] **Step 2: Run the RPC test and observe the expected state**

Run:

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --no-restore --filter FullyQualifiedName~JournalTraceRpcTests
```

Expected during this task: pass without DTO changes because `JournalTraceAssembler` already associates findings to frame-tree nodes by `PrimaryFrameId`. A failure at that association is a design discrepancy: stop and report it before expanding Task 4 scope.

- [ ] **Step 3: Verify frozen debug contracts**

Run:

```powershell
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --no-build --filter "FullyQualifiedName~DebugInspectRpcTests|FullyQualifiedName~DebugTraceAdvancedRpcTests|FullyQualifiedName~NetworkAndTraceRpcTests"
```

Expected: all existing contract tests pass unchanged.

- [ ] **Step 4: Commit Task 4**

```powershell
git add -- Schlieren.Tests/RPC/JournalTraceRpcTests.cs
git commit -m "test(rpc): pin reentrancy evidence and frame links"
```

---

### Task 5: React Security Findings and Evidence Navigation

**Files:**
- Modify: `schlieren-ui/src/engine/journal-view.ts`
- Modify: `schlieren-ui/src/engine/journal-view.test.ts`
- Modify: `schlieren-ui/src/views/Workbench/Diagnostics.tsx`
- Modify: `schlieren-ui/src/views/Workbench/Diagnostics.css`
- Create: `schlieren-ui/src/views/Workbench/Diagnostics.test.tsx`

**Interfaces:**
- Consumes: existing `JournalSecurityFinding`, `JournalEvent`, `TraceStep`, and pre-built `frameTree` types.
- Produces: `findSecurityFindingStepIndex(...)` and interactive Diagnostics cards.

- [ ] **Step 1: Add failing pure navigation tests**

Add the helper import and tests:

```typescript
it('navigates a finding through its server instruction link', () => {
  const finding = makeFinding({ primaryFrameId: 7, primaryInstructionId: 99 });
  const events = [
    makeEvent({ kind: 'storageRead', sequence: 12, instructionId: 99, frameId: 7 }),
    makeEvent({ kind: 'opcodeGas', sequence: 13, instructionId: 99, frameId: 7 }),
  ];
  const steps = [makeStep({ sequence: 13, frameId: 7 })];

  expect(findSecurityFindingStepIndex(finding, events, steps)).toBe(0);
});

it('falls back to the first step in the server primary frame', () => {
  const finding = makeFinding({ primaryFrameId: 7, primaryInstructionId: null });
  const steps = [makeStep({ sequence: 4, frameId: 1 }), makeStep({ sequence: 8, frameId: 7 })];

  expect(findSecurityFindingStepIndex(finding, [], steps)).toBe(1);
});
```

Factories must return complete typed objects with neutral defaults and apply the supplied partial override.

- [ ] **Step 2: Run Vitest and verify the missing export failure**

Run:

```powershell
npm test -- src/engine/journal-view.test.ts
```

from `schlieren-ui`.

Expected: failure because `findSecurityFindingStepIndex` does not exist.

- [ ] **Step 3: Implement evidence navigation without classification**

Add:

```typescript
export function findSecurityFindingStepIndex(
  finding: JournalSecurityFinding,
  events: JournalEvent[],
  steps: TraceStep[],
): number | null {
  if (finding.primaryInstructionId !== null) {
    const opcodeEvent = events.find((event) =>
      event.kind === 'opcodeGas' && event.instructionId === finding.primaryInstructionId);
    if (opcodeEvent) {
      const linked = steps.findIndex((step) => step.sequence === opcodeEvent.sequence);
      if (linked >= 0) return linked;
    }
  }
  const firstInFrame = steps.findIndex((step) => step.frameId === finding.primaryFrameId);
  return firstInFrame >= 0 ? firstInFrame : null;
}
```

Import the three required types from `store`. Do not inspect `ruleId`, derive severity, or walk parents.

- [ ] **Step 4: Add failing Diagnostics render tests**

Use `renderToStaticMarkup` from `react-dom/server` and reset the Zustand store after each test. Assert:

```typescript
it('renders server-provided security proof fields', () => {
  useAppStore.setState({ result: makeExecutionWithFinding(), currentStep: 0 });
  const html = renderToStaticMarkup(<Diagnostics />);

  expect(html).toContain('STATE_CONTACT');
  expect(html).toContain('MEDIUM');
  expect(html).toContain('Frame 7');
  expect(html).toContain('simulationDiscarded');
  expect(html).toContain('Observed path only');
});

it('shows a true empty state only when the server returned no findings', () => {
  useAppStore.setState({ result: makeExecutionWithoutFindings(), currentStep: 0 });
  const html = renderToStaticMarkup(<Diagnostics />);

  expect(html).toContain('No findings in this execution');
  expect(html).not.toContain('unchecked returns');
  expect(html).not.toContain('gas griefing');
});
```

- [ ] **Step 5: Render interactive finding cards**

In `Diagnostics`, derive rows from the server tree and findings:

```typescript
const findings = result
  ? buildSecurityRows(result.frameTree, result.securityFindings)
  : [];

const focusFinding = (finding: JournalSecurityFinding) => {
  if (!result) return;
  const index = findSecurityFindingStepIndex(finding, result.events, result.steps);
  if (index !== null) setCurrentStep(index);
};
```

Render each finding as a `<button type="button">` card. Display `severity`, `ruleId` suffix, `summary`, `limitation`, `primaryFrameId`, execution/persistence dispositions, addresses, slots, and evidence count directly from the DTO. Use lowercase severity only as a CSS class name; do not map it to another level.

- [ ] **Step 6: Add focused styles**

Add `.diag-finding`, `.diag-finding-header`, `.diag-finding-severity`, `.diag-finding-evidence`, and severity classes matching server strings. Keep card layout readable at narrow panel widths and preserve keyboard focus visibility.

- [ ] **Step 7: Run React tests, lint, and production build**

Run from `schlieren-ui`:

```powershell
npm test
npm run lint
npm run build
```

Expected: all tests pass, lint exits zero, and TypeScript/Vite build succeeds. Record any existing bundle-size advisory separately from failures.

- [ ] **Step 8: Commit Task 5**

```powershell
git add -- schlieren-ui/src/engine/journal-view.ts schlieren-ui/src/engine/journal-view.test.ts schlieren-ui/src/views/Workbench/Diagnostics.tsx schlieren-ui/src/views/Workbench/Diagnostics.css schlieren-ui/src/views/Workbench/Diagnostics.test.tsx
git commit -m "feat(react): render proof-linked security findings"
```

---

### Task 6: Architecture Gate, Full Validation, and Phase Report

**Files:**
- Modify: `Schlieren.Tests/Execution/CanonicalExecutionArchitectureTests.cs`
- Create outside the repository: `C:\Users\Erick\Documents\Codex\2026-08-23\c-projects-schlieren\outputs\journal-legacy-phase-4a-reentrancy-recovery.md`

**Interfaces:**
- Verifies the complete Phase 4A slice and produces the user-facing review artifact.

- [ ] **Step 1: Add legacy-detector disconnection assertions**

Add assertions that the removed types are not present in the core assembly:

```csharp
[Theory]
[InlineData("Schlieren.Core.Security.ReentrancyDetector")]
[InlineData("Schlieren.Core.Security.LiveReentrancyDetector")]
public void LegacyTraceReentrancyTypes_AreNotProductionTypes(string typeName)
{
    Assert.Null(typeof(StateTransition).Assembly.GetType(typeName));
}
```

Run the test immediately. It must pass; this is a preservation assertion, not authorization to restore or rename a legacy type.

- [ ] **Step 2: Run focused .NET gates with durable TRX output**

```powershell
New-Item -ItemType Directory -Force -Path TestResults\phase4a-focused | Out-Null
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --no-restore --filter "FullyQualifiedName~JournalDispositionTests|FullyQualifiedName~JournalSecurityAnalyzerTests|FullyQualifiedName~ReentrancyJournalExecutionTests|FullyQualifiedName~JournalTraceRpcTests|FullyQualifiedName~CanonicalExecutionArchitectureTests|FullyQualifiedName~GasTraceInvariantTests|FullyQualifiedName~StateTransitionJournalTests" --logger "trx;LogFileName=phase4a-focused.trx" --results-directory TestResults\phase4a-focused
```

Expected: zero focused failures.

- [ ] **Step 3: Run the full solution build and full .NET suites**

```powershell
dotnet build Schlieren.sln --no-restore
dotnet test Schlieren.Tests\Schlieren.Tests.csproj --no-build --logger "trx;LogFileName=phase4a-core.trx" --results-directory TestResults\phase4a-core
dotnet test Schlieren.EELS.Tests\Schlieren.EELS.Tests.csproj --no-build --filter "FullyQualifiedName~JournalEelsAlignmentTests|FullyQualifiedName~TypedDiscrepancyTests|FullyQualifiedName~Layer1DiagnosisBridgeTests" --logger "trx;LogFileName=phase4a-eels.trx" --results-directory TestResults\phase4a-eels
```

Expected: build succeeds; classify missing ignored-fixture directories separately from behavioral failures exactly as in Phase 3.

- [ ] **Step 4: Re-run React validation**

From `schlieren-ui`:

```powershell
npm test
npm run lint
npm run build
```

Expected: zero test/lint/build failures.

- [ ] **Step 5: Verify architectural disconnection and repository cleanliness**

```powershell
rg -n "ReentrancyDetector|LiveReentrancyDetector|OnStep.*security|WorkbenchExecutionService" Schlieren.Core Schlieren.RPC Schlieren.UI schlieren-ui/src
git diff --check
git status --short
```

Expected: no legacy detector production references; only intended uncommitted report-independent changes before the final code commit; no whitespace errors.

- [ ] **Step 6: Commit the architecture assertion**

```powershell
git add -- Schlieren.Tests/Execution/CanonicalExecutionArchitectureTests.cs
git commit -m "test(architecture): keep trace reentrancy paths retired"
```

- [ ] **Step 7: Write the Phase 4A report**

Use `apply_patch` to create the output report. Include:

- starting and final commits;
- every implementation commit;
- rules and exact severity semantics;
- real `A → B → A` frame/evidence results;
- rollback and false-positive results;
- RPC compatibility result;
- React navigation behavior;
- focused/full test totals and exact failing case IDs;
- legacy paths confirmed disconnected;
- final `git status`;
- explicit statement that Phase 4B did not begin.

- [ ] **Step 8: Stop at the Phase 4A review gate**

Do not begin storage-collision redesign, gas-tree presentation recovery, Workbench scenario recovery, Phase 5 validation, or mainline integration. Present the report and request separate approval for the next slice.
