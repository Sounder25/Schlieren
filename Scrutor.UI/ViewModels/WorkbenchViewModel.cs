using System.Collections.ObjectModel;
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

    // ============================================
    // CODE EDITOR & FILE SYSTEM
    // ============================================
    public ObservableCollection<ProjectFileViewModel> ProjectFiles { get; } = new();
    public ObservableCollection<CodeLineViewModel> ActiveCodeLines { get; } = new();
    
    [ObservableProperty] private ProjectFileViewModel? _selectedFile;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isInspectorExpanded = true;
    [ObservableProperty] private bool _isCallGraphVisible;
    public CallTopologyViewModel CallTopology { get; } = new();
    
    public string CurrentFileTitle => SelectedFile != null ? $"{SelectedFile.FileName} — Line {CurrentStepIndex + 1} / {SelectedFile.Lines.Count}" : "No file loaded";

    [RelayCommand]
    private void SelectFile(ProjectFileViewModel file)
    {
        IsCallGraphVisible = false;
        foreach (var f in ProjectFiles) f.IsSelected = false;
        SelectedFile = file;
        SelectedFile.IsSelected = true;
        
        ActiveCodeLines.Clear();
        foreach (var line in file.Lines)
            ActiveCodeLines.Add(line);
    }
    
    [RelayCommand]
    private void ShowCallGraph()
    {
        foreach (var f in ProjectFiles) f.IsSelected = false;
        IsCallGraphVisible = true;
    }
    
    [RelayCommand]
    private void JumpToStep(int stepIndex)
    {
        if (stepIndex >= 0 && stepIndex < TotalSteps)
            CurrentStepIndex = stepIndex;
    }

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
    [ObservableProperty] private bool _opSecEnabled = true;

    // ============================================
    // STATE INSPECTOR
    // ============================================
    public ObservableCollection<string> StackRows { get; } = new();
    public ObservableCollection<string> MemoryRows { get; } = new();
    public ObservableCollection<string> StorageRows { get; } = new();

    // ============================================
    // INSTRUCTIONS & GAS TREE
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
        "",
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
    [ObservableProperty] private string _statusMessage = "Ready — Load a contract workspace or run simulation";

    // ============================================
    // INITIALIZATION
    // ============================================
    public WorkbenchViewModel()
    {
        LoadSampleWorkspace();
        OpSecLockout.IsEnabled = true;
        RunTransaction();
    }

    private void LoadSampleWorkspace()
    {
        var vaultLines = new[]
        {
            "// SPDX-License-Identifier: MIT",
            "pragma solidity ^0.8.24;",
            "",
            "contract Vault {",
            "    mapping(address => uint256) public balances;",
            "    bool private locked;",
            "",
            "    event Deposit(address indexed sender, uint256 amount);",
            "    event Withdraw(address indexed sender, uint256 amount);",
            "",
            "    function deposit() external payable {",
            "        require(msg.value > 0, \"Zero deposit\");",
            "        balances[msg.sender] += msg.value;",
            "        emit Deposit(msg.sender, msg.value);",
            "    }",
            "",
            "    /// @notice Withdraw funds from vault",
            "    function withdraw(uint256 amount) external {",
            "        require(balances[msg.sender] >= amount, \"Insufficient balance\");",
            "        ",
            "        // 🔴 REENTRANCY VULNERABILITY:",
            "        // External interaction precedes state modification!",
            "        (bool success, ) = msg.sender.call{value: amount}(\"\");",
            "        require(success, \"Transfer failed\");",
            "",
            "        // State mutation AFTER external call!",
            "        balances[msg.sender] -= amount;",
            "        emit Withdraw(msg.sender, amount);",
            "    }",
            "}"
        };

        var proxyLines = new[]
        {
            "// SPDX-License-Identifier: MIT",
            "pragma solidity ^0.8.24;",
            "",
            "contract ERC1967Proxy {",
            "    // ⚠️ STORAGE COLLISION VULNERABILITY:",
            "    // Slot 0 holds owner address, but implementation uses slot 0 for balances!",
            "    address public owner;",
            "    address public implementation;",
            "",
            "    fallback() external payable {",
            "        address impl = implementation;",
            "        assembly {",
            "            calldatacopy(0, 0, calldatasize())",
            "            let result := delegatecall(gas(), impl, 0, calldatasize(), 0, 0)",
            "            returndatacopy(0, 0, returndatasize())",
            "            switch result",
            "            case 0 { revert(0, returndatasize()) }",
            "            default { return(0, returndatasize()) }",
            "        }",
            "    }",
            "}"
        };

        var tokenLines = new[]
        {
            "// SPDX-License-Identifier: MIT",
            "pragma solidity ^0.8.24;",
            "",
            "contract ERC20Token {",
            "    string public name = \"Scrutor Test Token\";",
            "    string public symbol = \"SCR\";",
            "    uint8 public decimals = 18;",
            "    uint256 public totalSupply = 1_000_000 * 10**18;",
            "    mapping(address => uint256) public balanceOf;",
            "",
            "    constructor() {",
            "        balanceOf[msg.sender] = totalSupply;",
            "    }",
            "}"
        };

        ProjectFiles.Add(new ProjectFileViewModel("Vault.sol", "contracts/Vault.sol", vaultLines, new HashSet<int> { 23 }));
        ProjectFiles.Add(new ProjectFileViewModel("Proxy.sol", "contracts/Proxy.sol", proxyLines, new HashSet<int> { 14 }));
        ProjectFiles.Add(new ProjectFileViewModel("Token.sol", "contracts/Token.sol", tokenLines));

        SelectedFile = ProjectFiles[0];
        SelectedFile.IsSelected = true;
        
        // Load code lines for display
        foreach (var line in SelectedFile.Lines)
            ActiveCodeLines.Add(line);
    }

    // ============================================
    // COMMANDS
    // ============================================

    [RelayCommand]
    private void ToggleInspector()
    {
        IsInspectorExpanded = !IsInspectorExpanded;
    }

    [RelayCommand]
    private void JumpToFinding(SecurityFindingViewModel finding)
    {
        // Find file
        var file = ProjectFiles.FirstOrDefault(f => f.FileName.Equals(finding.FileName, StringComparison.OrdinalIgnoreCase));
        if (file != null)
        {
            SelectFile(file);
            
            // Highlight target line
            foreach (var l in file.Lines)
            {
                l.IsActiveLine = (l.LineNumber == finding.LineNumber);
            }
        }

        // Scrub timeline to exact step
        if (finding.StepIndex >= 0 && finding.StepIndex < TotalSteps)
        {
            CurrentStepIndex = finding.StepIndex;
        }

        StatusMessage = $"Focused: {finding.LocationText} — {finding.Description}";
    }

    [RelayCommand]
    private void StepForward()
    {
        if (_currentTrace.Count == 0) RunTransaction();
        if (CurrentStepIndex < TotalSteps - 1)
            CurrentStepIndex++;
    }

    [RelayCommand]
    private void StepBack()
    {
        if (_currentTrace.Count == 0) RunTransaction();
        if (CurrentStepIndex > 0)
            CurrentStepIndex--;
    }

    [RelayCommand]
    private void JumpToStart()
    {
        if (_currentTrace.Count == 0) RunTransaction();
        CurrentStepIndex = 0;
    }

    [RelayCommand]
    private void JumpToEnd()
    {
        if (_currentTrace.Count == 0) RunTransaction();
        CurrentStepIndex = Math.Max(0, TotalSteps - 1);
    }

    [RelayCommand]
    private void TogglePlayback()
    {
        IsPlayingState = !IsPlayingState;
    }

    // ============================================
    // TRANSACTION RUNNER
    // ============================================
    
    [RelayCommand]
    private void RunTransaction()
    {
        StatusMessage = "Running transaction execution & security scanning...";
        
        var result = _executionService.RunFullTransaction();
        _currentTrace = result.TraceSteps;
        
        Instructions.Clear();
        StackRows.Clear();
        MemoryRows.Clear();
        StorageRows.Clear();
        SecurityFindings.Clear();
        GasTreeNodes.Clear();
        
        // Populate instructions
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
        
        // Security analysis
        var reentrancy = ReentrancyDetector.Analyze(result.TraceSteps);
        var collisions = StorageCollisionDetector.Analyze(result.TraceSteps);
        
        foreach (var f in reentrancy)
        {
            SecurityFindings.Add(new SecurityFindingViewModel
            {
                SeverityEmoji = f.Severity == ReentrancySeverity.Critical ? "🔴" : "⚠️",
                Description = $"REENTRANCY: {f.Severity} - Depth Delta {f.DepthDelta}",
                Details = $"Target: {f.TargetContract} | Re-entered at step {f.ReentryStep}",
                FileName = "Vault.sol",
                LineNumber = 23,
                StepIndex = f.ReentryStep
            });
        }
        
        foreach (var c in collisions)
        {
            SecurityFindings.Add(new SecurityFindingViewModel
            {
                SeverityEmoji = "⚠️",
                Description = $"STORAGE COLLISION: Slot {c.CollidingSlot}",
                Details = $"Proxy: {c.ProxyContract} | Implementation: {c.ImplementationContract}",
                FileName = "Proxy.sol",
                LineNumber = 14,
                StepIndex = c.StepIndex
            });
        }
        
        // Gas tree
        GasTreeNodes.Add(new GasNodeViewModel { DisplayText = $"▼ TOTAL: {result.GasUsed:N0} gas", Indent = new(0, 0, 0, 8), Color = "#FFFFFF" });
        GasTreeNodes.Add(new GasNodeViewModel { DisplayText = "├── Intrinsic: 21,000", Indent = new(16, 2) });
        GasTreeNodes.Add(new GasNodeViewModel { DisplayText = $"├── Computation: {result.GasUsed - 21000:N0}", Indent = new(16, 2) });
        GasTreeNodes.Add(new GasNodeViewModel { DisplayText = $"│   └── Steps: {result.TraceSteps.Count}", Indent = new(32, 2), Color = "#FFAA00" });
        GasTreeNodes.Add(new GasNodeViewModel { DisplayText = "└── Refunds: 0", Indent = new(16, 2), Color = "#00D4AA" });
        
        TotalSteps = result.TraceSteps.Count;
        
        // Load call topology from trace
        CallTopology.LoadFromTrace(result.TraceSteps);
        CurrentStepIndex = Math.Min(28, result.TraceSteps.Count - 1);
        CriticalCount = reentrancy.Count(r => r.Severity == ReentrancySeverity.Critical);
        WarningCount = collisions.Count + reentrancy.Count(r => r.Severity == ReentrancySeverity.Medium);
        
        StatusMessage = $"✓ COMPLETE: {result.TraceSteps.Count} steps | {CriticalCount} Critical | {WarningCount} Warnings | {result.GasUsed:N0} gas";
    }

    partial void OnCurrentStepIndexChanged(int value)
    {
        if (_currentTrace.Count == 0 || value < 0 || value >= _currentTrace.Count)
            return;

        var step = _currentTrace[value];
        CurrentStep = step;

        StackRows.Clear();
        for (int i = step.Stack.Count - 1; i >= 0; i--)
            StackRows.Add($"[{i}] {step.Stack[i]}");

        MemoryRows.Clear();
        foreach (var row in step.Memory)
            MemoryRows.Add(row);

        StorageRows.Clear();
        foreach (var kvp in step.Storage)
            StorageRows.Add($"{kvp.Key}: {kvp.Value}");

        // Map step PC to line highlighting in active file & ActiveCodeLines
        UpdateActiveLineForStep(value);

        StatusMessage = $"Step {value + 1} / {TotalSteps} | {step.Op} | Depth: {step.Depth} | Gas: {step.Gas}";
    }

    private void UpdateActiveLineForStep(int stepIndex)
    {
        if (SelectedFile == null) return;

        int targetLine = 1;
        if (SelectedFile.FileName.Equals("Vault.sol", StringComparison.OrdinalIgnoreCase))
        {
            targetLine = stepIndex switch
            {
                < 4 => 11,
                < 8 => 12,
                < 12 => 13,
                < 16 => 14,
                < 20 => 18,
                < 24 => 19,
                < 29 => 23,
                < 32 => 24,
                < 34 => 27,
                _ => 28
            };
        }
        else if (SelectedFile.FileName.Equals("Proxy.sol", StringComparison.OrdinalIgnoreCase))
        {
            targetLine = (stepIndex % 2 == 0) ? 14 : 11;
        }

        foreach (CodeLineViewModel l in SelectedFile.Lines)
        {
            l.IsActiveLine = (l.LineNumber == targetLine);
        }

        ActiveCodeLines.Clear();
        foreach (CodeLineViewModel line in SelectedFile.Lines)
        {
            ActiveCodeLines.Add(line);
        }
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
