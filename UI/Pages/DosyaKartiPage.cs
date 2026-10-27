using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HukukErp.Core;
using HukukErp.Services;

namespace HukukErp.UI.Pages;

/// <summary>Dosya kartı: bir davaya bağlı her şey (aşamalar, süreler, tebligatlar, duruşma notları, müvekkil bilgilendirmeleri, zaman, tahsilat, masraf, emanet, evrak) tek ekranda ve zaman çizelgesinde.</summary>
public class DosyaKartiPage : BasePage
{
    /// <summary>Başka sayfalardan "Dosya Kartı" ile gelinirken açılacak dava.</summary>
    public static long Hedef;

    // zaman sayacı: sayfalar arası gezinirken de çalışmaya devam eder (program kapanırsa kaybolur)
    static long _sayacDava;
    static DateTime? _sayacBas;

    static readonly string[] Kanallar = { "Telefon", "WhatsApp", "SMS", "E-posta", "Yüz yüze", "Diğer" };

    readonly ComboBox _combo = new() { MinWidth = 420, Margin = new Thickness(0, 0, 8, 0) };
    readonly TextBlock _bilgi = Ui.Text("", 13, false, "text", true);
    readonly TextBlock _ozet = Ui.Text("", 13, true, "text", true);
    readonly DataTable _t = new(("Tarih", "tarih", Fmt.Date), ("Tür", "tur", null), ("Açıklama", "aciklama", null), ("Tutar / Süre", "tutar", null));
    readonly Button _sayacBtn;
    readonly DispatcherTimer _sayacTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    bool _busy;

    public DosyaKartiPage(AppCtx ctx) : base(ctx)
    {
        Top(Ui.PageHeader("Dosya Kartı", "Bir davaya bağlı her şey tek ekranda • evrakı pencereye sürükleyip bırakabilirsiniz"));
        _combo.SelectionChanged += (_, _) => { if (!_busy) Yukle(); };
        Top(Ui.HRow(Ui.Muted("Dava:", false), _combo, Ui.Btn("＋ Süreli İş", "Primary", SureEkle),
                    Ui.Btn("İş Listesi", "", () => Ctx.Navigate("is_listesi", null)), Ui.Btn("Dava Sayfası", "", () => Ctx.Navigate("dava", null)),
                    Ui.Btn("Emanet / Masraf", "", () => Ctx.Navigate("emanet", null))));
        _sayacBtn = Ui.Btn("▶ Sayaç Başlat", "", SayacDegistir);
        Top(Ui.HRow(Ui.Btn("⚖ Duruşma Notu", "Primary", DurusmaNotu), Ui.Btn("📞 Müvekkile Bildirdim", "", Bilgilendirme),
                    _sayacBtn, Ui.Btn("＋ Zaman", "", ZamanEkle), Ui.Btn("📎 Evrak Ekle…", "", EvrakSec),
                    Ui.Btn("🔒 Dosyayı Kapat / Aç", "", KapatAc), Ui.Btn("Seçili Kaydı Sil", "Danger", KayitSil)));
        var info = new Card();
        info.Top(_bilgi);
        info.Fill(_ozet);
        Top(info);
        Fill(_t);

        _t.MouseDoubleClick += (_, _) =>
        {
            var c = _t.Current;
            if (c != null && c.S("tur") == "Evrak" && c.S("yol").Length > 0 && File.Exists(c.S("yol"))) Ui.OpenExternal(c.S("yol"));
        };
        AllowDrop = true;
        PreviewDragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        PreviewDrop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] f) EvrakEkle(f);
            e.Handled = true;
        };
        _sayacTimer.Tick += (_, _) => SayacYaz();
        _sayacTimer.Start();
    }

    long Secili => (_combo.SelectedItem as ComboItem)?.Value is { } v ? Convert.ToInt64(v) : 0;

    public override void Refresh()
    {
        _busy = true;
        try
        {
            var onceki = Hedef > 0 ? Hedef : Secili;
            Hedef = 0;
            _combo.Items.Clear();
            foreach (var d in Db.Query("SELECT d.id, d.mahkeme_adi, d.esas_no, m.ad_soyad FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id ORDER BY d.id DESC"))
                _combo.Items.Add(new ComboItem { Value = d.L("id"), Label = $"{d.S("mahkeme_adi")} {d.S("esas_no")} — {d.S("ad_soyad")}".Trim(' ', '—', ' ') });
            var i = _combo.Items.Cast<ComboItem>().ToList().FindIndex(x => Convert.ToInt64(x.Value) == onceki);
            _combo.SelectedIndex = i >= 0 ? i : (_combo.Items.Count > 0 ? 0 : -1);
        }
        finally { _busy = false; }
        Yukle();
    }

    static Row Satir(string tarih, string tur, string aciklama, string tutar = "", string tablo = "", long kid = 0, string yol = "") => new()
    {
        ["tarih"] = tarih, ["tur"] = tur, ["aciklama"] = aciklama, ["tutar"] = tutar, ["tablo"] = tablo, ["kid"] = kid, ["yol"] = yol,
    };

    static string Sure(long dk) => dk >= 60 ? $"{dk / 60} sa {dk % 60} dk" : $"{dk} dk";

    void Yukle()
    {
        SayacYaz();
        var id = Secili;
        if (id == 0) { _bilgi.Text = "Dava seçin ya da önce 'Dava & Müvekkil Takibi'nde dava ekleyin."; _ozet.Text = ""; _t.Load(new List<Row>()); return; }
        var d = Db.One("SELECT d.*, m.ad_soyad, m.telefon FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id WHERE d.id=?", id);
        if (d == null) return;
        var kapali = d.S("kapanis_tarihi").Length > 0;
        _bilgi.Text = $"Müvekkil: {d.S("ad_soyad")} {(d.S("telefon").Length > 0 ? "• " + d.S("telefon") : "")}\n" +
                      $"Mahkeme: {d.S("mahkeme_adi")} • Esas: {d.S("esas_no")} • Tür: {d.S("dava_turu")}\n" +
                      $"Karşı taraf: {d.S("karsi_taraf")} • Aşama: {d.S("asama_durum")} • Duruşma: {Utils.FmtDate(d.S("durusma_tarihi"))}" +
                      (kapali ? $"\n🔒 DOSYA KAPALI: {Utils.FmtDate(d.S("kapanis_tarihi"))} • saklama bitişi: {Utils.FmtDate(d.S("saklama_bitis"))}" : "");

        var l = new List<Row>();
        foreach (var (k, ad) in new[] { ("dava_dilekcesi_tarih", "Dava dilekçesi"), ("cevap_dilekcesi_tarih", "Cevap dilekçesi"),
                                        ("cevaba_cevap_tarih", "Cevaba cevap"), ("ikinci_cevap_tarih", "İkinci cevap"), ("durusma_tarihi", "Duruşma") })
            if (d.S(k).Length > 0) l.Add(Satir(d.S(k), "Aşama", ad));
        if (kapali) l.Add(Satir(d.S("kapanis_tarihi"), "Kapanış", d.S("kapanis_notu")));
        foreach (var r in Db.Query("SELECT * FROM ajanda WHERE dava_id=?", id))
            l.Add(Satir(r.S("tarih"), r.S("tur").Length > 0 ? r.S("tur") : "Ajanda", $"{r.S("saat")} {r.S("baslik")}".Trim()));
        int acik = 0;
        foreach (var r in Db.Query("SELECT * FROM sureler WHERE dava_id=?", id))
        {
            if (r.S("durum") == "Açık") acik++;
            l.Add(Satir(r.S("son_tarih"), r.S("durum") == "Açık" ? "SÜRE (açık)" : "Süre (tamam)", $"{r.S("baslik")} • {r.S("dayanak")}"));
        }
        int teb = 0;
        foreach (var r in Db.Query("SELECT * FROM genel_tebligatlar WHERE dava_id=?", id))
        {
            teb++;
            l.Add(Satir(r.S("teblig_tarihi"), "Tebligat", $"{r.S("gonderen_kurum")} – {r.S("konu")} ({r.S("durum")})"));
        }
        foreach (var r in Db.Query("SELECT * FROM durusma_notlari WHERE dava_id=?", id))
            l.Add(Satir(r.S("tarih"), "Duruşma notu", r.S("ozet") + (r.S("sonraki_durusma").Length > 0 ? $" → sonraki: {Utils.FmtDate(r.S("sonraki_durusma"))}" : ""), "", "durusma_notlari", r.L("id")));
        foreach (var r in Db.Query("SELECT * FROM muvekkil_bilgilendirme WHERE dava_id=?", id))
            l.Add(Satir(r.S("tarih"), "Müvekkil bilgi", $"{r.S("kanal")}: {r.S("konu")} {r.S("ozet")}".Trim(), "", "muvekkil_bilgilendirme", r.L("id")));
        long dk = 0;
        foreach (var r in Db.Query("SELECT * FROM zaman_kayitlari WHERE dava_id=?", id))
        {
            dk += r.L("dakika");
            l.Add(Satir(r.S("tarih"), "Zaman", r.S("aciklama"), Sure(r.L("dakika")), "zaman_kayitlari", r.L("id")));
        }
        foreach (var r in Db.Query("SELECT * FROM dava_evraklari WHERE dava_id=?", id))
            l.Add(Satir(r.S("eklenme"), "Evrak", r.S("ad"), "", "dava_evraklari", r.L("id"), Workflows.DosyaYolu(Cfg, r.S("yol"))));
        foreach (var r in Db.Query("SELECT h.tarih, h.tutar, h.aciklama FROM tahsilat_hareketleri h JOIN finans_tahsilat f ON f.id=h.finans_id WHERE f.dava_id=?", id))
            l.Add(Satir(r.S("tarih"), "Tahsilat", r.S("aciklama"), Utils.FmtMoney(r.D("tutar"))));
        double masraf = 0;
        foreach (var r in Db.Query("SELECT * FROM dava_masraflari WHERE dava_id=?", id))
        {
            masraf += r.D("tutar");
            l.Add(Satir(r.S("tarih"), "Masraf", r.S("masraf_kalemi"), Utils.FmtMoney(r.D("tutar"))));
        }
        double emanet = 0;
        foreach (var r in Db.Query("SELECT * FROM emanet_hareketleri WHERE dava_id=?", id))
        {
            emanet += r.S("tur") == "Giriş" ? r.D("tutar") : -r.D("tutar");
            l.Add(Satir(r.S("tarih"), "Emanet " + r.S("tur"), r.S("aciklama"), Utils.FmtMoney(r.D("tutar"))));
        }
        var f = Db.One("SELECT SUM(toplam_ucret) t, SUM(alinan_tutar) a, SUM(kalan_bakiye) k FROM finans_tahsilat WHERE dava_id=?", id);
        _ozet.Text = $"Açık süre: {acik} • Tebligat: {teb} • Ücret: {Utils.FmtMoney(f?.D("t") ?? 0)} • Alınan: {Utils.FmtMoney(f?.D("a") ?? 0)} • Kalan: {Utils.FmtMoney(f?.D("k") ?? 0)}\n" +
                     $"Dosya masrafı: {Utils.FmtMoney(masraf)} • Emanet bakiyesi: {Utils.FmtMoney(emanet)} • Harcanan zaman: {Sure(dk)}";

        l = l.OrderByDescending(x => x.S("tarih")).ToList();
        _t.Load(l, new() { ["tur"] = r => r.S("tur").StartsWith("SÜRE") ? "danger" : r.S("tur") == "Duruşma" ? "warn" : "" });
    }

    // ------------------------------------------------------------------ süreli iş
    void SureEkle()
    {
        var id = Secili;
        if (id == 0) { Ui.Info(this, "Önce bir dava seçin."); return; }
        var h = IsListesiPage.KatalogHazirla(this, Db, null);
        if (h == null) return;
        h["dava_id"] = id;
        var v = IsListesiPage.Sor(this, Db, h);
        if (v == null) return;
        Db.Insert("sureler", v);
        Yukle();
    }

    // ------------------------------------------------------------------ duruşma notu
    void DurusmaNotu()
    {
        var id = Secili;
        if (id == 0) { Ui.Info(this, "Önce bir dava seçin."); return; }
        var v = FormDialog.Ask(this, "Duruşma Notu", new List<Field>
        {
            new("tarih", "Duruşma tarihi", "date", def: Utils.TodayIso()),
            new("ozet", "Duruşmada ne oldu?", "multiline", required: true),
            new("sonraki_durusma", "Sonraki duruşma tarihi", "date"),
            new("kesin_sure_gun", "Verilen kesin süre (gün) – yoksa 0", "int", def: 0),
            new("kesin_sure_aciklama", "Kesin sürenin konusu (ör. bilirkişi ücreti, belge sunma)"),
            new("metin", "Müvekkil bilgilendirme metnini panoya kopyala", "check", def: 1L),
        });
        if (v == null) return;
        var tarih = Convert.ToString(v["tarih"]) ?? "";
        var ozet = Convert.ToString(v["ozet"]) ?? "";
        var sonraki = Convert.ToString(v["sonraki_durusma"]) ?? "";
        int gun = Convert.ToInt32(v["kesin_sure_gun"] ?? 0);
        var aciklama = Convert.ToString(v["kesin_sure_aciklama"]) ?? "";
        Db.Insert("durusma_notlari", new Dictionary<string, object?>
        {
            ["dava_id"] = id, ["tarih"] = tarih, ["ozet"] = ozet, ["sonraki_durusma"] = sonraki,
            ["kesin_sure_gun"] = gun, ["kesin_sure_aciklama"] = aciklama,
        });
        if (sonraki.Length > 0) Db.Update("davalar", id, new Dictionary<string, object?> { ["durusma_tarihi"] = sonraki });
        string sureMetni = "";
        var bas = Utils.ParseDate(tarih);
        if (gun > 0 && bas != null)
        {
            var son = IsSureleri.IsGunuyaKaydir(bas.Value.AddDays(gun));
            Db.Insert("sureler", new Dictionary<string, object?>
            {
                ["baslik"] = "Kesin süre – " + (aciklama.Length > 0 ? aciklama : "duruşma tutanağı"),
                ["dayanak"] = $"HMK 94 • duruşma tutanağı ({Utils.FmtDate(tarih)})", ["sure_turu"] = "Mahkeme Verdiği Süre",
                ["baslangic"] = tarih, ["son_tarih"] = Utils.Iso(son), ["dava_id"] = id, ["durum"] = "Açık",
                ["notlar"] = $"{gun} gün kesin süre. Tutanaktaki süre başlangıcını ve adli tatil etkisini kontrol edin.",
            });
            sureMetni = $"\n\nSüreli iş açıldı: son gün {son:dd.MM.yyyy}.";
        }
        if (Convert.ToInt64(v["metin"] ?? 0) == 1)
        {
            var d = Db.One("SELECT d.mahkeme_adi, d.esas_no, m.ad_soyad FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id WHERE d.id=?", id);
            var metin = $"Sayın {d?.S("ad_soyad")},\n{d?.S("mahkeme_adi")} {d?.S("esas_no")} sayılı dosyanızın {Utils.FmtDate(tarih)} tarihli duruşması yapılmıştır.\n{ozet}\n" +
                        (sonraki.Length > 0 ? $"Bir sonraki duruşma tarihi: {Utils.FmtDate(sonraki)}.\n" : "") +
                        (gun > 0 ? $"Mahkemece {gun} gün süre verilmiştir{(aciklama.Length > 0 ? " (" + aciklama + ")" : "")}.\n" : "") +
                        "Bilgilerinize sunarım.";
            try { Clipboard.SetText(metin); sureMetni += "\nMüvekkil bilgilendirme metni panoya kopyalandı."; } catch (Exception) { }
        }
        Yukle();
        if (sureMetni.Length > 0) Ui.Info(this, "Duruşma notu kaydedildi." + sureMetni);
    }

    // ------------------------------------------------------------------ müvekkil bilgilendirme kaydı
    void Bilgilendirme()
    {
        var id = Secili;
        if (id == 0) { Ui.Info(this, "Önce bir dava seçin."); return; }
        var v = FormDialog.Ask(this, "Müvekkile Bilgi Verildi", new List<Field>
        {
            new("tarih", "Tarih", "date", def: Utils.TodayIso()),
            new("kanal", "Kanal", "combo", Field.Of(Kanallar), def: "Telefon"),
            new("konu", "Konu", required: true),
            new("ozet", "Ayrıntı", "multiline"),
        });
        if (v == null) return;
        var mid = Db.One("SELECT muvekkil_id FROM davalar WHERE id=?", id)?.L("muvekkil_id") ?? 0;
        v["dava_id"] = id; v["muvekkil_id"] = mid;
        Db.Insert("muvekkil_bilgilendirme", v);
        Yukle();
    }

    // ------------------------------------------------------------------ zaman
    void SayacYaz()
    {
        if (_sayacBas == null) { _sayacBtn.Content = "▶ Sayaç Başlat"; return; }
        var dk = (long)(DateTime.Now - _sayacBas.Value).TotalMinutes;
        _sayacBtn.Content = _sayacDava == Secili ? $"■ Durdur ({Sure(dk)})" : $"■ Başka dosyada çalışıyor ({Sure(dk)})";
    }

    void SayacDegistir()
    {
        var id = Secili;
        if (id == 0) { Ui.Info(this, "Önce bir dava seçin."); return; }
        if (_sayacBas == null)
        {
            _sayacBas = DateTime.Now; _sayacDava = id;
            SayacYaz();
            return;
        }
        var dk = Math.Max(1, (long)Math.Round((DateTime.Now - _sayacBas.Value).TotalMinutes));
        var hedef = _sayacDava;
        var v = FormDialog.Ask(this, $"Sayaç durdu: {Sure(dk)}", new List<Field>
        {
            new("dakika", "Süre (dakika) – gerekirse düzeltin", "int", def: (int)dk),
            new("aciklama", "Ne yapıldı?"),
        });
        if (v == null) return;   // vazgeçilirse sayaç çalışmaya devam eder
        _sayacBas = null; _sayacDava = 0;
        Db.Insert("zaman_kayitlari", new Dictionary<string, object?>
        {
            ["dava_id"] = hedef, ["tarih"] = Utils.TodayIso(), ["dakika"] = v["dakika"], ["aciklama"] = v["aciklama"],
        });
        Yukle();
    }

    void ZamanEkle()
    {
        var id = Secili;
        if (id == 0) { Ui.Info(this, "Önce bir dava seçin."); return; }
        var v = FormDialog.Ask(this, "Zaman Kaydı", new List<Field>
        {
            new("tarih", "Tarih", "date", def: Utils.TodayIso()),
            new("dakika", "Süre (dakika)", "int", def: 30, required: true),
            new("aciklama", "Ne yapıldı?"),
        });
        if (v == null) return;
        v["dava_id"] = id;
        Db.Insert("zaman_kayitlari", v);
        Yukle();
    }

    // ------------------------------------------------------------------ evrak
    void EvrakSec()
    {
        if (Secili == 0) { Ui.Info(this, "Önce bir dava seçin."); return; }
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Dosyaya eklenecek evrak", Multiselect = true };
        if (dlg.ShowDialog() == true) EvrakEkle(dlg.FileNames);
    }

    void EvrakEkle(string[] dosyalar)
    {
        var id = Secili;
        if (id == 0) { Ui.Info(this, "Önce bir dava seçin."); return; }
        int n = 0;
        var klasor = Cfg.Subdir("Dosyalar", "Dava", id.ToString());
        foreach (var f in dosyalar.Where(File.Exists))
        {
            var yeni = Db.CopyFile(f, klasor, $"{DateTime.Now:yyyyMMdd_HHmmss}_{Path.GetFileName(f)}");
            Db.Insert("dava_evraklari", new Dictionary<string, object?>
            {
                ["dava_id"] = id, ["ad"] = Path.GetFileName(f), ["yol"] = Path.GetRelativePath(Cfg.DataDir, yeni),
            });
            n++;
        }
        if (n > 0) { Yukle(); Ctx.Status($"{n} evrak eklendi."); }
    }

    // ------------------------------------------------------------------ kapanış
    void KapatAc()
    {
        var id = Secili;
        if (id == 0) { Ui.Info(this, "Önce bir dava seçin."); return; }
        var d = Db.One("SELECT * FROM davalar WHERE id=?", id);
        if (d == null) return;
        if (d.S("kapanis_tarihi").Length > 0)
        {
            if (!Ui.Confirm(this, "Dosya yeniden açılsın mı? Kapanış ve saklama bilgisi silinir.")) return;
            Db.Update("davalar", id, new Dictionary<string, object?>
            {
                ["kapanis_tarihi"] = "", ["saklama_bitis"] = "", ["kapanis_notu"] = "", ["asama_durum"] = "Karar Aşaması",
            });
            Yukle();
            return;
        }
        var sorunlar = new List<string>();
        var acik = Db.ScalarL("SELECT COUNT(*) FROM sureler WHERE dava_id=? AND durum='Açık'", id);
        if (acik > 0) sorunlar.Add($"{acik} açık süreli iş var");
        var kalan = Db.ScalarD("SELECT COALESCE(SUM(kalan_bakiye),0) FROM finans_tahsilat WHERE dava_id=?", id);
        if (kalan > 0.009) sorunlar.Add($"tahsil edilmemiş vekalet ücreti: {Utils.FmtMoney(kalan)}");
        var em = Db.ScalarD("SELECT COALESCE(SUM(CASE WHEN tur='Giriş' THEN tutar ELSE -tutar END),0) FROM emanet_hareketleri WHERE dava_id=?", id);
        if (Math.Abs(em) > 0.009) sorunlar.Add($"emanet bakiyesi sıfır değil: {Utils.FmtMoney(em)}");
        if (sorunlar.Count > 0 && !Ui.Confirm(this, "Kapatmadan önce dikkat:\n• " + string.Join("\n• ", sorunlar) + "\n\nYine de dosya kapatılsın mı?")) return;

        var v = FormDialog.Ask(this, "Dosyayı Kapat", new List<Field>
        {
            new("kapanis_tarihi", "Kapanış tarihi", "date", def: Utils.TodayIso(), required: true),
            new("yil", "Saklama süresi (yıl) – mevzuatınıza göre belirleyin", "int", def: 10, required: true),
            new("k1", "Vekalet ücreti ve masraflar tamamlandı", "check", def: sorunlar.Count == 0 ? 1L : 0L),
            new("k2", "Emanet bakiyesi kapatıldı / iade edildi", "check", def: Math.Abs(em) < 0.01 ? 1L : 0L),
            new("k3", "Asıl evraklar müvekkile iade edildi", "check"),
            new("k4", "Müvekkile kapanış bilgisi verildi", "check"),
            new("not", "Kapanış notu (sonuç, kesinleşme tarihi vb.)", "multiline"),
        });
        if (v == null) return;
        var eksik = new[] { ("k1", "ücret/masraf"), ("k2", "emanet"), ("k3", "evrak iadesi"), ("k4", "müvekkil bilgisi") }
            .Where(x => Convert.ToInt64(v[x.Item1] ?? 0) != 1).Select(x => x.Item2).ToList();
        var kt = Utils.ParseDate(Convert.ToString(v["kapanis_tarihi"])) ?? DateTime.Today;
        var not = Convert.ToString(v["not"]) ?? "";
        if (eksik.Count > 0) not += (not.Length > 0 ? "\n" : "") + "Kapanışta tamamlanmayanlar: " + string.Join(", ", eksik);
        Db.Update("davalar", id, new Dictionary<string, object?>
        {
            ["kapanis_tarihi"] = Utils.Iso(kt), ["saklama_bitis"] = Utils.Iso(kt.AddYears(Math.Max(1, Convert.ToInt32(v["yil"] ?? 10)))),
            ["kapanis_notu"] = not, ["asama_durum"] = "Karara Çıktı",
        });
        Yukle();
    }

    void KayitSil()
    {
        var c = _t.Current;
        if (c == null || c.S("tablo").Length == 0) { Ui.Info(this, "Silinebilen bir kayıt seçin (duruşma notu, müvekkil bilgi, zaman, evrak)."); return; }
        var ek = c.S("tablo") == "dava_evraklari" ? "\n(Dosyanın kendisi diskte kalır.)" : "";
        if (!Ui.Confirm(this, "Seçili kayıt silinsin mi?" + ek)) return;
        Db.Delete(c.S("tablo"), c.L("kid"));
        Yukle();
    }
}
