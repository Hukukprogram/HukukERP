using System.Windows;
using System.Windows.Controls;
using HukukErp.Core;
using HukukErp.Services;

namespace HukukErp.UI.Pages;

/// <summary>Dava & Müvekkil Takip (master-detail, HMK zaman çizelgesi, masraflar, notlar).</summary>
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
    readonly TextBlock _hdr = Ui.H2("Bir dava seçin");
    readonly DataTable _tTl = new(("Aşama", "asama", null), ("Tarih", "tarih", Fmt.Date), ("Dayanak", "dayanak", null));
    readonly DataTable _tM = new(("Tarih", "tarih", Fmt.Date), ("Masraf Kalemi", "masraf_kalemi", null), ("Tutar", "tutar", Fmt.Money));
    readonly TextBlock _mSum = Ui.H2("");
    readonly TextBox _notes = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalContentAlignment = VerticalAlignment.Top,
    };

    public DavaPage(AppCtx ctx) : base(ctx)
    {
        Top(Ui.PageHeader("Dava & Müvekkil Takip", "Sol: dava listesi • Sağ: seçili davanın aşamaları, masrafları ve notları"));
        var left = new Card();
        left.Bottom(Ui.HRow(Ui.Btn("＋ Dava", "Primary", AddD), Ui.Btn("Düzenle", "", EditD), Ui.Btn("📂 Dosya Kartı", "", DosyaKarti), Ui.Btn("Sil", "Danger", DelD)));
        left.Fill(_tD);

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
        nb.HorizontalAlignment = HorizontalAlignment.Left; nb.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(nb, Dock.Bottom);
        nw.Children.Add(nb); nw.Children.Add(_notes);
        tabs.Items.Add(Ui.Tab("Genel Notlar", nw));

        Fill(Ui.HSplit(left, tabs, 3, 2));
        _tD.SelectionChanged += (_, _) => LoadDetail();
    }

    public override void Refresh()
    {
        var rows = Db.Query("SELECT d.*, COALESCE(m.ad_soyad,'—') AS muvekkil FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id " +
                            "ORDER BY d.durusma_tarihi='' , d.durusma_tarihi");
        _tD.Load(rows, new() { ["durusma_tarihi"] = r => Ui.DeadlineKind(r.S("durusma_tarihi")) });
        LoadDetail();
    }

    void LoadDetail()
    {
        var d = _tD.Current;
        if (d == null)
        {
            _hdr.Text = "Bir dava seçin";
            _tTl.Load(new()); _tM.Load(new());
            _mSum.Text = ""; _notes.Text = "";
            return;
        }
        _hdr.Text = $"{d.S("mahkeme_adi")} • {d.S("esas_no")} • Aşama: {d.S("asama_durum")}";
        _tTl.Load(Hmk.Select(h => new Row { ["asama"] = h.Ad, ["tarih"] = d.S(h.Key), ["dayanak"] = h.Dayanak }).ToList());
        var ms = Db.Query("SELECT * FROM dava_masraflari WHERE dava_id=? ORDER BY tarih DESC, id DESC", d.L("id"));
        _tM.Load(ms);
        _mSum.Text = $"Toplam masraf: {Utils.FmtMoney(ms.Sum(m => m.D("tutar")))}";
        _notes.Text = d.S("genel_notlar");
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
        var v = FormDialog.Ask(this, "Yeni Dava", DFields());
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
        var d = _tD.Current;
        if (d == null) return;
        Db.Update("davalar", d.L("id"), new Dictionary<string, object?> { ["genel_notlar"] = _notes.Text });
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
