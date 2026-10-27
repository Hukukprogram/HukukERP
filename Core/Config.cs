using System.Text.Json;
using System.Text.Json.Nodes;

namespace HukukErp.Core;

/// <summary>config.json — Python sürümüyle aynı biçim ve anahtarlar.</summary>
public class Config
{
    public string FilePath { get; }
    public JsonObject Data { get; private set; }

    static readonly JsonSerializerOptions Opt = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Veri klasörü: %LOCALAPPDATA%\HukukERP\Veri (güncellemelerde silinmez). Program klasöründen ayrıdır.</summary>
    public static string AppDir()
    {
        var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HukukERP", "Veri");
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>İlk çalıştırmada, program klasöründe (veya bir üstünde) eski config.json + .db varsa veri klasörüne KOPYALAR (üzerine yazmaz).</summary>
    public static void EskiVeriyiAktar()
    {
        try
        {
            var hedef = AppDir();
            if (File.Exists(Path.Combine(hedef, "config.json"))) return;
            var baz = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var kaynak in new[] { baz, Path.GetDirectoryName(baz) ?? baz })
            {
                var cfg = Path.Combine(kaynak, "config.json");
                if (!File.Exists(cfg)) continue;
                File.Copy(cfg, Path.Combine(hedef, "config.json"), false);
                foreach (var f in Directory.GetFiles(kaynak, "*.db"))
                    if (!File.Exists(Path.Combine(hedef, Path.GetFileName(f)))) File.Copy(f, Path.Combine(hedef, Path.GetFileName(f)), false);
                return;
            }
        }
        catch (Exception) { }
    }

    static JsonObject Defaults() => new()
    {
        ["db_path"] = "hukuk.db",
        ["theme"] = "dark",
        ["uets_url"] = "https://ptt.etebligat.gov.tr/messages/1",
        ["uets_oto_tarama_dk"] = 0,
        ["hatirlatma_kontrol_dk"] = 30,
        ["tahsilat_hatirlatma_gun"] = 3,
        ["gorev_hatirlatma_gun"] = 2,
        ["guncelleme_url"] = "",
        ["uets_secicileri"] = new JsonObject
        {
            ["tebligat_satiri"] = "table tbody tr",
            ["indir_dugmesi"] = "a[href*='indir'], button:has-text('İndir'), a:has-text('İndir')",
        },
    };

    public Config(string? path = null)
    {
        FilePath = path ?? Path.Combine(AppDir(), "config.json");
        Data = Defaults();
        Load();
    }

    void Load()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject loaded)
                    foreach (var kv in loaded.ToList())
                    {
                        loaded.Remove(kv.Key);
                        Data[kv.Key] = kv.Value;
                    }
            }
            catch (Exception) { /* bozuk dosya: varsayılanlarla devam */ }
        }
        else Save();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, Data.ToJsonString(Opt)); } catch (IOException) { }
    }

    public string GetString(string key, string def = "") =>
        Data.TryGetPropertyValue(key, out var n) && n != null ? (n.ToString() ?? def) : def;

    public int GetInt(string key, int def = 0)
    {
        if (!Data.TryGetPropertyValue(key, out var n) || n == null) return def;
        try { return n.GetValueKind() == JsonValueKind.String ? int.Parse(n.GetValue<string>()) : n.GetValue<int>(); }
        catch { return def; }
    }

    public double GetDouble(string key, double def = 0)
    {
        if (!Data.TryGetPropertyValue(key, out var n) || n == null) return def;
        try { return n.GetValueKind() == JsonValueKind.String ? double.Parse(n.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture) : n.GetValue<double>(); }
        catch { return def; }
    }

    public void Set(string key, object? value)
    {
        Data[key] = value switch
        {
            null => null,
            int i => JsonValue.Create(i),
            long l => JsonValue.Create(l),
            double d => JsonValue.Create(d),
            bool b => JsonValue.Create(b),
            _ => JsonValue.Create(value.ToString()),
        };
        Save();
    }

    public string DbPath
    {
        get
        {
            var p = GetString("db_path", "hukuk.db");
            if (p.Equals("sirket_hukuk.db", StringComparison.OrdinalIgnoreCase)) { p = "hukuk.db"; Set("db_path", p); }
            var yol = Path.IsPathRooted(p) || p.StartsWith(@"\\") ? p : Path.Combine(AppDir(), p);
            EskiAdiTasi(yol);
            return yol;
        }
    }

    /// <summary>Eski adlı veritabanı (sirket_hukuk.db) ve yedekleri yeni ada TAŞINIR (veri kaybolmaz, eski ad kalmaz).</summary>
    static void EskiAdiTasi(string yeni)
    {
        try
        {
            var dir = Path.GetDirectoryName(yeni);
            if (dir == null || !Path.GetFileName(yeni).Equals("hukuk.db", StringComparison.OrdinalIgnoreCase)) return;
            var eski = Path.Combine(dir, "sirket_hukuk.db");
            if (File.Exists(eski) && !File.Exists(yeni)) File.Move(eski, yeni);
            var yedek = Path.Combine(dir, "Yedekler");
            if (Directory.Exists(yedek))
                foreach (var f in Directory.GetFiles(yedek, "sirket_hukuk_*.db"))
                {
                    var hedef = Path.Combine(yedek, "hukuk_" + Path.GetFileName(f)["sirket_hukuk_".Length..]);
                    if (!File.Exists(hedef)) File.Move(f, hedef);
                }
        }
        catch (Exception) { }
    }

    /// <summary>Dosyalar/ ve Yedekler/ veritabanının yanında durur.</summary>
    public string DataDir => Path.GetDirectoryName(DbPath) ?? AppDir();

    public string Subdir(params string[] parts)
    {
        var d = Path.Combine(new[] { DataDir }.Concat(parts).ToArray());
        Directory.CreateDirectory(d);
        return d;
    }
}
