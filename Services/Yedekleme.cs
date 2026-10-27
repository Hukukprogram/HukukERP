using System.Security.Cryptography;
using System.Text;
using HukukErp.Core;

namespace HukukErp.Services;

/// <summary>Yedek alma, ikinci konuma kopyalama ve yedekten geri yükleme.</summary>
public static class Yedekleme
{
    public static string Klasor(Config cfg) => cfg.Subdir("Yedekler");

    /// <summary>Yedek alır; ikinci konum tanımlıysa oraya da kopyalar. Başarısızsa null.</summary>
    public static string? Yap(Db db, Config cfg)
    {
        var dosya = db.Backup(Klasor(cfg), Math.Max(5, cfg.GetInt("yedek_sayisi", 30)));
        if (dosya == null) return null;
        var ikinci = cfg.GetString("yedek_ikinci_konum");
        if (ikinci.Length > 0)
        {
            try
            {
                Directory.CreateDirectory(ikinci);
                File.Copy(dosya, Path.Combine(ikinci, Path.GetFileName(dosya)), true);
                var eski = Directory.GetFiles(ikinci, "hukuk_*.db").OrderBy(x => x, StringComparer.Ordinal).ToList();
                foreach (var e in eski.Take(Math.Max(0, eski.Count - Math.Max(5, cfg.GetInt("yedek_sayisi", 30))))) try { File.Delete(e); } catch { }
            }
            catch (Exception) { /* ikinci konum erişilemiyorsa ana yedek yine de alınmış durumda */ }
        }
        cfg.Set("son_yedek", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        return dosya;
    }

    /// <summary>Günde bir kez (açılışta) otomatik yedek.</summary>
    public static void GunlukYedek(Db db, Config cfg)
    {
        var bugun = DateTime.Today.ToString("yyyy-MM-dd");
        if (cfg.GetString("son_yedek_gun") == bugun) return;
        if (Yap(db, cfg) != null) cfg.Set("son_yedek_gun", bugun);
    }

    static string Bekleyen(Config cfg) => Path.Combine(cfg.DataDir, "geri_yukle.bekleyen");

    /// <summary>Çalışan veritabanı kapatılamadığı için geri yükleme bir sonraki açılışta uygulanır.</summary>
    public static void GeriYuklemeIste(Config cfg, string yedekDosya) => File.Copy(yedekDosya, Bekleyen(cfg), true);

    /// <summary>Program açılırken, veritabanı açılmadan önce çağrılır. Mevcut veritabanını "oncesi_" adıyla saklar.</summary>
    public static bool BekleyeniUygula(Config cfg)
    {
        var b = Bekleyen(cfg);
        if (!File.Exists(b)) return false;
        try
        {
            var db = cfg.DbPath;
            if (File.Exists(db))
                File.Copy(db, Path.Combine(Klasor(cfg), $"oncesi_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.db"), true);
            foreach (var ek in new[] { db + "-wal", db + "-shm" }) if (File.Exists(ek)) File.Delete(ek);
            File.Copy(b, db, true);
            File.Delete(b);
            return true;
        }
        catch (Exception) { return false; }
    }
}

/// <summary>Program kilidi: parola yalnızca arayüzü kilitler, veritabanı dosyasını şifrelemez.</summary>
public static class Kilit
{
    public static bool Var(Config cfg) => cfg.GetString("kilit_hash").Length > 0;

    static string Ozet(string parola, byte[] salt) =>
        Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(parola), salt, 100_000, HashAlgorithmName.SHA256, 32));

    public static void Ayarla(Config cfg, string parola)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        cfg.Set("kilit_salt", Convert.ToHexString(salt));
        cfg.Set("kilit_hash", Ozet(parola, salt));
    }

    public static void Kaldir(Config cfg) { cfg.Set("kilit_hash", ""); cfg.Set("kilit_salt", ""); }

    public static bool Dogru(Config cfg, string parola)
    {
        try { return Ozet(parola, Convert.FromHexString(cfg.GetString("kilit_salt"))) == cfg.GetString("kilit_hash"); }
        catch (Exception) { return false; }
    }
}
