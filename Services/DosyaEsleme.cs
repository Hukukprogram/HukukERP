using System.Text.RegularExpressions;
using HukukErp.Core;

namespace HukukErp.Services;

/// <summary>Tebligat–dava eşleştirme ve çıkar çatışması kontrolü.</summary>
public static class DosyaEsleme
{
    /// <summary>"2026/411 E." → "2026/411"</summary>
    public static string EsasNorm(string? s)
    {
        var m = Regex.Match(s ?? "", @"(\d{4})\s*/\s*(\d+)");
        return m.Success ? $"{m.Groups[1].Value}/{m.Groups[2].Value}" : "";
    }

    /// <summary>Esas numarası ve kuruma göre dava bulur. Tek aday varsa onu, birden çoksa kurum adı en çok örtüşeni döndürür; belirsizse 0.</summary>
    public static long DavaBul(Db db, string kurum, string esas)
    {
        var n = EsasNorm(esas);
        if (n.Length == 0) return 0;
        var adaylar = db.Query("SELECT id, mahkeme_adi, esas_no FROM davalar").Where(d => EsasNorm(d.S("esas_no")) == n).ToList();
        if (adaylar.Count == 1) return adaylar[0].L("id");
        if (adaylar.Count == 0) return 0;
        var kk = Kelimeler(kurum);
        var puanli = adaylar.Select(d => (d, p: Kelimeler(d.S("mahkeme_adi")).Count(x => kk.Contains(x)))).OrderByDescending(x => x.p).ToList();
        return puanli[0].p > 0 && (puanli.Count == 1 || puanli[0].p > puanli[1].p) ? puanli[0].d.L("id") : 0;
    }

    static HashSet<string> Kelimeler(string s) =>
        Utils.TrLower(s).Split(new[] { ' ', '.', ',', '-', '/' }, StringSplitOptions.RemoveEmptyEntries).Where(x => x.Length > 1).ToHashSet();

    static bool Benzer(string a, string b)
    {
        a = Utils.TrLower(a).Trim(); b = Utils.TrLower(b).Trim();
        if (a.Length < 4 || b.Length < 4) return false;
        return a == b || a.Contains(b) || b.Contains(a);
    }

    /// <summary>Yeni müvekkil/karşı taraf için olası çıkar çatışmalarını listeler (boşsa sorun yok).</summary>
    public static List<string> Catisma(Db db, string? muvekkilAd, string? karsiTaraf, long muvekkilId = 0)
    {
        var l = new List<string>();
        if (!string.IsNullOrWhiteSpace(karsiTaraf))
            foreach (var m in db.Query("SELECT id, ad_soyad FROM muvekkiller"))
                if (m.L("id") != muvekkilId && Benzer(m.S("ad_soyad"), karsiTaraf))
                    l.Add($"Karşı taraf '{karsiTaraf}', kayıtlı müvekkilinizle eşleşiyor: {m.S("ad_soyad")}");
        if (!string.IsNullOrWhiteSpace(muvekkilAd))
            foreach (var d in db.Query("SELECT mahkeme_adi, esas_no, karsi_taraf FROM davalar WHERE karsi_taraf!=''"))
                if (Benzer(d.S("karsi_taraf"), muvekkilAd))
                    l.Add($"'{muvekkilAd}', {d.S("mahkeme_adi")} {d.S("esas_no")} dosyasında karşı taraf olarak kayıtlı");
        return l.Distinct().ToList();
    }
}
