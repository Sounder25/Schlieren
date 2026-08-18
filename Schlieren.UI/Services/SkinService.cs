using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Schlieren.UI.Branding;

namespace Schlieren.UI.Services;

/// <summary>
/// Applies curated UI skins via Application DynamicResource keys and persists choice.
/// </summary>
public static class SkinService
{
    public const string PrefFileName = "ui-skin.json";

    public static event Action<UiSkin>? SkinChanged;

    public static UiSkin Current { get; private set; } = SkinCatalog.Get(SkinCatalog.DefaultSkinId);

    public static string PreferencesPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Schlieren",
            PrefFileName);

    public static void LoadAndApply()
    {
        var id = SkinCatalog.DefaultSkinId;
        try
        {
            var path = PreferencesPath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("skinId", out var prop))
                    id = prop.GetString() ?? id;
            }
        }
        catch
        {
            // keep default
        }

        Apply(SkinCatalog.Get(id), persist: false);
    }

    public static void Apply(string skinId) => Apply(SkinCatalog.Get(skinId), persist: true);

    public static void Apply(UiSkin skin, bool persist = true)
    {
        Current = skin;
        var app = Application.Current;
        if (app is null) return;

        var r = app.Resources;
        SetBrush(r, "Schlieren.ShellBg", skin.ShellBg);
        SetBrush(r, "Schlieren.PanelDeep", skin.PanelDeep);
        SetBrush(r, "Schlieren.PanelBg", skin.PanelBg);
        SetBrush(r, "Schlieren.PanelElevated", skin.PanelElevated);
        SetBrush(r, "Schlieren.Border", skin.Border);
        SetBrush(r, "Schlieren.BorderSubtle", skin.BorderSubtle);
        SetBrush(r, "Schlieren.TextPrimary", skin.TextPrimary);
        SetBrush(r, "Schlieren.TextMuted", skin.TextMuted);
        SetBrush(r, "Schlieren.TextDim", skin.TextDim);
        SetBrush(r, "Schlieren.Accent", skin.Accent);
        SetBrush(r, "Schlieren.AccentAlt", skin.AccentAlt);
        SetBrush(r, "Schlieren.AccentHover", skin.AccentHover);
        SetBrush(r, "Schlieren.AccentSoft", skin.AccentSoft);
        SetBrush(r, "Schlieren.Success", skin.Success);
        SetBrush(r, "Schlieren.Danger", skin.Danger);
        SetBrush(r, "Schlieren.Warning", skin.Warning);
        SetBrush(r, "Schlieren.Alert", skin.Alert);
        SetBrush(r, "Schlieren.ActiveLineBg", skin.ActiveLineBg);
        SetBrush(r, "Schlieren.VulnerableLineBg", skin.VulnerableLineBg);
        SetBrush(r, "Schlieren.SelectionBg", skin.SelectionBg);

        // String tokens for code that builds SolidColorBrush from hex
        r["Schlieren.ShellBg.Hex"] = skin.ShellBg;
        r["Schlieren.Accent.Hex"] = skin.Accent;
        r["Schlieren.AccentAlt.Hex"] = skin.AccentAlt;
        r["Schlieren.Success.Hex"] = skin.Success;
        r["Schlieren.Danger.Hex"] = skin.Danger;
        r["Schlieren.Warning.Hex"] = skin.Warning;
        r["Schlieren.WatermarkBoost"] = skin.WatermarkBoost;
        r["Schlieren.ArtMotif"] = skin.ArtMotif.ToString();

        if (persist)
            Save(skin.Id);

        SkinChanged?.Invoke(skin);
    }

    public static void Save(string skinId)
    {
        try
        {
            var dir = Path.GetDirectoryName(PreferencesPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(new { skinId });
            File.WriteAllText(PreferencesPath, json);
        }
        catch
        {
            // non-fatal
        }
    }

    private static void SetBrush(IResourceDictionary r, string key, string hex)
    {
        r[key] = new SolidColorBrush(Color.Parse(hex));
    }
}
