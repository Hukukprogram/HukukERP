using System.Windows;
using System.Windows.Controls;
using HukukErp.Core;

namespace HukukErp.UI.Pages;

/// <summary>Müvekkil emanet hesabı: müvekkilden alınan harç/masraf avansı ve yapılan harcamalar/iadeler. Büronun kendi parasından ayrı izlenir.</summary>
public class EmanetPage : BasePage
{
    readonly DataTable _t = new(("Tarih", "tarih", Fmt.Date), ("Müvekkil", "ad_soyad", null), ("Dava", "dava", null), ("Tür", "tur", null),
                                ("Tutar", "tutar", Fmt.Money), ("Açıklama", "aciklama", null));
    readonly DataTable _b = new(("Müvekkil", "ad_soyad", null), ("Giriş", "giris", Fmt.Money), ("Çıkış", "cikis", Fmt.Money), ("Bakiye", "bakiye", Fmt.Money));
    readonly ComboBox _filtre = new() { MinWidth = 280, Margin = new Thickness(0, 0, 8, 0) };
    bool _busy;

    public EmanetPage(AppCtx ctx) : base(ctx)
    {
        Top(Ui.PageHeader("Emanet Hesabı", "Müvekkil adına alınan avanslar ve onlardan yapılan harcamalar • bakiye müvekkile aittir"));
        _filtre.SelectionChanged += (_, _) => { if (!_busy) YukleHareket(); };
        Top(Ui.HRow(Ui.Btn("＋ Giriş (müvekkilden alındı)", "Primary", () => Ekle("Giriş")), Ui.Btn("＋ Çıkış (harcama / iade)", "", () => Ekle("Çıkış")),
                    Ui.Btn("Düzenle", "", Duzenle), Ui.Btn("Sil", "Danger", Sil), Ui.Muted("Göster:", false), _filtre));
        _b.MaxHeight = 170;
        var alt = new Card();
        alt.Top(Ui.H2("Müvekkil Bakiyeleri"));
        alt.Fill(_b);
        var dock = new DockPanel();
        DockPanel.SetDock(alt, Dock.Bottom);
        alt.Margin = new Thickness(0, 10, 0, 0);
        dock.Children.Add(alt);
        dock.Children.Add(_t);
        Fill(dock);
    }

    long Filtre => (_filtre.SelectedItem as ComboItem)?.Value is { } v ? Convert.ToInt64(v) : 0;

    public override void Refresh()
    {
        _busy = true;
        var onceki = Filtre;
        _filtre.Items.Clear();
        _filtre.Items.Add(new ComboItem { Value = 0L, Label = "Tüm müvekkiller" });
        foreach (var m in Db.Query("SELECT id, ad_soyad FROM muvekkiller ORDER BY ad_soyad"))
            _filtre.Items.Add(new ComboItem { Value = m.L("id"), Label = m.S("ad_soyad") });
        var i = _filtre.Items.Cast<ComboItem>().ToList().FindIndex(x => Convert.ToInt64(x.Value) == onceki);
        _filtre.SelectedIndex = i >= 0 ? i : 0;
        _busy = false;
        YukleHareket();
        _b.Load(Db.Query("SELECT m.ad_soyad, " +
                         "COALESCE(SUM(CASE WHEN e.tur='Giriş' THEN e.tutar END),0) AS giris, " +
                         "COALESCE(SUM(CASE WHEN e.tur='Çıkış' THEN e.tutar END),0) AS cikis, " +
                         "COALESCE(SUM(CASE WHEN e.tur='Giriş' THEN e.tutar ELSE -e.tutar END),0) AS bakiye " +
                         "FROM emanet_hareketleri e JOIN muvekkiller m ON m.id=e.muvekkil_id GROUP BY m.id, m.ad_soyad ORDER BY m.ad_soyad"),
                 new() { ["bakiye"] = r => r.D("bakiye") < 0 ? "danger" : "ok" });
    }

    void YukleHareket()
    {
        var f = Filtre;
        _t.Load(Db.Query("SELECT e.*, m.ad_soyad, TRIM(COALESCE(d.mahkeme_adi,'') || ' ' || COALESCE(d.esas_no,'')) AS dava " +
                         "FROM emanet_hareketleri e JOIN muvekkiller m ON m.id=e.muvekkil_id LEFT JOIN davalar d ON d.id=e.dava_id " +
                         (f > 0 ? "WHERE e.muvekkil_id=? " : "WHERE ?=0 ") + "ORDER BY e.tarih DESC, e.id DESC", f),
                new() { ["tur"] = r => r.S("tur") == "Giriş" ? "ok" : "warn" });
    }

    List<Field> Fields(string tur) => new()
    {
        new("muvekkil_id", "Müvekkil", "combo", Secenek.Muvekkiller(Db), required: true),
        new("dava_id", "Bağlı dava", "combo", Secenek.Davalar(Db), def: 0L),
        new("tarih", "Tarih", "date", def: Utils.TodayIso()),
        new("tur", "Tür", "combo", Field.Of("Giriş", "Çıkış"), def: tur),
        new("tutar", "Tutar", "money", required: true),
        new("aciklama", "Açıklama (harç, bilirkişi avansı, iade vb.)"),
    };

    void Ekle(string tur)
    {
        var v = FormDialog.Ask(this, tur == "Giriş" ? "Emanet Girişi" : "Emanet Çıkışı", Fields(tur), Filtre > 0 ? new Dictionary<string, object?> { ["muvekkil_id"] = Filtre } : null);
        if (v == null) return;
        Db.Insert("emanet_hareketleri", v);
        Refresh();
        UyarBakiye(Convert.ToInt64(v["muvekkil_id"]));
    }

    void Duzenle()
    {
        var c = _t.Current;
        if (c == null) { Ui.Info(this, "Önce bir kayıt seçin."); return; }
        var v = FormDialog.Ask(this, "Emanet Kaydı", Fields(c.S("tur")), Db.One("SELECT * FROM emanet_hareketleri WHERE id=?", c.L("id")));
        if (v == null) return;
        Db.Update("emanet_hareketleri", c.L("id"), v);
        Refresh();
        UyarBakiye(Convert.ToInt64(v["muvekkil_id"]));
    }

    void Sil()
    {
        var c = _t.Current;
        if (c != null && Ui.Confirm(this, "Kayıt silinsin mi?")) { Db.Delete("emanet_hareketleri", c.L("id")); Refresh(); }
    }

    /// <summary>Emanet bakiyesi negatife düşerse (müvekkilin parasından fazla harcama) uyarır.</summary>
    void UyarBakiye(long muvekkil)
    {
        var b = Db.ScalarD("SELECT COALESCE(SUM(CASE WHEN tur='Giriş' THEN tutar ELSE -tutar END),0) FROM emanet_hareketleri WHERE muvekkil_id=?", muvekkil);
        if (b < 0) Ui.Warn(this, $"Dikkat: bu müvekkilin emanet bakiyesi eksiye düştü ({Utils.FmtMoney(b)}). Müvekkilin parası olmadan harcama yapılmış görünüyor.");
    }
}
