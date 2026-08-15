using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Inspect;
using Schlieren.Core.Security;
using Schlieren.UI.Services;

namespace Schlieren.UI.ViewModels;

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
    private CancellationTokenSource? _runCts;
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
    public ObservableCollection<DiagnosticFinding> Diagnostics { get; } = new();
    public ObservableCollection<string> EventLogRows { get; } = new();
    public ObservableCollection<string> AccountStateRows { get; } = new();

    public ObservableCollection<string> AvailableForks { get; } = new()
    {
        "Osaka", "Prague", "Cancun", "Shanghai", "Paris", "London", "Berlin", "Istanbul"
    };

    [ObservableProperty] private ProjectFileViewModel? _selectedFile;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isInspectorExpanded = true;
    [ObservableProperty] private bool _isCallGraphVisible;
    [ObservableProperty] private string _selectedFork = "Osaka";
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

    // Tx construction (auditor essentials)
    [ObservableProperty] private string _txFrom = "0x0000000000000000000000000000000000000001";
    [ObservableProperty] private string _txTo = "0x00000000000000000000000000000000000000aa";
    [ObservableProperty] private string _txValueWei = "0";
    [ObservableProperty] private string _callDataHex = "";
    [ObservableProperty] private ulong _gasPriceGwei = 1;
    [ObservableProperty] private bool _isTxParamsExpanded;
    [ObservableProperty] private bool _isPrestateExpanded;
    [ObservableProperty] private string _prestateSummary = "No extra accounts — File → Load Pre-state JSON";
    [ObservableProperty] private string _prestateSource = "";

    public ObservableCollection<string> PrestateAccountRows { get; } = new();
    private readonly List<WorkbenchAccountSeed> _extraAccounts = new();
    private readonly List<WorkbenchFixtureLoader.ExpectedAccount> _expectedPost = new();
    private string? _baseFeeWei;
    private string? _gasPriceWei;
    private string? _maxFeeWei;
    private string? _maxPriorityWei;
    private byte _txType;
    private ulong _txNonce;
    private bool? _lastFixturePostMatches;
    private string _lastFixtureNote = "";
    private IReadOnlyList<Schlieren.Core.State.AccessListEntry>? _accessList;
    private IReadOnlyList<Schlieren.Core.State.Eip7702Authorization>? _authorizations;

    // Last-run outcome
    [ObservableProperty] private bool _lastRunSuccess;
    [ObservableProperty] private string _resultBanner = "No run yet";
    [ObservableProperty] private string _resultBannerColor = "#A9A9A9";
    [ObservableProperty] private string _returnDataHex = "0x";
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private string _pcSearch = "";
    [ObservableProperty] private string _lastCallerAddress = "";
    [ObservableProperty] private string _lastContractAddress = "";
    [ObservableProperty] private string _resultVerdict = "WAITING";
    [ObservableProperty] private string _resultExplain = "Nothing has run yet. Open or paste bytecode, set the fork, then press RUN or F5.";
    [ObservableProperty] private string _stackText = "(empty)";
    [ObservableProperty] private string _memoryText = "(empty)";
    [ObservableProperty] private string _storageText = "(empty — scrub to last step after RUN)";
    [ObservableProperty] private string _accountsText = "(empty)";
    [ObservableProperty] private string _logsText = "(empty)";
    [ObservableProperty] private string _gasText = "(empty)";
    [ObservableProperty] private string _diagnosisText = "(no diagnosis — run with mismatches)";
    
    private InspectResult? _lastInspectResult;
    [ObservableProperty] private bool _isCallFramePinned;
    private IReadOnlyDictionary<string, IReadOnlyList<string>> _postStorage =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>
    /// Soft center watermark. Stronger when empty, ghosted when code is up.
    /// Multiplied by the active skin's WatermarkBoost (art skins can go bolder).
    /// </summary>
    public double WatermarkOpacity
    {
        get
        {
            var bas = HasOpenFiles || HasTrace ? 0.07 : 0.20;
            var boost = Services.SkinService.Current.WatermarkBoost;
            return Math.Clamp(bas * boost, 0.02, 0.42);
        }
    }

    public string CurrentFileTitle =>
        SelectedFile != null
            ? $"{SelectedFile.FileName} ({SelectedFile.Lines.Count} lines)"
            : "No file loaded";

    /// <summary>What this RUN will execute — not the editor tab.</summary>
    public string ExecutionTargetLine
    {
        get
        {
            BytecodeExecutionService.TryParseHexBytes(BytecodeInput, out var box);
            var fromPre = _extraAccounts.FirstOrDefault(a =>
                a.AddressHex.Equals(TxTo, StringComparison.OrdinalIgnoreCase));
            var preLen = 0;
            if (fromPre != null)
                BytecodeExecutionService.TryParseHexBytes(fromPre.CodeHex, out var pc);
            if (fromPre != null)
            {
                BytecodeExecutionService.TryParseHexBytes(fromPre.CodeHex, out var preCode);
                preLen = preCode.Length;
            }
            var bytes = box.Length > 0 ? box.Length : preLen;
            var src = box.Length > 0 ? "bytecode box" : preLen > 0 ? "pre-state" : "no code";
            return $"EXEC {ShortAddr(TxTo)}  ·  {bytes} B ({src})  ·  {SelectedFork}";
        }
    }

    public string EditorFileLine =>
        SelectedFile == null
            ? "OPEN (none)"
            : LooksLikeExecutionHex(SelectedFile)
                ? $"OPEN {SelectedFile.FileName}  ·  editor matches hex target"
                : $"OPEN {SelectedFile.FileName}  ·  editor only — not this run";

    private static bool LooksLikeExecutionHex(ProjectFileViewModel file)
    {
        var joined = string.Join("", file.Lines).Trim();
        return joined.Length >= 4 && joined.All(ch =>
            Uri.IsHexDigit(ch) || ch is 'x' or 'X' or '0' or '\n' or '\r' or ' ');
    }

    private static string ShortAddr(string? a)
    {
        if (string.IsNullOrWhiteSpace(a)) return "(no To)";
        a = a.Trim();
        return a.Length > 12 && a.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? a[..6] + "…" + a[^4..]
            : a;
    }

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
    /// Live bytecode runs use this fork's rule set (precompiles, gas schedule).
    /// </summary>
    public string ForkNote =>
        $"{SelectedFork} · live EVM rules + block fields";

    public WorkbenchViewModel()
    {
        ApplyOpSec();
        RefreshFilteredFiles();
        Services.SkinService.SkinChanged += OnSkinChanged;
    }

    private void OnSkinChanged(Branding.UiSkin _)
        => OnPropertyChanged(nameof(WatermarkOpacity));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Services.SkinService.SkinChanged -= OnSkinChanged;
        StopAutoPlay();
        try { _runCts?.Cancel(); } catch { /* ignore */ }
        _runCts?.Dispose();
        GC.SuppressFinalize(this);
    }

    partial void OnSearchQueryChanged(string value) => RefreshFilteredFiles();
    partial void OnOpSecEnabledChanged(bool value) => ApplyOpSec();
    partial void OnSelectedForkChanged(string value)
    {
        OnPropertyChanged(nameof(ForkNote));
        NotifyExecutionChrome();
    }
    partial void OnBytecodeInputChanged(string value) => NotifyExecutionChrome();
    partial void OnTxToChanged(string value) => NotifyExecutionChrome();

    private void NotifyExecutionChrome()
    {
        OnPropertyChanged(nameof(ExecutionTargetLine));
        OnPropertyChanged(nameof(EditorFileLine));
    }

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
        NotifyExecutionChrome();
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
    private void SelectCallGraphRow(CallGraphRowViewModel? row)
    {
        if (row is null) return;

        if (row.IsEdge && row.StepIndex >= 0)
        {
            CurrentStepIndex = row.StepIndex;
            StatusMessage = row.Title;
            return;
        }

        if (row.FrameKey is "root" or "")
        {
            UnpinCallFrame();
            return;
        }

        IsCallFramePinned = true;
        ReturnDataHex = string.IsNullOrWhiteSpace(row.ReturnHint) ? ReturnDataHex : row.ReturnHint;
        if (row.Kind.Equals("Precompile", StringComparison.OrdinalIgnoreCase))
            StorageText = "(precompile — no contract storage)";
        else if (!string.IsNullOrEmpty(row.Address) &&
                 _postStorage.TryGetValue(row.Address, out var slots) &&
                 slots.Count > 0)
            StorageText = string.Join(Environment.NewLine, slots);
        else if (row.Kind.Equals("Contract", StringComparison.OrdinalIgnoreCase))
            StorageText = "(no post-state storage on this account)";
        AccountsText =
            $"{row.Title}{Environment.NewLine}" +
            $"{row.Kind}{Environment.NewLine}" +
            $"{row.Address}{Environment.NewLine}" +
            $"{(row.Success is true ? "SUCCESS" : row.Success is false ? "FAILURE" : "UNKNOWN")}{Environment.NewLine}" +
            $"gas used: {row.GasUsed?.ToString("N0") ?? "—"}";
        ResultVerdict = row.Success is false ? "FAIL" : "PASS";
        ResultExplain =
            $"Showing the {row.Title} frame. Click Root or move the step slider to return to the parent contract.";
        if (row.StepIndex >= 0)
            CurrentStepIndex = row.StepIndex;
        StatusMessage = $"Inspecting {row.Title}";
    }

    private void UnpinCallFrame()
    {
        if (!IsCallFramePinned) return;
        IsCallFramePinned = false;
        RefreshInspectorTexts();
        RefreshResultExplain();
        StatusMessage = "Back to parent frame";
    }

    [RelayCommand]
    private void ShowCallGraph()
    {
        foreach (var f in ProjectFiles)
            f.IsSelected = false;
        CallTopology.LoadFromTrace(_currentTrace);
        IsCallGraphVisible = true;
        StatusMessage = CallTopology.EmptyHint;
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
    private void ToggleTxParams() => IsTxParamsExpanded = !IsTxParamsExpanded;

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
        GasPriceGwei = GasPriceGwei,
        ChainId = ChainId,
        CoinbaseHex = CoinbaseAddress,
        CallerHex = TxFrom,
        ContractHex = TxTo,
        ValueWei = TxValueWei,
        CallDataHex = CallDataHex,
        ForkLabel = SelectedFork,
        ExtraAccounts = _extraAccounts.Count == 0 ? null : _extraAccounts.ToList(),
        BaseFeeWei = _baseFeeWei,
        GasPriceWei = _gasPriceWei,
        MaxFeeWei = _maxFeeWei,
        MaxPriorityWei = _maxPriorityWei,
        TxType = _txType,
        AccessList = _accessList,
        AuthorizationList = _authorizations,
        Nonce = _txNonce,
        SnapshotAddresses = _expectedPost.Select(a => a.AddressHex).ToList()
    };

    public string LoadPrestateJson(string json, string sourceName)
    {
        var parsed = WorkbenchPrestateLoader.Parse(json);
        if (!parsed.Ok)
        {
            StatusMessage = parsed.Error;
            return parsed.Error;
        }

        _extraAccounts.Clear();
        _extraAccounts.AddRange(parsed.Accounts);
        PrestateSource = sourceName;
        RebuildPrestateRows();
        IsPrestateExpanded = true;
        StatusMessage = $"Pre-state loaded: {_extraAccounts.Count} account(s) from {sourceName}";
        return StatusMessage;
    }

    /// <summary>
    /// Load official state_test, workbench pre-state, or raw hex. Returns a status line.
    /// </summary>
    public string ImportContractSource(string text, string sourceName, string? fork = null, string? caseId = null)
    {
        if (WorkbenchFixtureLoader.LooksLikeStateTest(text))
        {
            var parsed = WorkbenchFixtureLoader.Parse(text, fork ?? SelectedFork, caseId);
            if (!parsed.Ok || parsed.Fixture is null)
            {
                StatusMessage = parsed.Error;
                return parsed.Error;
            }

            ApplyFixture(parsed.Fixture, sourceName);
            return StatusMessage;
        }

        if (WorkbenchPrestateLoader.LooksLikePrestate(text))
            return LoadPrestateJson(text, sourceName);

        var joined = text.Trim();
        if (LooksLikeHexBytecode(joined))
        {
            BytecodeInput = joined;
            StatusMessage = $"Loaded {sourceName} as bytecode — press F5";
            NotifyExecutionChrome();
            return StatusMessage;
        }

        StatusMessage = $"{sourceName}: not a state_test, pre-state JSON, or hex bytecode.";
        return StatusMessage;
    }

    public void ApplyFixture(WorkbenchFixtureLoader.LoadedFixture fx, string sourceName)
    {
        SelectedFork = AvailableForks.Contains(fx.Fork) ? fx.Fork : fx.Fork;
        if (!AvailableForks.Contains(SelectedFork))
            AvailableForks.Add(SelectedFork);

        TxFrom = fx.SenderHex;
        TxTo = fx.ToHex ?? "";
        CallDataHex = fx.CallDataHex;
        TxValueWei = fx.ValueWei;
        if (fx.GasLimit > 0) TxGasLimit = fx.GasLimit;
        if (fx.ChainId > 0) ChainId = fx.ChainId;
        CoinbaseAddress = fx.CoinbaseHex;
        _baseFeeWei = fx.BaseFeeWei;
        _gasPriceWei = fx.GasPriceWei;
        _maxFeeWei = fx.MaxFeeWei;
        _maxPriorityWei = fx.MaxPriorityWei;
        _txType = fx.TxType;
        _txNonce = fx.Nonce;
        _accessList = fx.AccessList.Count == 0 ? null : fx.AccessList;
        _authorizations = fx.Authorizations.Count == 0 ? null : fx.Authorizations;

        _extraAccounts.Clear();
        _extraAccounts.AddRange(fx.PreAccounts);
        _expectedPost.Clear();
        _expectedPost.AddRange(fx.ExpectedPost);
        PrestateSource = sourceName;

        BytecodeInput = "";
        if (string.IsNullOrEmpty(fx.ToHex))
            BytecodeInput = fx.CallDataHex;

        RebuildPrestateRows();
        IsPrestateExpanded = true;
        IsTxParamsExpanded = true;
        NotifyExecutionChrome();
        StatusMessage =
            $"Loaded fixture {ShortName(fx.CaseName)} · {fx.Fork} · {fx.PreAccounts.Count} pre accounts" +
            (fx.ExpectedPost.Count > 0 ? $" · {fx.ExpectedPost.Count} expected post" : "") +
            " — press F5";
    }

    private static string ShortName(string name) =>
        name.Length <= 72 ? name : name[..32] + "…" + name[^20..];

    private void AppendExpectedDiff(WorkbenchRunResult run)
    {
        _lastFixturePostMatches = null;
        _lastFixtureNote = "";
        if (_expectedPost.Count == 0) return;

        AccountStateRows.Add("fixture expected vs this run (Conformance check):");
        var mismatches = 0;
        var engineMismatches = new List<string>();
        foreach (var exp in _expectedPost)
        {
            var addr = exp.AddressHex;
            run.PostBalances.TryGetValue(addr, out var actualBal);
            actualBal ??= "(missing)";
            WorkbenchQuantity.TryBigInteger(exp.BalanceWei, out var expBal);
            var expBalText = WorkbenchQuantity.ToDecimalString(expBal);
            if (!string.Equals(actualBal, expBalText, StringComparison.Ordinal))
            {
                AccountStateRows.Add($"  MISMATCH {ShortAddr(addr)} balance expected {expBalText} got {actualBal}");
                mismatches++;
                if (WorkbenchQuantity.TryBigInteger(actualBal, out var actualBalParsed))
                {
                    engineMismatches.Add(InspectMismatchFormat.Balance(
                        addr,
                        InspectMapper.ToHex(expBal),
                        InspectMapper.ToHex(actualBalParsed)));
                }
            }

            if (!run.PostNonces.TryGetValue(addr, out var actualNonce))
            {
                AccountStateRows.Add($"  MISMATCH {ShortAddr(addr)} account missing (expected nonce {exp.Nonce})");
                mismatches++;
            }
            else if (actualNonce != exp.Nonce)
            {
                AccountStateRows.Add($"  MISMATCH {ShortAddr(addr)} nonce expected {exp.Nonce} got {actualNonce}");
                mismatches++;
                engineMismatches.Add(InspectMismatchFormat.Nonce(addr, exp.Nonce, actualNonce));
            }

            if (exp.StorageHex.Count == 0) continue;
            run.PostStorage.TryGetValue(addr, out var slots);
            slots ??= Array.Empty<string>();
            foreach (var (k, v) in exp.StorageHex)
            {
                WorkbenchQuantity.TryBigInteger(k, out var slot);
                WorkbenchQuantity.TryBigInteger(v, out var word);
                var want = $"slot 0x{slot:x} = 0x{word:x}";
                if (!slots.Any(s => s.Equals(want, StringComparison.OrdinalIgnoreCase)))
                {
                    AccountStateRows.Add($"  MISMATCH {ShortAddr(addr)} {want}");
                    mismatches++;
                }
            }
        }

        _lastFixturePostMatches = mismatches == 0;
        _lastFixtureNote = mismatches == 0
            ? $"Compared {_expectedPost.Count} expected account(s) on {SelectedFork}."
            : $"{mismatches} field(s) differ from fixture expected post on {SelectedFork}.";
        AccountStateRows.Add(mismatches == 0
            ? "  MATCH — post-state agrees with fixture expected"
            : $"  {mismatches} expected-post mismatch(es)");

        if (engineMismatches.Count > 0)
        {
            var inspect = InspectionAssembler.FromCanonical(
                new InspectRequest { Tx = run.Tx, Block = run.Block, Mismatches = engineMismatches },
                run.Result);
            _lastInspectResult = inspect;  // Store for diagnosis display
            var root = inspect.Diagnosis?.Root;
            if (root != null)
                AccountStateRows.Add($"  DIAGNOSIS  {root.RuleId}  {root.Grade}  {root.Why}");
        }
        else
        {
            _lastInspectResult = null;  // Clear when no mismatches
        }
    }

    [RelayCommand]
    private void ClearPrestate()
    {
        _extraAccounts.Clear();
        _expectedPost.Clear();
        _lastFixturePostMatches = null;
        _lastFixtureNote = "";
        _baseFeeWei = _gasPriceWei = _maxFeeWei = _maxPriorityWei = null;
        _txType = 0;
        _txNonce = 0;
        _accessList = null;
        _authorizations = null;
        PrestateSource = "";
        RebuildPrestateRows();
        StatusMessage = "Pre-state cleared";
    }

    [RelayCommand]
    private void TogglePrestate() => IsPrestateExpanded = !IsPrestateExpanded;

    private void RebuildPrestateRows()
    {
        PrestateAccountRows.Clear();
        foreach (var a in _extraAccounts)
        {
            BytecodeExecutionService.TryParseHexBytes(a.CodeHex, out var code);
            var slots = a.StorageHex?.Count ?? 0;
            PrestateAccountRows.Add(
                $"{a.AddressHex}  bal={a.BalanceWei}  nonce={a.Nonce}  code={code.Length}B  storage={slots}");
        }

        PrestateSummary = _extraAccounts.Count == 0
            ? "No extra accounts — File → Load Pre-state JSON"
            : $"{_extraAccounts.Count} extra account(s)" +
              (string.IsNullOrEmpty(PrestateSource) ? "" : $" · {PrestateSource}");
        NotifyExecutionChrome();
    }

    [RelayCommand]
    private async Task RunBytecodeAsync()
    {
        var hasBox = BytecodeExecutionService.TryParseHexBytes(BytecodeInput, out var boxCode) && boxCode.Length > 0;
        var hasPreTo = _extraAccounts.Any(a =>
            a.AddressHex.Equals(TxTo, StringComparison.OrdinalIgnoreCase) &&
            BytecodeExecutionService.TryParseHexBytes(a.CodeHex, out var c) && c.Length > 0);
        var isCreate = string.IsNullOrWhiteSpace(TxTo) || TxTo.Trim() is "0x" or "0x0" or "0x00";
        var hasCalldata = BytecodeExecutionService.TryParseHexBytes(CallDataHex, out var cd) && cd.Length > 0;
        if (!hasBox && !hasPreTo && !(isCreate && (hasBox || hasCalldata)))
        {
            StatusMessage = "Need code at To (paste hex, load a fixture/pre-state), or a CREATE (empty To + initcode).";
            return;
        }

        StopAutoPlay();
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;

        IsRunning = true;
        StatusMessage = $"Executing live EVM (gas≤{TxGasLimit:N0}, value={TxValueWei}, chain {ChainId})...";

        try
        {
            var run = await BytecodeExecutionService.RunAsync(BytecodeInput, BuildRunOptions(), ct);
            if (run is null)
            {
                StatusMessage = "Invalid hex (bytecode or calldata) — check input";
                ResultBanner = "INVALID INPUT";
                ResultBannerColor = "#FF4500";
                return;
            }

            LastCallerAddress = run.CallerAddress;
            LastContractAddress = run.ContractAddress;
            AccountStateRows.Clear();
            AccountStateRows.Add($"fork {run.Fork}  (StateTransition / EELS engine)");
            AccountStateRows.Add($"caller  {run.CallerAddress}");
            AccountStateRows.Add($"  balance {run.CallerBalanceWei} wei");
            AccountStateRows.Add($"contract {run.ContractAddress}");
            AccountStateRows.Add($"  balance {run.ContractBalanceWei} wei");
            AccountStateRows.Add($"code size {run.CodeSize} B | calldata {run.CallDataSize} B");
            if (run.StateDiff.Count > 0)
            {
                AccountStateRows.Add("state diff:");
                foreach (var line in run.StateDiff)
                    AccountStateRows.Add("  " + line);
            }
            _postStorage = run.PostStorage;
            foreach (var (addr, slots) in run.PostStorage)
            {
                if (slots.Count == 0) continue;
                AccountStateRows.Add($"storage {addr}:");
                foreach (var s in slots)
                    AccountStateRows.Add("  " + s);
            }

            AppendExpectedDiff(run);
            PopulateFromResult(run.Result, isBytecodeRun: true, runMeta: run);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Run cancelled";
            ResultBanner = "CANCELLED";
            ResultBannerColor = "#FFD700";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Run failed: {ex.Message}";
            ResultBanner = "INTERNAL ERROR";
            ResultBannerColor = "#FF4500";
            ErrorText = ex.Message;
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void CancelRun()
    {
        if (!IsRunning)
        {
            StatusMessage = "No run in progress";
            return;
        }
        _runCts?.Cancel();
        StatusMessage = "Cancelling...";
    }

    /// <summary>
    /// Clears bytecode, calldata, open files, and the last live/synthetic trace.
    /// Keeps fork, gas, and address fields so the next run can reuse them.
    /// </summary>
    [RelayCommand]
    private void ResetWorkbench()
    {
        StopAutoPlay();
        if (IsRunning)
            _runCts?.Cancel();

        BytecodeInput = string.Empty;
        CallDataHex = string.Empty;
        _extraAccounts.Clear();
        _expectedPost.Clear();
        _lastFixturePostMatches = null;
        _lastFixtureNote = "";
        _baseFeeWei = _gasPriceWei = _maxFeeWei = _maxPriorityWei = null;
        _txType = 0;
        _txNonce = 0;
        _accessList = null;
        _authorizations = null;
        PrestateSource = "";
        RebuildPrestateRows();
        IsPrestateExpanded = false;
        SearchQuery = string.Empty;
        PcSearch = string.Empty;
        IsBytecodeMode = false;
        IsCallGraphVisible = false;

        ProjectFiles.Clear();
        SelectedFile = null;
        ActiveCodeLines.Clear();
        RefreshFilteredFiles();

        _currentTrace = new List<ExecutionTraceStep>();
        HasTrace = false;
        TotalSteps = 0;
        CurrentStepIndex = 0;
        CurrentStep = null;
        LastRunSuccess = false;
        ResultBanner = "No run yet";
        ResultBannerColor = "#A9A9A9";
        ReturnDataHex = "0x";
        ErrorText = string.Empty;
        LastCallerAddress = string.Empty;
        LastContractAddress = string.Empty;
        CurrentOpcodeSpec = string.Empty;
        CurrentGasFormulaBreakdown = string.Empty;
        CurrentStepDetail = string.Empty;

        Instructions.Clear();
        StackRows.Clear();
        MemoryRows.Clear();
        StorageRows.Clear();
        SecurityFindings.Clear();
        GasTreeNodes.Clear();
        EventLogRows.Clear();
        AccountStateRows.Clear();
        CallTopology.LoadFromTrace(_currentTrace);

        CriticalCount = 0;
        WarningCount = 0;
        CenterEmptyHint = "Open a .sol/.hex file or paste bytecode above, then Run.";
        StatusMessage = "Workbench reset";
        StackText = MemoryText = AccountsText = LogsText = GasText = "(empty)";
        StorageText = "(empty — scrub to last step after RUN)";
        DiagnosisText = "(no diagnosis — run with mismatches)";
        _lastInspectResult = null;
        IsCallFramePinned = false;
        RefreshResultExplain();
        OnPropertyChanged(nameof(CurrentFileTitle));
        OnPropertyChanged(nameof(MaxStepIndex));
        OnPropertyChanged(nameof(WatermarkOpacity));
        NotifyStepProps();
    }

    [RelayCommand]
    private async Task CopyInspectorAsync(string? section)
    {
        var text = (section ?? "all").ToLowerInvariant() switch
        {
            "verdict" => $"{ResultVerdict}{Environment.NewLine}{ResultExplain}{Environment.NewLine}{ResultBanner}",
            "return" => ReturnDataHex,
            "storage" => StorageText,
            "stack" => StackText,
            "memory" => MemoryText,
            "accounts" => AccountsText,
            "logs" => LogsText,
            "gas" => GasText,
            "diagnosis" => DiagnosisText,
            _ => BuildFullCopyText()
        };

        if (await TryCopyAsync(text))
            StatusMessage = $"Copied {section ?? "results"}";
        else
            StatusMessage = "Clipboard unavailable";
    }

    private string BuildFullCopyText() =>
        $"""
        VERDICT: {ResultVerdict}
        {ResultExplain}
        BANNER: {ResultBanner}
        FORK: {SelectedFork}
        RETURN: {ReturnDataHex}
        ERROR: {ErrorText}
        STORAGE:
        {StorageText}
        STACK:
        {StackText}
        ACCOUNTS:
        {AccountsText}
        """;

    private static async Task<bool> TryCopyAsync(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desk)
                return false;
            var clip = desk.MainWindow?.Clipboard;
            if (clip is null) return false;
            await clip.SetTextAsync(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void RefreshResultExplain()
    {
        var (verdict, explain) = WorkbenchResultText.Build(
            HasTrace, LastRunSuccess, ResultBanner, ErrorText, ReturnDataHex, StorageRows,
            _lastFixturePostMatches, _lastFixtureNote);
        ResultVerdict = verdict;
        ResultExplain = explain;
    }

    private void RefreshInspectorTexts()
    {
        StackText = WorkbenchResultText.JoinOrEmpty(StackRows, "(empty)");
        MemoryText = WorkbenchResultText.JoinOrEmpty(MemoryRows, "(empty)");
        StorageText = WorkbenchResultText.JoinOrEmpty(StorageRows, "(empty — scrub to last step after RUN)");
        AccountsText = WorkbenchResultText.JoinOrEmpty(AccountStateRows, "(empty)");
        LogsText = WorkbenchResultText.JoinOrEmpty(EventLogRows, "(empty)");
        GasText = WorkbenchResultText.JoinOrEmpty(GasTreeNodes.Select(g => g.DisplayText), "(empty)");
        
        // Build diagnosis text from last inspect result
        if (_lastInspectResult?.Diagnosis?.Root != null)
        {
            var root = _lastInspectResult.Diagnosis.Root;
            var sb = new StringBuilder();
            sb.AppendLine($"Rule: {root.RuleId}");
            sb.AppendLine($"Grade: {root.Grade}");
            sb.AppendLine($"Phase: {root.Phase}");
            if (root.GasDelta != null)
                sb.AppendLine($"Gas Delta: {root.GasDelta:N0}");
            sb.AppendLine($"Why: {root.Why}");
            if (!string.IsNullOrEmpty(root.Proof))
                sb.AppendLine($"Proof: {root.Proof}");
            if (!string.IsNullOrEmpty(root.Consequences))
                sb.AppendLine($"Consequences: {root.Consequences}");
            if (!string.IsNullOrEmpty(root.LikelyFix))
                sb.AppendLine($"Likely Fix: {root.LikelyFix}");
            if (!string.IsNullOrEmpty(root.CodeBoundary))
                sb.AppendLine($"Code: {root.CodeBoundary}");
            if (!string.IsNullOrEmpty(root.ProtocolRule))
                sb.AppendLine($"Protocol: {root.ProtocolRule}");
                
            DiagnosisText = sb.ToString().TrimEnd();
        }
        else
        {
            DiagnosisText = "(no diagnosis — run with mismatches)";
        }
    }

    [RelayCommand]
    private void JumpToInstruction(InstructionViewModel? instr)
    {
        if (instr is null || instr.StepIndex < 0 || instr.StepIndex >= TotalSteps) return;
        CurrentStepIndex = instr.StepIndex;
    }

    [RelayCommand]
    private void JumpToPc()
    {
        if (_currentTrace.Count == 0)
        {
            StatusMessage = "No trace — run first";
            return;
        }

        var raw = (PcSearch ?? "").Trim();
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            raw = raw[2..];
        if (!int.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var pc)
            && !int.TryParse(PcSearch, NumberStyles.Integer, CultureInfo.InvariantCulture, out pc))
        {
            StatusMessage = "Enter PC as hex (e.g. 0x0a) or decimal";
            return;
        }

        for (var i = 0; i < _currentTrace.Count; i++)
        {
            if (_currentTrace[i].Pc == pc)
            {
                CurrentStepIndex = i;
                StatusMessage = $"Jumped to first step at PC 0x{pc:X}";
                return;
            }
        }

        StatusMessage = $"No step with PC 0x{pc:X}";
    }

    [RelayCommand]
    private async Task ExportTraceJsonAsync()
    {
        if (_currentTrace.Count == 0)
        {
            StatusMessage = "No trace to export";
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), $"schlieren_trace_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        await ExportTraceToPathAsync(path);
    }

    public async Task ExportTraceToPathAsync(string path)
    {
        var payload = new
        {
            format = "schlieren-structLog-v1",
            forkLabel = SelectedFork,
            chainId = ChainId,
            success = LastRunSuccess,
            returnData = ReturnDataHex,
            error = ErrorText,
            gasUsedBanner = ResultBanner,
            caller = LastCallerAddress,
            contract = LastContractAddress,
            steps = _currentTrace.Select((s, i) => new
            {
                step = i,
                pc = s.Pc,
                op = s.Op,
                gas = s.Gas,
                gasCost = s.GasCost,
                depth = s.Depth,
                stack = s.Stack,
                memory = s.Memory,
                storage = s.Storage,
                callType = s.CallType?.ToString(),
                contract = s.ContractAddress,
                caller = s.CallerAddress
            })
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
        StatusMessage = $"Exported trace ({_currentTrace.Count} steps): {path}";
    }

    [RelayCommand]
    private void RunSyntheticDemo()
    {
        StopAutoPlay();
        IsBytecodeMode = false;
        AccountStateRows.Clear();
        AccountStateRows.Add("(synthetic demo — no live balances)");
        var result = _syntheticService.RunFullTransaction();
        PopulateFromResult(result, isBytecodeRun: false);
        StatusMessage =
            $"Synthetic demo only: {result.TraceSteps.Count} steps | {CriticalCount} critical | {WarningCount} warnings";
    }

    public async Task GenerateAuditReportAsync(string savePath)
    {
        // Compute calldata intrinsic gas (nonzero bytes × 16, zero bytes × 4)
        ulong calldataGas = 0UL;
        if (BytecodeExecutionService.TryParseHexBytes(CallDataHex, out var calldata) && calldata.Length > 0)
        {
            var nonzeroBytes = calldata.Count(b => b != 0);
            var zeroBytes = calldata.Length - nonzeroBytes;
            calldataGas = (ulong)(nonzeroBytes * 16 + zeroBytes * 4);
        }

        var totalGas = Instructions.Count > 0
            ? (ulong)Instructions.Sum(i => i.GasCost) + 21_000UL + calldataGas
            : 0UL;

        await AuditReportExporter.GenerateReportAsync(
            CurrentFileTitle,
            SelectedFork,
            BlockGasLimit,
            BaseFeeGwei,
            TotalSteps,
            totalGas,
            SecurityFindings,
            Diagnostics,
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

    private void PopulateFromResult(ExecutionResult result, bool isBytecodeRun, WorkbenchRunResult? runMeta = null)
    {
        _currentTrace = result.TraceSteps ?? new List<ExecutionTraceStep>();
        HasTrace = _currentTrace.Count > 0;

        Instructions.Clear();
        StackRows.Clear();
        MemoryRows.Clear();
        StorageRows.Clear();
        SecurityFindings.Clear();
        GasTreeNodes.Clear();
        EventLogRows.Clear();

        LastRunSuccess = result.IsSuccess;
        ReturnDataHex = BytecodeExecutionService.ToHex(result.ReturnData);
        ErrorText = result.IsSuccess
            ? ""
            : $"{result.Error}" + (result.ReturnData is { Length: > 0 }
                ? $" | ret={ReturnDataHex}"
                : "");

        if (result.IsSuccess)
        {
            ResultBanner = $"SUCCESS · {result.GasUsed:N0} gas · {_currentTrace.Count} steps · refund {result.GasRefundCounter:N0}";
            ResultBannerColor = "#00D4AA";
        }
        else
        {
            ResultBanner = $"{result.Error} · {result.GasUsed:N0} gas · {_currentTrace.Count} steps";
            ResultBannerColor = "#FF4500";
        }

        for (var i = 0; i < _currentTrace.Count; i++)
        {
            var step = _currentTrace[i];
            var gas = ParseGasCost(step.GasCost);
            var desc = BytecodeExecutionService.DescribeOpcode(step.Op);
            Instructions.Add(new InstructionViewModel(
                i,
                step.Pc.ToString("X4"),
                step.Op,
                gas,
                step.CallType?.ToString() ?? $"D{step.Depth}",
                desc));
        }

        if (result.Logs is { Count: > 0 })
        {
            for (var i = 0; i < result.Logs.Count; i++)
            {
                var log = result.Logs[i];
                var topics = log.Topics is { Count: > 0 }
                    ? string.Join(", ", log.Topics.Take(4))
                    : "(no topics)";
                EventLogRows.Add($"[{i}] {log.Address} topics=[{topics}] data={log.Data}");
            }
        }
        else
        {
            EventLogRows.Add("(no logs)");
        }

        var reentrancy = ReentrancyDetector.Analyze(_currentTrace);
        var collisions = StorageCollisionDetector.Analyze(_currentTrace);
        var libraryGuard = LibraryGuardDetector.Analyze(_currentTrace);
        var proxyUnresolved = ProxyImplementationUnresolvedDetector.Analyze(_currentTrace);

        // Security Findings (actual vulnerabilities)
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
        
        // Diagnostics (execution context explanations)
        Diagnostics.Clear();
        
        if (libraryGuard != null)
            Diagnostics.Add(libraryGuard);
        
        if (proxyUnresolved != null)
            Diagnostics.Add(proxyUnresolved);

        if (runMeta?.GasTree != null)
        {
            foreach (var line in GasTreeRenderer.Render(runMeta.GasTree)
                         .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                GasTreeNodes.Add(new GasNodeViewModel
                {
                    DisplayText = line,
                    Indent = new(4, 1),
                    Color = line.Contains("canonical", StringComparison.OrdinalIgnoreCase)
                        ? "#FFFFFF"
                        : "#E0E0E0"
                });
            }
        }
        else
        {
            GasTreeNodes.Add(new GasNodeViewModel
            {
                DisplayText = $"TOTAL USED: {result.GasUsed:N0}  (no tree — same ExecutionResult)",
                Indent = new(0, 0, 0, 8),
                Color = "#FFFFFF"
            });
        }

        if (!isBytecodeRun)
        {
            AccountStateRows.Clear();
            AccountStateRows.Add("(synthetic demo — no live balances)");
        }

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
            ? $"LIVE EVM [{SelectedFork}]: {_currentTrace.Count} steps | {(result.IsSuccess ? "SUCCESS" : $"FAIL ({result.Error})")} | {result.GasUsed:N0} gas | refund {result.GasRefundCounter}"
            : $"Synthetic demo: {_currentTrace.Count} steps | {CriticalCount} critical | {WarningCount} warnings | {result.GasUsed:N0} gas";
        RefreshInspectorTexts();
        RefreshResultExplain();
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
            StorageRows.Add($"slot {kvp.Key} = {kvp.Value}");

        ComputeEelsSpecCitation(step);
        UpdateActiveLineForStep(value);
        HighlightInstruction(value);
        if (!IsCallFramePinned)
        {
            RefreshInspectorTexts();
            RefreshResultExplain();
        }
        NotifyStepProps();

        StatusMessage =
            $"Step {value + 1}/{TotalSteps} | {step.Op} | depth {step.Depth} | gas {step.Gas}";
    }

    private void HighlightInstruction(int stepIndex)
    {
        foreach (var instr in Instructions)
            instr.IsActive = instr.StepIndex == stepIndex;
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

public partial class InstructionViewModel : ObservableObject
{
    public int StepIndex { get; }
    public string PC { get; }
    public string Opcode { get; }
    public int GasCost { get; }
    public string CallType { get; }
    public string Description { get; }

    [ObservableProperty] private bool _isActive;

    public string DisplayText => string.IsNullOrEmpty(Description)
        ? $"0x{PC}  {Opcode}  ({GasCost})"
        : $"0x{PC}  {Opcode}  ({GasCost})  {Description}";

    public InstructionViewModel(int stepIndex, string pc, string opcode, int gasCost, string callType, string description = "")
    {
        StepIndex = stepIndex;
        PC = pc;
        Opcode = opcode;
        GasCost = gasCost;
        CallType = callType;
        Description = description;
    }

    // Back-compat for tests that used the 4-arg constructor
    public InstructionViewModel(string pc, string opcode, int gasCost, string callType)
        : this(0, pc, opcode, gasCost, callType)
    {
    }
}
