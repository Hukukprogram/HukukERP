using System.Windows;
using System.Windows.Controls;
using HukukErp.Core;

namespace HukukErp.UI.Pages;

/// <summary>Çöp kutusu: silinen müvekkil, dava, süre, görev vb. kayıtlar 60 gün saklanır; buradan geri alınır.</summary>
public class CopKutusuPage : BasePage
{
    static readonly Dictionary<string, string> Ad = new()
    {
        ["muvekkiller"] = "Müvekkil", ["davalar"] = "Dava", ["sureler"] = "Süreli iş", ["gorevler"] = "Görev", ["ajanda"] = "Ajanda",
        ["finans_tahsilat"] = "Cari kayıt", ["tahsilat_hareketleri"] = "Tahsilat", ["vekaletnameler"] = "Vekaletname",
        ["dava_masraflari"] = "Masraf", ["genel_tebligatlar"] = "Tebligat", ["emsal_notlari"] = "Emsal/Not", ["sablonlar"] = "Şablon",
    };

    readonly DataTable _t = new(("Silinme", "silinme", null), ("Tür", "tur", null), ("Kayıt", "ozet", null), ("Alt kayıt", "alt", null));

    public CopKutusuPage(AppCtx ctx) : base(ctx)
    {
        Top(Ui.PageHeader("Çöp Kutusu", "Silinen kayıtlar 60 gün burada saklanır • yanlışlıkla silineni geri alabilirsiniz"));
        Top(Ui.HRow(Ui.Btn("↩ Geri Al", "Primary", GeriAl), Ui.Btn("Kalıcı Sil", "Danger", KaliciSil), Ui.Btn("Çöp Kutusunu Boşalt", "Danger", Bosalt)));
        _t.MouseDoubleClick += (_, _) => GeriAl();
        Fill(_t);
    }

    public override void Refresh()
    {
        var rows = Db.Query("SELECT c.*, (SELECT COUNT(*) FROM cop_kutusu a WHERE a.ust_id=c.id) AS altsay FROM cop_kutusu c WHERE c.ust_id=0 ORDER BY c.id DESC");
        foreach (var r in rows)
        {
            r["tur"] = Ad.TryGetValue(r.S("tablo"), out var a) ? a : r.S("tablo");
            r["alt"] = r.L("altsay") > 0 ? $"+{r.L("altsay")} bağlı kayıt" : "";
        }
        _t.Load(rows);
    }

    void GeriAl()
    {
        var c = _t.Current;
        if (c == null) { Ui.Info(this, "Önce listeden bir kayıt seçin."); return; }
        try
        {
            Db.CopGeriAl(c.L("id"));
            Ctx.Status("Kayıt geri alındı");
            Refresh();
        }
        catch (Exception e)
        {
            Ui.Warn(this, "Geri alınamadı. Aynı T.C. numarası ya da aynı kayıt şu an zaten var olabilir.\n\n" + e.Message);
        }
    }

    void KaliciSil()
    {
        var c = _t.Current;
        if (c != null && Ui.Confirm(this, "Bu kayıt kalıcı silinsin mi? Geri alınamaz.")) { Db.CopSil(c.L("id")); Refresh(); }
    }

    void Bosalt()
    {
        if (_t.Rows.Count == 0) return;
        if (!Ui.Confirm(this, "Çöp kutusundaki TÜM kayıtlar kalıcı silinsin mi? Geri alınamaz.")) return;
        foreach (var r in _t.Rows.ToList()) Db.CopSil(r.L("id"));
        Refresh();
    }
}
