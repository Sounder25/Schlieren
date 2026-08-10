namespace Scrutor.UI.Branding;

/// <summary>
/// Curated skins — tuned for long audit sessions, not just brand board screenshots.
/// </summary>
public static class SkinCatalog
{
    public const string DefaultSkinId = "arctic-night";

    public static IReadOnlyList<UiSkin> All { get; } =
    [
        // Soft Nord-inspired — easiest on eyes for long runs (default)
        new UiSkin(
            Id: "arctic-night",
            DisplayName: "Arctic Night",
            Description: "Nord-inspired blue-grey. Lowest eye strain for long conformance runs.",
            ShellBg: "#0B0E14",
            PanelDeep: "#0D1117",
            PanelBg: "#151B23",
            PanelElevated: "#1C2430",
            Border: "#2A3441",
            BorderSubtle: "#232B36",
            TextPrimary: "#E6EDF3",
            TextMuted: "#9AA7B5",
            TextDim: "#6B7785",
            Accent: "#6C9EFF",
            AccentAlt: "#7FDBCA",
            AccentHover: "#8FB4FF",
            AccentSoft: "#A8B4FF",
            Success: "#3DDC97",
            Danger: "#FF6B7A",
            Warning: "#E6C07B",
            Alert: "#FF8A65",
            ActiveLineBg: "#1A2740",
            VulnerableLineBg: "#2A1818",
            SelectionBg: "#243044"),

        // Current brand board — keep for marketing screenshots
        new UiSkin(
            Id: "brand-classic",
            DisplayName: "Brand Classic",
            Description: "Official board: Execution Indigo + Blob Aqua. Punchy for demos.",
            ShellBg: "#0D0D0D",
            PanelDeep: "#0C0D14",
            PanelBg: "#1A1A2E",
            PanelElevated: "#1E1E2E",
            Border: "#2D2D2D",
            BorderSubtle: "#2D2D3D",
            TextPrimary: "#F0F4F8",
            TextMuted: "#A9A9A9",
            TextDim: "#888888",
            Accent: "#4A00E0",
            AccentAlt: "#19D7E5",
            AccentHover: "#5B14F0",
            AccentSoft: "#B794F6",
            Success: "#22C55E",
            Danger: "#EF4444",
            Warning: "#FFD700",
            Alert: "#FF4500",
            ActiveLineBg: "#241B47",
            VulnerableLineBg: "#2E141B",
            SelectionBg: "#1A1040"),

        // Softer indigo — brand-adjacent without the neon blast
        new UiSkin(
            Id: "midnight-soft",
            DisplayName: "Midnight Soft",
            Description: "Brand DNA, desaturated. Indigo/aqua without the headache.",
            ShellBg: "#0C0E16",
            PanelDeep: "#10131C",
            PanelBg: "#171B28",
            PanelElevated: "#1E2333",
            Border: "#2C3348",
            BorderSubtle: "#252A3A",
            TextPrimary: "#E8ECF4",
            TextMuted: "#9BA3B5",
            TextDim: "#6E768A",
            Accent: "#6B5CFF",
            AccentAlt: "#4ECFDC",
            AccentHover: "#8478FF",
            AccentSoft: "#A89BFF",
            Success: "#3ECF8E",
            Danger: "#F07178",
            Warning: "#E6B450",
            Alert: "#FF7B54",
            ActiveLineBg: "#1E2240",
            VulnerableLineBg: "#2A1A1E",
            SelectionBg: "#222840"),

        // Warm amber IDE — like a good late-night terminal
        new UiSkin(
            Id: "obsidian-amber",
            DisplayName: "Obsidian Amber",
            Description: "Warm charcoal + amber. Feels expensive, easy at night.",
            ShellBg: "#0E0C0A",
            PanelDeep: "#12100E",
            PanelBg: "#1A1714",
            PanelElevated: "#221E1A",
            Border: "#3A342C",
            BorderSubtle: "#2E2923",
            TextPrimary: "#F2EDE6",
            TextMuted: "#B0A697",
            TextDim: "#7A7268",
            Accent: "#E8A54B",
            AccentAlt: "#D4A574",
            AccentHover: "#F0B86A",
            AccentSoft: "#C9A87C",
            Success: "#8FBF6A",
            Danger: "#E07060",
            Warning: "#E8C060",
            Alert: "#E08050",
            ActiveLineBg: "#2A2218",
            VulnerableLineBg: "#2A1814",
            SelectionBg: "#2C241C"),

        // Soft phosphor — hacker energy without laser-green
        new UiSkin(
            Id: "phosphor",
            DisplayName: "Phosphor",
            Description: "Muted matrix green on deep black. Audit-terminal vibes.",
            ShellBg: "#080B09",
            PanelDeep: "#0A0F0C",
            PanelBg: "#101814",
            PanelElevated: "#16201A",
            Border: "#24332A",
            BorderSubtle: "#1C2820",
            TextPrimary: "#D6E8DC",
            TextMuted: "#8FAE9A",
            TextDim: "#5F7A68",
            Accent: "#3DDC84",
            AccentAlt: "#6EE7B7",
            AccentHover: "#55E898",
            AccentSoft: "#7DCEA0",
            Success: "#4ADE80",
            Danger: "#F87171",
            Warning: "#FBBF24",
            Alert: "#FB923C",
            ActiveLineBg: "#14241A",
            VulnerableLineBg: "#241414",
            SelectionBg: "#1A2C22"),

        // High contrast for projectors / accessibility
        new UiSkin(
            Id: "high-contrast",
            DisplayName: "High Contrast",
            Description: "Max contrast for projectors and accessibility demos.",
            ShellBg: "#000000",
            PanelDeep: "#0A0A0A",
            PanelBg: "#121212",
            PanelElevated: "#1A1A1A",
            Border: "#555555",
            BorderSubtle: "#444444",
            TextPrimary: "#FFFFFF",
            TextMuted: "#CCCCCC",
            TextDim: "#999999",
            Accent: "#00B7FF",
            AccentAlt: "#00FFD0",
            AccentHover: "#33C5FF",
            AccentSoft: "#80D4FF",
            Success: "#00FF88",
            Danger: "#FF3355",
            Warning: "#FFDD00",
            Alert: "#FF6600",
            ActiveLineBg: "#003355",
            VulnerableLineBg: "#330000",
            SelectionBg: "#002244"),
    ];

    public static UiSkin Get(string? id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? All.First(s => s.Id == DefaultSkinId);
}
