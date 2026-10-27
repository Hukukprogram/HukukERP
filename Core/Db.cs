using System.Text;
using Microsoft.Data.Sqlite;

namespace HukukErp.Core;

public static class Schema
{
    public const string Sql = @"
CREATE TABLE IF NOT EXISTS muvekkiller (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    tc_no TEXT UNIQUE NOT NULL,
    ad_soyad TEXT NOT NULL,
    telefon TEXT DEFAULT '',
    eposta TEXT DEFAULT '',
    adres TEXT DEFAULT '',
    vekaletname_yolu TEXT DEFAULT '',
    kayit_tarihi TEXT DEFAULT (date('now'))
);
CREATE TABLE IF NOT EXISTS genel_tebligatlar (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    gonderen_kurum TEXT DEFAULT '',
    konu TEXT DEFAULT '',
    esas_no TEXT DEFAULT '',
    teblig_tarihi TEXT DEFAULT '',
    son_cevap_tarihi TEXT DEFAULT '',
    durum TEXT DEFAULT 'Yeni',
    pdf_yolu TEXT DEFAULT '',
    uets_hesap_adi TEXT DEFAULT '',
    hesap_tipi TEXT DEFAULT 'Sahsi' CHECK (hesap_tipi = 'Sahsi'),
    dosya_hash TEXT DEFAULT '',
    ayrisan_json TEXT DEFAULT '',
    olusturma TEXT DEFAULT (datetime('now','localtime'))
);
CREATE TABLE IF NOT EXISTS davalar (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    mahkeme_adi TEXT DEFAULT '',
    esas_no TEXT DEFAULT '',
    dava_turu TEXT DEFAULT '',
    karsi_taraf TEXT DEFAULT '',
    durusma_tarihi TEXT DEFAULT '',
    asama_durum TEXT DEFAULT 'Dava Dilekçesi',
    genel_notlar TEXT DEFAULT '',
    dava_dilekcesi_tarih TEXT DEFAULT '',
    cevap_dilekcesi_tarih TEXT DEFAULT '',
    cevaba_cevap_tarih TEXT DEFAULT '',
    ikinci_cevap_tarih TEXT DEFAULT '',
    muvekkil_id INTEGER REFERENCES muvekkiller(id) ON DELETE SET NULL
);
CREATE TABLE IF NOT EXISTS dava_masraflari (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    dava_id INTEGER NOT NULL REFERENCES davalar(id) ON DELETE CASCADE,
    tarih TEXT DEFAULT (date('now')),
    masraf_kalemi TEXT DEFAULT '',
    tutar REAL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS gorevler (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    baslik TEXT NOT NULL,
    aciklama TEXT DEFAULT '',
    olusturma_tarihi TEXT DEFAULT (date('now')),
    son_tarih TEXT DEFAULT '',
    durum TEXT DEFAULT 'Yapılacak',
    oncelik TEXT DEFAULT 'Orta'
);
CREATE TABLE IF NOT EXISTS finans_tahsilat (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    muvekkil_id INTEGER NOT NULL REFERENCES muvekkiller(id) ON DELETE CASCADE,
    dava_id INTEGER DEFAULT 0,
    dosya_konusu TEXT DEFAULT '',
    toplam_ucret REAL DEFAULT 0,
    alinan_tutar REAL DEFAULT 0,
    kalan_bakiye REAL DEFAULT 0,
    sonraki_tahsilat_tarihi TEXT DEFAULT '',
    makbuz_kesildi INTEGER DEFAULT 0,
    notlar TEXT DEFAULT ''
);
CREATE TABLE IF NOT EXISTS tahsilat_hareketleri (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    finans_id INTEGER NOT NULL REFERENCES finans_tahsilat(id) ON DELETE CASCADE,
    tarih TEXT DEFAULT '',
    tutar REAL DEFAULT 0,
    aciklama TEXT DEFAULT ''
);
CREATE INDEX IF NOT EXISTS ix_hareket_finans ON tahsilat_hareketleri(finans_id);
CREATE TABLE IF NOT EXISTS ajanda (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    tur TEXT DEFAULT 'Duruşma',
    tarih TEXT NOT NULL,
    saat TEXT DEFAULT '',
    baslik TEXT DEFAULT '',
    mahkeme TEXT DEFAULT '',
    esas_no TEXT DEFAULT '',
    dava_id INTEGER DEFAULT 0,
    notlar TEXT DEFAULT ''
);
CREATE INDEX IF NOT EXISTS idx_ajanda_tarih ON ajanda(tarih);
CREATE TABLE IF NOT EXISTS sureler (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    baslik TEXT NOT NULL,
    dayanak TEXT DEFAULT '',
    baslangic TEXT DEFAULT '',
    sure_turu TEXT DEFAULT '',
    son_tarih TEXT NOT NULL,
    dava_id INTEGER DEFAULT 0,
    notlar TEXT DEFAULT '',
    durum TEXT DEFAULT 'Açık',
    olusturma TEXT DEFAULT (date('now'))
);
CREATE INDEX IF NOT EXISTS ix_sure_tarih ON sureler(son_tarih);
CREATE TABLE IF NOT EXISTS vekaletnameler (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    muvekkil_id INTEGER NOT NULL REFERENCES muvekkiller(id) ON DELETE CASCADE,
    tur TEXT DEFAULT 'Genel vekaletname',
    tarih TEXT DEFAULT '',
    dava_id INTEGER DEFAULT 0,
    aciklama TEXT DEFAULT '',
    dosya_yolu TEXT DEFAULT ''
);
CREATE INDEX IF NOT EXISTS ix_vekalet_muv ON vekaletnameler(muvekkil_id);
CREATE TABLE IF NOT EXISTS sablonlar (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    sablon_adi TEXT NOT NULL,
    sablon_tipi TEXT DEFAULT 'docx',
    taslak_metin TEXT DEFAULT ''
);
CREATE TABLE IF NOT EXISTS ayarlar (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    smtp_server TEXT DEFAULT '',
    smtp_port INTEGER DEFAULT 587,
    sender_email TEXT DEFAULT '',
    sender_password TEXT DEFAULT '',
    telegram_bot_token TEXT DEFAULT '',
    telegram_kisisel_chat_id TEXT DEFAULT ''
);
CREATE TABLE IF NOT EXISTS uets_hesaplari (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    hesap_adi TEXT NOT NULL,
    kimlik_no TEXT DEFAULT '',
    hesap_tipi TEXT DEFAULT 'Sahsi' CHECK (hesap_tipi = 'Sahsi'),
    durum TEXT DEFAULT 'Aktif'
);
CREATE TABLE IF NOT EXISTS bildirim_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    tur TEXT NOT NULL,
    ref_id INTEGER NOT NULL,
    gun TEXT NOT NULL,
    UNIQUE (tur, ref_id, gun)
);
CREATE INDEX IF NOT EXISTS ix_teb_tip_durum ON genel_tebligatlar(hesap_tipi, durum);
CREATE INDEX IF NOT EXISTS ix_teb_hash ON genel_tebligatlar(dosya_hash);
CREATE INDEX IF NOT EXISTS ix_masraf_dava ON dava_masraflari(dava_id);
CREATE TABLE IF NOT EXISTS audit_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    zaman TEXT DEFAULT (datetime('now','localtime')),
    tablo TEXT DEFAULT '',
    kayit_id INTEGER DEFAULT 0,
    islem TEXT DEFAULT '',
    ozet TEXT DEFAULT ''
);
CREATE INDEX IF NOT EXISTS ix_audit_zaman ON audit_log(zaman);
CREATE TABLE IF NOT EXISTS emanet_hareketleri (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    muvekkil_id INTEGER NOT NULL REFERENCES muvekkiller(id) ON DELETE CASCADE,
    dava_id INTEGER DEFAULT 0,
    tarih TEXT DEFAULT (date('now')),
    tur TEXT DEFAULT 'Giriş',
    tutar REAL DEFAULT 0,
    aciklama TEXT DEFAULT ''
);
CREATE INDEX IF NOT EXISTS ix_emanet_muv ON emanet_hareketleri(muvekkil_id);
CREATE TABLE IF NOT EXISTS durusma_notlari (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    dava_id INTEGER NOT NULL REFERENCES davalar(id) ON DELETE CASCADE,
    tarih TEXT DEFAULT (date('now')),
    ozet TEXT DEFAULT '',
    sonraki_durusma TEXT DEFAULT '',
    kesin_sure_gun INTEGER DEFAULT 0,
    kesin_sure_aciklama TEXT DEFAULT ''
);
CREATE INDEX IF NOT EXISTS ix_durnot_dava ON durusma_notlari(dava_id);
CREATE TABLE IF NOT EXISTS muvekkil_bilgilendirme (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    muvekkil_id INTEGER DEFAULT 0,
    dava_id INTEGER DEFAULT 0,
    tarih TEXT DEFAULT (date('now')),
    kanal TEXT DEFAULT '',
    konu TEXT DEFAULT '',
    ozet TEXT DEFAULT ''
);
CREATE INDEX IF NOT EXISTS ix_bilgi_dava ON muvekkil_bilgilendirme(dava_id);
CREATE TABLE IF NOT EXISTS zaman_kayitlari (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    dava_id INTEGER NOT NULL REFERENCES davalar(id) ON DELETE CASCADE,
    tarih TEXT DEFAULT (date('now')),
    dakika INTEGER DEFAULT 0,
    aciklama TEXT DEFAULT ''
);
CREATE INDEX IF NOT EXISTS ix_zaman_dava ON zaman_kayitlari(dava_id);
CREATE TABLE IF NOT EXISTS emsal_notlari (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    baslik TEXT NOT NULL,
    kaynak TEXT DEFAULT '',
    karar_tarihi TEXT DEFAULT '',
    konu TEXT DEFAULT '',
    ozet TEXT DEFAULT '',
    etiketler TEXT DEFAULT '',
    dava_id INTEGER DEFAULT 0,
    olusturma TEXT DEFAULT (date('now'))
);
CREATE TABLE IF NOT EXISTS dava_evraklari (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    dava_id INTEGER NOT NULL REFERENCES davalar(id) ON DELETE CASCADE,
    ad TEXT DEFAULT '',
    yol TEXT DEFAULT '',
    eklenme TEXT DEFAULT (date('now'))
);
CREATE INDEX IF NOT EXISTS ix_evrak_dava ON dava_evraklari(dava_id);
";

    public static readonly (string Ad, string Tip, string Metin)[] DefaultTemplates =
    {
        ("Avukatlık (Vekalet) Ücret Sözleşmesi", "docx",
            "AVUKATLIK ÜCRET SÖZLEŞMESİ\n\n" +
            "MÜVEKKİL: {{ad_soyad}} (T.C. {{tc_no}})\nAdres: {{adres}}\nTelefon: {{telefon}}\n\n" +
            "Konu: {{dosya_konusu}}\n\n" +
            "MADDE 1- Müvekkil, yukarıda belirtilen konuda kendisini temsil etmesi için avukata vekalet vermiştir.\n" +
            "MADDE 2- Taraflar vekalet ücretini {{toplam_ucret}} olarak kararlaştırmıştır. " +
            "Ödeme planı: {{odeme_plani}}\n" +
            "MADDE 3- Yargılama giderleri ve harçlar müvekkile aittir.\n" +
            "MADDE 4- İşbu sözleşme iki nüsha olarak {{tarih}} tarihinde imzalanmıştır.\n\n" +
            "MÜVEKKİL                                   AVUKAT\n{{ad_soyad}}"),
        ("İbraname", "docx",
            "İBRANAME\n\n" +
            "Ben {{ad_soyad}} (T.C. {{tc_no}}); {{dosya_konusu}} konusundaki tüm hak ve alacaklarımı " +
            "tam ve eksiksiz olarak aldığımı, bu nedenle karşı tarafı ve vekilini ibra ettiğimi, " +
            "herhangi bir talebimin kalmadığını beyan ve kabul ederim.\n\n{{tarih}}\n\n{{ad_soyad}}"),
    };
}

/// <summary>SQLite veri katmanı. Her thread kendi bağlantısını kullanır; mevcut Python veritabanıyla uyumludur.</summary>
public class Db
{
    public string Path { get; }
    readonly ThreadLocal<SqliteConnection?> _local = new(() => null);

    public Db(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        InitSchema();
    }

    public SqliteConnection Conn
    {
        get
        {
            var c = _local.Value;
            if (c == null)
            {
                c = new SqliteConnection($"Data Source={Path};Default Timeout=30;Pooling=False");
                c.Open();
                Exec(c, "PRAGMA foreign_keys = ON");
                Exec(c, "PRAGMA busy_timeout = 30000");
                Exec(c, "PRAGMA journal_mode = DELETE"); // ağ paylaşımında WAL güvenli değildir
                _local.Value = c;
            }
            return c;
        }
    }

    public void CloseThreadConn()
    {
        _local.Value?.Dispose();
        _local.Value = null;
    }

    static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // '?' yer tutucularını @p0, @p1 … olarak çevirir (tırnak içindekileri atlar)
    static string Bind(SqliteCommand cmd, string sql, object?[] ps)
    {
        var sb = new StringBuilder();
        int n = 0;
        bool q = false;
        foreach (var ch in sql)
        {
            if (ch == '\'') q = !q;
            if (ch == '?' && !q)
            {
                var name = "@p" + n;
                sb.Append(name);
                var v = n < ps.Length ? ps[n] : null;
                cmd.Parameters.AddWithValue(name, v switch
                {
                    null => DBNull.Value,
                    bool b => b ? 1L : 0L,
                    DateTime d => d.ToString("yyyy-MM-dd"),
                    _ => v,
                });
                n++;
            }
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    SqliteCommand Cmd(string sql, object?[] ps)
    {
        var cmd = Conn.CreateCommand();
        cmd.CommandText = Bind(cmd, sql, ps);
        return cmd;
    }

    /// <summary>Eski sürümlerden kalan şirket/maaş haczi verilerini ve tablolarını siler.</summary>
    void EskiSirketVerisiniTemizle()
    {
        try
        {
            foreach (var t in new[] { "icra_odemeleri", "icra_dosyalari", "personel" })
                Execute($"DROP TABLE IF EXISTS {t}");
            foreach (var t in new[] { "genel_tebligatlar", "uets_hesaplari" })
            {
                var sql = Convert.ToString(Scalar("SELECT sql FROM sqlite_master WHERE type='table' AND name=?", t)) ?? "";
                if (sql.Length == 0) continue;
                Execute($"DELETE FROM {t} WHERE hesap_tipi='Sirket'");
                if (sql.Contains("'Sirket'"))
                {
                    var yeniSql = System.Text.RegularExpressions.Regex.Replace(sql, @"\s*CHECK\s*\(\s*hesap_tipi\s+IN\s*\([^)]*\)\s*\)", "")
                        .Replace("DEFAULT 'Sirket'", "DEFAULT 'Sahsi'")
                        .Replace($"CREATE TABLE \"{t}\"", $"CREATE TABLE {t}_yeni").Replace($"CREATE TABLE {t}", $"CREATE TABLE {t}_yeni");
                    Execute($"DROP TABLE IF EXISTS {t}_yeni");
                    Execute(yeniSql);
                    Execute($"INSERT INTO {t}_yeni SELECT * FROM {t}");
                    Execute($"DROP TABLE {t}");
                    Execute($"ALTER TABLE {t}_yeni RENAME TO {t}");
                }
            }
            foreach (var kol in new[] { "muhasebe_email", "telegram_genel_chat_id" })
                if (Query("PRAGMA table_info(ayarlar)").Any(r => r.S("name") == kol))
                    Execute($"ALTER TABLE ayarlar DROP COLUMN {kol}");
        }
        catch (Exception) { }
    }

    public void InitSchema()
    {
        EskiSirketVerisiniTemizle();
        Exec(Conn, Schema.Sql);
        Execute("INSERT OR IGNORE INTO ayarlar (id) VALUES (1)");
        Execute("INSERT INTO tahsilat_hareketleri (finans_id, tarih, tutar, aciklama) " +
                "SELECT f.id, '', f.alinan_tutar, 'Önceki kayıt (devir)' FROM finans_tahsilat f " +
                "WHERE f.alinan_tutar>0 AND NOT EXISTS (SELECT 1 FROM tahsilat_hareketleri h WHERE h.finans_id=f.id)");
        if (Convert.ToInt64(Scalar("SELECT COUNT(*) FROM sablonlar")) == 0)
            foreach (var (ad, tip, metin) in Schema.DefaultTemplates)
                Execute("INSERT INTO sablonlar (sablon_adi, sablon_tipi, taslak_metin) VALUES (?,?,?)", ad, tip, metin);

        Execute("DELETE FROM sablonlar WHERE sablon_adi LIKE 'Maaş Haczi Cevap%'");
        if (!Query("PRAGMA table_info(sureler)").Any(r => r.S("name") == "tebligat_id"))
            Execute("ALTER TABLE sureler ADD COLUMN tebligat_id INTEGER DEFAULT 0");
        if (!Query("PRAGMA table_info(genel_tebligatlar)").Any(r => r.S("name") == "dava_id"))
            Execute("ALTER TABLE genel_tebligatlar ADD COLUMN dava_id INTEGER DEFAULT 0");
        if (!Query("PRAGMA table_info(finans_tahsilat)").Any(r => r.S("name") == "dava_id"))
            Execute("ALTER TABLE finans_tahsilat ADD COLUMN dava_id INTEGER DEFAULT 0");
        KolonEkle("davalar", "kapanis_tarihi", "TEXT DEFAULT ''");
        KolonEkle("davalar", "saklama_bitis", "TEXT DEFAULT ''");
        KolonEkle("davalar", "kapanis_notu", "TEXT DEFAULT ''");
        KolonEkle("muvekkiller", "kvkk_tarih", "TEXT DEFAULT ''");
        // Şema sürümü: ileride değişiklik gerekirse buradan geçiş adımları sürüme göre eklenir.
        if (ScalarL("PRAGMA user_version") < SemaSurumu) Execute($"PRAGMA user_version = {SemaSurumu}");
    }

    /// <summary>Şema sürümü. 1: ilk C# sürümü, 2: dosya kartı/emanet/denetim izi/duruşma notu/zaman/emsal/evrak/kapanış.</summary>
    public const int SemaSurumu = 2;

    void KolonEkle(string tablo, string kolon, string tanim)
    {
        if (!Query($"PRAGMA table_info({tablo})").Any(r => r.S("name") == kolon))
            Execute($"ALTER TABLE {tablo} ADD COLUMN {kolon} {tanim}");
    }

    /// <summary>SQLite bütünlük denetimi. "ok" dönerse sorun yok.</summary>
    public string SaglikKontrolu()
    {
        try { return Convert.ToString(Scalar("PRAGMA integrity_check")) ?? "bilinmiyor"; }
        catch (Exception e) { return e.Message; }
    }

    // --- CRUD yardımcıları --------------------------------------------------
    public List<Row> Query(string sql, params object?[] ps)
    {
        using var cmd = Cmd(sql, ps);
        using var rd = cmd.ExecuteReader();
        var list = new List<Row>();
        while (rd.Read())
        {
            var r = new Row();
            for (int i = 0; i < rd.FieldCount; i++)
                r[rd.GetName(i)] = rd.IsDBNull(i) ? null : rd.GetValue(i);
            list.Add(r);
        }
        return list;
    }

    public Row? One(string sql, params object?[] ps) => Query(sql, ps).FirstOrDefault();

    public object? Scalar(string sql, params object?[] ps)
    {
        using var cmd = Cmd(sql, ps);
        var v = cmd.ExecuteScalar();
        return v == DBNull.Value ? null : v;
    }

    public double ScalarD(string sql, params object?[] ps)
    {
        var v = Scalar(sql, ps);
        return v == null ? 0 : Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
    }

    public long ScalarL(string sql, params object?[] ps)
    {
        var v = Scalar(sql, ps);
        return v == null ? 0 : Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>INSERT/UPDATE/DELETE çalıştırır; son eklenen satır id'sini döndürür.</summary>
    public long Execute(string sql, params object?[] ps)
    {
        using (var cmd = Cmd(sql, ps)) cmd.ExecuteNonQuery();
        using var c2 = Conn.CreateCommand();
        c2.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt64(c2.ExecuteScalar());
    }

    public long Insert(string table, IDictionary<string, object?> data)
    {
        var cols = string.Join(", ", data.Keys);
        var marks = string.Join(", ", data.Keys.Select(_ => "?"));
        var id = Execute($"INSERT INTO {table} ({cols}) VALUES ({marks})", data.Values.ToArray());
        Audit(table, id, "Ekleme", string.Join(", ", data.Keys));
        return id;
    }

    public void Update(string table, long id, IDictionary<string, object?> data)
    {
        var sets = string.Join(", ", data.Keys.Select(k => k + "=?"));
        Execute($"UPDATE {table} SET {sets} WHERE id=?", data.Values.Append(id).ToArray());
        Audit(table, id, "Değişiklik", string.Join(", ", data.Keys));
    }

    public void Delete(string table, long id)
    {
        Execute($"DELETE FROM {table} WHERE id=?", id);
        Audit(table, id, "Silme", "");
    }

    // --- denetim izi: kim değil, ne/ne zaman (tek kullanıcılı program). Şifre içerebilecek ayarlar tablosu kaydedilmez.
    static readonly HashSet<string> AuditHaric = new() { "audit_log", "bildirim_log", "ayarlar" };

    void Audit(string table, long id, string islem, string ozet)
    {
        if (AuditHaric.Contains(table)) return;
        try
        {
            Execute("INSERT INTO audit_log (tablo, kayit_id, islem, ozet) VALUES (?,?,?,?)", table, id, islem,
                    ozet.Length > 200 ? ozet[..200] : ozet);
        }
        catch (Exception) { /* günlük yazılamazsa asıl işlem bozulmasın */ }
    }

    // --- ayarlar -------------------------------------------------------------
    public Row GetSettings() => One("SELECT * FROM ayarlar WHERE id=1") ?? new Row();
    public void SaveSettings(IDictionary<string, object?> data) => Update("ayarlar", 1, data);

    // --- yedek ---------------------------------------------------------------
    public string? Backup(string backupDir, int keep = 30)
    {
        try
        {
            Directory.CreateDirectory(backupDir);
            var target = System.IO.Path.Combine(backupDir, $"hukuk_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.db");
            using (var dst = new SqliteConnection($"Data Source={target};Pooling=False"))
            {
                dst.Open();
                Conn.BackupDatabase(dst);
            }
            var olds = Directory.GetFiles(backupDir, "hukuk_*.db").OrderBy(x => x, StringComparer.Ordinal).ToList();
            foreach (var old in olds.Take(Math.Max(0, olds.Count - keep)))
                try { File.Delete(old); } catch { }
            return target;
        }
        catch (Exception) { return null; }
    }

    public static string CopyFile(string src, string dstDir, string? newName = null)
    {
        Directory.CreateDirectory(dstDir);
        var dst = System.IO.Path.Combine(dstDir, newName ?? System.IO.Path.GetFileName(src));
        if (System.IO.Path.GetFullPath(src) != System.IO.Path.GetFullPath(dst))
            File.Copy(src, dst, true);
        return dst;
    }
}
