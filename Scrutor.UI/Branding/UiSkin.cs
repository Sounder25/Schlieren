namespace Scrutor.UI.Branding;

/// <summary>
/// One complete UI skin / palette. Values are #RRGGBB hex strings.
/// </summary>
public sealed record UiSkin(
    string Id,
    string DisplayName,
    string Description,
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
    string SelectionBg);
