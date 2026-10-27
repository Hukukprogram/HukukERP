using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HukukErp.Core;

namespace HukukErp.UI.Pages;

/// <summary>Genel arama: müvekkil, dava, tebligat, süre, görev ve ajanda kayıtlarında tek kutudan arar. Çift tıklayınca ilgili sayfa açılır.</summary>
public class AramaPage : BasePage
{
    readonly TextBox _kutu = new() { MinWidth = 420, Margin = new Thickness(0, 0, 8, 0) };
    readonly TextBlock _sayi = Ui.Muted("");
    readonly DataTable _t = new(("Tür", "tur", null), ("Kayıt", "baslik", null), ("Ayrıntı", "detay", null));

    public AramaPage(AppCtx ctx) : base(ctx)
    {
        Top(Ui.PageHeader("Genel Arama", "Ad, T.C., esas no, kurum, konu, not… yazın • çift tıklayınca kaydın sayfası açılır"));
        _kutu.KeyDown += (_, e) => { if (e.Key == Key.Enter) Ara(); };
        Top(Ui.HRow(_kutu, Ui.Btn("🔎 Ara", "Primary", Ara), _sayi));
        _t.MouseDoubleClick += (_, _) => Git();
        Fill(_t);
    }

    public override void Refresh() => _kutu.Focus();

    static Row R(string tur, string baslik, string detay, string sayfa, long id) =>
        new() { ["tur"] = tur, ["baslik"] = baslik, ["detay"] = detay, ["sayfa"] = sayfa, ["kid"] = id };

    void Ara()
    {
        var q = Utils.TrLower(_kutu.Text.Trim());
        if (q.Length < 2) { _sayi.Text = "En az 2 karakter yazın."; return; }
        bool Var(params string?[] alanlar) => alanlar.Any(a => !string.IsNullOrEmpty(a) && Utils.TrLower(a).Contains(q));
        var l = new List<Row>();

        foreach (var m in Db.Query("SELECT * FROM muvekkiller"))
            if (Var(m.S("ad_soyad"), m.S("tc_no"), m.S("telefon"), m.S("eposta"), m.S("adres")))
                l.Add(R("Müvekkil", m.S("ad_soyad"), $"{m.S("tc_no")} • {m.S("telefon")}", "muvekkil", m.L("id")));
        foreach (var d in Db.Query("SELECT d.*, m.ad_soyad FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id"))
            if (Var(d.S("mahkeme_adi"), d.S("esas_no"), d.S("karsi_taraf"), d.S("dava_turu"), d.S("genel_notlar"), d.S("ad_soyad")))
                l.Add(R("Dava", $"{d.S("mahkeme_adi")} {d.S("esas_no")}", $"{d.S("ad_soyad")} • karşı: {d.S("karsi_taraf")} • {d.S("asama_durum")}", "dosya_karti", d.L("id")));
        foreach (var t in Db.Query("SELECT * FROM genel_tebligatlar ORDER BY id DESC"))
            if (Var(t.S("gonderen_kurum"), t.S("konu"), t.S("esas_no"), t.S("ayrisan_json")))
                l.Add(R("Tebligat", $"{t.S("gonderen_kurum")} {t.S("esas_no")}".Trim(), $"{t.S("konu")} • {Utils.FmtDate(t.S("teblig_tarihi"))} • {t.S("durum")}", "avukat_uets", t.L("id")));
        foreach (var s in Db.Query("SELECT * FROM sureler"))
            if (Var(s.S("baslik"), s.S("dayanak"), s.S("notlar")))
                l.Add(R("Süreli İş", s.S("baslik"), $"Son tarih {Utils.FmtDate(s.S("son_tarih"))} • {s.S("durum")}", "is_listesi", s.L("id")));
        foreach (var g in Db.Query("SELECT * FROM gorevler"))
            if (Var(g.S("baslik"), g.S("aciklama")))
                l.Add(R("Görev", g.S("baslik"), $"{g.S("durum")} • {Utils.FmtDate(g.S("son_tarih"))}", "kanban", g.L("id")));
        foreach (var a in Db.Query("SELECT * FROM ajanda"))
            if (Var(a.S("baslik"), a.S("mahkeme"), a.S("esas_no"), a.S("notlar")))
                l.Add(R("Ajanda", a.S("baslik"), $"{Utils.FmtDate(a.S("tarih"))} {a.S("saat")} • {a.S("mahkeme")}", "ajanda", a.L("id")));

        foreach (var e in Db.Query("SELECT * FROM emsal_notlari"))
            if (Var(e.S("baslik"), e.S("kaynak"), e.S("konu"), e.S("etiketler"), e.S("ozet")))
                l.Add(R("Emsal/Not", e.S("baslik"), $"{e.S("kaynak")} • {e.S("konu")}", "emsal", e.L("id")));
        _t.Load(l);
        _sayi.Text = $"{l.Count} sonuç";
    }

    void Git()
    {
        var c = _t.Current;
        if (c == null) return;
        var sayfa = c.S("sayfa");
        if (sayfa == "dosya_karti") DosyaKartiPage.Hedef = c.L("kid");
        Ctx.Navigate(sayfa, null);
    }
}
