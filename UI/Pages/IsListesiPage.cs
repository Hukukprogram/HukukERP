using System.Windows;
using System.Windows.Controls;
using HukukErp.Core;
using HukukErp.Services;

namespace HukukErp.UI.Pages;

/// <summary>Süreli işler (iş listesi): son tarihi olan işler; UETS tebligatlarına ve dava dosyalarına bağlanabilir.</summary>
public class IsListesiPage : BasePage
{
    static readonly string[] Turler = { "Hukuk", "İcra", "Ceza", "İdari", "İş", "Yüksek Mahkeme", "Yasal Süre", "Mahkeme Verdiği Süre", "Tebligat Cevabı", "Diğer" };
    static readonly string[] Durumlar = { "Açık", "Tamamlandı" };

    readonly DataTable _t;
    readonly ComboBox _filtre = new() { Width = 150, Margin = new Thickness(0, 0, 8, 0) };
    readonly TextBox _ara = new() { Width = 220, ToolTip = "Başlık, dayanak, dava veya tebligat içinde ara" };
    bool _busy;

    public IsListesiPage(AppCtx ctx) : base(ctx)
    {
        Top(Ui.PageHeader("İş Listesi", "Son tarihi olan işler • UETS tebligatlarından tek tıkla eklenir • çift tıklayınca düzenlenir"));

        foreach (var x in new[] { "Açık İşler", "Tamamlananlar", "Tümü" }) _filtre.Items.Add(x);
        _filtre.SelectedIndex = 0;
        _filtre.SelectionChanged += (_, _) => { if (!_busy) Refresh(); };
        _ara.TextChanged += (_, _) => { if (!_busy) Refresh(); };

        Top(Ui.HRow(Ui.Btn("＋ Yeni İş", "Primary", Ekle), Ui.Btn("⚖ Tebligat Süresi Ekle (Katalog)", "Primary", KatalogEkle), Ui.Btn("Düzenle", "", Duzenle),
                    Ui.Btn("✔ Tamamlandı", "Ok", () => DurumYap("Tamamlandı")), Ui.Btn("↺ Yeniden Aç", "", () => DurumYap("Açık")),
                    Ui.Btn("Sil", "Danger", Sil)));
        Top(Ui.HRow(Ui.Muted("Göster:", false), _filtre, Ui.Muted("Ara:", false), _ara,
                    Ui.Btn("📬 UETS'e Git", "", () => Ctx.Navigate("avukat_uets", null)),
                    Ui.Btn("📁 Davaya Git", "", () => Ctx.Navigate("dava", null))));

        _t = new DataTable(("Son Tarih", "son_tarih", Fmt.Date), ("Kalan", "kalan_txt", null), ("İş", "baslik", null),
                           ("Tür", "sure_turu", null), ("Dayanak", "dayanak", null), ("Dava", "dava", null),
                           ("UETS", "uets", null), ("Durum", "durum", null));
        _t.MouseDoubleClick += (_, _) => Duzenle();
        Fill(_t);
    }

    public override void Refresh()
    {
        _busy = true;
        try
        {
            var f = _filtre.SelectedIndex;
            var q = Utils.TrLower(_ara.Text.Trim());
            var rows = Db.Query(
                "SELECT s.*, TRIM(COALESCE(d.mahkeme_adi,'') || ' ' || COALESCE(d.esas_no,'')) AS dava, " +
                "CASE WHEN s.tebligat_id>0 THEN TRIM(COALESCE(g.gonderen_kurum,'') || ' ' || COALESCE(g.esas_no,'')) ELSE '' END AS uets " +
                "FROM sureler s LEFT JOIN davalar d ON d.id=s.dava_id LEFT JOIN genel_tebligatlar g ON g.id=s.tebligat_id " +
                (f == 0 ? "WHERE s.durum='Açık' " : f == 1 ? "WHERE s.durum='Tamamlandı' " : "") +
                "ORDER BY CASE s.durum WHEN 'Açık' THEN 0 ELSE 1 END, s.son_tarih");
            if (q.Length > 0)
                rows = rows.Where(r => Utils.TrLower(r.S("baslik") + " " + r.S("dayanak") + " " + r.S("dava") + " " + r.S("uets") + " " + r.S("notlar")).Contains(q)).ToList();
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

    /// <summary>Süreli iş formu. values içindeki tebligat_id sonuca aynen taşınır.</summary>
    public static Dictionary<string, object?>? Sor(DependencyObject? owner, Db db, IDictionary<string, object?>? values = null)
    {
        var dv = new List<(object? Value, string Label)> { (0L, "— Bağlı dava yok —") };
        foreach (var d in db.Query("SELECT d.id, d.mahkeme_adi, d.esas_no, m.ad_soyad FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id ORDER BY d.id DESC"))
            dv.Add((d.L("id"), $"{d.S("mahkeme_adi")} {d.S("esas_no")} — {d.S("ad_soyad")}".Trim(' ', '—', ' ')));
        var fields = new List<Field>
        {
            new("baslik", "İş / Süre Başlığı", required: true),
            new("sure_turu", "Süre Türü", "combo", Field.Of(Turler), def: "Yasal Süre"),
            new("dayanak", "Dayanak (kanun maddesi / tebligat vb.)"),
            new("baslangic", "Başlangıç (tebliğ) Tarihi", "date"),
            new("son_tarih", "Son Tarih", "date", required: true),
            new("dava_id", "Bağlı Dava", "combo", dv, def: 0L),
            new("notlar", "Notlar", "multiline"),
            new("durum", "Durum", "combo", Field.Of(Durumlar), def: "Açık"),
        };
        var r = FormDialog.Ask(owner, values != null && values.ContainsKey("id") ? "Süreli İşi Düzenle" : "Yeni Süreli İş", fields, values);
        if (r == null) return null;
        if (values != null && values.TryGetValue("tebligat_id", out var tb) && tb != null) r["tebligat_id"] = tb;
        return r;
    }

    public void YeniIs() => Ekle();
    public void KatalogtanSure() => KatalogEkle();

    void Ekle()
    {
        var v = Sor(this, Db);
        if (v == null) return;
        Db.Insert("sureler", v);
        Refresh();
    }

    /// <summary>Katalogdan süre seçtirir, son tarihi hesaplar ve iş formu için hazır değerleri döndürür (iptalde null).
    /// tebligat verilirse başlangıç tarihi, metin eşleşmesi ve bağlantı (tebligat_id) ondan gelir.</summary>
    public static Dictionary<string, object?>? KatalogHazirla(DependencyObject? owner, Db db, Row? tebligat)
    {
        var liste = IsSureleri.Liste(Path.GetDirectoryName(db.Path));
        var opts = new List<(object? Value, string Label)> { (-1L, "— Katalogda yok: elle gireceğim —") };
        for (int i = 0; i < liste.Count; i++) opts.Add(((long)i, liste[i].Ad));

        long varsayilan = -1;
        if (tebligat != null)
        {
            var eslesme = IsSureleri.EnIyiEslesme($"{tebligat.S("konu")} {tebligat.S("gonderen_kurum")} {tebligat.S("ayrisan_json")}");
            if (eslesme >= 0) varsayilan = eslesme;
        }
        var f = new List<Field>
        {
            new("sure", "Süre (tebligata göre önerilen seçili gelir)", "combo", opts, def: varsayilan),
            new("baslangic", "UETS'e ulaşma / tebliğ tarihi", "date", def: tebligat?.S("teblig_tarihi") ?? ""),
            new("etebligat", $"UETS e-tebligatı (tebliğ tarihi = ulaşma + {IsSureleri.ETebligatGunu} gün)", "check", def: 1L),
            new("degisken", "Süre elle (gün) – yalnız ara kararda yazan süreler için", "int", def: 0L),
        };
        var a = FormDialog.Ask(owner, "Tebligat Süresi", f);
        if (a == null) return null;

        var v = new Dictionary<string, object?>();
        if (tebligat != null)
        {
            v["tebligat_id"] = tebligat.L("id");
            if (tebligat.L("dava_id") > 0) v["dava_id"] = tebligat.L("dava_id");
            v["baslik"] = $"{tebligat.S("gonderen_kurum")} {tebligat.S("esas_no")}".Trim();
            v["baslangic"] = tebligat.S("teblig_tarihi");
            v["dayanak"] = $"UETS tebligatı ({Utils.FmtDate(tebligat.S("teblig_tarihi"))}) – {tebligat.S("konu")}".Trim(' ', '–');
        }
        long idx = Convert.ToInt64(a["sure"] ?? -1L);
        if (idx < 0)
        {
            if (tebligat != null) v["son_tarih"] = tebligat.S("son_cevap_tarihi");
            return v;
        }
        var bas = Utils.ParseDate(Convert.ToString(a["baslangic"]));
        if (bas == null) { Ui.Warn(owner, "Geçerli bir tarih girin."); return null; }
        var sd = liste[(int)idx];
        int deg = (int)Convert.ToInt64(a["degisken"] ?? 0L);
        if (sd.Sure == null && deg <= 0) { Ui.Warn(owner, "Bu süre ara kararda yazar; gün sayısını girin."); return null; }
        var sn = IsSureleri.Hesapla(sd, bas.Value, Convert.ToInt64(a["etebligat"] ?? 0L) == 1, deg);

        var oncekiBaslik = v.TryGetValue("baslik", out var bb) ? Convert.ToString(bb) : "";
        v["baslik"] = ($"{sd.Islem} – {oncekiBaslik}").Trim(' ', '–');
        v["sure_turu"] = Turler.Contains(sd.Kategori) ? sd.Kategori : "Yasal Süre";
        v["dayanak"] = sd.Kanun;
        v["baslangic"] = Utils.Iso(sn.Tebligat);
        v["son_tarih"] = Utils.Iso(sn.Son);
        v["notlar"] = (sd.Not.Length > 0 ? sd.Not + "\n" : "") + sn.Aciklama +
                      "\nHesap yardımcıdır; süreyi kendiniz de kontrol edin.";
        return v;
    }

    void KatalogEkle()
    {
        var h = KatalogHazirla(this, Db, null);
        if (h == null) return;
        var v = Sor(this, Db, h);
        if (v == null) return;
        Db.Insert("sureler", v);
        Refresh();
    }

    void Duzenle()
    {
        var c = _t.Current;
        if (c == null) { Ui.Info(this, "Önce bir iş seçin."); return; }
        var v = Sor(this, Db, Db.One("SELECT * FROM sureler WHERE id=?", c.L("id")));
        if (v == null) return;
        Db.Update("sureler", c.L("id"), v);
        Refresh();
    }

    void DurumYap(string durum)
    {
        var c = _t.Current;
        if (c == null) { Ui.Info(this, "Önce bir iş seçin."); return; }
        Db.Update("sureler", c.L("id"), new Dictionary<string, object?> { ["durum"] = durum });
        Refresh();
    }

    void Sil()
    {
        var c = _t.Current;
        if (c != null && Ui.Confirm(this, "Seçili iş silinsin mi?")) { Db.Delete("sureler", c.L("id")); Refresh(); }
    }
}
