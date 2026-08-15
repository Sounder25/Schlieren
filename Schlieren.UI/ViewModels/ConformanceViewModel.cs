using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Schlieren.UI.Services;

namespace Schlieren.UI.ViewModels;

public partial class ConformanceViewModel : ObservableObject, IDisposable
{
    /// <summary>Public label for articles / screenshots — fixture provenance.</summary>
    public const string SuiteSource = "ethereum/execution-specs";
    public const string SuiteVersion = "tests@v20.0.1";

    // ── State ────────────────────────────────────────────────────────────────
    [ObservableProperty] private string  _selectedFork        = "Osaka";
    [ObservableProperty] private string  _fixturesBasePath    = @"C:\projects\Schlieren\fixtures\state_tests";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isRunning;

    [ObservableProperty] private bool    _hasResults;
    [ObservableProperty] private int     _passed;
    [ObservableProperty] private int     _failed;
    [ObservableProperty] private int     _total;
    [ObservableProperty] private string  _currentCase         = string.Empty;
    [ObservableProperty] private string  _statusMessage       = "Resolving fixtures…";
    [ObservableProperty] private string  _statusColor         = "#7a82a8";
    [ObservableProperty] private double  _progressRatio;
    [ObservableProperty] private string  _progressText        = "0 / 0";
    [ObservableProperty] private string  _passRateText        = "—";
    [ObservableProperty] private string  _passRateColor       = "#7a82a8";
    [ObservableProperty] private string  _elapsedText         = "00:00";
    [ObservableProperty] private string  _resolvedFixturePath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _fixturePathValid;

    [ObservableProperty] private int     _discoveredFixtureFiles;
    [ObservableProperty] private string  _fixtureSuiteSource  = SuiteSource;
    [ObservableProperty] private string  _fixtureSuiteVersion = SuiteVersion;
    [ObservableProperty] private string  _readySummary        = string.Empty;
    [ObservableProperty] private bool    _showEmptyFailures   = true;
    [ObservableProperty] private bool    _excludePortedStatic = true;

    // Selection / detail / clusters
    [ObservableProperty] private ConformanceFailureRow? _selectedFailure;
    [ObservableProperty] private bool    _hasSelectedFailure;
    [ObservableProperty] private string  _detailBody          = string.Empty;
    [ObservableProperty] private string  _detailTitle         = "Select a failure";
    [ObservableProperty] private string  _detailSubtitle      = "Click any failure row for full mismatches, gas ledger clues, and cluster membership.";
    [ObservableProperty] private string? _activeClusterFilter;
    [ObservableProperty] private string  _clusterFilterLabel  = "All failures";
    [ObservableProperty] private bool    _hasClusters;
    [ObservableProperty] private string  _gasHint             = string.Empty;
    [ObservableProperty] private bool    _hasLayer1Diagnosis;
    [ObservableProperty] private string  _layer1Headline      = string.Empty;
    [ObservableProperty] private string  _layer1Body          = string.Empty;

    public ObservableCollection<string>                  AvailableForks { get; } = new(ConformanceRunService.SupportedForks);
    public ObservableCollection<ConformanceFailureRow>   Failures       { get; } = new();
    public ObservableCollection<ConformanceClusterRow>   Clusters       { get; } = new();

    private readonly List<ConformanceFailureRow> _allFailures = new();
    private readonly Dictionary<string, ConformanceClusterRow> _clusterMap =
        new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _cts;
    private System.Diagnostics.Stopwatch? _stopwatch;
    private DispatcherTimer? _clockTimer;

    public ConformanceViewModel()
    {
        RefreshFixturePath();
        ClearSelectionUi();
    }

    // ── Derived ──────────────────────────────────────────────────────────────
    partial void OnSelectedForkChanged(string value)     => RefreshFixturePath();
    partial void OnFixturesBasePathChanged(string value) => RefreshFixturePath();
    partial void OnExcludePortedStaticChanged(bool value) => RefreshFixturePath();

    private void RefreshFixturePath()
    {
        var resolved = ConformanceRunService.ResolveFixtureRoot(FixturesBasePath, SelectedFork);
        ResolvedFixturePath = resolved ?? $"(not found — expected: for_{SelectedFork.ToLowerInvariant()})";
        FixturePathValid    = resolved != null;

        if (!FixturePathValid)
        {
            DiscoveredFixtureFiles = 0;
            ReadySummary = string.Empty;
            StatusColor  = "#ef4444";
            StatusMessage = $"Fixture folder not found for {SelectedFork}. Check path above.";
            return;
        }

        try
        {
            DiscoveredFixtureFiles = Directory
                .EnumerateFiles(resolved!, "*.json", SearchOption.AllDirectories)
                .Count();
        }
        catch
        {
            DiscoveredFixtureFiles = 0;
        }

        ReadySummary =
            $"{SelectedFork} · {DiscoveredFixtureFiles:N0} fixture files · {SuiteVersion}"
            + (ExcludePortedStatic ? " · excluding ported_static" : "");
        StatusColor   = "#22c55e";
        StatusMessage = $"Ready — live EELS state tests ({SuiteVersion}). Click RUN.";
    }

    // ── Commands ─────────────────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (!FixturePathValid) return;

        _allFailures.Clear();
        _clusterMap.Clear();
        Failures.Clear();
        Clusters.Clear();
        HasClusters = false;
        ActiveClusterFilter = null;
        ClusterFilterLabel = "All failures";
        ClearSelectionUi();
        ShowEmptyFailures = false;

        Passed = Failed = Total = 0;
        ProgressRatio   = 0;
        ProgressText    = "0 / 0";
        PassRateText    = "—";
        PassRateColor   = "#7a82a8";
        HasResults      = false;
        IsRunning       = true;
        CurrentCase     = "Loading fixtures…";
        StatusMessage   = $"Running {SelectedFork} against {SuiteVersion}…";
        StatusColor     = "#19D7E5";

        _cts       = new CancellationTokenSource();
        _stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) =>
        {
            if (_stopwatch != null)
                ElapsedText = _stopwatch.Elapsed.ToString(@"mm\:ss");
        };
        _clockTimer.Start();

        var progress = new Progress<ConformanceProgress>(OnProgress);

        // Let Avalonia paint "Loading fixtures…" before the first await hits disk I/O.
        await Task.Yield();

        try
        {
            var exclude = ExcludePortedStatic ? "ported_static" : null;
            var (p, f, t) = await ConformanceRunService.RunAsync(
                ResolvedFixturePath, SelectedFork, progress, _cts.Token, exclude);

            Passed = p; Failed = f; Total = t;
            UpdateDerived();
            RebuildClusterList();
            ApplyFailureFilter();
            HasResults    = true;
            ShowEmptyFailures = Failures.Count == 0;
            StatusMessage = Failed == 0
                ? $"✅ {SelectedFork} · {SuiteVersion} — 100% ({p:N0} / {t:N0} cases)"
                : $"⚠️ {SelectedFork} · {SuiteVersion} — {p:N0} / {t:N0} passed  ({f:N0} failures · {Clusters.Count} clusters)";
            StatusColor   = Failed == 0 ? "#22c55e" : "#f59e0b";
            CurrentCase   = "Done.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
            StatusColor   = "#7a82a8";
            CurrentCase   = string.Empty;
            RebuildClusterList();
            ApplyFailureFilter();
            ShowEmptyFailures = Failures.Count == 0;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            StatusColor   = "#ef4444";
            ShowEmptyFailures = Failures.Count == 0;
        }
        finally
        {
            _stopwatch?.Stop();
            _clockTimer?.Stop();
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private bool CanRun() => !IsRunning && FixturePathValid;

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void SelectFailure(ConformanceFailureRow? row)
    {
        if (row is null) return;
        SelectedFailure = row;
        HasSelectedFailure = true;
        DetailTitle = row.CaseId;
        DetailSubtitle = row.HasLayer1
            ? $"{row.Layer1Diagnoses[0].Confidence} · {row.ClusterKey}"
            : $"{row.PrimaryCategory} · {row.EipCluster} · gas {row.GasUsed:N0}";
        DetailBody = row.BuildDetailBody();
        GasHint = row.BuildGasHint();
        HasLayer1Diagnosis = row.HasLayer1;
        Layer1Headline = row.Layer1Headline;
        Layer1Body = row.Layer1Body;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        ClearSelectionUi();
    }

    /// <summary>json, file name, suite fork, failing case id.</summary>
    public event Action<string, string, string, string>? OpenInWorkbenchRequested;

    [RelayCommand]
    private void OpenSelectedInWorkbench()
    {
        var path = SelectedFailure?.FixturePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusMessage = "No fixture file on this row. Use OPEN FIXTURE and pick a JSON from the suite.";
            StatusColor = "#f59e0b";
            return;
        }

        OpenFixturePath(path);
    }

    /// <summary>Read a state_test JSON and ask the shell to load it in the workbench.</summary>
    public bool OpenFixturePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusMessage = "Fixture file not found.";
            StatusColor = "#ef4444";
            return false;
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Cannot read fixture: {ex.Message}";
            StatusColor = "#ef4444";
            return false;
        }

        var name = Path.GetFileName(path);
        var caseId = SelectedFailure?.CaseId ?? "";
        OpenInWorkbenchRequested?.Invoke(json, name, SelectedFork, caseId);
        StatusMessage = $"Opened {name} ({SelectedFork}" +
                        (string.IsNullOrEmpty(caseId) ? "" : $", {caseId}") +
                        ") in workbench — F5 compares to fixture expected post.";
        StatusColor = "#22c55e";
        return true;
    }

    [RelayCommand]
    private void SelectCluster(ConformanceClusterRow? cluster)
    {
        if (cluster is null) return;
        ActiveClusterFilter = cluster.Key;
        ClusterFilterLabel = $"Filter: {cluster.Key} ({cluster.Count})";
        ApplyFailureFilter();

        // Auto-select first failure in cluster for instant detail
        if (Failures.Count > 0)
            SelectFailure(Failures[0]);
        else
            ClearSelectionUi();
    }

    [RelayCommand]
    private void ClearClusterFilter()
    {
        ActiveClusterFilter = null;
        ClusterFilterLabel = "All failures";
        ApplyFailureFilter();
    }

    /// <summary>
    /// Clears the last suite run (scores, failures, clusters, selection).
    /// Keeps fork and fixture path. Cancels an in-flight run first.
    /// </summary>
    [RelayCommand]
    private void ResetResults()
    {
        if (IsRunning)
            _cts?.Cancel();

        _stopwatch?.Stop();
        _clockTimer?.Stop();
        _stopwatch = null;
        IsRunning = false;

        _allFailures.Clear();
        _clusterMap.Clear();
        Failures.Clear();
        Clusters.Clear();
        HasClusters = false;
        ActiveClusterFilter = null;
        ClusterFilterLabel = "All failures";
        ClearSelectionUi();
        ShowEmptyFailures = true;

        Passed = Failed = Total = 0;
        ProgressRatio = 0;
        ProgressText = "0 / 0";
        PassRateText = "—";
        PassRateColor = "#7a82a8";
        HasResults = false;
        CurrentCase = string.Empty;
        ElapsedText = "00:00";

        RefreshFixturePath();
        if (FixturePathValid)
            StatusMessage = $"Reset — ready to run {SelectedFork} ({SuiteVersion}).";
    }

    // ── Progress handler (fires on UI thread via Progress<T>) ─────────────
    private void OnProgress(ConformanceProgress p)
    {
        Passed  = p.Passed;
        Failed  = p.Failed;
        Total   = p.Total;
        CurrentCase = p.CurrentCase;
        UpdateDerived();

        if (p.Passed == 0 && p.Failed == 0 && !string.IsNullOrEmpty(p.CurrentCase))
            StatusMessage = p.CurrentCase;

        if (p.FailureDetail is null)
            return;

        ShowEmptyFailures = false;

        var row = new ConformanceFailureRow(
            caseId: p.CurrentCase,
            summary: p.FailureDetail,
            mismatches: p.Mismatches ?? Array.Empty<string>(),
            fixturePath: p.FixturePath ?? string.Empty,
            gasUsed: p.GasUsed,
            gasRefundCounter: p.GasRefundCounter,
            primaryCategory: string.IsNullOrEmpty(p.PrimaryCategory) ? "other" : p.PrimaryCategory,
            eipCluster: string.IsNullOrEmpty(p.EipCluster) ? "unknown" : p.EipCluster,
            clusterKey: string.IsNullOrEmpty(p.ClusterKey)
                ? ConformanceRunService.BuildClusterKey(
                    string.IsNullOrEmpty(p.PrimaryCategory) ? "other" : p.PrimaryCategory,
                    string.IsNullOrEmpty(p.EipCluster) ? "unknown" : p.EipCluster)
                : p.ClusterKey,
            layer1Diagnoses: p.Layer1Diagnoses ?? Array.Empty<Layer1DiagnosisInfo>());

        _allFailures.Add(row);
        UpsertCluster(row);

        // Bound visible list while streaming (still keep all for clusters)
        if (ActiveClusterFilter is null ||
            string.Equals(row.ClusterKey, ActiveClusterFilter, StringComparison.OrdinalIgnoreCase))
        {
            if (Failures.Count >= 500)
                Failures.RemoveAt(0);
            Failures.Add(row);
        }

        // Refresh cluster ranking periodically (every 8 failures) for screenshot-friendly order
        if (_allFailures.Count % 8 == 0)
            RebuildClusterList();
    }

    private void UpsertCluster(ConformanceFailureRow row)
    {
        if (!_clusterMap.TryGetValue(row.ClusterKey, out var cluster))
        {
            cluster = new ConformanceClusterRow(row.ClusterKey, row.PrimaryCategory, row.EipCluster);
            _clusterMap[row.ClusterKey] = cluster;
            HasClusters = true;
        }

        cluster.AddSample(row);
    }

    private void RebuildClusterList()
    {
        var ordered = _clusterMap.Values
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Clusters.Clear();
        foreach (var c in ordered)
            Clusters.Add(c);

        HasClusters = Clusters.Count > 0;
    }

    private void ApplyFailureFilter()
    {
        Failures.Clear();
        IEnumerable<ConformanceFailureRow> src = _allFailures;
        if (!string.IsNullOrEmpty(ActiveClusterFilter))
            src = src.Where(f => string.Equals(f.ClusterKey, ActiveClusterFilter, StringComparison.OrdinalIgnoreCase));

        foreach (var f in src.TakeLast(500))
            Failures.Add(f);

        ShowEmptyFailures = Failures.Count == 0 && !IsRunning;
    }

    private void ClearSelectionUi()
    {
        SelectedFailure = null;
        HasSelectedFailure = false;
        DetailTitle = "Select a failure";
        DetailSubtitle = "Click any failure row for Layer 1 diagnoses, full mismatches, and cluster membership.";
        DetailBody = string.Empty;
        GasHint = string.Empty;
        HasLayer1Diagnosis = false;
        Layer1Headline = string.Empty;
        Layer1Body = string.Empty;
    }

    private void UpdateDerived()
    {
        int done = Passed + Failed;
        ProgressRatio = Total > 0 ? (double)done / Total : 0;
        ProgressText  = $"{done:N0} / {Total:N0}";

        if (done > 0)
        {
            double pct = (double)Passed / done * 100.0;
            PassRateText  = $"{pct:F1}%";
            PassRateColor = pct >= 99.9 ? "#22c55e" : pct >= 95.0 ? "#f59e0b" : "#ef4444";
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _clockTimer?.Stop();
    }
}

/// <summary>Single failure row — clickable for full detail.</summary>
public sealed class ConformanceFailureRow
{
    public string CaseId { get; }
    public string Summary { get; }
    public string Detail => Summary; // XAML binds Detail historically
    public IReadOnlyList<string> Mismatches { get; }
    public string FixturePath { get; }
    public ulong GasUsed { get; }
    public long GasRefundCounter { get; }
    public string PrimaryCategory { get; }
    public string EipCluster { get; }
    public string ClusterKey { get; }
    public IReadOnlyList<Layer1DiagnosisInfo> Layer1Diagnoses { get; }
    public string CategoryBadge => PrimaryCategory.ToUpperInvariant();
    public string GasLine => $"gasUsed={GasUsed:N0}  refundCounter={GasRefundCounter:N0}";
    public bool HasLayer1 => Layer1Diagnoses.Count > 0;
    public string Layer1Headline => HasLayer1
        ? $"{Layer1Diagnoses[0].Confidence} — {Layer1Diagnoses[0].Summary}"
        : string.Empty;
    public string Layer1Body
    {
        get
        {
            if (!HasLayer1) return string.Empty;
            if (!string.IsNullOrWhiteSpace(Layer1Diagnoses[0].InspectorBody))
            {
                if (Layer1Diagnoses.Count == 1)
                    return Layer1Diagnoses[0].InspectorBody!;
                var ranked = new StringBuilder();
                ranked.AppendLine(Layer1Diagnoses[0].InspectorBody);
                ranked.AppendLine();
                ranked.AppendLine("OTHER CANDIDATES");
                for (int i = 1; i < Layer1Diagnoses.Count; i++)
                {
                    var d = Layer1Diagnoses[i];
                    ranked.AppendLine($"{i}. [{d.Confidence}] {d.Category} — {d.Summary}");
                }
                return ranked.ToString().TrimEnd();
            }
            var sb = new StringBuilder();
            for (int i = 0; i < Layer1Diagnoses.Count; i++)
            {
                var d = Layer1Diagnoses[i];
                if (i > 0) sb.AppendLine();
                sb.AppendLine($"{i + 1}. [{d.Confidence}] {d.Category}");
                sb.AppendLine($"   {d.Summary}");
                sb.AppendLine($"   Protocol : {d.ProtocolRule}");
                sb.AppendLine($"   Look in  : {d.CodeBoundary}");
                sb.AppendLine($"   Evidence : {d.Evidence}");
            }
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>One-line strip for the failures list (Layer 1 when present).</summary>
    public string DiagnosisLine => HasLayer1
        ? $"[{Layer1Diagnoses[0].Confidence}] {Layer1Diagnoses[0].Category}: {Layer1Diagnoses[0].Summary}"
        : string.Empty;

    public ConformanceFailureRow(
        string caseId,
        string summary,
        IReadOnlyList<string> mismatches,
        string fixturePath,
        ulong gasUsed,
        long gasRefundCounter,
        string primaryCategory,
        string eipCluster,
        string clusterKey,
        IReadOnlyList<Layer1DiagnosisInfo>? layer1Diagnoses = null)
    {
        CaseId = caseId;
        Summary = summary;
        Mismatches = mismatches;
        FixturePath = fixturePath;
        GasUsed = gasUsed;
        GasRefundCounter = gasRefundCounter;
        PrimaryCategory = primaryCategory;
        EipCluster = eipCluster;
        ClusterKey = clusterKey;
        Layer1Diagnoses = layer1Diagnoses ?? Array.Empty<Layer1DiagnosisInfo>();
    }

    public string BuildDetailBody()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CASE  {CaseId}");
        sb.AppendLine($"CLUSTER  {ClusterKey}");
        sb.AppendLine($"CATEGORY  {PrimaryCategory}");
        sb.AppendLine($"EIP / FEATURE  {EipCluster}");
        sb.AppendLine($"GAS  used={GasUsed:N0}  refundCounter={GasRefundCounter:N0}");
        if (GasUsed > 0)
        {
            var cap = GasUsed / 5;
            var cappedRefund = Math.Min((ulong)Math.Max(0, GasRefundCounter), cap);
            sb.AppendLine($"EIP-3529 CAP  min(refund, gasUsed/5) = {cappedRefund:N0}");
        }
        sb.AppendLine();
        if (HasLayer1)
        {
            var head = Layer1Diagnoses[0];
            if (!string.IsNullOrWhiteSpace(head.InspectorBody))
            {
                sb.AppendLine(head.InspectorBody);
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine($"DIAGNOSES ({Layer1Diagnoses.Count})");
                sb.AppendLine(Layer1Body);
                sb.AppendLine();
            }
        }
        sb.AppendLine("FIXTURE");
        sb.AppendLine(string.IsNullOrEmpty(FixturePath) ? "(unknown)" : FixturePath);
        sb.AppendLine();
        sb.AppendLine($"MISMATCHES ({Mismatches.Count})");
        if (Mismatches.Count == 0)
            sb.AppendLine("  (none recorded)");
        else
        {
            foreach (var m in Mismatches)
                sb.AppendLine("  • " + m);
        }

        return sb.ToString().TrimEnd();
    }

    public string BuildGasHint()
    {
        if (HasLayer1 && !string.IsNullOrWhiteSpace(Layer1Diagnoses[0].InspectorBody))
            return "Root cause is the first divergent phase. Downstream balance/storage/missing-account lines are consequences, not competing causes.";

        if (PrimaryCategory.Equals("balance", StringComparison.OrdinalIgnoreCase))
        {
            return "Balance cluster — often gas residual / refund (EIP-3529) or coinbase priority fee " +
                   "(EIP-1559). refundCounter is pre-cap; effective refund = min(counter, gasUsed/5).";
        }

        if (PrimaryCategory.Equals("storage", StringComparison.OrdinalIgnoreCase))
        {
            return "Storage cluster — check SSTORE net gas (EIP-2200), cold/warm access (EIP-2929), " +
                   "or reentrancy stipend guard (gas_left ≤ 2300).";
        }

        if (PrimaryCategory.Equals("receipt_status", StringComparison.OrdinalIgnoreCase))
        {
            return "Receipt status cluster — execution success/failure diverged. Often OOG, " +
                   "exceptional halt, or CALL depth / stipend edge.";
        }

        if (PrimaryCategory.Equals("nonce", StringComparison.OrdinalIgnoreCase))
        {
            return "Nonce cluster — sender/contract nonce write path, CREATE, or EIP-7702 auth loop.";
        }

        if (PrimaryCategory.Equals("code", StringComparison.OrdinalIgnoreCase))
        {
            return "Code cluster — CREATE/CREATE2 return data, EIP-3541 EF-prefix reject, or EIP-7702 delegation code.";
        }

        return "Open mismatches above; group by cluster to see if many cases share one root cause.";
    }
}

/// <summary>Failure cluster — category × EIP folder, live count while suite runs.</summary>
public sealed partial class ConformanceClusterRow : ObservableObject
{
    public string Key { get; }
    public string PrimaryCategory { get; }
    public string EipCluster { get; }

    [ObservableProperty] private int _count;
    [ObservableProperty] private string _sampleCase = string.Empty;
    [ObservableProperty] private string _displayLine = string.Empty;

    public ConformanceClusterRow(string key, string primaryCategory, string eipCluster)
    {
        Key = key;
        PrimaryCategory = primaryCategory;
        EipCluster = eipCluster;
        UpdateDisplay();
    }

    public void AddSample(ConformanceFailureRow row)
    {
        Count++;
        if (string.IsNullOrEmpty(SampleCase))
            SampleCase = row.CaseId;
        UpdateDisplay();
    }

    private void UpdateDisplay()
        => DisplayLine = $"{Count,4}  {PrimaryCategory}  ·  {Key}";
}
