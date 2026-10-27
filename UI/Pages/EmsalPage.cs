using System.Windows;
using System.Windows.Controls;
using HukukErp.Core;

namespace HukukErp.UI.Pages;

/// <summary>Emsal karar ve not arşivi: karar/doktrin özetlerini konuya, etikete ve dosyaya bağlayıp aramak için.</summary>
public class EmsalPage : BasePage
{
    readonly DataTable _t = new(("Başlık", "baslik", null), ("Kaynak", "kaynak", null), ("Karar Tarihi", "karar_tarihi", Fmt.Date),
                                ("Konu", "konu", null), ("Etiketler", "etiketler", null), ("Dosya", "dava", null));
    readonly TextBox _ara = new() { Width = 280, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Başlık, kaynak, konu, etiket veya özet içinde ara" };
    readonly TextBox _ozet = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 120 };

    public EmsalPage(AppCtx ctx) : base(ctx)
    {
        Top(Ui.PageHeader("Emsal & Notlar", "Yargıtay/Danıştay kararı, doktrin ve dilekçe notları • konu ve etiketle arama"));
        _ara.TextChanged += (_, _) => Refresh();
        Top(Ui.HRow(Ui.Btn("＋ Not / Karar", "Primary", Ekle), Ui.Btn("Düzenle", "", Duzenle), Ui.Btn("Sil", "Danger", Sil), Ui.Muted("Ara:", false), _ara));
        _t.SelectionChanged += (_, _) => _ozet.Text = _t.Current?.S("ozet") ?? "";
        _t.MouseDoubleClick += (_, _) => Duzenle();
        var alt = new Card { Margin = new Thickness(0, 10, 0, 0) };
        alt.Top(Ui.H2("Özet / Not"));
        alt.Fill(_ozet);
        var dock = new DockPanel();
        DockPanel.SetDock(alt, Dock.Bottom);
        dock.Children.Add(alt); dock.Children.Add(_t);
        Fill(dock);
    }

    public override void Refresh()
    {
        var q = Utils.TrLower(_ara.Text.Trim());
        var rows = Db.Query("SELECT e.*, TRIM(COALESCE(d.mahkeme_adi,'') || ' ' || COALESCE(d.esas_no,'')) AS dava FROM emsal_notlari e LEFT JOIN davalar d ON d.id=e.dava_id ORDER BY e.id DESC");
        if (q.Length > 0)
            rows = rows.Where(r => Utils.TrLower($"{r.S("baslik")} {r.S("kaynak")} {r.S("konu")} {r.S("etiketler")} {r.S("ozet")} {r.S("dava")}").Contains(q)).ToList();
        _t.Load(rows);
    }

    List<Field> Fields() => new()
    {
        new("baslik", "Başlık", required: true),
        new("kaynak", "Kaynak (daire, esas/karar no, kitap...)"),
        new("karar_tarihi", "Karar tarihi", "date"),
        new("konu", "Konu"),
        new("etiketler", "Etiketler (virgülle)"),
        new("dava_id", "Bağlı dosya", "combo", Secenek.Davalar(Db), def: 0L),
        new("ozet", "Özet / not", "multiline"),
    };

    void Ekle()
    {
        var v = FormDialog.Ask(this, "Yeni Not / Karar", Fields());
        if (v == null) return;
        Db.Insert("emsal_notlari", v);
        Refresh();
    }

    void Duzenle()
    {
        var c = _t.Current;
        if (c == null) { Ui.Info(this, "Önce bir kayıt seçin."); return; }
        var v = FormDialog.Ask(this, "Not / Karar", Fields(), Db.One("SELECT * FROM emsal_notlari WHERE id=?", c.L("id")));
        if (v == null) return;
        Db.Update("emsal_notlari", c.L("id"), v);
        Refresh();
    }

    void Sil()
    {
        var c = _t.Current;
        if (c != null && Ui.Confirm(this, "Kayıt silinsin mi?")) { Db.Delete("emsal_notlari", c.L("id")); Refresh(); }
    }
}
