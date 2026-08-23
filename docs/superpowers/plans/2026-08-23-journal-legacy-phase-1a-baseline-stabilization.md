# Journal Legacy Phase 1A Baseline Stabilization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Execute inline; do not dispatch subagents.

**Goal:** Make the isolated `20005b3` integration baseline buildable without integrating journal history or changing execution behavior.

**Architecture:** Remove the unregistered, dependency-incomplete `import-tx` source from CLI compilation while preserving the file in Git; complete the Avalonia view-model visibility contract already required by XAML; and make the initial React layout and motion constants satisfy their committed library contracts. Each slice is independently verified and committed.

**Tech Stack:** .NET 8, C#, xUnit, Avalonia, React 19, TypeScript 6, Framer Motion, rc-dock, npm

**Spec:** `docs/superpowers/specs/2026-08-23-journal-legacy-recovery-integration-design.md`

## Global Constraints

- Work only in `C:\projects\Schlieren\.worktrees\journal-legacy-recovery` on `integration/journal-legacy-recovery`.
- Do not merge, cherry-pick, or apply any commit from the journal range.
- Do not modify `StateTransition`, `EvmMachine`, `ExecutionContext`, `IntrinsicGas`, `EthHandlers`, or `Schlieren.Core/Execution/Journal/`.
- Do not modify `main`, `feature/journal-gas-tree-rpc-react`, or `recovery/legacy-deleted-paths`.
- Preserve `Schlieren.CLI/Commands/ImportTxCommand.cs` as source; it is excluded from compilation because no command registration or supporting implementation exists.
- Do not change existing RPC JSON contracts or runtime execution behavior.
- Commit each bounded slice separately.

---

### Task 1: Exclude the orphaned CLI source from compilation

**Files:**
- Modify: `Schlieren.CLI/Schlieren.CLI.csproj`
- Preserve unchanged: `Schlieren.CLI/Commands/ImportTxCommand.cs`

**Interfaces:**
- Consumes: the existing CLI command registry in `Schlieren.CLI/Program.cs`, which does not register `ImportTxCommand`
- Produces: a buildable `Schlieren.CLI` with its existing six registered commands unchanged

- [ ] **Step 1: Confirm the red build**

Run:

```powershell
dotnet build .\Schlieren.CLI\Schlieren.CLI.csproj --nologo --no-restore --verbosity minimal
```

Expected: fail with `CS0234` at `ImportTxCommand.cs:6` because `Schlieren.Core.Ethereum` does not exist.

- [ ] **Step 2: Exclude only the orphaned file**

Add this item to `Schlieren.CLI/Schlieren.CLI.csproj`:

```xml
  <ItemGroup>
    <!-- Preserved design source; no registered command or supporting Ethereum client/model implementation exists yet. -->
    <Compile Remove="Commands\ImportTxCommand.cs" />
  </ItemGroup>
```

- [ ] **Step 3: Verify the CLI is green**

Run the command from Step 1.

Expected: build succeeds with zero errors.

- [ ] **Step 4: Commit the CLI stabilization**

```powershell
git add -- Schlieren.CLI/Schlieren.CLI.csproj
git commit -m "fix(cli): exclude incomplete import command"
```

### Task 2: Complete the Avalonia security-findings visibility contract

**Files:**
- Create: `Schlieren.Tests/WorkbenchSecurityFindingsTests.cs`
- Modify: `Schlieren.UI/ViewModels/WorkbenchViewModel.cs`

**Interfaces:**
- Consumes: `ObservableCollection<SecurityFindingViewModel> SecurityFindings`
- Produces: `bool HasSecurityFindings` and `PropertyChanged` notifications whenever that collection changes

- [ ] **Step 1: Write the failing behavioral test**

Create `WorkbenchSecurityFindingsTests.cs`:

```csharp
using Schlieren.UI.ViewModels;

namespace Schlieren.Tests;

public sealed class WorkbenchSecurityFindingsTests
{
    [Fact]
    public void SecurityFindings_UpdatesVisibilityContractAndNotifiesBindings()
    {
        using var vm = new WorkbenchViewModel();
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        Assert.False(vm.HasSecurityFindings);

        vm.SecurityFindings.Add(new SecurityFindingViewModel());

        Assert.True(vm.HasSecurityFindings);
        Assert.Contains(nameof(vm.HasSecurityFindings), notifications);

        notifications.Clear();
        vm.SecurityFindings.Clear();

        Assert.False(vm.HasSecurityFindings);
        Assert.Contains(nameof(vm.HasSecurityFindings), notifications);
    }
}
```

The production mutation caught by this test is a missing or stale collection-to-visibility notification, which would leave the “No findings” display incorrect.

- [ ] **Step 2: Verify the test is red**

Run:

```powershell
dotnet test .\Schlieren.Tests\Schlieren.Tests.csproj --nologo --no-restore --filter "FullyQualifiedName~WorkbenchSecurityFindingsTests" --verbosity minimal
```

Expected: compilation fails because `WorkbenchViewModel.HasSecurityFindings` is absent; the existing XAML validator reports the same missing contract.

- [ ] **Step 3: Implement the minimum observable contract**

In `WorkbenchViewModel.cs`:

```csharp
using System.Collections.Specialized;
```

Add beside `SecurityFindings`:

```csharp
public bool HasSecurityFindings => SecurityFindings.Count > 0;
```

Subscribe in the constructor:

```csharp
SecurityFindings.CollectionChanged += OnSecurityFindingsChanged;
```

Add the handler:

```csharp
private void OnSecurityFindingsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    => OnPropertyChanged(nameof(HasSecurityFindings));
```

Unsubscribe in `Dispose()`:

```csharp
SecurityFindings.CollectionChanged -= OnSecurityFindingsChanged;
```

- [ ] **Step 4: Verify the focused test is green**

Run the command from Step 2.

Expected: 1 passed, 0 failed.

- [ ] **Step 5: Verify the Avalonia project is green**

```powershell
dotnet build .\Schlieren.UI\Schlieren.UI.csproj --nologo --no-restore --verbosity minimal
```

Expected: build succeeds with zero errors.

- [ ] **Step 6: Commit the Avalonia contract**

```powershell
git add -- Schlieren.UI/ViewModels/WorkbenchViewModel.cs Schlieren.Tests/WorkbenchSecurityFindingsTests.cs
git commit -m "fix(ui): expose security findings visibility"
```

### Task 3: Make the React baseline type-complete

**Files:**
- Modify: `schlieren-ui/src/App.tsx`
- Modify: `schlieren-ui/src/components/CaseBar.tsx`
- Modify: `schlieren-ui/src/views/Workbench/MachineState.tsx`
- Modify: `schlieren-ui/src/views/Workbench/Workbench.tsx`

**Interfaces:**
- Consumes: Framer Motion `Variants`, rc-dock `TabData`, and the existing `loadTab` behavior
- Produces: the same visible panels and animation values with compile-time-complete types

- [ ] **Step 1: Confirm the red TypeScript build**

Run:

```powershell
Set-Location .\schlieren-ui
npm run build
```

Expected: fail on Framer Motion easing types, unused symbols, and id-only rc-dock `TabData` literals.

- [ ] **Step 2: Type motion variants and remove unused symbols**

- Import `type Variants` from `framer-motion` in `App.tsx` and `MachineState.tsx`.
- Annotate `viewVariants` and `stackItemVariants` as `Variants`.
- Remove the unused `step` variable from `CaseBar.tsx`.
- Replace `import React, { useMemo }` with `import { useMemo }` in `MachineState.tsx`.
- Remove `useEffect` from the `Workbench.tsx` React import.

- [ ] **Step 3: Create full default dock tabs**

Add this helper after `loadTab` in `Workbench.tsx`:

```tsx
function createTab(id: string): TabData {
  const panel = panels[id];
  const Component = panel.component;
  return {
    id,
    title: panel.title,
    content: <Component />,
    closable: false,
  };
}
```

Replace the four default-layout id-only objects with:

```tsx
createTab('disassembly')
createTab('trace')
createTab('machine-state')
createTab('diagnostics')
```

Saved layouts remain hydrated through the existing `loadTab` callback.

- [ ] **Step 4: Verify the React build is green**

Run the command from Step 1.

Expected: TypeScript and Vite complete successfully.

- [ ] **Step 5: Commit the React stabilization**

```powershell
git add -- schlieren-ui/src/App.tsx schlieren-ui/src/components/CaseBar.tsx schlieren-ui/src/views/Workbench/MachineState.tsx schlieren-ui/src/views/Workbench/Workbench.tsx
git commit -m "fix(react): satisfy baseline type contracts"
```

### Task 4: Re-establish the Phase 1 baseline gate

**Files:**
- Create: external Phase 1A verification report under the Codex outputs directory
- Modify: none in the repository

**Interfaces:**
- Consumes: the three stabilization commits from Tasks 1–3
- Produces: a clean build/test baseline suitable for explicit Phase 2 approval

- [ ] **Step 1: Run the full solution build**

```powershell
dotnet build .\Schlieren.sln --nologo --no-restore --verbosity minimal
```

Expected: zero errors.

- [ ] **Step 2: Run the core suite**

```powershell
dotnet test .\Schlieren.Tests\Schlieren.Tests.csproj --nologo --no-build --logger "trx;LogFileName=phase1a-core.trx" --results-directory .\TestResults\phase1a-core --verbosity minimal
```

Expected: all executable tests pass; skips are reported by exact name.

- [ ] **Step 3: Re-run the focused EELS baseline**

```powershell
$filter = 'FullyQualifiedName~ForkGasDataValidationTests|FullyQualifiedName~CancunOpcodeGasConformanceTests|FullyQualifiedName~Eip7623CoinbaseTest|FullyQualifiedName~Eip7702BasicTest|FullyQualifiedName~Layer1'
dotnet test .\Schlieren.EELS.Tests\Schlieren.EELS.Tests.csproj --nologo --no-build --filter $filter --logger "trx;LogFileName=phase1a-focused-eels.trx" --results-directory .\TestResults\phase1a-focused-eels --verbosity minimal
```

Expected: 141 passed, 0 failed, 0 skipped.

- [ ] **Step 4: Re-run the React build**

```powershell
Set-Location .\schlieren-ui
npm run build
```

Expected: TypeScript and Vite complete successfully.

- [ ] **Step 5: Verify safety boundaries**

Confirm the branch contains only the plan and three stabilization commits, the worktree is clean, main and journal dirty-state fingerprints are unchanged, and `2219379` is not an ancestor of the integration branch.

- [ ] **Step 6: Publish the report and stop**

Publish exact commands, results, commit hashes, failures, and skips. Request explicit Phase 2 approval for range `5e3e07e..2219379`. Do not merge it.
