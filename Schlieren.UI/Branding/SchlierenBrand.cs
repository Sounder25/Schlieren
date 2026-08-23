using Schlieren.UI.Services;

namespace Schlieren.UI.Branding;

/// <summary>
/// Schlieren brand tokens. Static consts remain the official board defaults;
/// live UI chrome should prefer <see cref="SkinService.Current"/> / DynamicResource.
/// Source board: Assets/brand-board-source.png
/// </summary>
public static class SchlierenBrand
{
    // Official board defaults (Brand Classic skin)
    public const string ExecutionIndigo = "#4A00E0";
    public const string BlobAqua = "#19D7E5";
    public const string TracingWhite = "#F0F4F8";
    public const string WarmAccessYellow = "#FFD700";
    public const string ColdAccessGrey = "#A9A9A9";
    public const string LogRevertOrange = "#FF4500";
    public const string ShellBackground = "#0D0D0D";
    public const string PanelBackground = "#1A1A2E";
    public const string PanelElevated = "#1E1E2E";
    public const string PanelDeep = "#0C0D14";
    public const string BorderMuted = "#2D2D2D";

    // Product copy
    public const string ProductName = "SCHLIEREN";
    public const string ProductTagline = ".NET 8 Ethereum Execution & Verification Engine";
    public const string WindowTitle = "SCHLIEREN — .NET 8 Ethereum Execution & Verification Engine";

    // Asset paths (avares)
    public const string IconAvares = "avares://Schlieren.UI/Assets/schlieren-icon.png";

    /// <summary>Live accent from the active skin (for code-behind brushes).</summary>
    public static string LiveAccent => SkinService.Current.Accent;
    public static string LiveAccentAlt => SkinService.Current.AccentAlt;
    public static string LiveShell => SkinService.Current.ShellBg;
}
