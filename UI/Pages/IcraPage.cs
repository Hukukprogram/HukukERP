using System.Windows;
using System.Windows.Controls;
using HukukErp.Core;

namespace HukukErp.UI.Pages;

/// <summary>İcra dosyaları: bireysel takip dosyalarının listesi (icra dairesi, esas no, borçlu, alacak / tahsilat / kalan, durum, sıradaki işlem).</summary>
public class IcraPage : BasePage
{
    static readonly string[] Durumlar = { "Derdest", "Haciz aşamasında", "Satış aşamasında", "Ödeme bekleniyor", "İtiraz / dava", "Kapandı" };
    static readonly string[] Turler = { "İlamsız", "İlamlı", "Kambiyo senedi", "Kira alacağı (tahliye)", "Rehin / ipotek", "Diğer" };

    readonly DataTable _t = new(("Müvekkil", "muvekkil", null), ("İcra Dairesi", "icra_dairesi", null), ("Esas No", "esas_no", null), ("Borçlu", "borclu", null),
                                ("Takip", "takip_tarihi", Fmt.Date), ("Kesinleşme", "kesinlesme_tarihi", Fmt.Date), ("Alacak", "alacak_tutari", Fmt.Money), ("Tahsil", "tahsil_edilen", Fmt.Money),
                                ("Kalan", "kalan", Fmt.Money), ("Durum", "durum", null), ("Açık Görev", "gorev_txt", null));
    readonly TextBox _ara = new() { Width = 260, ToolTip = "Müvekkil, borçlu, icra dairesi veya esas no ile ara" };
    readonly ComboBox _filtre = new() { MinWidth = 190 };
    readonly TextBlock _ozet = Ui.Muted("", false);
    bool _busy;

    public IcraPage(AppCtx ctx) : base(ctx)
    {
        Top(Ui.PageHeader("İcra Dosyaları", "Bireysel icra takipleri • çift tıklayınca düzenlenir • süreli görevler \"İcra Görevleri\" sayfasında da listelenir"));
        _filtre.Items.Add("Derdest dosyalar"); _filtre.Items.Add("Kapananlar"); _filtre.Items.Add("Tümü");
        _filtre.SelectedIndex = 0;
        _filtre.SelectionChanged += (_, _) => { if (!_busy) Refresh(); };
        _ara.TextChanged += (_, _) => { if (!_busy) Refresh(); };
        Top(Ui.HRow(Ui.Btn("＋ Yeni İcra Dosyası", "Primary", Yeni), Ui.Btn("Düzenle", "", Duzenle), Ui.Btn("⏱ Süreli görev ekle", "Primary", GorevEkle), Ui.Btn("＋ Tahsilat işle", "Ok", Tahsilat), Ui.Btn("Sil", "Danger", Sil),
                    Ui.Muted("Göster:", false), _filtre, Ui.Muted("Ara:", false), _ara));
        _ozet.Margin = new Thickness(0, 0, 0, 8);
        Top(_ozet, 0);
        _t.MouseDoubleClick += (_, _) => Duzenle();
        Fill(_t);
    }

    public override void Refresh()
    {
        _busy = true;
        try
        {
            var rows = Db.Query("SELECT i.*, COALESCE(m.ad_soyad,'—') AS muvekkil, " +
                "(SELECT COUNT(*) FROM sureler s WHERE s.icra_id=i.id AND s.durum='Açık') AS gorev_say, " +
                "(SELECT MIN(s.son_tarih) FROM sureler s WHERE s.icra_id=i.id AND s.durum='Açık') AS gorev_son " +
                "FROM icra_takipleri i LEFT JOIN muvekkiller m ON m.id=i.muvekkil_id ORDER BY i.id DESC");
            var q = Utils.TrLower(_ara.Text.Trim());
            var list = new List<Row>();
            foreach (var r in rows)
            {
                bool kapali = r.S("durum") == "Kapandı";
                if (_filtre.SelectedIndex == 0 && kapali) continue;
                if (_filtre.SelectedIndex == 1 && !kapali) continue;
                if (q.Length > 0 && !Utils.TrLower($"{r.S("muvekkil")} {r.S("borclu")} {r.S("icra_dairesi")} {r.S("esas_no")}").Contains(q)) continue;
                r["kalan"] = r.D("alacak_tutari") - r.D("tahsil_edilen");
                r["gorev_txt"] = r.L("gorev_say") == 0 ? "" : $"{r.L("gorev_say")} • en yakın {Utils.FmtDate(r.S("gorev_son"))}";
                list.Add(r);
            }
            _t.Load(list, new() { ["durum"] = r => r.S("durum") == "Kapandı" ? "ok" : "warn", ["kalan"] = r => r.D("kalan") > 0 ? "danger" : "ok" });
            _ozet.Text = $"{list.Count} dosya • toplam alacak {Utils.FmtMoney(list.Sum(r => r.D("alacak_tutari")))} • tahsil edilen {Utils.FmtMoney(list.Sum(r => r.D("tahsil_edilen")))} • kalan {Utils.FmtMoney(list.Sum(r => r.D("kalan")))}";
        }
        finally { _busy = false; }
    }

    List<Field> Fields() => new()
    {
        new("muvekkil_id", "Müvekkil", "combo", Secenek.Muvekkiller(Db), required: true),
        new("icra_dairesi", "İcra Dairesi", required: true),
        new("esas_no", "Esas No (örn. 2026/123)"),
        new("takip_turu", "Takip Türü", "combo", Field.Of(Turler), def: "İlamsız"),
        new("borclu", "Borçlu", required: true),
        new("borclu_tc", "Borçlu T.C. / Vergi No"),
        new("takip_tarihi", "Takip Tarihi", "date", def: Utils.TodayIso()),
        new("kesinlesme_tarihi", "Kesinleşme Tarihi (ilamlı takipte)", "date"),
        new("alacak_tutari", "Alacak Tutarı (asıl + işlemiş faiz vb.)", "money"),
        new("tahsil_edilen", "Tahsil Edilen", "money"),
        new("durum", "Durum", "combo", Field.Of(Durumlar), def: "Derdest"),
        new("notlar", "Notlar", "multiline"),
    };

    public void Yeni()
    {
        var v = FormDialog.Ask(this, "Yeni İcra Dosyası", Fields());
        if (v == null) return;
        Db.Insert("icra_takipleri", v);
        Refresh();
    }

    void Duzenle()
    {
        var c = _t.Current;
        if (c == null) { Ui.Info(this, "Önce bir dosya seçin."); return; }
        var v = FormDialog.Ask(this, "İcra Dosyası", Fields(), Db.One("SELECT * FROM icra_takipleri WHERE id=?", c.L("id")));
        if (v == null) return;
        Db.Update("icra_takipleri", c.L("id"), v);
        Refresh();
    }

    void GorevEkle()
    {
        var c = _t.Current;
        if (c == null) { Ui.Info(this, "Önce bir icra dosyası seçin."); return; }
        IcraGorevPage.GorevEkle(this, Db, c.L("id"));
        Refresh();
    }

    void Tahsilat()
    {
        var c = _t.Current;
        if (c == null) { Ui.Info(this, "Önce bir dosya seçin."); return; }
        var v = FormDialog.Ask(this, "Tahsilat İşle", new List<Field> { new("tutar", "Tahsil edilen tutar", "money", required: true) });
        if (v == null) return;
        var yeni = c.D("tahsil_edilen") + Convert.ToDouble(v["tutar"] ?? 0.0);
        Db.Update("icra_takipleri", c.L("id"), new Dictionary<string, object?> { ["tahsil_edilen"] = yeni });
        Refresh();
    }

    void Sil()
    {
        var c = _t.Current;
        if (c != null && Ui.Confirm(this, "İcra dosyası silinsin mi?")) { Db.Delete("icra_takipleri", c.L("id")); Refresh(); }
    }
}
