using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scrutor.Core.Execution;
using Scrutor.Core.Security;
using Avalonia.Data.Converters;

namespace Scrutor.UI.ViewModels;

/// <summary>
/// Main ViewModel for the Scrutor EVM Workbench.
/// Connects directly to the core engine sensors and security detectors.
/// </summary>
public partial class WorkbenchViewModel : ObservableObject
{
    // ============================================
    // EXECUTION STATE
    // ============================================
    
    [ObservableProperty]
    private bool _isLoading;
    
    [ObservableProperty]
    private string _statusMessage = "Ready";
    
    [ObservableProperty]
    private string _forkVersion = "CANCUN";
    
    [ObservableProperty]
    private bool _opSecEnabled;
    
    // ============================================
    // TRANSACTIONS INPUTS
    // ============================================
    
    [ObservableProperty]
    private string _toAddress = string.Empty;
    
    [ObservableProperty]
    private string _gasLimit = "500000";
    
    [ObservableProperty]
    private string _calldata = string.Empty;
    
    // ============================================
    // TRACE SCROLLBACK
    // ============================================
    
    [ObservableProperty]
    private int _currentStepIndex;
    
    [ObservableProperty]
    private int _totalSteps;
    
    [ObservableProperty]
    private bool _isPlaying;
    
    public ObservableCollection<ExecutionTraceStep> TraceSteps { get; } = new();
    
    // ============================================
    // SECURITY FINDINGS
    // ============================================
    
    public ObservableCollection<ReentrancyFinding> ReentrancyFindings { get; } = new();
    public ObservableCollection<StorageCollisionFinding> StorageCollisionFindings { get; } = new();
    
    [ObservableProperty]
    private int _criticalCount;
    
    [ObservableProperty]
    private int _warningCount;
    
    // ============================================
    // STATE INSPECTOR
    // ============================================
    
    public ObservableCollection<string> StackItems { get; } = new();
    public ObservableCollection<string> MemoryRows { get; } = new();
    public ObservableCollection<string> StorageSlots { get; } = new();
    public ObservableCollection<string> EventLogs { get; } = new();
    
    // ============================================
    // GAS TREE
    // ============================================
    
    [ObservableProperty]
    private string _gasTreeRoot = "No execution yet";
    
    [ObservableProperty]
    private ulong _totalGasUsed;
    
    // ============================================
    // CURRENT STEP DETAILS
    // ============================================
    
    [ObservableProperty]
    private string _currentOpcode = string.Empty;
    
    [ObservableProperty]
    private int _currentPc;
    
    [ObservableProperty]
    private int _currentDepth;
    
    [ObservableProperty]
    private string _currentCallType = string.Empty;
    
    [ObservableProperty]
    private int _currentGasCost;
    
    /// <summary>
    /// Loads an execution result into the workbench.
    /// </summary>
    public void LoadExecutionResult(ExecutionResult result)
    {
        IsLoading = true;
        StatusMessage = "Loading execution trace...";
        
        try
        {
            // Clear previous state
            TraceSteps.Clear();
            ReentrancyFindings.Clear();
            StorageCollisionFindings.Clear();
            StackItems.Clear();
            MemoryRows.Clear();
            StorageSlots.Clear();
            EventLogs.Clear();
            
            // Load trace steps
            if (result.TraceSteps != null)
            {
                foreach (var step in result.TraceSteps)
                {
                    TraceSteps.Add(step);
                }
                
                TotalSteps = TraceSteps.Count;
                CurrentStepIndex = 0;
            }
            
            // Run security analysis
            RunSecurityAnalysis();
            
            // Update gas totals
            TotalGasUsed = result.GasUsed;
            
            // Build gas tree visualization
            BuildGasTreeVisualization(result);
            
            StatusMessage = $"Loaded {TotalSteps} steps, {CriticalCount} critical findings";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    /// <summary>
    /// Runs all security detectors on the loaded trace.
    /// </summary>
    private void RunSecurityAnalysis()
    {
        if (TraceSteps.Count == 0) return;
        
        // Run reentrancy detection
        var reentrancyFindings = ReentrancyDetector.Analyze(TraceSteps);
        foreach (var finding in reentrancyFindings)
        {
            ReentrancyFindings.Add(finding);
        }
        
        // Run storage collision detection
        var collisionFindings = StorageCollisionDetector.Analyze(TraceSteps);
        foreach (var finding in collisionFindings)
        {
            StorageCollisionFindings.Add(finding);
        }
        
        // Update counts
        CriticalCount = ReentrancyFindings.Count(f => f.Severity == ReentrancySeverity.Critical)
                      + StorageCollisionFindings.Count;
        WarningCount = ReentrancyFindings.Count(f => f.Severity == ReentrancySeverity.Medium);
    }
    
    /// <summary>
    /// Updates the state inspector for the current step.
    /// </summary>
    partial void OnCurrentStepIndexChanged(int value)
    {
        if (value < 0 || value >= TraceSteps.Count) return;
        
        var step = TraceSteps[value];
        UpdateStepDetails(step);
    }
    
    private void UpdateStepDetails(ExecutionTraceStep step)
    {
        // Update opcode display
        CurrentOpcode = step.Op ?? "UNKNOWN";
        CurrentPc = step.Pc;
        CurrentDepth = step.Depth;
        CurrentCallType = step.CallType?.ToString() ?? "Root";
        
        // Update stack display
        StackItems.Clear();
        if (step.Stack != null)
        {
            for (int i = 0; i < step.Stack.Count; i++)
            {
                StackItems.Add($"{i}: {step.Stack[i]}");
            }
        }
        
        // Update memory display
        MemoryRows.Clear();
        if (step.Memory != null)
        {
            for (int i = 0; i < step.Memory.Count; i++)
            {
                MemoryRows.Add($"{i:X4}: {step.Memory[i]}");
            }
        }
        
        // Update storage display
        StorageSlots.Clear();
        if (step.Storage != null)
        {
            foreach (var kvp in step.Storage)
            {
                StorageSlots.Add($"{kvp.Key}: {kvp.Value}");
            }
        }
        
        // Show contract address
        if (!string.IsNullOrEmpty(step.ContractAddress))
        {
            StatusMessage = $"Step {CurrentStepIndex}/{TotalSteps} | {step.ContractAddress} | {step.Op}";
        }
    }
    
    private void BuildGasTreeVisualization(ExecutionResult result)
    {
        if (result.GasUsed == 0)
        {
            GasTreeRoot = "No gas used";
            return;
        }
        
        // Simple gas tree for now
        GasTreeRoot = $"▼ Tx Total: {result.GasUsed:N0} gas\n" +
                      $"├── Intrinsic Base: 21,000\n" +
                      $"└── Execution: {result.GasUsed - 21000:N0}";
    }
    
    // ============================================
    // PLAYBACK COMMANDS
    // ============================================
    
    [RelayCommand]
    private void StepBack()
    {
        if (CurrentStepIndex > 0)
            CurrentStepIndex--;
    }
    
    [RelayCommand]
    private void StepForward()
    {
        if (CurrentStepIndex < TotalSteps - 1)
            CurrentStepIndex++;
    }
    
    [RelayCommand]
    private void JumpToStart()
    {
        CurrentStepIndex = 0;
    }
    
    [RelayCommand]
    private void JumpToEnd()
    {
        CurrentStepIndex = TotalSteps - 1;
    }
    
    [RelayCommand]
    private void JumpToStep(int stepIndex)
    {
        if (stepIndex >= 0 && stepIndex < TotalSteps)
            CurrentStepIndex = stepIndex;
    }
    
    /// <summary>
    /// Jumps to the step where a security finding occurred.
    /// </summary>
    public void JumpToFinding(ReentrancyFinding finding)
    {
        JumpToStep(finding.ReentryStep);
    }
    
    public void JumpToFinding(StorageCollisionFinding finding)
    {
        JumpToStep(finding.StepIndex);
    }
    
    // ============================================
    // OPSEC TOGGLE
    // ============================================
    
    [RelayCommand]
    private void ToggleOpSec()
    {
        OpSecEnabled = !OpSecEnabled;
        OpSecLockout.IsEnabled = OpSecEnabled;
        StatusMessage = OpSecEnabled ? "🔒 OpSec Mode ACTIVE - No network calls" : "🔓 OpSec Mode OFF";
    }
    
    // ============================================
    // VALUE CONVERTERS
    // ============================================
    
    public static readonly IValueConverter PlayPauseConverter = 
        new FuncValueConverter<bool, string>(isPlaying => isPlaying ? "⏸" : "▶");
    
    public static readonly IValueConverter OpSecColorConverter =
        new FuncValueConverter<bool, string>(enabled => enabled ? "#FF4444" : "#333");
}
