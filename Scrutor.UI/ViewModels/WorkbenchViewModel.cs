using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scrutor.Core.Execution;
using Scrutor.Core.Security;
using Scrutor.UI.Services;

namespace Scrutor.UI.ViewModels;

/// <summary>Optional one-click hex snippets for live EVM smoke tests (not loaded at startup).</summary>
public static class DemoBytecodes
{
    public const string SimpleAdd = "600560030160005260206000f3";
    public const string CounterLoop = "600060005b600190016001900380600c57505b";
    public const string TstoreTload = "60016001b36001b260005260206000f3";
    public const string Keccak256Empty = "600060002060005260206000f3";
    public const string BigHash =
        "7fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff60005260206000206000526020600080f3";
}

public partial class WorkbenchViewModel : ObservableObject, IDisposable
{
    private readonly WorkbenchExecutionService _syntheticService = new();
    private List<ExecutionTraceStep> _currentTrace = new();
    private DispatcherTimer? _autoPlayTimer;
    private bool _disposed;

    public ObservableCollection<ProjectFileViewModel> ProjectFiles { get; } = new();
    public ObservableCollection<ProjectFileViewModel> FilteredProjectFiles { get; } = new();
    public ObservableCollection<CodeLineViewModel> ActiveCodeLines { get; } = new();
    public CallTopologyViewModel CallTopology { get; } = new();
    public ObservableCollection<string> StackRows { get; } = new();
    public ObservableCollection<string> MemoryRows { get; } = new();
    public ObservableCollection<string> StorageRows { get; } = new();
    public ObservableCollection<InstructionViewModel> Instructions { get; } = new();
    public ObservableCollection<GasNodeViewModel> GasTreeNodes { get; } = new();
    public ObservableCollection<SecurityFindingViewModel> SecurityFindings { get; } = new();

    public ObservableCollection<string> AvailableForks { get; } = new()
    {
        "Cancun", "Prague", "Shanghai", "London", "Berlin"
    };

    [ObservableProperty] private ProjectFileViewModel? _selectedFile;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isInspectorExpanded = true;
    [ObservableProperty] private bool _isCallGraphVisible;
    [ObservableProperty] private string _selectedFork = "Cancun";
    [ObservableProperty] private ulong _baseFeeGwei = 1;
    [ObservableProperty] private ulong _blockGasLimit = 30_000_000;
    [ObservableProperty] private ulong _txGasLimit = 10_000_000;
    [ObservableProperty] private ulong _chainId = 1;
    [ObservableProperty] private string _coinbaseAddress = "0x0000000000000000000000000000000000000000";
    [ObservableProperty] private bool _isBlockContextExpanded;
    [ObservableProperty] private int _currentStepIndex;
    [ObservableProperty] private int _totalSteps;
    [ObservableProperty] private ExecutionTraceStep? _currentStep;
    [ObservableProperty] private bool _opSecEnabled = true;
    [ObservableProperty] private int _criticalCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private string _statusMessage =
        "Ready — open a contract, or paste bytecode and Run";
    [ObservableProperty] private bool _isAutoPlaying;
    [ObservableProperty] private string _currentOpcodeSpec = string.Empty;
    [ObservableProperty] private string _currentGasFormulaBreakdown = string.Empty;
    [ObservableProperty] private string _currentStepDetail = string.Empty;
    [ObservableProperty] private string _bytecodeInput = string.Empty;
    [ObservableProperty] private bool _isBytecodeMode;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _hasTrace;
    [ObservableProperty] private bool _hasOpenFiles;
    [ObservableProperty] private string _centerEmptyHint =
        "Open a .sol/.hex file or paste bytecode above, then Run.";
    [ObservableProperty] private string _opSecLabel = "OPSEC: ON";

    /// <summary>
    /// Full brand mark as a soft center watermark. Stronger when empty, ghosted when code is up.
    /// </summary>
    public double WatermarkOpacity => HasOpenFiles || HasTrace ? 0.07 : 0.20;

    public string CurrentFileTitle =>
        SelectedFile != null
            ? $"{SelectedFile.FileName} ({SelectedFile.Lines.Count} lines)"
            : "No file loaded";

    public string StepProgress => TotalSteps <= 0
        ? "0 / 0"
        : $"{CurrentStepIndex + 1} / {TotalSteps}";

    public string StepPercentage => TotalSteps > 0
        ? $"{(CurrentStepIndex + 1) * 100 / TotalSteps}%"
        : "0%";

    public double StepProgressRatio => TotalSteps <= 0
        ? 0.0
        : (double)(CurrentStepIndex + 1) / TotalSteps;

    /// <summary>Slider max index (TotalSteps - 1, or 0 when empty).</summary>
    public int MaxStepIndex => Math.Max(0, TotalSteps - 1);

    /// <summary>
    /// Honest note: fork name is report metadata. Core uses the unified modern opcode set.
    /// Block fields (gas, base fee, chain id, coinbase) are applied to live runs.
    /// </summary>
    public string ForkNote =>
        $"{SelectedFork} · block fields apply; fork is report label";

    public WorkbenchViewModel()
    {
        ApplyOpSec();
        RefreshFilteredFiles();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopAutoPlay();
        GC.SuppressFinalize(this);
    }

    partial void OnSearchQueryChanged(string value) => RefreshFilteredFiles();
    partial void OnOpSecEnabledChanged(bool value) => ApplyOpSec();
    partial void OnSelectedForkChanged(string value) => OnPropertyChanged(nameof(ForkNote));

    private void ApplyOpSec()
    {
        OpSecLockout.IsEnabled = OpSecEnabled;
        OpSecLabel = OpSecEnabled ? "OPSEC: ON" : "OPSEC: OFF";
    }

    private void RefreshFilteredFiles()
    {
        FilteredProjectFiles.Clear();
        var q = SearchQuery?.Trim() ?? string.Empty;
        IEnumerable<ProjectFileViewModel> src = ProjectFiles;
        if (!string.IsNullOrEmpty(q))
        {
            src = ProjectFiles.Where(f =>
                f.FileName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || f.Lines.Any(l => l.Text.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var f in src)
            FilteredProjectFiles.Add(f);

        HasOpenFiles = ProjectFiles.Count > 0;
        OnPropertyChanged(nameof(WatermarkOpacity));
    }

    // ---------- files ----------

    [RelayCommand]
    private void SelectFile(ProjectFileViewModel file)
    {
        IsCallGraphVisible = false;
        foreach (var f in ProjectFiles)
            f.IsSelected = false;

        SelectedFile = file;
        file.IsSelected = true;

        ActiveCodeLines.Clear();
        foreach (var line in file.Lines)
            ActiveCodeLines.Add(line);

        OnPropertyChanged(nameof(CurrentFileTitle));
        CenterEmptyHint = string.Empty;
    }

    public void AddCustomFile(string fileName, string filePath, IEnumerable<string> lines)
    {
        var existing = ProjectFiles.FirstOrDefault(f =>
            f.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            SelectFile(existing);
            return;
        }

        var newFile = new ProjectFileViewModel(fileName, filePath, lines);
        ProjectFiles.Add(newFile);
        RefreshFilteredFiles();
        SelectFile(newFile);

        // If opened file looks like hex bytecode, load into run box
        var joined = string.Join("", lines).Trim();
        if (LooksLikeHexBytecode(joined))
        {
            BytecodeInput = joined;
            StatusMessage = $"Loaded {fileName} as bytecode candidate";
        }
        else
        {
            StatusMessage = $"Loaded {fileName} (source view — paste compiled hex to execute)";
        }
    }

    private static bool LooksLikeHexBytecode(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length < 4) return false;
        var c = s.Replace("0x", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "").Replace("\n", "").Replace("\r", "");
        if (c.Length < 4 || c.Length % 2 != 0) return false;
        return c.All(ch => Uri.IsHexDigit(ch));
    }

    [RelayCommand]
    private void CloseTab(ProjectFileViewModel? file)
    {
        var target = file ?? SelectedFile;
        if (target is null) return;

        var idx = ProjectFiles.IndexOf(target);
        if (idx < 0) return;

        ProjectFiles.RemoveAt(idx);
        RefreshFilteredFiles();

        if (ProjectFiles.Count == 0)
        {
            SelectedFile = null;
            ActiveCodeLines.Clear();
            OnPropertyChanged(nameof(CurrentFileTitle));
            CenterEmptyHint = "Open a .sol/.hex file or paste bytecode above, then Run.";
            StatusMessage = "No files open";
            return;
        }

        var next = ProjectFiles.ElementAtOrDefault(Math.Min(idx, ProjectFiles.Count - 1))
                   ?? ProjectFiles[0];
        SelectFile(next);
    }

    [RelayCommand]
    private void ShowCallGraph()
    {
        foreach (var f in ProjectFiles)
            f.IsSelected = false;
        IsCallGraphVisible = true;
    }

    [RelayCommand]
    private void ShowSource()
    {
        IsCallGraphVisible = false;
        if (SelectedFile != null)
            SelectFile(SelectedFile);
    }

    [RelayCommand]
    private void ToggleBlockContext() => IsBlockContextExpanded = !IsBlockContextExpanded;

    [RelayCommand]
    private void ToggleInspector() => IsInspectorExpanded = !IsInspectorExpanded;

    [RelayCommand]
    private void ToggleOpSec() => OpSecEnabled = !OpSecEnabled;

    // ---------- scrubber ----------

    [RelayCommand]
    private void JumpToStep(int stepIndex)
    {
        if (stepIndex >= 0 && stepIndex < TotalSteps)
            CurrentStepIndex = stepIndex;
    }

    [RelayCommand]
    private void StepForward()
    {
        if (_currentTrace.Count == 0) return;
        if (CurrentStepIndex < TotalSteps - 1)
            CurrentStepIndex++;
    }

    [RelayCommand]
    private void StepBack()
    {
        if (_currentTrace.Count == 0) return;
        if (CurrentStepIndex > 0)
            CurrentStepIndex--;
    }

    [RelayCommand]
    private void JumpToStart()
    {
        if (_currentTrace.Count == 0) return;
        CurrentStepIndex = 0;
    }

    [RelayCommand]
    private void JumpToEnd()
    {
        if (_currentTrace.Count == 0) return;
        CurrentStepIndex = Math.Max(0, TotalSteps - 1);
    }

    [RelayCommand]
    private void ToggleAutoPlay()
    {
        if (_currentTrace.Count == 0)
        {
            StatusMessage = "No trace yet — run bytecode first";
            IsAutoPlaying = false;
            return;
        }

        if (IsAutoPlaying)
            StopAutoPlay();
        else
            StartAutoPlay();
    }

    private void StartAutoPlay()
    {
        StopAutoPlay();
        IsAutoPlaying = true;
        _autoPlayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _autoPlayTimer.Tick += (_, _) =>
        {
            if (!IsAutoPlaying || _currentTrace.Count == 0) return;
            if (CurrentStepIndex < TotalSteps - 1)
                CurrentStepIndex++;
            else
                CurrentStepIndex = 0;
        };
        _autoPlayTimer.Start();
    }

    private void StopAutoPlay()
    {
        IsAutoPlaying = false;
        if (_autoPlayTimer is null) return;
        _autoPlayTimer.Stop();
        _autoPlayTimer = null;
    }

    // ---------- execution ----------

    [RelayCommand]
    private void LoadDemoBytecode(string tag)
    {
        BytecodeInput = tag switch
        {
            "add" => DemoBytecodes.SimpleAdd,
            "loop" => DemoBytecodes.CounterLoop,
            "tstore" => DemoBytecodes.TstoreTload,
            "keccak" => DemoBytecodes.Keccak256Empty,
            "bighash" => DemoBytecodes.BigHash,
            _ => BytecodeInput
        };
        IsBytecodeMode = true;
        StatusMessage = $"Demo bytecode loaded ({tag}) — press Run";
    }

    private BytecodeRunOptions BuildRunOptions() => new()
    {
        GasLimit = TxGasLimit,
        BlockGasLimit = BlockGasLimit,
        BaseFeeGwei = BaseFeeGwei,
        ChainId = ChainId,
        CoinbaseHex = CoinbaseAddress,
        ForkLabel = SelectedFork
    };

    [RelayCommand]
    private async Task RunBytecodeAsync()
    {
        if (string.IsNullOrWhiteSpace(BytecodeInput))
        {
            StatusMessage = "Paste hex bytecode first (e.g. 6005600301...)";
            return;
        }

        StopAutoPlay();
        IsRunning = true;
        StatusMessage = $"Executing on live EVM (gas≤{TxGasLimit:N0}, chain {ChainId})...";

        ExecutionResult? result;
        try
        {
            result = await BytecodeExecutionService.RunAsync(BytecodeInput, BuildRunOptions());
        }
        catch (Exception ex)
        {
            StatusMessage = $"Run failed: {ex.Message}";
            return;
        }
        finally
        {
            IsRunning = false;
        }

        if (result is null)
        {
            StatusMessage = "Invalid hex — check input";
            return;
        }

        PopulateFromResult(result.Value, isBytecodeRun: true);
    }

    [RelayCommand]
    private void RunSyntheticDemo()
    {
        StopAutoPlay();
        IsBytecodeMode = false;
        var result = _syntheticService.RunFullTransaction();
        PopulateFromResult(result, isBytecodeRun: false);
        StatusMessage =
            $"Synthetic demo only: {result.TraceSteps.Count} steps | {CriticalCount} critical | {WarningCount} warnings";
    }

    public async Task GenerateAuditReportAsync(string savePath)
    {
        var totalGas = Instructions.Count > 0
            ? (ulong)Instructions.Sum(i => i.GasCost) + 21_000UL
            : 0UL;

        await AuditReportExporter.GenerateReportAsync(
            CurrentFileTitle,
            SelectedFork,
            BlockGasLimit,
            BaseFeeGwei,
            TotalSteps,
            totalGas,
            SecurityFindings,
            Instructions,
            savePath);

        StatusMessage = $"Wrote audit report: {Path.GetFileName(savePath)}";
    }

    [RelayCommand]
    private void JumpToFinding(SecurityFindingViewModel finding)
    {
        var file = ProjectFiles.FirstOrDefault(f =>
            f.FileName.Equals(finding.FileName, StringComparison.OrdinalIgnoreCase));
        if (file != null)
            SelectFile(file);

        if (finding.StepIndex >= 0 && finding.StepIndex < TotalSteps)
            CurrentStepIndex = finding.StepIndex;

        if (SelectedFile != null && finding.LineNumber > 0)
        {
            foreach (var line in SelectedFile.Lines)
                line.IsActiveLine = line.LineNumber == finding.LineNumber;
        }

        StatusMessage = $"Focused: {finding.LocationText}";
    }

    // ---------- result plumbing ----------

    private void PopulateFromResult(ExecutionResult result, bool isBytecodeRun)
    {
        _currentTrace = result.TraceSteps ?? new List<ExecutionTraceStep>();
        HasTrace = _currentTrace.Count > 0;

        Instructions.Clear();
        StackRows.Clear();
        MemoryRows.Clear();
        StorageRows.Clear();
        SecurityFindings.Clear();
        GasTreeNodes.Clear();

        foreach (var step in _currentTrace)
        {
            var gas = ParseGasCost(step.GasCost);
            var desc = BytecodeExecutionService.DescribeOpcode(step.Op);
            Instructions.Add(new InstructionViewModel(
                step.Pc.ToString("X4"),
                step.Op,
                gas,
                step.CallType?.ToString() ?? $"D{step.Depth}",
                desc));
        }

        var reentrancy = ReentrancyDetector.Analyze(_currentTrace);
        var collisions = StorageCollisionDetector.Analyze(_currentTrace);

        foreach (var f in reentrancy)
        {
            SecurityFindings.Add(new SecurityFindingViewModel
            {
                SeverityEmoji = f.Severity == ReentrancySeverity.Critical ? "🔴" : "⚠️",
                Description = $"REENTRANCY: {f.Severity} — depth Δ {f.DepthDelta}",
                Details = $"Target: {f.TargetContract} | re-entry step {f.ReentryStep}",
                FileName = isBytecodeRun ? string.Empty : "Vault.sol",
                LineNumber = isBytecodeRun ? 0 : 23,
                StepIndex = f.ReentryStep
            });
        }

        foreach (var c in collisions)
        {
            SecurityFindings.Add(new SecurityFindingViewModel
            {
                SeverityEmoji = "⚠️",
                Description = $"STORAGE COLLISION: slot {c.CollidingSlot}",
                Details = $"Proxy: {c.ProxyContract} | Impl: {c.ImplementationContract}",
                FileName = isBytecodeRun ? string.Empty : "Proxy.sol",
                LineNumber = isBytecodeRun ? 0 : 14,
                StepIndex = c.StepIndex
            });
        }

        var gasUsed = result.GasUsed;
        var refund = result.GasRefundCounter;
        GasTreeNodes.Add(new GasNodeViewModel
        {
            DisplayText = $"▼ TOTAL USED: {gasUsed:N0}",
            Indent = new(0, 0, 0, 8),
            Color = "#FFFFFF"
        });
        GasTreeNodes.Add(new GasNodeViewModel
        {
            DisplayText = $"├── Tx gas limit: {TxGasLimit:N0}",
            Indent = new(16, 2)
        });
        GasTreeNodes.Add(new GasNodeViewModel
        {
            DisplayText = $"├── Steps: {_currentTrace.Count}",
            Indent = new(16, 2),
            Color = "#FFAA00"
        });
        GasTreeNodes.Add(new GasNodeViewModel
        {
            DisplayText = $"└── Refund counter: {refund:N0}",
            Indent = new(16, 2),
            Color = "#00D4AA"
        });

        TotalSteps = _currentTrace.Count;
        HasTrace = _currentTrace.Count > 0;
        OnPropertyChanged(nameof(MaxStepIndex));
        OnPropertyChanged(nameof(WatermarkOpacity));
        CallTopology.LoadFromTrace(_currentTrace);

        CriticalCount = reentrancy.Count(r => r.Severity == ReentrancySeverity.Critical);
        WarningCount = collisions.Count + reentrancy.Count(r => r.Severity == ReentrancySeverity.Medium);

        if (TotalSteps > 0)
        {
            CenterEmptyHint = string.Empty;
            if (CurrentStepIndex == 0)
                OnCurrentStepIndexChanged(0);
            else
                CurrentStepIndex = 0;
        }
        else
        {
            CurrentStep = null;
            CurrentOpcodeSpec = string.Empty;
            CurrentGasFormulaBreakdown = string.Empty;
            CurrentStepDetail = string.Empty;
            NotifyStepProps();
        }

        StatusMessage = isBytecodeRun
            ? $"LIVE EVM [{SelectedFork} label]: {_currentTrace.Count} steps | {(result.IsSuccess ? "SUCCESS" : $"REVERT ({result.Error})")} | {gasUsed:N0} gas | refund {refund}"
            : $"Synthetic demo: {_currentTrace.Count} steps | {CriticalCount} critical | {WarningCount} warnings | {gasUsed:N0} gas";
    }

    partial void OnCurrentStepIndexChanged(int value)
    {
        if (_currentTrace.Count == 0 || value < 0 || value >= _currentTrace.Count)
        {
            NotifyStepProps();
            return;
        }

        var step = _currentTrace[value];
        CurrentStep = step;

        StackRows.Clear();
        for (var i = step.Stack.Count - 1; i >= 0; i--)
            StackRows.Add($"[{i}] {step.Stack[i]}");

        MemoryRows.Clear();
        foreach (var row in step.Memory)
            MemoryRows.Add(row);

        StorageRows.Clear();
        foreach (var kvp in step.Storage)
            StorageRows.Add($"{kvp.Key}: {kvp.Value}");

        ComputeEelsSpecCitation(step);
        UpdateActiveLineForStep(value);
        NotifyStepProps();

        StatusMessage =
            $"Step {value + 1}/{TotalSteps} | {step.Op} | depth {step.Depth} | gas {step.Gas}";
    }

    private void NotifyStepProps()
    {
        OnPropertyChanged(nameof(StepProgress));
        OnPropertyChanged(nameof(StepPercentage));
        OnPropertyChanged(nameof(StepProgressRatio));
        OnPropertyChanged(nameof(MaxStepIndex));
        OnPropertyChanged(nameof(CurrentFileTitle));
    }

    private void ComputeEelsSpecCitation(ExecutionTraceStep step)
    {
        var op = step.Op.ToUpperInvariant();
        CurrentOpcodeSpec = op switch
        {
            "SSTORE" => "EELS: sstore(evm) — EIP-2200 / EIP-2929",
            "SLOAD" => "EELS: sload(evm) — cold 2100 / warm 100",
            "TSTORE" => "EELS: tstore(evm) — EIP-1153",
            "TLOAD" => "EELS: tload(evm) — EIP-1153",
            "MCOPY" => "EELS: mcopy(evm) — EIP-5656",
            "CALL" => "EELS: call(evm) — EIP-150 63/64ths",
            "DELEGATECALL" => "EELS: delegatecall(evm)",
            "STATICCALL" => "EELS: staticcall(evm)",
            "CREATE2" => "EELS: create2(evm)",
            "KECCAK256" or "SHA3" => "EELS: keccak256(evm)",
            _ when op.StartsWith("PUSH") => $"EELS: push — {op}",
            _ when op.StartsWith("DUP") => $"EELS: dup — {op}",
            _ when op.StartsWith("SWAP") => $"EELS: swap — {op}",
            _ => $"EELS: {op.ToLowerInvariant()}(evm)"
        };

        CurrentGasFormulaBreakdown = op switch
        {
            "SSTORE" => "Cold access / set / clear refund per EIP-2200",
            "SLOAD" => "Cold 2,100 | Warm 100",
            "CALL" => "Stipend 2,300 on value | cold account +2,600 | 63/64ths",
            "MCOPY" => "3 gas/word + memory expansion",
            "KECCAK256" or "SHA3" => "30 + 6/word",
            _ => $"Reported cost: {step.GasCost}"
        };

        CurrentStepDetail = $"PC 0x{step.Pc:X4} | depth {step.Depth} | gas left {step.Gas}";
    }

    private void UpdateActiveLineForStep(int stepIndex)
    {
        if (SelectedFile is null || SelectedFile.Lines.Count == 0)
            return;

        // Honest proportional highlight until sourcemaps exist.
        var lineCount = SelectedFile.Lines.Count;
        var targetLine = Math.Clamp(stepIndex % lineCount + 1, 1, lineCount);

        var currentStep = stepIndex >= 0 && stepIndex < _currentTrace.Count
            ? _currentTrace[stepIndex]
            : null;
        var gasBadge = currentStep != null
            ? $"[{currentStep.Op} · {currentStep.GasCost}]"
            : string.Empty;

        foreach (var line in SelectedFile.Lines)
        {
            var active = line.LineNumber == targetLine;
            line.IsActiveLine = active;
            if (active)
            {
                line.GasBadgeText = gasBadge;
                line.IsColdAccess = currentStep?.GasCost is "0x834" or "2100" or "0x83A" or "2106";
            }
            else
            {
                line.GasBadgeText = string.Empty;
                line.IsColdAccess = false;
            }
        }
    }

    private static int ParseGasCost(string? gasCost)
    {
        if (string.IsNullOrWhiteSpace(gasCost))
            return 0;

        var s = gasCost.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hx)
                ? hx
                : 0;
        }

        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }
}

public class InstructionViewModel
{
    public string PC { get; }
    public string Opcode { get; }
    public int GasCost { get; }
    public string CallType { get; }
    public string Description { get; }
    public string DisplayText => string.IsNullOrEmpty(Description)
        ? $"0x{PC}  {Opcode}  ({GasCost})"
        : $"0x{PC}  {Opcode}  ({GasCost})  {Description}";

    public InstructionViewModel(string pc, string opcode, int gasCost, string callType, string description = "")
    {
        PC = pc;
        Opcode = opcode;
        GasCost = gasCost;
        CallType = callType;
        Description = description;
    }
}
