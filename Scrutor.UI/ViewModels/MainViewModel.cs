using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scrutor.UI.Services;
using Scrutor.RPC.Logging;
using System.Collections.ObjectModel;
using System.Windows;
using System.Threading.Tasks;
using Scrutor.Core.Configuration;
using Scrutor.Core.State;
using Scrutor.Core.Primitives;
using System.Linq;

namespace Scrutor.UI.ViewModels;

public enum NodeStatus
{
    Inactive,
    Starting,
    Active,
    Stopping,
    Error
}

public partial class MainViewModel : ObservableObject
{
    private readonly NodeHostService _nodeHost;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(MineBlockCommand))]
    [NotifyPropertyChangedFor(nameof(IsInactive))]
    private NodeStatus _status = NodeStatus.Inactive;

    public bool IsInactive => Status == NodeStatus.Inactive || Status == NodeStatus.Error;

    [ObservableProperty]
    private int _port = 8545;

    [ObservableProperty]
    private ulong _chainId = 31337;

    [ObservableProperty]
    private int _accounts = 10;

    [ObservableProperty]
    private decimal _initialBalance = 10000;

    [ObservableProperty]
    private string? _mnemonic;

    [ObservableProperty]
    private string _derivationPath = "m/44'/60'/0'/0/";

    [ObservableProperty]
    private bool _autoImpersonate = true;

    [ObservableProperty]
    private string? _forkUrl;

    [ObservableProperty]
    private ulong? _forkBlock;

    [ObservableProperty]
    private bool _isForked;

    [ObservableProperty]
    private ulong _blockHeight;

    [ObservableProperty]
    private int _mempoolCount;

    [ObservableProperty]
    private bool _isLogsPaused;

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private string _logLevelFilter = "ALL";

    public ObservableCollection<string> Logs { get; } = new();
    public ObservableCollection<AccountViewModel> ManagedAccounts { get; } = new();
    private readonly List<string> _fullLogHistory = new();

    public MainViewModel()
    {
        _nodeHost = new NodeHostService(msg => AddLog(msg));
        
        // Subscribe to Agent 2's global logger
        ObservableLogger.LogEmitted += (s, e) => 
        {
            var formatted = $"[{e.Entry.Timestamp:HH:mm:ss}] [{e.Entry.Level.ToString()[0]}] {e.Entry.Message}";
            AddLog(formatted, e.Entry.Level.ToString());
        };

        // Start background polling
        _ = PollMetricsAsync();
    }

    private async Task PollMetricsAsync()
    {
        while (true)
        {
            if (Status == NodeStatus.Active)
            {
                BlockHeight = _nodeHost.GetBlockHeight();
                MempoolCount = _nodeHost.GetMempoolCount();

                var state = _nodeHost.GetService<IGlobalState>();
                var impersonation = _nodeHost.GetService<IImpersonationService>();

                if (state != null && ManagedAccounts.Count > 0)
                {
                    foreach (var accVm in ManagedAccounts.ToList())
                    {
                        var address = Address.FromHex(accVm.Address);
                        var balance = await state.GetBalanceAsync(address);
                        accVm.UpdateBalance(balance);
                        if (impersonation != null)
                        {
                            accVm.IsImpersonated = impersonation.IsImpersonated(address);
                        }
                    }
                }
            }
            else if (Status == NodeStatus.Inactive)
            {
                BlockHeight = 0;
                MempoolCount = 0;
            }
            await Task.Delay(1000);
        }
    }

    private void AddLog(string message, string level = "INFO")
    {
        _fullLogHistory.Add(message);

        if (IsLogsPaused) return;

        // Apply filtering (simplified: just check level string for now)
        if (LogLevelFilter != "ALL" && !level.Equals(LogLevelFilter, StringComparison.OrdinalIgnoreCase))
            return;

        Application.Current?.Dispatcher?.Invoke(() => 
        {
            Logs.Insert(0, message); // Latest on top
            if (Logs.Count > 1000) Logs.RemoveAt(Logs.Count - 1);
        });
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        try 
        {
            Status = NodeStatus.Starting;
            
            var config = new NodeConfiguration
            {
                Port = Port,
                ChainId = ChainId,
                Accounts = Accounts,
                Balance = InitialBalance,
                Mnemonic = Mnemonic,
                DerivationPath = DerivationPath,
                AutoImpersonate = AutoImpersonate,
                ForkUrl = ForkUrl,
                ForkBlockNumber = ForkBlock
            };

            await _nodeHost.StartAsync(config);
            
            // Sync Managed Accounts
            var accountManager = _nodeHost.GetService<IAccountManager>();
            if (accountManager != null)
            {
                Application.Current?.Dispatcher?.Invoke(() => {
                    ManagedAccounts.Clear();
                    foreach (var addr in accountManager.GetAddresses())
                    {
                        var pk = accountManager.GetPrivateKey(addr);
                        ManagedAccounts.Add(new AccountViewModel(addr, pk));
                    }
                });
                Mnemonic = accountManager.Mnemonic;
            }

            Status = NodeStatus.Active;
        }
        catch (Exception ex)
        {
            Status = NodeStatus.Error;
            AddLog($"[Critical] Node failed to start: {ex.Message}", "ERROR");
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        Status = NodeStatus.Stopping;
        await _nodeHost.StopAsync();
        Status = NodeStatus.Inactive;
        IsForked = false;
        Application.Current?.Dispatcher?.Invoke(() => ManagedAccounts.Clear());
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task MineBlockAsync()
    {
        var mining = _nodeHost.GetService<IMiningService>();
        if (mining != null)
        {
            await mining.MineAsync();
            AddLog("[System] Manual block mined.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void IncreaseTime(string secondsStr)
    {
        if (long.TryParse(secondsStr, out var seconds))
        {
            var chainState = _nodeHost.GetService<IChainState>();
            if (chainState != null)
            {
                chainState.TimeOffset += seconds;
                AddLog($"[System] Time increased by {seconds}s (Total offset: {chainState.TimeOffset}s)");
            }
        }
    }

    private bool CanStart() => Status == NodeStatus.Inactive || Status == NodeStatus.Error;
    private bool CanStop() => Status == NodeStatus.Active;
}
