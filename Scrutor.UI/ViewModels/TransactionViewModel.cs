using CommunityToolkit.Mvvm.ComponentModel;
using Scrutor.Core.Primitives;
using System.Collections.Generic;
using System.Numerics;

namespace Scrutor.UI.ViewModels;

public partial class TransactionViewModel : ObservableObject
{
    public string Hash { get; init; } = string.Empty;
    public string From { get; init; } = string.Empty;
    public string? To { get; init; }
    public ulong BlockNumber { get; init; }
    public ulong GasUsed { get; init; }
    public bool IsSuccess { get; init; }
    public string Value { get; init; } = "0 ETH";
    
    [ObservableProperty]
    private bool _isExpanded;

    public List<TransactionLogViewModel> Logs { get; } = new();

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;
}

public class TransactionLogViewModel
{
    public string Address { get; init; } = string.Empty;
    public List<string> Topics { get; init; } = new();
    public string Data { get; init; } = string.Empty;
}
