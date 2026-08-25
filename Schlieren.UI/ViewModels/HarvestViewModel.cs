using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Schlieren.UI.Services;

namespace Schlieren.UI.ViewModels;

public sealed partial class HarvestViewModel : ObservableObject, IDisposable
{
    private readonly HarvestService _svc;
    private readonly HarvestServiceOptions _options;
    private CancellationTokenSource _cts = new();

    /// <summary>Exposes the configured corpus directory for UI consumers (e.g. MainWindow).</summary>
    public string? CorpusDirectory => _options.CorpusDirectory;

    /// <summary>
    /// Explicit constructor. Both dependencies are supplied by the composition root
    /// (App.OnFrameworkInitializationCompleted). HarvestViewModel must not construct
    /// HarvestService internally.
    /// </summary>
    public HarvestViewModel(HarvestService service, HarvestServiceOptions options)
    {
        _svc     = service;
        _options = options;
    }

    // ─── Pipeline state ────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isHarvesting;
    [ObservableProperty] private bool   _wfAActive;
    [ObservableProperty] private bool   _wfBActive;
    [ObservableProperty] private bool   _n8nReachable;

    // ─── Live stats ────────────────────────────────────────────────────────

    [ObservableProperty] private int    _queueDepth;
    [ObservableProperty] private string _lastBlock   = "—";
    [ObservableProperty] private int    _totalReplayed;
    [ObservableProperty] private int    _passCount;
    [ObservableProperty] private int    _divergenceCount;
    [ObservableProperty] private int    _failedCount;
    [ObservableProperty] private string _lastRunText = "—";

    // ─── Corpus feed ───────────────────────────────────────────────────────

    [ObservableProperty] private string _activeFilter = "all";  // all | divergence | pass | failed

    private readonly List<HarvestEntry> _allEntries = [];
    public ObservableCollection<HarvestEntry> VisibleEntries { get; } = [];

    // ─── Status / error ────────────────────────────────────────────────────

    [ObservableProperty] private string _statusMessage = "Connecting to n8n…";

    // ─── Init ──────────────────────────────────────────────────────────────

    public void StartPolling()
    {
        _ = Task.Run(PollLoopAsync);
    }

    private async Task PollLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync();
            }
            catch { /* never crash the UI thread */ }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), _cts.Token);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var (a, b)   = await _svc.GetPipelineStatusAsync();
            var lastRun  = await _svc.GetLastRunTextAsync(HarvestService.WfBId) ?? "—";
            var entries  = await _svc.ReadCorpusAsync(500);

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                WfAActive        = a;
                WfBActive        = b;
                N8nReachable     = true;
                LastRunText      = lastRun;
                StatusMessage    = "n8n connected";

                _allEntries.Clear();
                _allEntries.AddRange(entries);

                TotalReplayed   = entries.Count;
                PassCount       = entries.Count(e => e.IsPass);
                DivergenceCount = entries.Count(e => e.IsDivergence);
                FailedCount     = entries.Count(e => e.IsFailed);

                // Derive last block from corpus — best approximation without n8n queue access
                var maxBlock = entries.Count > 0
                    ? entries.Max(e => e.BlockNumber)
                    : 0;
                LastBlock  = maxBlock > 0 ? $"{maxBlock:N0}" : "—";
                QueueDepth = 0; // n8n static data not accessible via MCP; shown as 0 until WF-A runs

                ApplyFilter();
            });
        }
        catch (Exception ex)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                N8nReachable  = false;
                StatusMessage = $"n8n unreachable — {ex.Message}";
            });
        }
    }

    [RelayCommand]
    private void SetFilter(string filter)
    {
        ActiveFilter = filter;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        VisibleEntries.Clear();

        var filtered = ActiveFilter switch
        {
            "divergence" => _allEntries.Where(e => e.IsDivergence),
            "pass"       => _allEntries.Where(e => e.IsPass),
            "failed"     => _allEntries.Where(e => e.IsFailed),
            _            => (IEnumerable<HarvestEntry>)_allEntries
        };

        var sorted = SortBy switch
        {
            "block" => filtered.OrderByDescending(e => e.BlockNumber),
            "type"  => filtered.OrderBy(e => e.CandidateType),
            _       => filtered.OrderByDescending(e => e.PriorityScore)
        };

        foreach (var e in sorted)
            VisibleEntries.Add(e);
    }

    // ─── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task TogglePipelineAsync()
    {
        if (IsHarvesting) { StatusMessage = "Already running…"; return; }

        IsHarvesting  = true;
        WfAActive     = true;
        StatusMessage = "Running harvester…";

        try
        {
            var result = await Task.Run(() =>
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName               = "python",
                    Arguments              = @"tools\harvester.py --blocks 25",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                };
                using var proc = System.Diagnostics.Process.Start(psi)!;
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(60_000);
                return (proc.ExitCode, output);
            });

            StatusMessage = result.ExitCode == 0
                ? "Harvest complete"
                : $"Harvester error (exit {result.ExitCode})";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to start harvester: {ex.Message}";
        }
        finally
        {
            IsHarvesting = false;
            WfAActive    = false;
        }

        // Refresh feed
        _ = Task.Delay(TimeSpan.FromSeconds(1)).ContinueWith(_ => RefreshAsync());
    }

    // ─── Selection ─────────────────────────────────────────────────────────

    [ObservableProperty] private HarvestEntry? _selectedEntry;

    [RelayCommand]
    private void SelectEntry(HarvestEntry entry)
    {
        SelectedEntry = entry;
    }

    // ─── Sort ──────────────────────────────────────────────────────────────

    [ObservableProperty] private string _sortBy = "score"; // score | block | type

    [RelayCommand]
    private void SetSort(string key)
    {
        SortBy = key;
        ApplyFilter();
    }

    // ─── Delete ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void DeleteEntry(HarvestEntry entry)
    {
        _allEntries.Remove(entry);
        if (SelectedEntry == entry) SelectedEntry = null;

        if (string.IsNullOrEmpty(_options.CorpusDirectory))
        {
            ApplyFilter();
            return;
        }

        // Persist deletion back to harvest_index.json
        var corpusDir = _options.CorpusDirectory;
        _ = Task.Run(async () =>
        {
            var indexFile = System.IO.Path.Combine(corpusDir, "harvest_index.json");
            if (!System.IO.File.Exists(indexFile)) return;
            try
            {
                var json = await System.IO.File.ReadAllTextAsync(indexFile);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Rebuild without deleted entry
                var candidates = root.GetProperty("candidates")
                    .EnumerateArray()
                    .Where(c =>
                    {
                        c.TryGetProperty("txHash", out var h);
                        return h.ValueKind == System.Text.Json.JsonValueKind.String
                               && h.GetString() != entry.TxHash;
                    })
                    .Select(c => System.Text.Json.JsonSerializer.Deserialize<object>(c.GetRawText()))
                    .ToList();

                var updated = new
                {
                    scannedAt   = root.TryGetProperty("scannedAt", out var s) ? s.GetString() : "",
                    totalScored = candidates.Count,
                    candidates
                };
                await System.IO.File.WriteAllTextAsync(indexFile,
                    System.Text.Json.JsonSerializer.Serialize(updated,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        });

        ApplyFilter();
    }

    [RelayCommand]
    private async Task LoadInWorkbenchAsync(HarvestEntry entry)
    {
        if (entry is null) return;

        // If we have a fixture file, load it directly
        if (!string.IsNullOrEmpty(entry.FixturePath) && File.Exists(entry.FixturePath))
        {
            var text = await File.ReadAllTextAsync(entry.FixturePath);
            LoadFixtureRequested?.Invoke(entry.FixturePath, entry.TxHash, entry.Fork);
            return;
        }

        if (string.IsNullOrEmpty(_options.CorpusDirectory))
        {
            StatusMessage = "Harvest corpus is not configured";
            return;
        }

        // DISCOVERED entry — fetch bytecode from RPC and build a minimal fixture
        StatusMessage = $"Fetching bytecode for {entry.ShortHash}…";
        try
        {
            var fixture = await Task.Run(() => BuildMinimalFixture(entry));
            if (fixture is null)
            {
                StatusMessage = "Could not fetch bytecode from RPC";
                return;
            }

            // Write to a temp fixture file then load
            var dir      = Path.Combine(_options.CorpusDirectory, "fixtures");
            Directory.CreateDirectory(dir);
            var safeName = entry.TxHash.Replace("0x", "")[..16];
            var path     = Path.Combine(dir, $"{safeName}.json");
            await File.WriteAllTextAsync(path, fixture);

            LoadFixtureRequested?.Invoke(path, entry.TxHash, entry.Fork);
            StatusMessage = $"Loaded {entry.ShortHash}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.Message}";
        }
    }

    private static string? BuildMinimalFixture(HarvestEntry entry)
    {
        // Fetch contract bytecode via public RPC
        const string rpc = "https://ethereum.publicnode.com";
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };

            var toAddr = entry.ToAddress;
            if (string.IsNullOrEmpty(toAddr)) return null;

            // Get code at the target address
            var codeReq = System.Text.Json.JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0", method = "eth_getCode",
                @params = new object[] { toAddr, "latest" }, id = 1
            });
            var resp = http.PostAsync(rpc,
                new System.Net.Http.StringContent(codeReq, System.Text.Encoding.UTF8, "application/json"))
                .Result;
            var json = resp.Content.ReadAsStringAsync().Result;
            using var doc  = System.Text.Json.JsonDocument.Parse(json);
            var bytecode   = doc.RootElement.GetProperty("result").GetString() ?? "0x";

            if (bytecode == "0x" || bytecode.Length < 4) return null;

            // Build minimal fixture the Workbench can load
            var fixture = new
            {
                _schlieren_harvest = new
                {
                    txHash        = entry.TxHash,
                    blockNumber   = entry.BlockNumber,
                    fork          = entry.Fork,
                    contractName  = entry.ContractName,
                    functionName  = entry.FunctionName,
                    from          = entry.FromAddress,
                    to            = entry.ToAddress,
                    gasLimit      = entry.GasMainnet,
                    inputData     = entry.InputData,
                    discoveredAt  = entry.HarvestedAt.ToString("O"),
                },
                bytecode = bytecode,
                calldata = entry.InputData,
                fork     = entry.Fork,
            };

            return System.Text.Json.JsonSerializer.Serialize(fixture,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch { return null; }
    }

    public event Action<string, string, string>? LoadFixtureRequested;

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        _allEntries.Clear();
        SelectedEntry = null;
        VisibleEntries.Clear();

        if (string.IsNullOrEmpty(_options.CorpusDirectory))
        {
            StatusMessage = "Harvest corpus is not configured";
            return;
        }

        var indexFile = System.IO.Path.Combine(_options.CorpusDirectory, "harvest_index.json");
        var empty = $"{{\"scannedAt\":\"{DateTime.UtcNow:O}\",\"totalScored\":0,\"candidates\":[]}}";
        try { await System.IO.File.WriteAllTextAsync(indexFile, empty); } catch { }

        StatusMessage = "Cleared";
    }

    [RelayCommand]
    private async Task RefreshNowAsync() => await RefreshAsync();

    // ─── Dispose ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _cts.Cancel();
        _svc.Dispose();
    }
}
