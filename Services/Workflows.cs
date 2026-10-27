using System.Text.RegularExpressions;
using HukukErp.Core;

namespace HukukErp.Services;

/// <summary>İş akışları: UETS evrak yönlendirme, vekaletname arşivi, hatırlatmalar.</summary>
public static class Workflows
{
    public const string QAvukat = "Avukat Dava Tebligatları Kuyruğu";

    // --------------------------------------------------------------- A. UETS yönlendirme
    /// <summary>PDF'i arşivler, ayrıştırır, kuyruğa yönlendirir. Tekrarlıysa null döner.
    /// Bireysel (Sahsi) hesaplar → 'Avukat Dava Tebligatları Kuyruğu'.</summary>
    public static long? IngestPdf(Db db, Config cfg, string pdf, Row hesap, bool bildir = true, string? teslimIso = null)
    {
        var h = Parser.FileHash(pdf);
        if (db.One("SELECT id FROM genel_tebligatlar WHERE dosya_hash=?", h) != null) return null;

        var tip = hesap.S("hesap_tipi");
        var klasor = cfg.Subdir("Dosyalar", "UETS", tip, Utils.SlugAd(hesap.S("hesap_adi")));
        var kayit = Db.CopyFile(pdf, klasor, $"{Utils.TodayIso()}_{h[..8]}_{Path.GetFileName(pdf)}");

        var text = TebligatDosya.Metin(kayit);
        var ayr = Parser.Ayristir(text, 7, teslimIso, Utils.Iso(File.GetLastWriteTime(pdf)));

        var durum = QAvukat;
        var konu = ayr.Tur.Length > 0 ? ayr.Tur : (TebligatDosya.Resim(kayit) ? "Tebligat (resim)" : "Tebligat");

        var tid = db.Insert("genel_tebligatlar", new Dictionary<string, object?>
        {
            ["gonderen_kurum"] = ayr.Kurum, ["konu"] = konu, ["esas_no"] = ayr.EsasNo,
            ["teblig_tarihi"] = ayr.TebligTarihi.Length > 0 ? ayr.TebligTarihi : Utils.TodayIso(),
            ["son_cevap_tarihi"] = ayr.SonCevapTarihi, ["durum"] = durum, ["pdf_yolu"] = kayit,
            ["uets_hesap_adi"] = hesap.S("hesap_adi"), ["hesap_tipi"] = tip, ["dosya_hash"] = h,
            ["ayrisan_json"] = ayr.ToJson(), ["dava_id"] = DosyaEsleme.DavaBul(db, ayr.Kurum, ayr.EsasNo),
        });

        if (bildir)
        {
            var s = db.GetSettings();
            var msg = $"📬 Yeni UETS tebligatı\nHesap: {hesap.S("hesap_adi")} ({tip})\nKurum: {(ayr.Kurum.Length > 0 ? ayr.Kurum : "-")}\n" +
                      $"Konu: {konu}\nEsas: {(ayr.EsasNo.Length > 0 ? ayr.EsasNo : "-")}\nSon cevap: {(Utils.FmtDate(ayr.SonCevapTarihi) is { Length: > 0 } f ? f : "-")}";
            try
            {
                Notify.SendPersonal(s, msg, kayit);
            }
            catch (NotifyError) { }
        }
        return tid;
    }

    /// <summary>Eski kayıtlardaki eksik/hatalı kurum adlarını PDF'ten yeniden okur (bir kez çalışır).</summary>
    public static int KurumlariYenidenOku(Db db, Config cfg)
    {
        const int SURUM = 4;
        if (cfg.GetInt("kurum_okuma_surumu", 0) >= SURUM) return 0;
        int n = 0;
        foreach (var r in db.Query("SELECT id, pdf_yolu, gonderen_kurum FROM genel_tebligatlar WHERE pdf_yolu!=''"))
        {
            try
            {
                var yol = DosyaYolu(cfg, r.S("pdf_yolu"));
                if (!File.Exists(yol)) continue;
                var kurum = Parser.KurumBul(Parser.PdfText(yol));
                if (kurum.Length > 0 && kurum != r.S("gonderen_kurum"))
                {
                    db.Update("genel_tebligatlar", r.L("id"), new Dictionary<string, object?> { ["gonderen_kurum"] = kurum });
                    n++;
                }
            }
            catch (Exception) { }
        }
        cfg.Set("kurum_okuma_surumu", SURUM);
        return n;
    }

    // --------------------------------------------------------------- B. Vekaletname arşivi
    public static string VekaletnameKaydet(Config cfg, string adSoyad, string kaynakPdf)
    {
        var hedef = cfg.Subdir("Dosyalar", "Vekaletnameler");
        var yeni = Db.CopyFile(kaynakPdf, hedef, $"{Utils.SlugAd(adSoyad)}_Vekaletname_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
        var root = Path.GetFullPath(cfg.DataDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(yeni).StartsWith(root, StringComparison.OrdinalIgnoreCase) ? Path.GetRelativePath(cfg.DataDir, yeni) : yeni;
    }

    public static string DosyaYolu(Config cfg, string kayitli) =>
        Path.IsPathRooted(kayitli) ? kayitli : Path.Combine(cfg.DataDir, kayitli);

    // --------------------------------------------------------------- C/D. Finans & görev hatırlatmaları (SADECE kişisel Telegram)
    public static double Bakiye(double toplam, double alinan) => Math.Round(toplam - alinan, 2);

    public static void TahsilatYenidenHesapla(Db db, long finansId)
    {
        var alinan = db.ScalarD("SELECT SUM(tutar) FROM tahsilat_hareketleri WHERE finans_id=?", finansId);
        var f = db.One("SELECT toplam_ucret FROM finans_tahsilat WHERE id=?", finansId);
        if (f != null)
            db.Update("finans_tahsilat", finansId, new Dictionary<string, object?>
            {
                ["alinan_tutar"] = Math.Round(alinan, 2), ["kalan_bakiye"] = Bakiye(f.D("toplam_ucret"), alinan),
            });
    }

    public static int HatirlatmalariGonder(Db db, Config cfg)
    {
        var s = db.GetSettings();
        if (s.S("telegram_bot_token").Length == 0 || s.S("telegram_kisisel_chat_id").Length == 0) return 0;
        var bugun = Utils.TodayIso();
        int sent = 0;
        int tg = cfg.GetInt("tahsilat_hatirlatma_gun", 3), gg = cfg.GetInt("gorev_hatirlatma_gun", 2);

        foreach (var r in db.Query("SELECT f.*, m.ad_soyad FROM finans_tahsilat f JOIN muvekkiller m ON m.id=f.muvekkil_id WHERE f.kalan_bakiye>0 AND f.sonraki_tahsilat_tarihi!=''"))
        {
            var k = Utils.DaysLeft(r.S("sonraki_tahsilat_tarihi"));
            if (k == null || k > tg) continue;
            if (db.One("SELECT 1 FROM bildirim_log WHERE tur='tahsilat' AND ref_id=? AND gun=?", r.L("id"), bugun) != null) continue;
            var durum = k < 0 ? "GECİKMİŞ" : (k == 0 ? "BUGÜN" : $"{k} gün kaldı");
            try
            {
                Notify.SendPersonal(s, $"💰 Tahsilat ({durum})\n{r.S("ad_soyad")} - {r.S("dosya_konusu")}\nKalan: {Utils.FmtMoney(r.D("kalan_bakiye"))}\nVade: {Utils.FmtDate(r.S("sonraki_tahsilat_tarihi"))}");
                db.Execute("INSERT OR IGNORE INTO bildirim_log (tur, ref_id, gun) VALUES ('tahsilat',?,?)", r.L("id"), bugun);
                sent++;
            }
            catch (NotifyError) { return sent; }
        }
        foreach (var r in db.Query("SELECT * FROM gorevler WHERE durum!='Tamamlandı' AND son_tarih!=''"))
        {
            var k = Utils.DaysLeft(r.S("son_tarih"));
            if (k == null || k > gg) continue;
            if (db.One("SELECT 1 FROM bildirim_log WHERE tur='gorev' AND ref_id=? AND gun=?", r.L("id"), bugun) != null) continue;
            var durum = k < 0 ? "GECİKMİŞ" : (k == 0 ? "BUGÜN" : $"{k} gün kaldı");
            try
            {
                Notify.SendPersonal(s, $"✅ Görev ({durum}) [{r.S("oncelik")}]\n{r.S("baslik")}\nSon tarih: {Utils.FmtDate(r.S("son_tarih"))}");
                db.Execute("INSERT OR IGNORE INTO bildirim_log (tur, ref_id, gun) VALUES ('gorev',?,?)", r.L("id"), bugun);
                sent++;
            }
            catch (NotifyError) { return sent; }
        }
        // --- süreli işler: 3 gün, 1 gün, aynı gün ve geçtikten sonra 7 güne kadar her gün
        foreach (var r in db.Query("SELECT sr.*, TRIM(COALESCE(d.mahkeme_adi,'') || ' ' || COALESCE(d.esas_no,'')) AS dava FROM sureler sr LEFT JOIN davalar d ON d.id=sr.dava_id WHERE sr.durum='Açık'"))
        {
            var k = Utils.DaysLeft(r.S("son_tarih"));
            if (k == null || !(k == 3 || k == 1 || k == 0 || (k < 0 && k >= -7))) continue;
            var durum = k < 0 ? $"{-k} GÜN GEÇTİ" : (k == 0 ? "BUGÜN SON GÜN" : $"{k} gün kaldı");
            var dosya = r.S("dava").Length > 0 ? $"\nDosya: {r.S("dava")}" : "";
            var dayanak = r.S("dayanak").Length > 0 ? $"\nDayanak: {r.S("dayanak")}" : "";
            var sonuc = Bildir(db, s, "sure", r.L("id"), bugun, $"⏱ SÜRE ({durum})\n{r.S("baslik")}\nSon gün: {Utils.FmtDate(r.S("son_tarih"))}{dayanak}{dosya}");
            if (sonuc < 0) return sent;
            sent += sonuc;
        }
        // --- duruşmalar: 7, 3, 1 gün ve aynı gün
        foreach (var r in db.Query("SELECT d.*, m.ad_soyad FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id WHERE d.durusma_tarihi!='' AND d.asama_durum!='Karara Çıktı'"))
        {
            var k = Utils.DaysLeft(r.S("durusma_tarihi"));
            if (k == null || !(k == 7 || k == 3 || k == 1 || k == 0)) continue;
            var durum = k == 0 ? "BUGÜN" : $"{k} gün kaldı";
            var sonuc = Bildir(db, s, "durusma", r.L("id"), bugun, $"⚖ DURUŞMA ({durum})\n{r.S("mahkeme_adi")} {r.S("esas_no")}\nMüvekkil: {r.S("ad_soyad")}\nTarih: {Utils.FmtDate(r.S("durusma_tarihi"))}");
            if (sonuc < 0) return sent;
            sent += sonuc;
        }
        // --- açılmamış e-tebligat: otomatik tebliğ (kayıtlı tarih + 5 gün) yaklaşıyor
        foreach (var r in db.Query("SELECT * FROM genel_tebligatlar WHERE durum=?", QAvukat))
        {
            var t = Utils.ParseDate(r.S("teblig_tarihi"));
            if (t == null) continue;
            var son = t.Value.AddDays(IsSureleri.ETebligatGunu);
            var k = (son.Date - DateTime.Today).Days;
            if (k != 1 && k != 0) continue;
            var sonuc = Bildir(db, s, "ototeb", r.L("id"), bugun, $"📬 Tebligat {(k == 0 ? "BUGÜN" : "yarın")} tebliğ edilmiş sayılacak\n{r.S("gonderen_kurum")} {r.S("esas_no")}\n{r.S("konu")}\nOtomatik tebliğ: {son:dd.MM.yyyy}\nSüre bu tarihten işler; henüz süreli iş eklemediyseniz ekleyin.");
            if (sonuc < 0) return sent;
            sent += sonuc;
        }
        // --- kapalı dosyaların saklama süresi: 30 gün kala ve bittiği gün
        foreach (var r in db.Query("SELECT * FROM davalar WHERE saklama_bitis!=''"))
        {
            var k = Utils.DaysLeft(r.S("saklama_bitis"));
            if (k == null || !(k == 30 || k == 0)) continue;
            var sonuc = Bildir(db, s, "saklama", r.L("id"), bugun, $"🗄 Dosya saklama süresi {(k == 0 ? "BUGÜN doluyor" : "30 gün sonra doluyor")}\n{r.S("mahkeme_adi")} {r.S("esas_no")}\nSaklama bitişi: {Utils.FmtDate(r.S("saklama_bitis"))}\nİmha/iade kararı için dosyayı gözden geçirin.");
            if (sonuc < 0) return sent;
            sent += sonuc;
        }
        return sent;
    }

    /// <summary>Günde bir kez bildirim. 1: gönderildi, 0: bugün zaten gönderilmişti, -1: gönderilemedi.</summary>
    static int Bildir(Db db, Row ayar, string tur, long refId, string gun, string mesaj)
    {
        if (db.One("SELECT 1 FROM bildirim_log WHERE tur=? AND ref_id=? AND gun=?", tur, refId, gun) != null) return 0;
        try
        {
            Notify.SendPersonal(ayar, mesaj);
            db.Execute("INSERT OR IGNORE INTO bildirim_log (tur, ref_id, gun) VALUES (?,?,?)", tur, refId, gun);
            return 1;
        }
        catch (NotifyError) { return -1; }
    }
}
