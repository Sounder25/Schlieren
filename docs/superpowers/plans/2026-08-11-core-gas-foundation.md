# Core Gas Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the typed, immutable calculation, schedule, overlay, coverage-validation, and journal primitives required by Scrutor's executable per-fork gas model without migrating existing gas formulas yet.

**Architecture:** New types live in `Scrutor.Core.Gas` and do not modify the existing opcode or fork-rule execution paths in this subproject. Formula implementations will later produce validated `GasCalculation` values; immutable fork schedules resolve typed rules; coverage manifests reject missing rules; optional journal sinks retain chronological evidence while normal execution can use a no-op sink.

**Tech Stack:** .NET 8, C# 12, xUnit, PowerShell, Git.

## Global Constraints

- Follow `docs/superpowers/specs/2026-08-11-executable-fork-gas-schedule-design.md`.
- Use test-driven development: each production behavior must have a test that is observed failing for the expected reason before implementation.
- Do not edit `docs/gas/GAS_RULE_INVENTORY.md` or `docs/gas/GAS_COVERAGE_MATRIX.md`; those belong to the parallel inventory workstream.
- Do not migrate existing gas constants or formulas in this subproject.
- Do not modify `IForkRules` until at least one complete formula migration slice is ready.
- Use checked, exact arithmetic and immutable snapshots at public boundaries.
- Preserve the green baseline of 327 unit tests.

---

### Task 1: Immutable Calculation Primitives

**Files:**
- Create: `Scrutor.Core/Gas/GasRuleId.cs`
- Create: `Scrutor.Core/Gas/GasRuleMetadata.cs`
- Create: `Scrutor.Core/Gas/GasComponent.cs`
- Create: `Scrutor.Core/Gas/GasDecision.cs`
- Create: `Scrutor.Core/Gas/GasCalculation.cs`
- Create: `Scrutor.Tests/Gas/GasCalculationTests.cs`

**Interfaces:**
- Produces: `GasRuleId`, `GasRuleMetadata`, `GasComponent`, `GasDecision`, `GasCalculation`, `GasDisposition`, and `GasComponentKind`.
- Consumes: `Scrutor.Core.Forks.Fork`.

- [x] **Step 1: Write failing identity and calculation tests**

Create `Scrutor.Tests/Gas/GasCalculationTests.cs` with tests proving:

```csharp
using System.Numerics;
using Scrutor.Core.Forks;
using Scrutor.Core.Gas;

namespace Scrutor.Tests.Gas;

public sealed class GasCalculationTests
{
    private static readonly GasRuleId RuleId = new("CALL.VALUE_TRANSFER");

    [Fact]
    public void GasRuleId_RejectsBlankValue()
    {
        Assert.Throws<ArgumentException>(() => new GasRuleId("  "));
    }

    [Fact]
    public void Create_CopiesInputsAndValidatesComponentTotals()
    {
        var components = new[]
        {
            new GasComponent("base", "CALL base", GasComponentKind.Charge, 100, "warm_access"),
            new GasComponent("value", "Value transfer", GasComponentKind.Charge, 9_000, "value != 0"),
            new GasComponent("refund", "Refund delta", GasComponentKind.RefundCounter, -2_400, "clear slot")
        };
        var decisions = new[]
        {
            new GasDecision("warm", "Target is warm", "true", "warm", new[] { "cold" })
        };
        var metadata = new GasRuleMetadata(
            RuleId, "Calls", Fork.Berlin, "EIP-2929", "SystemOpcodes.cs");

        var calculation = GasCalculation.Create(
            metadata, Fork.Berlin, 9_100, -2_400,
            GasDisposition.Charge, components, decisions);

        components[0] = components[0] with { Amount = BigInteger.Zero };
        decisions[0] = decisions[0] with { SelectedBranch = "cold" };

        Assert.Equal((ulong)9_100, calculation.ChargedGas);
        Assert.Equal(-2_400, calculation.RefundCounterDelta);
        Assert.Equal(new BigInteger(100), calculation.Components[0].Amount);
        Assert.Equal("warm", calculation.Decisions[0].SelectedBranch);
    }

    [Fact]
    public void Create_RejectsChargeComponentMismatch()
    {
        var metadata = new GasRuleMetadata(
            RuleId, "Calls", Fork.Berlin, "EIP-2929", "SystemOpcodes.cs");

        var ex = Assert.Throws<ArgumentException>(() => GasCalculation.Create(
            metadata, Fork.Berlin, 2_600, 0, GasDisposition.Charge,
            new[] { new GasComponent("access", "Access", GasComponentKind.Charge, 100) },
            Array.Empty<GasDecision>()));

        Assert.Contains("charged gas", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_RejectsRefundComponentMismatch()
    {
        var metadata = new GasRuleMetadata(
            RuleId, "Storage", Fork.London, "EIP-3529", "StorageOpcodes.cs");

        var ex = Assert.Throws<ArgumentException>(() => GasCalculation.Create(
            metadata, Fork.London, 0, 4_800, GasDisposition.RefundCounterDelta,
            new[] { new GasComponent("refund", "Refund", GasComponentKind.RefundCounter, 15_000) },
            Array.Empty<GasDecision>()));

        Assert.Contains("refund", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [x] **Step 2: Run the tests and observe the expected compile failure**

Run:

```powershell
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter "FullyQualifiedName~Scrutor.Tests.Gas.GasCalculationTests" --logger "console;verbosity=minimal"
```

Expected: build fails because `Scrutor.Core.Gas` types do not exist.

- [x] **Step 3: Implement the minimal immutable calculation API**

Implement:

```csharp
public readonly record struct GasRuleId
{
    public GasRuleId(string value);
    public string Value { get; }
    public override string ToString();
}

public sealed record GasRuleMetadata(
    GasRuleId RuleId,
    string Category,
    Fork ActivationFork,
    string ProtocolReference,
    string ImplementationBoundary);

public enum GasComponentKind { Charge, RefundCounter, Informational }
public enum GasDisposition { Charge, TransferOut, TransferIn, Return, RefundCounterDelta, Burn, Settlement, Validation }

public sealed record GasComponent(
    string Id,
    string Label,
    GasComponentKind Kind,
    BigInteger Amount,
    string? Expression = null);

public sealed record GasDecision(
    string Id,
    string Condition,
    string ObservedValue,
    string SelectedBranch,
    IReadOnlyList<string> Alternatives);

public sealed class GasCalculation
{
    public static GasCalculation Create(
        GasRuleMetadata metadata,
        Fork fork,
        ulong chargedGas,
        long refundCounterDelta,
        GasDisposition disposition,
        IEnumerable<GasComponent> components,
        IEnumerable<GasDecision> decisions);
}
```

`Create` materializes both enumerables into read-only snapshots, rejects duplicate component or decision IDs, verifies that `Charge` component amounts sum exactly to `ChargedGas`, verifies that `RefundCounter` component amounts sum exactly to `RefundCounterDelta`, and rejects negative charge components.

- [x] **Step 4: Run calculation tests green**

Run the Task 1 test command.

Expected: 4 tests pass.

- [x] **Step 5: Commit the calculation primitives**

```powershell
git add -- Scrutor.Core/Gas Scrutor.Tests/Gas/GasCalculationTests.cs
git commit -m "feat(gas): add immutable calculation primitives"
```

### Task 2: Typed Rules, Fork Schedules, and Overlays

**Files:**
- Create: `Scrutor.Core/Gas/IGasRule.cs`
- Create: `Scrutor.Core/Gas/ForkGasSchedule.cs`
- Create: `Scrutor.Core/Gas/ForkGasScheduleBuilder.cs`
- Create: `Scrutor.Core/Gas/GasScheduleException.cs`
- Create: `Scrutor.Tests/Gas/ForkGasScheduleTests.cs`

**Interfaces:**
- Consumes: Task 1 calculation primitives.
- Produces: `IGasRule`, `IGasRule<TContext>`, `ForkGasSchedule`, `ForkGasScheduleBuilder`, and `GasScheduleException`.

- [x] **Step 1: Write failing typed schedule tests**

Create tests using a private `ConstantRule : IGasRule<int>` that prove:

```csharp
[Fact]
public void Build_ProvidesTypedCalculation()
{
    var id = new GasRuleId("OP.ADD");
    var schedule = ForkGasScheduleBuilder.Empty(Fork.Frontier)
        .Set(new ConstantRule(id, 3))
        .Build();

    var result = schedule.Calculate(id, 0);

    Assert.Equal((ulong)3, result.ChargedGas);
    Assert.Equal(Fork.Frontier, result.Fork);
}

[Fact]
public void FromParent_ReplacesRuleWithoutMutatingParent()
{
    var id = new GasRuleId("OP.SLOAD");
    var frontier = ForkGasScheduleBuilder.Empty(Fork.Frontier)
        .Set(new ConstantRule(id, 50))
        .Build();
    var tangerine = ForkGasScheduleBuilder.From(frontier, Fork.TangerineWhistle)
        .Set(new ConstantRule(id, 200, Fork.TangerineWhistle))
        .Build();

    Assert.Equal((ulong)50, frontier.Calculate(id, 0).ChargedGas);
    Assert.Equal((ulong)200, tangerine.Calculate(id, 0).ChargedGas);
}

[Fact]
public void Calculate_RejectsWrongContextType()
{
    var id = new GasRuleId("OP.ADD");
    var schedule = ForkGasScheduleBuilder.Empty(Fork.Frontier)
        .Set(new ConstantRule(id, 3))
        .Build();

    Assert.Throws<GasScheduleException>(() => schedule.Calculate(id, "wrong"));
}

[Fact]
public void Build_RejectsForkBeforeParent()
{
    var parent = ForkGasScheduleBuilder.Empty(Fork.Berlin).Build();
    Assert.Throws<GasScheduleException>(() =>
        ForkGasScheduleBuilder.From(parent, Fork.Istanbul));
}
```

The test helper is:

```csharp
private sealed class ConstantRule : IGasRule<int>
{
    private readonly ulong _cost;

    public ConstantRule(GasRuleId id, ulong cost, Fork activationFork = Fork.Frontier)
    {
        _cost = cost;
        Metadata = new GasRuleMetadata(id, "Test", activationFork, "test", "test");
    }

    public GasRuleMetadata Metadata { get; }

    public GasCalculation Calculate(int context, Fork fork) => GasCalculation.Create(
        Metadata,
        fork,
        _cost,
        0,
        GasDisposition.Charge,
        new[] { new GasComponent("base", "Base", GasComponentKind.Charge, _cost) },
        Array.Empty<GasDecision>());
}
```

- [x] **Step 2: Run schedule tests red**

Run:

```powershell
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter "FullyQualifiedName~Scrutor.Tests.Gas.ForkGasScheduleTests" --logger "console;verbosity=minimal"
```

Expected: build fails because schedule types do not exist.

- [x] **Step 3: Implement typed rules and immutable schedules**

Implement these exact public contracts:

```csharp
public interface IGasRule
{
    GasRuleMetadata Metadata { get; }
    Type ContextType { get; }
    GasCalculation CalculateObject(object context, Fork fork);
}

public interface IGasRule<in TContext> : IGasRule
{
    GasCalculation Calculate(TContext context, Fork fork);
}

public sealed class ForkGasSchedule
{
    public Fork Fork { get; }
    public IReadOnlyCollection<GasRuleId> RuleIds { get; }
    public GasCalculation Calculate<TContext>(GasRuleId id, TContext context);
    public IGasRule GetRequired(GasRuleId id);
}

public sealed class ForkGasScheduleBuilder
{
    public static ForkGasScheduleBuilder Empty(Fork fork);
    public static ForkGasScheduleBuilder From(ForkGasSchedule parent, Fork fork);
    public ForkGasScheduleBuilder Set(IGasRule rule);
    public ForkGasScheduleBuilder Remove(GasRuleId id);
    public ForkGasSchedule Build();
}
```

The schedule stores an immutable dictionary snapshot. `Calculate` rejects missing rules and incompatible contexts with messages containing the rule ID, expected type, actual type, and fork. `From` requires a strictly later fork and copies the parent's resolved rule map before applying replacements. `Set` rejects rules whose activation fork is later than the schedule fork. `Remove` disables an inherited rule without mutating the parent schedule.

- [x] **Step 4: Run schedule and calculation tests green**

Run:

```powershell
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter "FullyQualifiedName~Scrutor.Tests.Gas" --logger "console;verbosity=minimal"
```

Expected: all Task 1 and Task 2 tests pass.

- [x] **Step 5: Commit typed schedules**

```powershell
git add -- Scrutor.Core/Gas Scrutor.Tests/Gas/ForkGasScheduleTests.cs
git commit -m "feat(gas): add typed fork schedules and overlays"
```

### Task 3: Coverage Manifest Validation

**Files:**
- Create: `Scrutor.Core/Gas/GasCoverageManifest.cs`
- Modify: `Scrutor.Core/Gas/ForkGasScheduleBuilder.cs`
- Create: `Scrutor.Tests/Gas/GasCoverageManifestTests.cs`

**Interfaces:**
- Consumes: Task 2 schedule builder.
- Produces: `GasCoverageManifest` and `Build(GasCoverageManifest manifest)`.

- [x] **Step 1: Write failing manifest tests**

Tests prove that a manifest snapshots required IDs, schedule construction rejects missing IDs with a sorted list, and inherited rules satisfy a child schedule's manifest.

Use this public API:

```csharp
var manifest = new GasCoverageManifest(new[]
{
    new GasRuleId("OP.ADD"),
    new GasRuleId("OP.SLOAD")
});

var ex = Assert.Throws<GasScheduleException>(() =>
    ForkGasScheduleBuilder.Empty(Fork.Frontier)
        .Set(new ConstantRule(new GasRuleId("OP.ADD"), 3))
        .Build(manifest));

Assert.Contains("OP.SLOAD", ex.Message);
```

- [x] **Step 2: Run manifest tests red**

Run:

```powershell
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter "FullyQualifiedName~Scrutor.Tests.Gas.GasCoverageManifestTests" --logger "console;verbosity=minimal"
```

Expected: build fails because manifest types and overload do not exist.

- [x] **Step 3: Implement immutable coverage validation**

`GasCoverageManifest` rejects duplicate IDs and exposes an immutable sorted list. `Build(manifest)` compares required IDs with the resolved rule map and throws one `GasScheduleException` listing every missing ID in ordinal order. Extra schedule rules are allowed because migration slices may register rules before they become mandatory in a complete manifest.

- [x] **Step 4: Run all gas foundation tests green**

Run the Task 2 combined gas-test command.

Expected: all gas tests pass.

- [x] **Step 5: Commit coverage validation**

```powershell
git add -- Scrutor.Core/Gas Scrutor.Tests/Gas/GasCoverageManifestTests.cs
git commit -m "feat(gas): validate schedule coverage manifests"
```

### Task 4: Optional Chronological Gas Journal

**Files:**
- Create: `Scrutor.Core/Gas/GasMovementKind.cs`
- Create: `Scrutor.Core/Gas/GasJournalEntry.cs`
- Create: `Scrutor.Core/Gas/IGasJournalSink.cs`
- Create: `Scrutor.Core/Gas/NullGasJournalSink.cs`
- Create: `Scrutor.Core/Gas/InMemoryGasJournal.cs`
- Create: `Scrutor.Tests/Gas/GasJournalTests.cs`

**Interfaces:**
- Consumes: `GasCalculation`, `CallType`, and `Address`.
- Produces: optional journal sinks with immutable chronological entries.

- [x] **Step 1: Write failing journal tests**

Tests prove:

- `NullGasJournalSink.Instance.IsEnabled` is false and `Append` has no effect.
- `InMemoryGasJournal.IsEnabled` is true and exposes immutable snapshots.
- Entries must have strictly increasing sequence numbers.
- Duplicate sequences and a child entry whose `ParentFrameId` equals its `FrameId` are rejected.
- A `RelatedSequence` must point backward to an entry already present.

Use this contract:

```csharp
public interface IGasJournalSink
{
    bool IsEnabled { get; }
    void Append(GasJournalEntry entry);
}

public sealed record GasJournalEntry(
    long Sequence,
    string TransactionId,
    long FrameId,
    long? ParentFrameId,
    CallType? CallType,
    int Depth,
    Address? ContractAddress,
    Address? CodeAddress,
    int? ProgramCounter,
    string? Opcode,
    ulong GasBefore,
    ulong GasAfter,
    GasMovementKind MovementKind,
    long? RelatedSequence,
    GasCalculation Calculation,
    bool Succeeded,
    string? Error);
```

- [x] **Step 2: Run journal tests red**

Run:

```powershell
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter "FullyQualifiedName~Scrutor.Tests.Gas.GasJournalTests" --logger "console;verbosity=minimal"
```

Expected: build fails because journal types do not exist.

- [x] **Step 3: Implement journal sinks and validation**

`GasMovementKind` contains `Charge`, `TransferOut`, `TransferIn`, `Return`, `RefundCounterDelta`, `Burn`, and `Settlement`. `NullGasJournalSink.Append` returns immediately. `InMemoryGasJournal` validates entries under a private lock, stores them in append order, and returns a copied read-only snapshot from `Entries`.

- [x] **Step 4: Run all gas tests and full unit suite**

Run:

```powershell
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter "FullyQualifiedName~Scrutor.Tests.Gas" --logger "console;verbosity=minimal"
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --no-build --logger "console;verbosity=minimal"
```

Expected: all gas tests pass, followed by all 327 baseline tests plus the new gas tests.

- [x] **Step 5: Commit the journal foundation**

```powershell
git add -- Scrutor.Core/Gas Scrutor.Tests/Gas/GasJournalTests.cs
git commit -m "feat(gas): add chronological accounting journal"
```

### Task 5: Foundation Verification and Documentation

**Files:**
- Modify: `docs/superpowers/plans/2026-08-11-core-gas-foundation.md` only to mark completed checkboxes during execution.
- Verify: all files created in Tasks 1–4.

**Interfaces:**
- Consumes: complete core foundation.
- Produces: verified, reviewable branch ready to receive formula migration plans.

- [x] **Step 1: Run formatting and placeholder checks**

```powershell
git diff --check
rg -n -i "TBD|FIXME|implement later|throw new NotImplementedException" Scrutor.Core/Gas Scrutor.Tests/Gas
```

Expected: no diff errors and no placeholders.

- [x] **Step 2: Run final focused and full verification**

```powershell
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --filter "FullyQualifiedName~Scrutor.Tests.Gas" --logger "console;verbosity=minimal"
dotnet test Scrutor.Tests/Scrutor.Tests.csproj --no-build --logger "console;verbosity=minimal"
```

Expected: all focused and full tests pass with zero failures.

- [x] **Step 3: Review branch ownership and commit plan tracking**

```powershell
git status --short
git diff a54a6bf...HEAD --name-only
```

Expected: changes are limited to the isolated baseline fix, this plan, `Scrutor.Core/Gas`, and `Scrutor.Tests/Gas`.

- [x] **Step 4: Commit the completed plan tracking**

```powershell
git add -- docs/superpowers/plans/2026-08-11-core-gas-foundation.md
git commit -m "docs: record core gas foundation execution"
```
