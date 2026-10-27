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
            ["bg"] = "#0f141b", ["surface"] = "#171e28", ["surface2"] = "#1f2835", ["border"] = "#2a3544",
            ["text"] = "#e6ebf2", ["muted"] = "#8b98aa", ["accent"] = "#4c8dff", ["accent_h"] = "#6ba1ff",
            ["ok"] = "#2ecc8f", ["warn"] = "#f5a524", ["danger"] = "#ff5d6c", ["sidebar"] = "#0b1017", ["sel"] = "#223250",
        },
        ["light"] = new()
        {
            ["bg"] = "#f3f5f9", ["surface"] = "#ffffff", ["surface2"] = "#eef1f6", ["border"] = "#d9dfe8",
            ["text"] = "#1b2433", ["muted"] = "#66738a", ["accent"] = "#2f6fe4", ["accent_h"] = "#1f5ccc",
            ["ok"] = "#12a56e", ["warn"] = "#d98a00", ["danger"] = "#d93f50", ["sidebar"] = "#1b2740", ["sel"] = "#dce8ff",
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
