using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Linq;

namespace Scrutor.UI.ViewModels;

public partial class StateInspectorViewModel : ObservableObject
{
    private readonly Services.NodeHostService _nodeHost;

    [ObservableProperty]
    private string _searchAddress = string.Empty;

    [ObservableProperty]
    private string _balance = "0 ETH";

    [ObservableProperty]
    private ulong _nonce;

    [ObservableProperty]
    private string _bytecode = "0x";

    [ObservableProperty]
    private bool _hasBytecode;

    [ObservableProperty]
    private bool _isAccountFound;

    [ObservableProperty]
    private string _statusMessage = "Enter an address to inspect state.";

    public ObservableCollection<StorageSlotViewModel> StorageSlots { get; } = new();

    public StateInspectorViewModel(Services.NodeHostService nodeHost)
    {
        _nodeHost = nodeHost;
    }

    [RelayCommand]
    private void InspectAddress()
    {
        if (string.IsNullOrWhiteSpace(SearchAddress))
        {
            StatusMessage = "Please enter a valid address.";
            return;
        }

        try
        {
            var address = Address.FromHex(SearchAddress.Trim());
            var globalState = _nodeHost.GetService<IGlobalState>();

            if (globalState == null)
            {
                StatusMessage = "Node must be active to inspect state.";
                return;
            }

            var snapshot = globalState.Snapshot();
            if (snapshot.TryGetValue(address, out var account))
            {
                IsAccountFound = true;
                var balanceEth = (decimal)account.Balance / 1_000_000_000_000_000_000m;
                Balance = $"{balanceEth:F4} ETH ({account.Balance} wei)";
                Nonce = account.Nonce;
                Bytecode = account.Code.Length > 0 ? "0x" + Convert.ToHexString(account.Code) : "0x";
                HasBytecode = account.Code.Length > 0;

                StorageSlots.Clear();
                foreach (var (key, value) in account.Storage.OrderBy(kv => kv.Key))
                {
                    StorageSlots.Add(new StorageSlotViewModel
                    {
                        Key = key.ToString("X64"),
                        Value = value.ToString("X64"),
                        DecimalValue = value.ToString()
                    });
                }

                StatusMessage = $"Showing state for {address}";
            }
            else
            {
                IsAccountFound = false;
                StatusMessage = "Account not found in current state.";
                ResetInspector();
            }
        }
        catch (Exception ex)
        {
            IsAccountFound = false;
            StatusMessage = $"Invalid address format: {ex.Message}";
            ResetInspector();
        }
    }

    private void ResetInspector()
    {
        Balance = "0 ETH";
        Nonce = 0;
        Bytecode = "0x";
        HasBytecode = false;
        StorageSlots.Clear();
    }
}

public class StorageSlotViewModel
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string DecimalValue { get; init; } = string.Empty;
}
