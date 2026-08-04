using System.Collections.ObjectModel;
using System.Timers;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scrutor.Core.Execution;
using Scrutor.Core.Security;
using Scrutor.UI.Services;

namespace Scrutor.UI.ViewModels;

public partial class WorkbenchViewModel : ObservableObject
{
    private readonly WorkbenchExecutionService _executionService = new();
    private List<ExecutionTraceStep> _currentTrace = new();
    private System.Timers.Timer? _playbackTimer;
    private bool _isPlaying = false;
    // ============================================
    // TRANSACTION PARAMS
    // ============================================
    [ObservableProperty] private string _toAddress = "0x0000000000000000000000000000000000000008";
    [ObservableProperty] private string _gasLimit = "500000";

    // ============================================
    // PLAYBACK STATE
    // ============================================
    [ObservableProperty] private int _currentStepIndex;
    [ObservableProperty] private int _totalSteps;
    [ObservableProperty] private ExecutionTraceStep? _currentStep;
    [ObservableProperty] private bool _isPlayingState;
    
    public string PlayPauseIcon => IsPlayingState ? "⏸" : "▶";
    public string StepProgress => $"{CurrentStepIndex} / {TotalSteps}";
    public string StepPercentage => TotalSteps > 0 ? $"{(CurrentStepIndex * 100 / TotalSteps)}%" : "0%";

    // ============================================
    // OPSEC STATE
    // ============================================
    [ObservableProperty] private bool _opSecEnabled;

    // ============================================
    // STATE INSPECTOR
    // ============================================
    public ObservableCollection<string> StackRows { get; } = new();
    public ObservableCollection<string> MemoryRows { get; } = new();
    public ObservableCollection<string> StorageRows { get; } = new();

    // ============================================
    // INSTRUCTIONS
    // ============================================
    public ObservableCollection<InstructionViewModel> Instructions { get; } = new();
    public ObservableCollection<GasNodeViewModel> GasTreeNodes { get; } = new();

    // ============================================
    // SECURITY FINDINGS
    // ============================================
    public ObservableCollection<SecurityFindingViewModel> SecurityFindings { get; } = new();
    
    [ObservableProperty] private int _criticalCount;
    [ObservableProperty] private int _warningCount;

    // ============================================
    // FUZZER
    // ============================================
    public ObservableCollection<string> Precompiles { get; } = new()
    {
        "",  // Empty default
        "SHA256 (0x02)",
        "RIPEMD160 (0x03)",
        "ID (0x04)",
        "MODEXP (0x05)",
        "BN254_ADD (0x06)",
        "BN254_MUL (0x07)",
        "BN254_PAIRING (0x08)",
        "BLAKE2_F (0x09)",
        "KZG_POINT_EVAL (0x0A)"
    };
    
    public ObservableCollection<string> FuzzerResults { get; } = new();

    // ============================================
    // STATUS
    // ============================================
    [ObservableProperty] private string _statusMessage = "Ready — Load a transaction trace to begin analysis";

    // ============================================
    // PLAYBACK COMMANDS
    // ============================================
    
    [RelayCommand]
    private void StepForward()
    {
        if (CurrentStepIndex < TotalSteps)
            CurrentStepIndex++;
    }

    [RelayCommand]
    private void StepBack()
    {
        if (CurrentStepIndex > 0)
            CurrentStepIndex--;
    }

    [RelayCommand]
    private void JumpToStart() => CurrentStepIndex = 0;

    [RelayCommand]
    private void JumpToEnd() => CurrentStepIndex = TotalSteps;

    [RelayCommand]
    private void TogglePlayback()
    {
        // Playback removed - just show results immediately
    }

    // ============================================
    // TRANSACTION COMMANDS
    // ============================================
    
    [RelayCommand]
    private void RunTransaction()
    {
        StatusMessage = "Executing transaction through Scrutor.Core...";
        
        // Execute transaction - get full trace and analysis
        var result = _executionService.RunFullTransaction();
        _currentTrace = result.TraceSteps;
        
        // Clear all panels
        Instructions.Clear();
        StackRows.Clear();
        MemoryRows.Clear();
        StorageRows.Clear();
        SecurityFindings.Clear();
        GasTreeNodes.Clear();
        
        // Populate instructions (disassembly)
        for (int i = 0; i < result.TraceSteps.Count; i++)
        {
            var step = result.TraceSteps[i];
            Instructions.Add(new InstructionViewModel(
                step.Pc.ToString("X4"),
                step.Op,
                int.TryParse(step.GasCost.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out int gc) ? gc : 0,
                step.CallType?.ToString() ?? "ROOT"
            ));
        }
        
        // Run security analysis
        var reentrancy = ReentrancyDetector.Analyze(result.TraceSteps);
        var collisions = StorageCollisionDetector.Analyze(result.TraceSteps);
        
        // Populate findings
        foreach (var f in reentrancy)
        {
            SecurityFindings.Add(new SecurityFindingViewModel
            {
                SeverityEmoji = f.Severity == ReentrancySeverity.Critical ? "🔴" : "⚠️",
                Description = $"REENTRANCY: {f.Severity} - Depth {f.DepthDelta}",
                Details = $"Target: {f.TargetContract} Entry: step {f.InitialEntryStep}",
                StepIndex = f.ReentryStep
            });
        }
        
        foreach (var c in collisions)
        {
            SecurityFindings.Add(new SecurityFindingViewModel
            {
                SeverityEmoji = "⚠️",
                Description = $"STORAGE COLLISION: Slot {c.CollidingSlot}",
                Details = $"Proxy: {c.ProxyContract} Impl: {c.ImplementationContract}",
                StepIndex = c.StepIndex
            });
        }
        
        // Populate gas tree
        GasTreeNodes.Add(new GasNodeViewModel { DisplayText = $"▼ TOTAL: {result.GasUsed:N0} gas", Indent = new(0, 0, 0, 8), Color = "#FFFFFF" });
        GasTreeNodes.Add(new GasNodeViewModel { DisplayText = "├── Intrinsic: 21,000", Indent = new(16, 2) });
        GasTreeNodes.Add(new GasNodeViewModel { DisplayText = $"├── Computation: {result.GasUsed - 21000:N0}", Indent = new(16, 2) });
        GasTreeNodes.Add(new GasNodeViewModel { DisplayText = $"│   └── Steps: {result.TraceSteps.Count}", Indent = new(32, 2), Color = "#FFAA00" });
        GasTreeNodes.Add(new GasNodeViewModel { DisplayText = "└── Refunds: 0", Indent = new(16, 2), Color = "#00D4AA" });
        
        // Show final state (last step)
        if (result.TraceSteps.Count > 0)
        {
            var lastStep = result.TraceSteps[^1];
            foreach (var item in lastStep.Stack.Take(5))
                StackRows.Add(item);
            foreach (var row in lastStep.Memory.Take(8))
                MemoryRows.Add(row);
        }
        
        TotalSteps = result.TraceSteps.Count;
        CurrentStepIndex = result.TraceSteps.Count - 1;
        CriticalCount = reentrancy.Count(r => r.Severity == ReentrancySeverity.Critical);
        WarningCount = collisions.Count + reentrancy.Count(r => r.Severity == ReentrancySeverity.Medium);
        
        StatusMessage = $"✓ COMPLETE: {result.TraceSteps.Count} steps | {CriticalCount} critical | {WarningCount} warnings | {result.GasUsed:N0} gas";
    }
    
    private void StopPlayback()
    {
        _isPlaying = false;
        IsPlayingState = false;
        _playbackTimer?.Stop();
        _playbackTimer?.Dispose();
        _playbackTimer = null;
    }
    
    private void UpdateStepDisplay()
    {
        if (_currentTrace.Count == 0 || CurrentStepIndex < 0 || CurrentStepIndex >= _currentTrace.Count)
            return;
        
        var step = _currentTrace[CurrentStepIndex];
        
        // Update stack
        StackRows.Clear();
        for (int i = step.Stack.Count - 1; i >= 0; i--)
        {
            StackRows.Add($"[{i}] {step.Stack[i]}");
        }
        
        // Update memory
        MemoryRows.Clear();
        foreach (var row in step.Memory)
        {
            MemoryRows.Add(row);
        }
        
        // Update storage
        StorageRows.Clear();
        foreach (var kvp in step.Storage)
        {
            StorageRows.Add($"{kvp.Key}: {kvp.Value}");
        }
        
        StatusMessage = $"Step {CurrentStepIndex + 1}/{TotalSteps} | {step.Op} | Depth: {step.Depth} | Gas: {step.Gas}";
    }

    // ============================================
    // OPSEC COMMANDS
    // ============================================
    // INITIALIZATION
    // ============================================
    
    public WorkbenchViewModel()
    {
        // Start with clean empty state - user must click RUN to load data
        StatusMessage = "Ready — Click RUN TRANSACTION to load a 421-step execution trace";
    }
}

public class InstructionViewModel
{
    public string PC { get; }
    public string Opcode { get; }
    public int GasCost { get; }
    public string CallType { get; }

    public InstructionViewModel(string pc, string opcode, int gasCost, string callType)
    {
        PC = pc;
        Opcode = opcode;
        GasCost = gasCost;
        CallType = callType;
    }
}
