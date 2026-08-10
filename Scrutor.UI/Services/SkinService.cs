using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Scrutor.UI.Branding;

namespace Scrutor.UI.Services;

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
            "Scrutor",
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
        SetBrush(r, "Scrutor.ShellBg", skin.ShellBg);
        SetBrush(r, "Scrutor.PanelDeep", skin.PanelDeep);
        SetBrush(r, "Scrutor.PanelBg", skin.PanelBg);
        SetBrush(r, "Scrutor.PanelElevated", skin.PanelElevated);
        SetBrush(r, "Scrutor.Border", skin.Border);
        SetBrush(r, "Scrutor.BorderSubtle", skin.BorderSubtle);
        SetBrush(r, "Scrutor.TextPrimary", skin.TextPrimary);
        SetBrush(r, "Scrutor.TextMuted", skin.TextMuted);
        SetBrush(r, "Scrutor.TextDim", skin.TextDim);
        SetBrush(r, "Scrutor.Accent", skin.Accent);
        SetBrush(r, "Scrutor.AccentAlt", skin.AccentAlt);
        SetBrush(r, "Scrutor.AccentHover", skin.AccentHover);
        SetBrush(r, "Scrutor.AccentSoft", skin.AccentSoft);
        SetBrush(r, "Scrutor.Success", skin.Success);
        SetBrush(r, "Scrutor.Danger", skin.Danger);
        SetBrush(r, "Scrutor.Warning", skin.Warning);
        SetBrush(r, "Scrutor.Alert", skin.Alert);
        SetBrush(r, "Scrutor.ActiveLineBg", skin.ActiveLineBg);
        SetBrush(r, "Scrutor.VulnerableLineBg", skin.VulnerableLineBg);
        SetBrush(r, "Scrutor.SelectionBg", skin.SelectionBg);

        // String tokens for code that builds SolidColorBrush from hex
        r["Scrutor.ShellBg.Hex"] = skin.ShellBg;
        r["Scrutor.Accent.Hex"] = skin.Accent;
        r["Scrutor.AccentAlt.Hex"] = skin.AccentAlt;
        r["Scrutor.Success.Hex"] = skin.Success;
        r["Scrutor.Danger.Hex"] = skin.Danger;
        r["Scrutor.Warning.Hex"] = skin.Warning;

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
