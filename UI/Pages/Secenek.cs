using HukukErp.Core;

namespace HukukErp.UI.Pages;

/// <summary>Formlardaki açılır listeler için ortak seçenek üreticileri.</summary>
public static class Secenek
{
    public static List<(object? Value, string Label)> Davalar(Db db, string bos = "— seçilmedi —")
    {
        var l = new List<(object? Value, string Label)> { (0L, bos) };
        foreach (var d in db.Query("SELECT d.id, d.mahkeme_adi, d.esas_no, m.ad_soyad FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id ORDER BY d.id DESC"))
            l.Add((d.L("id"), $"{d.S("mahkeme_adi")} {d.S("esas_no")} — {d.S("ad_soyad")}".Trim(' ', '—', ' ')));
        return l;
    }

    public static List<(object? Value, string Label)> Muvekkiller(Db db)
    {
        var l = new List<(object? Value, string Label)>();
        foreach (var m in db.Query("SELECT id, ad_soyad, tc_no FROM muvekkiller ORDER BY ad_soyad"))
            l.Add((m.L("id"), $"{m.S("ad_soyad")} ({m.S("tc_no")})"));
        return l;
    }
}
