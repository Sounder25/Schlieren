namespace Scrutor.UI.Branding;

/// <summary>
/// Scrutor brand system from the official brand board.
/// Source: Assets/brand-board-source.png
/// </summary>
public static class ScrutorBrand
{
    // Primary palette
    public const string ExecutionIndigo = "#4A00E0";
    public const string BlobAqua = "#19D7E5";
    public const string TracingWhite = "#F0F4F8";

    // Functional accents
    public const string WarmAccessYellow = "#FFD700";
    public const string ColdAccessGrey = "#A9A9A9";
    public const string LogRevertOrange = "#FF4500";

    // Shell / surfaces (dark IDE chrome)
    public const string ShellBackground = "#0D0D0D";
    public const string PanelBackground = "#1A1A2E";
    public const string PanelElevated = "#1E1E2E";
    public const string PanelDeep = "#0C0D14";
    public const string BorderMuted = "#2D2D2D";

    // Product copy
    public const string ProductName = "SCRUTOR";
    public const string ProductTagline = ".NET 8 Ethereum Execution & Verification Engine";
    public const string WindowTitle = "SCRUTOR — .NET 8 Ethereum Execution & Verification Engine";

    // Asset paths (avares) — different marks for different sizes
    /// <summary>Simple low-density mark for header / window icon (stays crisp at 32–48px).</summary>
    public const string IconAvares = "avares://Scrutor.UI/Assets/scrutor-icon.png";
    /// <summary>Full detailed mark for large watermark / splash (never force into tiny chrome).</summary>
    public const string WatermarkAvares = "avares://Scrutor.UI/Assets/scrutor-watermark.png";
    public const string LogoFullAvares = "avares://Scrutor.UI/Assets/scrutor-logo-full.png";
    public const string LockupAvares = "avares://Scrutor.UI/Assets/scrutor-lockup.png";
}
