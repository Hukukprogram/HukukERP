using System.Windows;
using System.Windows.Controls;
using HukukErp.Core;

namespace HukukErp.UI.Pages;

/// <summary>İcra görevleri: icra dosyalarına bağlı süreli görevlerin tek listesi (aynı kayıtlar İş Listesi'nde de görünür).</summary>
public class IcraGorevPage : BasePage
{
    readonly DataTable _t = new(("Son Tarih", "son_tarih", Fmt.Date), ("Kalan", "kalan_txt", null), ("Görev", "baslik", null), ("İcra Dosyası", "dosya", null),
                                ("Borçlu", "borclu", null), ("Müvekkil", "muvekkil", null), ("Durum", "durum", null));
    readonly TextBox _ara = new() { Width = 240, ToolTip = "Görev, icra dairesi, esas no, borçlu veya müvekkil içinde ara" };
    readonly ComboBox _filtre = new() { MinWidth = 160 };
    bool _busy;

    public IcraGorevPage(AppCtx ctx) : base(ctx)
    {
        Top(Ui.PageHeader("İcra Görevleri", "İcra dosyalarına bağlı süreli görevler • çift tıklayınca düzenlenir"));
        _filtre.Items.Add("Açık görevler"); _filtre.Items.Add("Tamamlananlar"); _filtre.Items.Add("Tümü");
        _filtre.SelectedIndex = 0;
        _filtre.SelectionChanged += (_, _) => { if (!_busy) Refresh(); };
        _ara.TextChanged += (_, _) => { if (!_busy) Refresh(); };
        Top(Ui.HRow(Ui.Btn("＋ Yeni İcra Görevi", "Primary", Yeni), Ui.Btn("Düzenle", "", Duzenle),
                    Ui.Btn("✔ Tamamlandı", "Ok", () => DurumYap("Tamamlandı")), Ui.Btn("↺ Yeniden Aç", "", () => DurumYap("Açık")), Ui.Btn("Sil", "Danger", Sil),
                    Ui.Muted("Göster:", false), _filtre, Ui.Muted("Ara:", false), _ara));
        _t.MouseDoubleClick += (_, _) => Duzenle();
        var hBaslik = Ui.H2("🔒 Haciz düşme takibi (uyarı süresine giren hacizler)");
        hBaslik.Margin = new Thickness(0, 10, 0, 4);
        var hAlt = new DockPanel();
        DockPanel.SetDock(hBaslik, Dock.Top);
        hAlt.Children.Add(hBaslik); hAlt.Children.Add(_tH);
        Fill(Ui.VSplit(_t, hAlt, 2, 1));
    }

    readonly DataTable _tH = new(("Tür", "tur", null), ("Açıklama", "aciklama", null), ("Müvekkil", "muvekkil", null), ("Borçlu", "borclu", null),
                                 ("İcra Dosyası", "dosya", null), ("Düşme Tarihi", "dusme_tarihi", Fmt.Date), ("Kalan", "kalan_txt", null));

    public override void Refresh()
    {
        var hs = IcraHaciz.Uyarilar(Db);
        foreach (var h in hs) { h["dosya"] = $"{h.S("icra_dairesi")} {h.S("esas_no")}".Trim(); h["kalan_txt"] = IcraHaciz.Kalan(h); }
        _tH.Load(hs, new() { ["kalan_txt"] = IcraHaciz.Renk, ["dusme_tarihi"] = IcraHaciz.Renk });
        _busy = true;
        try
        {
            var f = _filtre.SelectedIndex;
            var rows = Db.Query(
                "SELECT s.*, TRIM(COALESCE(i.icra_dairesi,'') || ' ' || COALESCE(i.esas_no,'')) AS dosya, COALESCE(i.borclu,'') AS borclu, COALESCE(m.ad_soyad,'') AS muvekkil " +
                "FROM sureler s JOIN icra_takipleri i ON i.id=s.icra_id LEFT JOIN muvekkiller m ON m.id=i.muvekkil_id WHERE s.icra_id>0 " +
                (f == 0 ? "AND s.durum='Açık' " : f == 1 ? "AND s.durum='Tamamlandı' " : "") +
                "ORDER BY CASE s.durum WHEN 'Açık' THEN 0 ELSE 1 END, s.son_tarih");
            var q = Utils.TrLower(_ara.Text.Trim());
            if (q.Length > 0)
                rows = rows.Where(r => Utils.TrLower($"{r.S("baslik")} {r.S("dosya")} {r.S("borclu")} {r.S("muvekkil")} {r.S("notlar")}").Contains(q)).ToList();
            foreach (var r in rows)
            {
                var k = Utils.DaysLeft(r.S("son_tarih"));
                r["kalan_txt"] = r.S("durum") == "Tamamlandı" ? "✔" : k == null ? "" : k < 0 ? $"{-k} gün geçti" : k == 0 ? "BUGÜN" : $"{k} gün";
            }
            _t.Load(rows, new()
            {
                ["kalan_txt"] = r => r.S("durum") == "Tamamlandı" ? "ok" : Ui.DeadlineKind(r.S("son_tarih")),
                ["son_tarih"] = r => r.S("durum") == "Tamamlandı" ? "muted" : Ui.DeadlineKind(r.S("son_tarih")),
                ["durum"] = r => r.S("durum") == "Tamamlandı" ? "ok" : "warn",
            });
        }
        finally { _busy = false; }
    }

    /// <summary>Verilen icra dosyasına süreli görev ekler (süre/son gün hesaplı tek ekran). Eklenirse true.</summary>
    public static bool GorevEkle(DependencyObject owner, Db db, long icraId)
    {
        var ic = db.One("SELECT icra_dairesi, esas_no, borclu FROM icra_takipleri WHERE id=?", icraId);
        var baslik = ic == null ? "" : $"{ic.S("icra_dairesi")} {ic.S("esas_no")} – ".Trim();
        var v = IsDialog.Goster(owner, db, new Dictionary<string, object?> { ["icra_id"] = icraId, ["baslik"] = baslik, ["sure_turu"] = "İcra" });
        if (v == null) return false;
        db.Insert("sureler", v);
        return true;
    }

    public void Yeni()
    {
        var opts = new List<(object? Value, string Label)>();
        foreach (var i in Db.Query("SELECT id, icra_dairesi, esas_no, borclu FROM icra_takipleri WHERE durum<>'Kapandı' ORDER BY id DESC"))
            opts.Add((i.L("id"), $"{i.S("icra_dairesi")} {i.S("esas_no")} — {i.S("borclu")}".Trim(' ', '—', ' ')));
        if (opts.Count == 0) { Ui.Info(this, "Önce İcra Dosyaları sayfasından bir icra dosyası ekleyin."); return; }
        var a = FormDialog.Ask(this, "Hangi icra dosyası?", new List<Field> { new("icra", "İcra dosyası", "combo", opts, required: true) });
        if (a == null) return;
        if (GorevEkle(this, Db, Convert.ToInt64(a["icra"]))) Refresh();
    }

    void Duzenle()
    {
        var c = _t.Current;
        if (c == null) { Ui.Info(this, "Önce bir görev seçin."); return; }
        var v = IsDialog.Goster(this, Db, Db.One("SELECT * FROM sureler WHERE id=?", c.L("id")));
        if (v == null) return;
        Db.Update("sureler", c.L("id"), v);
        Refresh();
    }

    void DurumYap(string durum)
    {
        var c = _t.Current;
        if (c == null) { Ui.Info(this, "Önce bir görev seçin."); return; }
        Db.Update("sureler", c.L("id"), new Dictionary<string, object?> { ["durum"] = durum });
        Refresh();
    }

    void Sil()
    {
        var c = _t.Current;
        if (c != null && Ui.Confirm(this, "Görev silinsin mi?")) { Db.Delete("sureler", c.L("id")); Refresh(); }
    }
}
