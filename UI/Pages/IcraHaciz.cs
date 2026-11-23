using System.Windows;
using HukukErp.Core;

namespace HukukErp.UI.Pages;

/// <summary>İcra dosyalarındaki taşınmaz / taşınır hacizleri: haciz tarihi + süre (yıl / ay) → düşme tarihi; yaklaşınca uyarı.
/// Varsayılan süreler (taşınmaz 1 yıl, taşınır 6 ay) kullanıcının bildirdiği sürelerdir; her kayıtta değiştirilebilir. İİK m.106 ve güncel mevzuat için kontrol edin.</summary>
public static class IcraHaciz
{
    public static readonly string[] Durumlar = { "Devam", "Satış istendi", "Yenilendi", "Kalktı", "Düştü" };
    public const string Devam = "Devam";

    public static string Dusme(DateTime bas, long n, string birim) => Utils.Iso(birim == "Yıl" ? bas.AddYears((int)n) : bas.AddMonths((int)n));

    static List<Field> Alanlar(string tur, bool duzenle) 
    {
        var (n, b) = tur == "Taşınmaz" ? (1, "Yıl") : (6, "Ay");
        var f = new List<Field>
        {
            new("aciklama", tur == "Taşınmaz" ? "Taşınmaz (il / ilçe, ada – parsel, tapu bilgisi)" : "Taşınır (araç plakası, eşya, hak / alacak vb.)", required: true),
            new("haciz_tarihi", "Haciz tarihi", "date", def: Utils.TodayIso(), required: true),
            new("sure_deger", "Süre (kaç)", "int", def: (long)n, required: true),
            new("sure_birim", "Süre birimi", "combo", Field.Of(new[] { "Ay", "Yıl" }), def: b),
            new("uyari_gun", "Düşmeden kaç gün önce uyarılsın", "int", def: 30L),
        };
        if (duzenle) f.Add(new Field("durum", "Durum", "combo", Field.Of(Durumlar), def: Devam));
        f.Add(new Field("notlar", "Notlar", "multiline"));
        return f;
    }

    static Dictionary<string, object?>? Sor(DependencyObject owner, string baslik, string tur, bool duzenle, Row? mevcut)
    {
        var v = FormDialog.Ask(owner, baslik, Alanlar(tur, duzenle), mevcut);
        if (v == null) return null;
        var bas = Utils.ParseDate(Convert.ToString(v["haciz_tarihi"]));
        if (bas == null) { Ui.Warn(owner, "Haciz tarihini girin."); return null; }
        var n = Math.Max(1, Convert.ToInt64(v["sure_deger"] ?? 1L));
        var birim = Convert.ToString(v["sure_birim"]) == "Yıl" ? "Yıl" : "Ay";
        v["sure_deger"] = n; v["sure_birim"] = birim;
        v["dusme_tarihi"] = Dusme(bas.Value, n, birim);
        v["tur"] = tur;
        return v;
    }

    public static bool Ekle(DependencyObject owner, Db db, long icraId, string tur)
    {
        var v = Sor(owner, tur == "Taşınmaz" ? "Taşınmaz Haczi Ekle" : "Taşınır Haczi Ekle", tur, false, null);
        if (v == null) return false;
        v["icra_id"] = icraId; v["durum"] = Devam;
        db.Insert("icra_hacizler", v);
        return true;
    }

    public static bool Duzenle(DependencyObject owner, Db db, long id)
    {
        var h = db.One("SELECT * FROM icra_hacizler WHERE id=?", id);
        if (h == null) return false;
        var v = Sor(owner, "Haciz Kaydı", h.S("tur"), true, h);
        if (v == null) return false;
        db.Update("icra_hacizler", id, v);
        return true;
    }

    /// <summary>Haczi yeniler: eskisi "Yenilendi" olur, aynı bilgilerle yeni haciz tarihli yeni kayıt açılır.</summary>
    public static bool Yenile(DependencyObject owner, Db db, long id)
    {
        var h = db.One("SELECT * FROM icra_hacizler WHERE id=?", id);
        if (h == null) return false;
        var kopya = new Dictionary<string, object?>
        {
            ["aciklama"] = h.S("aciklama"), ["haciz_tarihi"] = Utils.TodayIso(), ["sure_deger"] = h.L("sure_deger"), ["sure_birim"] = h.S("sure_birim"),
            ["uyari_gun"] = h.L("uyari_gun"), ["notlar"] = h.S("notlar"),
        };
        var v = FormDialog.Ask(owner, "Haczi Yenile (yeni haciz tarihi)", Alanlar(h.S("tur"), false), kopya);
        if (v == null) return false;
        var bas = Utils.ParseDate(Convert.ToString(v["haciz_tarihi"]));
        if (bas == null) { Ui.Warn(owner, "Haciz tarihini girin."); return false; }
        var n = Math.Max(1, Convert.ToInt64(v["sure_deger"] ?? 1L));
        var birim = Convert.ToString(v["sure_birim"]) == "Yıl" ? "Yıl" : "Ay";
        v["sure_deger"] = n; v["sure_birim"] = birim; v["dusme_tarihi"] = Dusme(bas.Value, n, birim);
        v["tur"] = h.S("tur"); v["icra_id"] = h.L("icra_id"); v["durum"] = Devam;
        db.Update("icra_hacizler", id, new Dictionary<string, object?> { ["durum"] = "Yenilendi" });
        db.Insert("icra_hacizler", v);
        return true;
    }

    /// <summary>Düşme tarihine "uyarı günü" kadar kalan (veya geçmiş) devam eden hacizler.</summary>
    public static List<Row> Uyarilar(Db db) => db.Query(
        "SELECT h.*, i.borclu, i.icra_dairesi, i.esas_no, COALESCE(m.ad_soyad,'') AS muvekkil " +
        "FROM icra_hacizler h JOIN icra_takipleri i ON i.id=h.icra_id LEFT JOIN muvekkiller m ON m.id=i.muvekkil_id " +
        "WHERE h.durum='Devam' AND h.dusme_tarihi!='' AND i.durum<>'Kapandı' AND h.dusme_tarihi <= date('now', '+' || h.uyari_gun || ' day') " +
        "ORDER BY h.dusme_tarihi");

    public static long UyariSayisi(Db db) => Uyarilar(db).Count;

    public static string SureMetni(Row h) => $"{h.L("sure_deger")} {h.S("sure_birim")}";

    public static string Kalan(Row h)
    {
        if (h.S("durum") != Devam) return h.S("durum");
        var k = Utils.DaysLeft(h.S("dusme_tarihi"));
        return k == null ? "" : k < 0 ? $"{-k} gün önce düştü" : k == 0 ? "BUGÜN düşüyor" : $"{k} gün";
    }

    /// <summary>Renk: düşmüş / ≤7 gün = danger, uyarı süresi içinde = warn, aksi muted.</summary>
    public static string Renk(Row h)
    {
        if (h.S("durum") != Devam) return "muted";
        var k = Utils.DaysLeft(h.S("dusme_tarihi"));
        if (k == null) return "muted";
        return k <= 7 ? "danger" : k <= h.L("uyari_gun") ? "warn" : "muted";
    }
}
