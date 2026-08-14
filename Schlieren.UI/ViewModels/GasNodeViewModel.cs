using Avalonia;

namespace Schlieren.UI.ViewModels;

public class GasNodeViewModel
{
    public string DisplayText { get; init; } = "";
    public Thickness Indent { get; init; } = new(16, 2);
    public string Color { get; init; } = "#E0E0E0";
}
