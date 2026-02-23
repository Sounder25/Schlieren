using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scrutor.Core.Primitives;
using System.Numerics;

namespace Scrutor.UI.ViewModels;

public partial class AccountViewModel : ObservableObject
{
    private readonly Address _address;
    private readonly string? _privateKey;

    [ObservableProperty]
    private string _balance = "0.00 ETH";

    [ObservableProperty]
    private bool _isImpersonated;

    [ObservableProperty]
    private bool _showPrivateKey;

    public string Address => _address.ToString();
    public string PrivateKey => _privateKey ?? "Unknown";
    public string MaskedPrivateKey => new string('*', 14);

    public AccountViewModel(Address address, string? privateKey = null)
    {
        _address = address;
        _privateKey = privateKey;
    }

    [RelayCommand]
    private void TogglePrivateKey()
    {
        ShowPrivateKey = !ShowPrivateKey;
    }

    public void UpdateBalance(BigInteger balanceWei)
    {
        var balanceEth = (decimal)balanceWei / 1_000_000_000_000_000_000m;
        Balance = $"{balanceEth:F4} ETH";
    }
}
