using System.Windows;
using System.Windows.Controls;
using HukukErp.Core;
using HukukErp.Services;

namespace HukukErp.UI.Pages;

/// <summary>Dava Takibi: müvekkil listesi → seçili müvekkilin davaları → aşamalar, masraflar, notlar.</summary>
public class DavaPage : BasePage
{
    static readonly string[] Asamalar = { "Dava Dilekçesi", "Cevap Dilekçesi", "Cevaba Cevap", "İkinci Cevap", "Tahkikat / Delil", "Bilirkişi", "Karar Aşaması", "Karara Çıktı" };
    static readonly (string Key, string Ad, string Dayanak)[] Hmk =
    {
        ("dava_dilekcesi_tarih", "Dava Dilekçesi", "HMK 119"), ("cevap_dilekcesi_tarih", "Cevap Dilekçesi", "HMK 127 (2 hafta)"),
        ("cevaba_cevap_tarih", "Cevaba Cevap", "HMK 136 (2 hafta)"), ("ikinci_cevap_tarih", "İkinci Cevap", "HMK 136 (2 hafta)"),
    };

    readonly DataTable _tD = new(("Müvekkil", "muvekkil", null), ("Mahkeme", "mahkeme_adi", null), ("Esas No", "esas_no", null),
        ("Tür", "dava_turu", null), ("Karşı Taraf", "karsi_taraf", null), ("Duruşma", "durusma_tarihi", Fmt.Date), ("Aşama", "asama_durum", null));
    readonly DataTable _tMv = new(("Müvekkil", "ad", null), ("Dava", "say", null));
    readonly TextBlock _hdr = Ui.H2("Bir dava seçin");
    readonly TextBlock _notDurum = Ui.Muted("");
    long _notId;
    bool _notKirli, _notYukleniyor;
    readonly DataTable _tTl = new(("Aşama", "asama", null), ("Tarih", "tarih", Fmt.Date), ("Dayanak", "dayanak", null));
    readonly DataTable _tM = new(("Tarih", "tarih", Fmt.Date), ("Masraf Kalemi", "masraf_kalemi", null), ("Tutar", "tutar", Fmt.Money));
    readonly TextBlock _mSum = Ui.H2("");
    readonly TextBox _notes = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalContentAlignment = VerticalAlignment.Top,
    };

    static readonly System.Globalization.CultureInfo Tr = new("tr-TR");

    bool _busy;

    public DavaPage(AppCtx ctx) : base(ctx)
    {
        Top(Ui.PageHeader("Dava Takibi", "Sol üst: müvekkiller • Sol alt: seçili müvekkilin davaları • Sağ: dava aşamaları, masraflar, notlar"));
        var left = new Card();
        left.Bottom(Ui.HRow(Ui.Btn("＋ Dava", "Primary", AddD), Ui.Btn("Düzenle", "", EditD), Ui.Btn("📂 Dosya Kartı", "", DosyaKarti), Ui.Btn("Sil", "Danger", DelD)));
        left.Fill(Ui.VSplit(_tMv, _tD, 2, 3));

        var tabs = new TabControl();
        // HMK zaman çizelgesi
        var tw = new DockPanel();
        var upd = Ui.Btn("Aşama Tarihlerini / Aşamayı Güncelle", "", EditStages);
        upd.HorizontalAlignment = HorizontalAlignment.Left;
        upd.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(_hdr, Dock.Top); _hdr.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(upd, Dock.Bottom);
        tw.Children.Add(_hdr); tw.Children.Add(upd); tw.Children.Add(_tTl);
        tabs.Items.Add(Ui.Tab("HMK Aşamaları", tw));
        // masraflar
        var mw = new DockPanel();
        var mb = Ui.HRow(Ui.Btn("＋ Masraf", "Primary", AddM), Ui.Btn("Düzenle", "", EditM), Ui.Btn("Sil", "Danger", DelM));
        mb.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(mb, Dock.Bottom);
        DockPanel.SetDock(_mSum, Dock.Bottom); _mSum.Margin = new Thickness(0, 8, 0, 0);
        mw.Children.Add(mb); mw.Children.Add(_mSum); mw.Children.Add(_tM);
        tabs.Items.Add(Ui.Tab("Masraflar", mw));
        // notlar
        var nw = new DockPanel();
        var nb = Ui.Btn("Notları Kaydet", "Primary", SaveNotes);
        var nbar = Ui.HRow(nb, _notDurum);
        nbar.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(nbar, Dock.Bottom);
        // tema renkleri açıkça verilir (yazılan not her zaman okunur olsun)
        _notes.SetResourceReference(Control.ForegroundProperty, "text");
        _notes.SetResourceReference(Control.BackgroundProperty, "surface2");
        _notes.SetResourceReference(Control.BorderBrushProperty, "border");
        _notes.SetResourceReference(TextBox.CaretBrushProperty, "text");
        _notes.Padding = new Thickness(8);
        _notes.TextChanged += (_, _) => { if (!_notYukleniyor && _notId > 0) { _notKirli = true; _notDurum.Text = "kaydedilmedi…"; } };
        _notes.LostFocus += (_, _) => NotlariYaz();
        Unloaded += (_, _) => NotlariYaz();
        nw.Children.Add(nbar); nw.Children.Add(_notes);
        tabs.Items.Add(Ui.Tab("Genel Notlar", nw));

        Fill(Ui.HSplit(left, tabs, 3, 2));
        _tMv.SelectionChanged += (_, _) => { if (!_busy) LoadDavalar(); };
        _tD.SelectionChanged += (_, _) => { if (!_busy) LoadDetail(); };
    }

    long MuvSecili => _tMv.Current?.L("id") ?? 0;

    public override void Refresh()
    {
        NotlariYaz();
        _busy = true;
        try
        {
            var cmp = StringComparer.Create(Tr, true);
            var ms = Db.Query("SELECT m.id, m.ad_soyad AS ad, (SELECT COUNT(*) FROM davalar d WHERE d.muvekkil_id=m.id) AS say FROM muvekkiller m")
                       .OrderBy(x => x.S("ad"), cmp).ToList();
            var tum = new Row { ["id"] = 0L, ["ad"] = "▸ Tüm müvekkiller", ["say"] = Db.ScalarL("SELECT COUNT(*) FROM davalar") };
            ms.Insert(0, tum);
            var onceki = MuvSecili;
            _tMv.Load(ms);
            if (_tMv.Current == null) _tMv.SelectId(onceki > 0 && ms.Any(x => x.L("id") == onceki) ? onceki : 0);
            if (_tMv.Current == null) _tMv.SelectFirst();
            DavaYukle();
        }
        finally { _busy = false; }
        LoadDetail();
    }

    /// <summary>Müvekkil değişince: o müvekkilin davalarını listeler, ilkini seçer.</summary>
    void LoadDavalar()
    {
        NotlariYaz();
        _busy = true;
        try { DavaYukle(); }
        finally { _busy = false; }
        LoadDetail();
    }

    void DavaYukle()
    {
        var mid = MuvSecili;
        var rows = mid > 0
            ? Db.Query("SELECT d.*, COALESCE(m.ad_soyad,'—') AS muvekkil FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id WHERE d.muvekkil_id=? ORDER BY d.durusma_tarihi='' , d.durusma_tarihi", mid)
            : Db.Query("SELECT d.*, COALESCE(m.ad_soyad,'—') AS muvekkil FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id ORDER BY d.durusma_tarihi='' , d.durusma_tarihi");
        _tD.Load(rows, new() { ["durusma_tarihi"] = r => Ui.DeadlineKind(r.S("durusma_tarihi")) });
        if (_tD.Current == null) _tD.SelectFirst();
    }

    void LoadDetail()
    {
        NotlariYaz();   // önceki davanın yazılmamış notu varsa önce kaydedilir
        var d = _tD.Current;
        _notYukleniyor = true;
        try
        {
            if (d == null)
            {
                _hdr.Text = "Bu müvekkilin dava dosyası yok";
                _tTl.Load(new()); _tM.Load(new());
                _mSum.Text = ""; _notes.Text = ""; _notId = 0; _notDurum.Text = "";
                return;
            }
            _hdr.Text = $"{d.S("mahkeme_adi")} • {d.S("esas_no")} • Aşama: {d.S("asama_durum")}";
            _tTl.Load(Hmk.Select(h => new Row { ["asama"] = h.Ad, ["tarih"] = d.S(h.Key), ["dayanak"] = h.Dayanak }).ToList());
            var ms = Db.Query("SELECT * FROM dava_masraflari WHERE dava_id=? ORDER BY tarih DESC, id DESC", d.L("id"));
            _tM.Load(ms);
            _mSum.Text = $"Toplam masraf: {Utils.FmtMoney(ms.Sum(m => m.D("tutar")))}";
            _notId = d.L("id");
            _notes.Text = d.S("genel_notlar");
            _notKirli = false; _notDurum.Text = "";
        }
        finally { _notYukleniyor = false; }
    }

    /// <summary>Yazılmış ama kaydedilmemiş notu veritabanına ve listedeki satıra yazar.</summary>
    void NotlariYaz()
    {
        if (!_notKirli || _notId <= 0) return;
        _notKirli = false;
        var txt = _notes.Text;
        Db.Update("davalar", _notId, new Dictionary<string, object?> { ["genel_notlar"] = txt });
        foreach (var r in _tD.Rows) if (r.L("id") == _notId) r["genel_notlar"] = txt;   // listedeki eski kopya güncel kalsın
        _notDurum.Text = $"✔ Kaydedildi {DateTime.Now:HH:mm}";
    }

    // ---- dava
    List<Field> DFields()
    {
        var ms = new List<(object?, string)> { (null, "— seçilmedi —") };
        ms.AddRange(Db.Query("SELECT id, ad_soyad, tc_no FROM muvekkiller ORDER BY ad_soyad").Select(m => ((object?)m.L("id"), $"{m.S("ad_soyad")} ({m.S("tc_no")})")));
        return new()
        {
            new("muvekkil_id", "Müvekkil", "combo", ms), new("mahkeme_adi", "Mahkeme", required: true), new("esas_no", "Esas No", required: true),
            new("dava_turu", "Dava Türü"), new("karsi_taraf", "Karşı Taraf"), new("durusma_tarihi", "Duruşma Tarihi", "date"),
            new("asama_durum", "Aşama", "combo", Field.Of(Asamalar)),
        };
    }

    public void YeniDava() => AddD();

    void DosyaKarti()
    {
        var c = _tD.Current;
        if (c == null) { Ui.Info(this, "Önce bir dava seçin."); return; }
        DosyaKartiPage.Hedef = c.L("id");
        Ctx.Navigate("dosya_karti", null);
    }

    void AddD()
    {
        var v = FormDialog.Ask(this, "Yeni Dava", DFields(), MuvSecili > 0 ? new Dictionary<string, object?> { ["muvekkil_id"] = MuvSecili } : null);
        if (v == null) return;
        long mid = v.TryGetValue("muvekkil_id", out var mv) && mv != null ? Convert.ToInt64(mv) : 0;
        var ad = mid > 0 ? Db.One("SELECT ad_soyad FROM muvekkiller WHERE id=?", mid)?.S("ad_soyad") : null;
        var uyari = DosyaEsleme.Catisma(Db, ad, v.TryGetValue("karsi_taraf", out var kt) ? kt as string : null, mid);
        if (uyari.Count > 0 && !Ui.Confirm(this, "Olası çıkar çatışması:\n• " + string.Join("\n• ", uyari) + "\n\nYine de kaydedilsin mi?")) return;
        Db.Insert("davalar", v);
        Refresh();
    }

    void EditD()
    {
        var d = _tD.Current;
        if (d == null) return;
        var v = FormDialog.Ask(this, "Davayı Düzenle", DFields(), d);
        if (v != null) { Db.Update("davalar", d.L("id"), v); Refresh(); }
    }

    void DelD()
    {
        var d = _tD.Current;
        if (d != null && Ui.Confirm(this, "Dava ve masrafları silinsin mi?")) { Db.Delete("davalar", d.L("id")); Refresh(); }
    }

    void EditStages()
    {
        var d = _tD.Current;
        if (d == null) { Ui.Info(this, "Önce dava seçin."); return; }
        var fields = Hmk.Select(h => new Field(h.Key, h.Ad, "date")).ToList();
        fields.Add(new Field("asama_durum", "Güncel Aşama", "combo", Field.Of(Asamalar)));
        var v = FormDialog.Ask(this, "HMK Aşamaları", fields, d);
        if (v != null) { Db.Update("davalar", d.L("id"), v); Refresh(); }
    }

    void SaveNotes()
    {
        if (_tD.Current == null) { Ui.Info(this, "Önce bir dava seçin."); return; }
        _notKirli = true;
        NotlariYaz();
        Ctx.Status("Notlar kaydedildi");
    }

    // ---- masraf
    void AddM()
    {
        var d = _tD.Current;
        if (d == null) { Ui.Info(this, "Önce dava seçin."); return; }
        var v = FormDialog.Ask(this, "Yeni Masraf", new List<Field>
        {
            new("tarih", "Tarih", "date", def: Utils.TodayIso()), new("masraf_kalemi", "Masraf Kalemi", required: true), new("tutar", "Tutar", "money"),
        });
        if (v == null) return;
        v["dava_id"] = d.L("id");
        Db.Insert("dava_masraflari", v);
        LoadDetail();
    }

    void EditM()
    {
        var m = _tM.Current;
        if (m == null) return;
        var v = FormDialog.Ask(this, "Masrafı Düzenle", new List<Field>
        {
            new("tarih", "Tarih", "date"), new("masraf_kalemi", "Masraf Kalemi", required: true), new("tutar", "Tutar", "money"),
        }, m);
        if (v != null) { Db.Update("dava_masraflari", m.L("id"), v); LoadDetail(); }
    }

    void DelM()
    {
        var m = _tM.Current;
        if (m != null && Ui.Confirm(this, "Masraf silinsin mi?")) { Db.Delete("dava_masraflari", m.L("id")); LoadDetail(); }
    }
}
