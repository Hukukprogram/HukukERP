using System.Windows;
using System.Windows.Media;

namespace HukukErp.UI;

/// <summary>Koyu/Açık tema. Fırçalar Application.Resources içinde tutulur; stiller DynamicResource ile bağlanır.</summary>
public static class Theme
{
    public static string Name { get; private set; } = "dark";

    static readonly Dictionary<string, Dictionary<string, string>> Palettes = new()
    {
        ["dark"] = new()
        {
            ["bg"] = "#0d1117", ["surface"] = "#151b24", ["surface2"] = "#1c2430", ["border"] = "#273142",
            ["text"] = "#f1f5fb", ["muted"] = "#9aa7bb", ["accent"] = "#5b8cff", ["accent_h"] = "#7aa2ff",
            ["ok"] = "#34d39e", ["warn"] = "#f7b23b", ["danger"] = "#ff6b7a", ["sidebar"] = "#090d13", ["sel"] = "#1f2e4d",
        },
        ["light"] = new()
        {
            ["bg"] = "#f4f6fb", ["surface"] = "#ffffff", ["surface2"] = "#f0f3f9", ["border"] = "#e1e6ef",
            ["text"] = "#161e2d", ["muted"] = "#6b7890", ["accent"] = "#3b6cf0", ["accent_h"] = "#2a58d6",
            ["ok"] = "#12a56e", ["warn"] = "#d98a00", ["danger"] = "#e04557", ["sidebar"] = "#17233d", ["sel"] = "#e3ecff",
        },
    };

    public static Color Col(string key) =>
        (Color)ColorConverter.ConvertFromString(Palettes[Name].TryGetValue(key, out var v) ? v : "#ff00ff");

    public static string Hex(string key) => Palettes[Name][key];

    public static void Apply(string name)
    {
        Name = Palettes.ContainsKey(name) ? name : "dark";
        foreach (var kv in Palettes[Name])
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(kv.Value));
            b.Freeze();
            Application.Current.Resources[kv.Key] = b;
        }
    }

    public static Brush B(string key) => (Brush)Application.Current.Resources[key];

    public static Brush Alpha(string key, byte a)
    {
        var c = Col(key);
        var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}
