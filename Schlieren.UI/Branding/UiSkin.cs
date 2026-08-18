namespace Schlieren.UI.Branding;

/// <summary>Decorative center-panel art motif for a skin.</summary>
public enum SkinArtMotif
{
    /// <summary>Soft Schlieren brand mark image.</summary>
    SchlierenMark,
    /// <summary>Ethereum diamond glyph (dev-geek classic).</summary>
    EthDiamond,
    /// <summary>Sounder field-ops orange/navy sigil.</summary>
    SounderSigil,
    /// <summary>Abstract radial sigil — cyber / void energy.</summary>
    VoidSigil,
    /// <summary>No watermark art.</summary>
    None
}

/// <summary>
/// One complete UI skin / palette. Values are #RRGGBB hex strings.
/// </summary>
public sealed record UiSkin(
    string Id,
    string DisplayName,
    string Description,
    string Category,
    // Surfaces
    string ShellBg,
    string PanelDeep,
    string PanelBg,
    string PanelElevated,
    string Border,
    string BorderSubtle,
    // Text
    string TextPrimary,
    string TextMuted,
    string TextDim,
    // Accents
    string Accent,
    string AccentAlt,
    string AccentHover,
    string AccentSoft,
    // Semantic
    string Success,
    string Danger,
    string Warning,
    string Alert,
    // Editor chrome
    string ActiveLineBg,
    string VulnerableLineBg,
    string SelectionBg,
    // Art
    SkinArtMotif ArtMotif = SkinArtMotif.SchlierenMark,
    /// <summary>Multiplies base watermark opacity (1.0 = default).</summary>
    double WatermarkBoost = 1.0);
