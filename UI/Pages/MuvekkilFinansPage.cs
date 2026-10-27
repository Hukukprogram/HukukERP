using System.Windows;
using System.Windows.Controls;
using HukukErp.Core;
using HukukErp.Services;
using Microsoft.Data.Sqlite;

namespace HukukErp.UI.Pages;

/// <summary>Müvekkiller & Finansal Takip (iki sekme).</summary>
public class MuvekkilFinansPage : BasePage
{
    readonly TabControl _tabs = new();
    readonly DataTable _tM = new(("T.C. No", "tc_no", null), ("Ad Soyad", "ad_soyad", null), ("Telefon", "telefon", null), ("E-posta", "eposta", null),
        ("Vekaletname", "vek", null), ("Kayıt", "kayit_tarihi", Fmt.Date));
    readonly DataTable _tF = new(("Müvekkil", "etiket", null), ("Dava Dosyası", "dava_etiket", null), ("Dosya Konusu", "dosya_konusu", null), ("Toplam Ücret", "toplam_ucret", Fmt.Money),
        ("Alınan", "alinan_tutar", Fmt.Money), ("Kalan Bakiye", "kalan_bakiye", Fmt.Money), ("Sonraki Tahsilat", "sonraki_tahsilat_tarihi", Fmt.Date),
        ("Makbuz", "makbuz_kesildi", v => v != null && Convert.ToInt64(v) != 0 ? "Kesildi" : "Bekliyor"));
    readonly DataTable _tV = new(("Tarih", "tarih", Fmt.Date), ("Tür", "tur", null), ("Bağlı Dava", "dava", null), ("Açıklama", "aciklama", null), ("Dosya", "dosya", null));
    readonly TextBlock _vTitle = Ui.H2("Vekaletnameler — müvekkil seçin");
    readonly TextBlock _fSum = Ui.Muted("");
    readonly TextBlock _hTitle = Ui.H2("Tahsilat hareketleri — cari kaydı seçin");
    readonly DataTable _tH = new(("Tahsilat Tarihi", "tarih", Fmt.Date), ("Tutar", "tutar", Fmt.Money), ("Açıklama", "aciklama", null));
    readonly TextBlock _hSum = Ui.Muted("");
    // gruplama: müvekkile göre / dava dosyasına göre
    readonly DataTable _tGM = new(("Müvekkil", "ad", null), ("Dosya", "dosya", null), ("Toplam", "toplam", Fmt.Money), ("Alınan", "alinan", Fmt.Money),
        ("Masraf", "masraf", Fmt.Money), ("Kalan", "kalan", Fmt.Money));
    readonly DataTable _tGD = new(("Dava Dosyası", "ad", null), ("Müvekkil", "muv", null), ("Toplam", "toplam", Fmt.Money), ("Alınan", "alinan", Fmt.Money),
        ("Masraf", "masraf", Fmt.Money), ("Kalan", "kalan", Fmt.Money));
    readonly ContentControl _grpHost = new();
    Button? _bModM, _bModD;
    bool _modDava;
    bool _busy;

    public MuvekkilFinansPage(AppCtx ctx) : base(ctx)
    {
        Top(Ui.PageHeader("Müvekkiller & Finansal Takip"));
        _tabs.Items.Add(Ui.Tab("Müvekkil Kartları & Vekaletnameler", BuildMuvekkil()));
        _tabs.Items.Add(Ui.Tab("Cari Hesap & Tahsilat", BuildFinans()));
        _tabs.SelectionChanged += (o, e) => { if (e.OriginalSource == _tabs) Refresh(); };
        Fill(_tabs);
        _tF.SelectionChanged += (_, _) => { if (!_busy) LoadHareket(); };
        _tM.SelectionChanged += (_, _) => { if (!_busy) LoadVekalet(); };
        _tV.MouseDoubleClick += (_, _) => VekAc();
        _tGM.SelectionChanged += (_, _) => { if (!_busy && !_modDava) { LoadCari(); LoadHareket(); } };
        _tGD.SelectionChanged += (_, _) => { if (!_busy && _modDava) { LoadCari(); LoadHareket(); } };
    }

    public void YeniMuvekkil() => AddM();

    public void TahsilatEkle()
    {
        _tabs.SelectedIndex = 1;
        Refresh();
        if (_tF.Current == null && _tF.Rows.Count > 0) _tF.SelectFirst();
        if (_tF.Current != null) Tahsilat(); else AddF();
    }

    // ------------------------------------------------------------ müvekkil
    UIElement BuildMuvekkil()
    {
        var top = new DockPanel();
        var b = Ui.HRow(Ui.Btn("＋ Müvekkil", "Primary", AddM), Ui.Btn("Düzenle", "", EditM), Ui.Btn("Sil", "Danger", DelM));
        b.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(b, Dock.Bottom);
        top.Children.Add(b); top.Children.Add(_tM);

        var bot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(_vTitle, Dock.Top); _vTitle.Margin = new Thickness(0, 0, 0, 6);
        var vb = Ui.HRow(Ui.Btn("＋ Vekalet Ekle", "Ok", VekEkle), Ui.Btn("Aç", "", VekAc), Ui.Btn("Düzenle", "", VekDuzenle), Ui.Btn("Sil", "Danger", VekSil));
        vb.Margin = new Thickness(0, 6, 0, 0);
        DockPanel.SetDock(vb, Dock.Bottom);
        bot.Children.Add(_vTitle); bot.Children.Add(vb); bot.Children.Add(_tV);
        return Ui.VSplit(top, bot, 3, 2);
    }

    // ---- vekaletnameler (müvekkil başına birden fazla)
    static readonly string[] VekTurleri = { "Genel vekaletname", "Özel vekaletname", "Dava vekaletnamesi", "Diğer" };

    List<Field> VFields(long muvekkilId)
    {
        var dv = new List<(object? Value, string Label)> { (0L, "— Dava seçilmedi —") };
        foreach (var d in Db.Query("SELECT id, mahkeme_adi, esas_no FROM davalar WHERE muvekkil_id=? ORDER BY id DESC", muvekkilId))
            dv.Add((d.L("id"), $"{d.S("mahkeme_adi")} {d.S("esas_no")}".Trim()));
        return new List<Field>
        {
            new("tur", "Tür", "combo", Field.Of(VekTurleri), def: VekTurleri[0]),
            new("tarih", "Vekalet tarihi", "date", def: Utils.TodayIso()),
            new("dava_id", "Bağlı dava (müvekkilin davaları)", "combo", dv, def: 0L),
            new("aciklama", "Açıklama (noter, yevmiye no, yetkiler…)"),
            new("pdf", "Vekaletname (PDF)", "file"),
        };
    }

    void VekEkle()
    {
        var m = _tM.Current;
        if (m == null) { Ui.Info(this, "Önce müvekkil seçin."); return; }
        var v = FormDialog.Ask(this, $"Vekalet ekle — {m.S("ad_soyad")}", VFields(m.L("id")));
        if (v == null) return;
        VekKaydet(null, m, v);
    }

    void VekDuzenle()
    {
        var m = _tM.Current; var r = _tV.Current;
        if (m == null || r == null) return;
        var v = FormDialog.Ask(this, "Vekaleti düzenle", VFields(m.L("id")), r);
        if (v == null) return;
        VekKaydet(r.L("id"), m, v);
    }

    void VekKaydet(long? id, Row m, Dictionary<string, object?> v)
    {
        var pdf = Convert.ToString(v["pdf"]) ?? "";
        v.Remove("pdf");
        v["dava_id"] = Convert.ToInt64(v["dava_id"] ?? 0L);
        if (pdf.Length > 0) v["dosya_yolu"] = Workflows.VekaletnameKaydet(Cfg, m.S("ad_soyad"), pdf);
        if (id == null) { v["muvekkil_id"] = m.L("id"); Db.Insert("vekaletnameler", v); }
        else Db.Update("vekaletnameler", id.Value, v);
        Refresh();
    }

    void VekAc()
    {
        var r = _tV.Current;
        if (r == null) return;
        if (r.S("dosya_yolu").Length == 0) { Ui.Info(this, "Bu vekalet kaydına PDF eklenmemiş. Düzenle ile PDF ekleyebilirsiniz."); return; }
        var p = Workflows.DosyaYolu(Cfg, r.S("dosya_yolu"));
        if (System.IO.File.Exists(p)) Ui.OpenExternal(p); else Ui.Warn(this, $"Dosya bulunamadı:\n{p}");
    }

    void VekSil()
    {
        var r = _tV.Current;
        if (r != null && Ui.Confirm(this, "Vekalet kaydı silinsin mi? (PDF dosyası bilgisayarda kalır)")) { Db.Delete("vekaletnameler", r.L("id")); Refresh(); }
    }

    void LoadVekalet()
    {
        var m = _tM.Current;
        if (m == null) { _vTitle.Text = "Vekaletnameler — müvekkil seçin"; _tV.Load(new()); return; }
        var rows = Db.Query("SELECT v.*, TRIM(COALESCE(d.mahkeme_adi,'') || ' ' || COALESCE(d.esas_no,'')) AS dava FROM vekaletnameler v " +
                            "LEFT JOIN davalar d ON d.id=v.dava_id WHERE v.muvekkil_id=? ORDER BY v.tarih DESC, v.id DESC", m.L("id"));
        foreach (var r in rows) r["dosya"] = r.S("dosya_yolu").Length > 0 ? "✔ PDF" : "—";
        _vTitle.Text = $"Vekaletnameler — {m.S("ad_soyad")} ({rows.Count})";
        _tV.Load(rows);
    }

    static List<Field> MFields(bool withPdf)
    {
        var f = new List<Field>
        {
            new("tc_no", "T.C. Kimlik No", required: true), new("ad_soyad", "Ad Soyad", required: true),
            new("telefon", "Telefon"), new("eposta", "E-posta"), new("adres", "Adres", "multiline"),
        };
        if (withPdf) f.Add(new Field("pdf", "Vekaletname (PDF)", "file"));
        return f;
    }

    static bool Unique(SqliteException e) => e.SqliteErrorCode == 19;

    void AddM()
    {
        var v = FormDialog.Ask(this, "Yeni Müvekkil", MFields(true));
        if (v == null) return;
        if (!Utils.ValidTc(v["tc_no"] as string) && !Ui.Confirm(this, "T.C. kimlik numarası doğrulamadan geçmedi. Yine de kaydedilsin mi?")) return;
        var catisma = DosyaEsleme.Catisma(Db, v["ad_soyad"] as string, null);
        if (catisma.Count > 0 && !Ui.Confirm(this, "Olası çıkar çatışması:\n• " + string.Join("\n• ", catisma) + "\n\nYine de kaydedilsin mi?")) return;
        var pdf = (v["pdf"] as string) ?? "";
        v.Remove("pdf");
        try
        {
            var mid = Db.Insert("muvekkiller", v);
            if (pdf.Length > 0)
                Db.Insert("vekaletnameler", new Dictionary<string, object?>
                {
                    ["muvekkil_id"] = mid, ["tur"] = VekTurleri[0], ["tarih"] = Utils.TodayIso(),
                    ["dosya_yolu"] = Workflows.VekaletnameKaydet(Cfg, (string)v["ad_soyad"]!, pdf),
                });
        }
        catch (SqliteException e) when (Unique(e)) { Ui.Warn(this, "Bu T.C. kimlik numarası ile kayıtlı müvekkil zaten var."); }
        Refresh();
    }

    void EditM()
    {
        var m = _tM.Current;
        if (m == null) return;
        var v = FormDialog.Ask(this, "Müvekkili Düzenle", MFields(false), m);
        if (v == null) return;
        try { Db.Update("muvekkiller", m.L("id"), v); }
        catch (SqliteException e) when (Unique(e)) { Ui.Warn(this, "Bu T.C. kimlik numarası başka bir müvekkilde kayıtlı."); }
        Refresh();
    }

    void DelM()
    {
        var m = _tM.Current;
        if (m != null && Ui.Confirm(this, $"{m.S("ad_soyad")} ve cari kayıtları silinsin mi?")) { Db.Delete("muvekkiller", m.L("id")); Refresh(); }
    }

    // ------------------------------------------------------------ finans
    DataTable Grp => _modDava ? _tGD : _tGM;

    void ModSec(bool dava)
    {
        _modDava = dava;
        _grpHost.Content = Grp;
        Aktif(_bModM, !dava);
        Aktif(_bModD, dava);
        LoadCari();
        LoadHareket();
    }

    static void Aktif(Button? b, bool on)
    {
        if (b == null) return;
        if (on && Application.Current.TryFindResource("Primary") is Style st) b.Style = st;
        else b.ClearValue(FrameworkElement.StyleProperty);
    }

    UIElement BuildFinans()
    {
        // sol: gruplama
        _bModM = Ui.Btn("Müvekkile göre", "Primary", () => ModSec(false));
        _bModD = Ui.Btn("Dava dosyasına göre", "", () => ModSec(true));
        var mod = Ui.HRow(_bModM, _bModD);
        mod.Margin = new Thickness(0, 0, 0, 8);
        var left = new DockPanel();
        DockPanel.SetDock(mod, Dock.Top);
        left.Children.Add(mod);
        _grpHost.Content = _tGM;
        left.Children.Add(_grpHost);

        // sağ: cari kayıtlar + hareketler
        var top = new DockPanel();
        var tb = Ui.HRow(Ui.Btn("＋ Cari Kayıt", "Primary", AddF), Ui.Btn("Düzenle", "", EditF), Ui.Btn("Sil", "Danger", DelF));
        tb.Margin = new Thickness(0, 6, 0, 0);
        DockPanel.SetDock(tb, Dock.Bottom);
        DockPanel.SetDock(_fSum, Dock.Bottom); _fSum.Margin = new Thickness(0, 6, 0, 0);
        top.Children.Add(tb); top.Children.Add(_fSum); top.Children.Add(_tF);

        var bot = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(_hTitle, Dock.Top); _hTitle.Margin = new Thickness(0, 0, 0, 6);
        var hb = Ui.HRow(Ui.Btn("＋ Tahsilat Ekle", "Ok", Tahsilat), Ui.Btn("Düzenle", "", EditH), Ui.Btn("Sil", "Danger", DelH));
        hb.Margin = new Thickness(0, 6, 0, 0);
        DockPanel.SetDock(hb, Dock.Bottom);
        DockPanel.SetDock(_hSum, Dock.Bottom); _hSum.Margin = new Thickness(0, 6, 0, 0);
        bot.Children.Add(_hTitle); bot.Children.Add(hb); bot.Children.Add(_hSum); bot.Children.Add(_tH);
        var right = Ui.VSplit(top, bot, 3, 2);
        right.Margin = new Thickness(10, 0, 0, 0);
        return Ui.HSplit(left, right, 4, 7);
    }

    List<Field> FFields(bool yeni)
    {
        var ms = Db.Query("SELECT id, ad_soyad, tc_no FROM muvekkiller ORDER BY ad_soyad");
        var dv = new List<(object? Value, string Label)> { (0L, "— Dava dosyası yok (genel cari) —") };
        foreach (var d in Db.Query("SELECT d.id, d.mahkeme_adi, d.esas_no, m.ad_soyad FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id ORDER BY d.id DESC"))
            dv.Add((d.L("id"), $"{d.S("mahkeme_adi")} {d.S("esas_no")}".Trim() + (d.S("ad_soyad").Length > 0 ? $" — {d.S("ad_soyad")}" : "")));
        var f = new List<Field>
        {
            new("muvekkil_id", "Müvekkil", "combo", ms.Select(m => ((object?)m.L("id"), $"{m.S("ad_soyad")} ({m.S("tc_no")})")), required: true),
            new("dava_id", "Bağlı dava dosyası", "combo", dv, def: 0L),
            new("dosya_konusu", "Dosya Konusu (boşsa davadan alınır)"), new("toplam_ucret", "Toplam Vekalet Ücreti", "money"),
            new("sonraki_tahsilat_tarihi", "Sonraki Tahsilat Tarihi", "date"),
            new("makbuz_kesildi", "Makbuz kesildi", "check"), new("notlar", "Notlar", "multiline"),
        };
        if (yeni)
            f.InsertRange(4, new[]
            {
                new Field("ilk_tutar", "İlk tahsilat tutarı (varsa)", "money"),
                new Field("ilk_tarih", "İlk tahsilat tarihi", "date", def: Utils.TodayIso()),
            });
        return f;
    }

    /// <summary>Dava seçildiyse konuyu/müvekkili tamamlar. Geçersizse false.</summary>
    bool DavaUyumla(Dictionary<string, object?> v)
    {
        var davaId = Convert.ToInt64(v["dava_id"] ?? 0L);
        v["dava_id"] = davaId;
        if (davaId > 0)
        {
            var d = Db.One("SELECT * FROM davalar WHERE id=?", davaId);
            if (d != null)
            {
                if (Convert.ToString(v["dosya_konusu"])?.Trim().Length is null or 0)
                    v["dosya_konusu"] = $"{d.S("mahkeme_adi")} {d.S("esas_no")}".Trim() + (d.S("dava_turu").Length > 0 ? $" — {d.S("dava_turu")}" : "");
                var mid = d.L("muvekkil_id");
                if (mid > 0 && mid != Convert.ToInt64(v["muvekkil_id"]) &&
                    !Ui.Confirm(this, "Seçilen dava dosyası başka bir müvekkile ait görünüyor. Yine de bu müvekkil için kaydedilsin mi?"))
                    return false;
            }
        }
        if (Convert.ToString(v["dosya_konusu"])?.Trim().Length is null or 0)
        {
            Ui.Warn(this, "Dosya konusu boş. Bir dava dosyası seçin veya konuyu yazın.");
            return false;
        }
        return true;
    }

    void AddF()
    {
        if (Db.ScalarL("SELECT COUNT(*) FROM muvekkiller") == 0) { Ui.Info(this, "Önce müvekkil ekleyin."); return; }
        // seçili gruba göre ön doldurma
        var ilk = new Dictionary<string, object?>();
        var g = Grp.Current;
        if (g != null && g.L("id") > 0)
        {
            if (_modDava)
            {
                ilk["dava_id"] = g.L("id");
                var mid = Db.One("SELECT muvekkil_id FROM davalar WHERE id=?", g.L("id"))?.L("muvekkil_id") ?? 0;
                if (mid > 0) ilk["muvekkil_id"] = mid;
            }
            else ilk["muvekkil_id"] = g.L("id");
        }
        var alanlar = FFields(true);
        var v = FormDialog.Ask(this, "Yeni Cari Kayıt", alanlar, ilk);
        while (v != null && !DavaUyumla(v)) v = FormDialog.Ask(this, "Yeni Cari Kayıt", alanlar, v);
        if (v == null) return;
        var ilkT = Convert.ToDouble(v["ilk_tutar"]); var ilkD = (string)v["ilk_tarih"]!;
        v.Remove("ilk_tutar"); v.Remove("ilk_tarih");
        v["kalan_bakiye"] = v["toplam_ucret"];
        var fid = Db.Insert("finans_tahsilat", v);
        if (ilkT > 0)
            Db.Insert("tahsilat_hareketleri", new Dictionary<string, object?> { ["finans_id"] = fid, ["tarih"] = ilkD, ["tutar"] = ilkT, ["aciklama"] = "İlk tahsilat" });
        Workflows.TahsilatYenidenHesapla(Db, fid);
        Refresh();
        _tF.SelectId(fid);
    }

    void EditF()
    {
        var r = _tF.Current;
        if (r == null) return;
        var alanlar = FFields(false);
        var v = FormDialog.Ask(this, "Cari Kaydı Düzenle", alanlar, r);
        while (v != null && !DavaUyumla(v)) v = FormDialog.Ask(this, "Cari Kaydı Düzenle", alanlar, v);
        if (v == null) return;
        Db.Update("finans_tahsilat", r.L("id"), v);
        Workflows.TahsilatYenidenHesapla(Db, r.L("id"));
        Refresh();
    }

    void DelF()
    {
        var r = _tF.Current;
        if (r != null && Ui.Confirm(this, "Cari kayıt ve tüm tahsilat hareketleri silinsin mi?")) { Db.Delete("finans_tahsilat", r.L("id")); Refresh(); }
    }

    const string MasrafM = "(SELECT COALESCE(SUM(x.tutar),0) FROM dava_masraflari x JOIN davalar dd ON dd.id=x.dava_id WHERE dd.muvekkil_id=m.id)";

    void GrupYukle()
    {
        // müvekkile göre
        var gm = Db.Query("SELECT m.id, m.ad_soyad || ' (' || m.tc_no || ')' AS ad, COUNT(f.id) AS dosya, COALESCE(SUM(f.toplam_ucret),0) AS toplam, " +
                          "COALESCE(SUM(f.alinan_tutar),0) AS alinan, COALESCE(SUM(f.kalan_bakiye),0) AS kalan, " + MasrafM + " AS masraf " +
                          "FROM muvekkiller m LEFT JOIN finans_tahsilat f ON f.muvekkil_id=m.id GROUP BY m.id ORDER BY kalan DESC, m.ad_soyad");
        gm.Insert(0, ToplamSatir("▸ Tümü", gm.Sum(x => x.D("toplam")), gm.Sum(x => x.D("alinan")), gm.Sum(x => x.D("kalan")), gm.Sum(x => x.D("masraf")), gm.Sum(x => x.L("dosya"))));
        _tGM.Load(gm, new() { ["kalan"] = r => r.D("kalan") > 0 ? "warn" : "muted" });
        if (_tGM.Current == null) _tGM.SelectFirst();

        // dava dosyasına göre
        var gd = Db.Query("SELECT d.id, TRIM(d.mahkeme_adi || ' ' || d.esas_no) AS ad, COALESCE(m.ad_soyad,'') AS muv, " +
                          "(SELECT COALESCE(SUM(f.toplam_ucret),0) FROM finans_tahsilat f WHERE f.dava_id=d.id) AS toplam, " +
                          "(SELECT COALESCE(SUM(f.alinan_tutar),0) FROM finans_tahsilat f WHERE f.dava_id=d.id) AS alinan, " +
                          "(SELECT COALESCE(SUM(f.kalan_bakiye),0) FROM finans_tahsilat f WHERE f.dava_id=d.id) AS kalan, " +
                          "(SELECT COALESCE(SUM(x.tutar),0) FROM dava_masraflari x WHERE x.dava_id=d.id) AS masraf " +
                          "FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id ORDER BY kalan DESC, d.id DESC");
        var bag = Db.One("SELECT COALESCE(SUM(toplam_ucret),0) AS toplam, COALESCE(SUM(alinan_tutar),0) AS alinan, COALESCE(SUM(kalan_bakiye),0) AS kalan " +
                         "FROM finans_tahsilat WHERE dava_id IS NULL OR dava_id=0") ?? new Row();
        var tum = ToplamSatir("▸ Tümü", Db.ScalarD("SELECT SUM(toplam_ucret) FROM finans_tahsilat"), Db.ScalarD("SELECT SUM(alinan_tutar) FROM finans_tahsilat"),
                              Db.ScalarD("SELECT SUM(kalan_bakiye) FROM finans_tahsilat"), Db.ScalarD("SELECT SUM(tutar) FROM dava_masraflari"), 0);
        tum["muv"] = "";
        var bagsiz = ToplamSatir("▸ Dava dosyasına bağlı olmayan", bag.D("toplam"), bag.D("alinan"), bag.D("kalan"), 0, 0);
        bagsiz["id"] = 0L; bagsiz["muv"] = "";
        gd.Insert(0, bagsiz); gd.Insert(0, tum);
        _tGD.Load(gd, new() { ["kalan"] = r => r.D("kalan") > 0 ? "warn" : "muted" });
        if (_tGD.Current == null) _tGD.SelectFirst();
    }

    static Row ToplamSatir(string ad, double toplam, double alinan, double kalan, double masraf, long dosya) => new()
    {
        ["id"] = -1L, ["ad"] = ad, ["toplam"] = toplam, ["alinan"] = alinan, ["kalan"] = kalan, ["masraf"] = masraf, ["dosya"] = dosya,
    };

    void LoadCari()
    {
        var g = Grp.Current;
        var where = "";
        var ps = new List<object?>();
        if (g != null && g.L("id") >= 0)
        {
            if (_modDava)
            {
                if (g.L("id") == 0) where = "WHERE f.dava_id IS NULL OR f.dava_id=0";
                else { where = "WHERE f.dava_id=?"; ps.Add(g.L("id")); }
            }
            else { where = "WHERE f.muvekkil_id=?"; ps.Add(g.L("id")); }
        }
        var fs = Db.Query("SELECT f.*, m.ad_soyad || ' (' || m.tc_no || ')' AS etiket, TRIM(COALESCE(d.mahkeme_adi,'') || ' ' || COALESCE(d.esas_no,'')) AS dava_etiket " +
                          "FROM finans_tahsilat f JOIN muvekkiller m ON m.id=f.muvekkil_id LEFT JOIN davalar d ON d.id=f.dava_id " + where +
                          " ORDER BY f.sonraki_tahsilat_tarihi='' , f.sonraki_tahsilat_tarihi", ps.ToArray());
        var bak = _busy;
        _busy = true;
        try
        {
            _tF.Load(fs, new() { ["sonraki_tahsilat_tarihi"] = r => r.D("kalan_bakiye") > 0 ? Ui.DeadlineKind(r.S("sonraki_tahsilat_tarihi")) : "muted" });
        }
        finally { _busy = bak; }
        var masraf = g != null ? g.D("masraf") : 0;
        _fSum.Text = (g != null ? $"{g.S("ad")}  •  " : "") + $"Toplam ücret: {Utils.FmtMoney(fs.Sum(r => r.D("toplam_ucret")))}  •  Alınan: {Utils.FmtMoney(fs.Sum(r => r.D("alinan_tutar")))}  •  " +
                     $"Kalan: {Utils.FmtMoney(fs.Sum(r => r.D("kalan_bakiye")))}" + (masraf > 0 ? $"  •  Dava masrafları: {Utils.FmtMoney(masraf)}" : "");
    }

    void LoadHareket()
    {
        var r = _tF.Current;
        if (r == null)
        {
            _hTitle.Text = "Tahsilat hareketleri — cari kaydı seçin";
            _tH.Load(new()); _hSum.Text = "";
            return;
        }
        _hTitle.Text = $"Tahsilat hareketleri — {r.S("etiket")} • {r.S("dosya_konusu")}";
        var rows = Db.Query("SELECT * FROM tahsilat_hareketleri WHERE finans_id=? ORDER BY tarih='' , tarih DESC, id DESC", r.L("id"));
        _tH.Load(rows);
        _hSum.Text = $"{rows.Count} tahsilat • Toplam alınan: {Utils.FmtMoney(rows.Sum(x => x.D("tutar")))} • Kalan bakiye: {Utils.FmtMoney(r.D("kalan_bakiye"))}";
    }

    static List<Field> HFields() => new()
    {
        new("tarih", "Tahsilat Tarihi", "date"), new("tutar", "Tutar", "money", required: true), new("aciklama", "Açıklama (nakit / havale / çek…)"),
    };

    void Tahsilat()
    {
        var r = _tF.Current;
        if (r == null) { Ui.Info(this, "Önce yukarıdan bir cari kayıt seçin."); return; }
        var fields = new List<Field>
        {
            new("tarih", "Tahsilat Tarihi", "date", def: Utils.TodayIso()), new("tutar", "Tutar", "money", required: true),
            new("aciklama", "Açıklama (nakit / havale / çek…)"), new("sonraki", "Sonraki tahsilat tarihi", "date", def: r.S("sonraki_tahsilat_tarihi")),
        };
        var v = FormDialog.Ask(this, $"Tahsilat — {r.S("etiket")}", fields);
        if (v == null || Convert.ToDouble(v["tutar"]) <= 0) return;
        var sonraki = (string)v["sonraki"]!;
        v.Remove("sonraki");
        v["finans_id"] = r.L("id");
        Db.Insert("tahsilat_hareketleri", v);
        Db.Update("finans_tahsilat", r.L("id"), new Dictionary<string, object?> { ["sonraki_tahsilat_tarihi"] = sonraki });
        Workflows.TahsilatYenidenHesapla(Db, r.L("id"));
        Refresh();
    }

    void EditH()
    {
        var h = _tH.Current;
        if (h == null) return;
        var v = FormDialog.Ask(this, "Tahsilatı Düzenle", HFields(), h);
        if (v == null) return;
        Db.Update("tahsilat_hareketleri", h.L("id"), v);
        Workflows.TahsilatYenidenHesapla(Db, h.L("finans_id"));
        Refresh();
    }

    void DelH()
    {
        var h = _tH.Current;
        if (h == null || !Ui.Confirm(this, "Bu tahsilat kaydı silinsin mi?")) return;
        Db.Delete("tahsilat_hareketleri", h.L("id"));
        Workflows.TahsilatYenidenHesapla(Db, h.L("finans_id"));
        Refresh();
    }

    // ------------------------------------------------------------ yenile
    public override void Refresh()
    {
        _busy = true;
        try
        {
            // eski tek-PDF alanını vekaletnameler listesine bir kez taşı
            Db.Execute("INSERT INTO vekaletnameler (muvekkil_id, tur, tarih, dosya_yolu) SELECT m.id, 'Genel vekaletname', m.kayit_tarihi, m.vekaletname_yolu " +
                       "FROM muvekkiller m WHERE m.vekaletname_yolu!='' AND NOT EXISTS (SELECT 1 FROM vekaletnameler v WHERE v.muvekkil_id=m.id)");
            var ms = Db.Query("SELECT m.*, (SELECT COUNT(*) FROM vekaletnameler v WHERE v.muvekkil_id=m.id) AS vsay FROM muvekkiller m ORDER BY m.ad_soyad");
            foreach (var m in ms) m["vek"] = m.L("vsay") > 0 ? $"✔ {m.L("vsay")} adet" : "—";
            _tM.Load(ms);
            GrupYukle();
        }
        finally { _busy = false; }
        LoadVekalet();
        LoadCari();
        LoadHareket();
    }
}
