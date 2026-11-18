using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HukukErp.Core;

namespace HukukErp.Services;

/// <summary>
/// UETS "Tebligatlarım" listesinden okunan satırları (yalnızca OKUMA: hiçbir düğmeye basılmaz, hiçbir tebligat açılmaz)
/// uets_liste tablosuna eşitler. Yeni görünen satırlar "Yeni" olur; Dashboard, menü rozeti ve Telegram bundan beslenir.
/// </summary>
public static class UetsEsitleme
{
    /// <summary>Sayfadaki liste satırlarının metnini döndürür (JSON dizi, hücreler " | " ile ayrılmış). Hiçbir şeye tıklamaz.</summary>
    public const string SatirlarJs = @"(() => {
  const re = /\d{2}\.\d{2}\.\d{4}/;
  let rows = Array.from(document.querySelectorAll('table tbody tr')).filter(r => re.test(r.innerText || ''));
  if (rows.length === 0) {
    const gs = Array.from(document.querySelectorAll('a,button,span')).filter(e => (e.textContent || '').trim() === 'Görüntüle');
    const seen = new Set();
    for (const g of gs) {
      let el = g;
      for (let i = 0; i < 12 && el.parentElement; i++) {
        el = el.parentElement;
        if (re.test(el.innerText || '') && el.querySelectorAll('button,a').length >= 2) break;
      }
      if (!seen.has(el)) { seen.add(el); rows.push(el); }
    }
  }
  return JSON.stringify(rows.slice(0, 300).map(r => (r.innerText || '').replace(/[\t\r\n]+/g, ' | ').replace(/\s+/g, ' ').trim()).filter(t => t.length > 0));
})()";

    static readonly Regex ReTarih = new(@"(\d{2})[./](\d{2})[./](\d{4})");
    static readonly Regex ReEsas = new(@"\b\d{4}\s*/\s*\d{1,7}\b");
    static readonly Regex ReKurum = new(@"mahkeme|müdürlü|başkanl|daire|savcıl|bakanl|kurum|noter|baro|icra|hakem|üniversite|belediye|komisyon|kurul|banka", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly HashSet<string> Etiket = new() { "görüntüle", "indir", "sil", "okundu", "okunmadı", "okunmuş", "yeni", "detay", "seç" };

    public sealed record Satir(string Anahtar, string Tarih, string Kurum, string Konu, string Esas, string Ham);

    /// <summary>Ham satır metinlerini ayrıştırır. Anahtar: tarih + kurum + esas + (aynı üçlü için eskiden yeniye sıra no); okundu/okunmadı gibi durum değişikliği anahtarı bozmaz.</summary>
    public static List<Satir> Ayristir(IEnumerable<string> hamlar)
    {
        var liste = new List<(string Taban, string Tarih, string Kurum, string Konu, string Esas, string Ham)>();
        foreach (var ham in hamlar)
        {
            var tokenlar = ham.Split(" | ", StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
            var m = ReTarih.Match(ham);
            var tarih = m.Success ? (Utils.ParseDate($"{m.Groups[1].Value}.{m.Groups[2].Value}.{m.Groups[3].Value}") is { } d ? Utils.Iso(d) : "") : "";
            var esasM = ReEsas.Match(ham);
            var esas = esasM.Success ? Regex.Replace(esasM.Value, @"\s+", "") : "";
            bool Anlamli(string t) => t.Any(char.IsLetter) && !Etiket.Contains(Utils.TrLower(t)) && !ReTarih.IsMatch(t) && !Regex.IsMatch(t, @"^\d+$");
            var adaylar = tokenlar.Where(Anlamli).ToList();
            var kurum = adaylar.FirstOrDefault(t => ReKurum.IsMatch(t)) ?? adaylar.FirstOrDefault() ?? "";
            var konu = string.Join(" • ", adaylar.Where(t => t != kurum && !ReEsas.IsMatch(t) || (t != kurum && t.Length > 12)).Take(4));
            if (konu.Length > 220) konu = konu[..220];
            if (tarih.Length == 0 && kurum.Length == 0 && esas.Length == 0) continue;
            liste.Add(($"{tarih}|{Utils.TrLower(kurum)}|{esas}", tarih, kurum, konu, esas, ham.Length > 600 ? ham[..600] : ham));
        }
        // sıra no: listenin altı eski, üstü yeni kabul edilir; en eskiye #1 verilir (yeni satır eklenince eskilerin anahtarı değişmez)
        var say = new Dictionary<string, int>();
        var sonuc = new List<Satir>();
        for (int i = liste.Count - 1; i >= 0; i--)
        {
            var x = liste[i];
            say[x.Taban] = say.TryGetValue(x.Taban, out var n) ? n + 1 : 1;
            var anahtar = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{x.Taban}#{say[x.Taban]}")))[..24];
            sonuc.Add(new Satir(anahtar, x.Tarih, x.Kurum, x.Konu, x.Esas, x.Ham));
        }
        sonuc.Reverse();
        return sonuc;
    }

    /// <summary>Okunan satırları kaydeder. İlk eşitlemede (tabloda hiç kayıt yokken) mevcut satırlar "Görüldü" olarak alınır; yeni sayılmaz.</summary>
    public static (int Yeni, int Toplam, bool Ilk, List<Satir> YeniSatirlar) Kaydet(Db db, string tip, List<Satir> satirlar)
    {
        var ilk = db.ScalarL("SELECT COUNT(*) FROM uets_liste WHERE hesap_tipi=?", tip) == 0;
        var var_ = db.Query("SELECT anahtar FROM uets_liste WHERE hesap_tipi=?", tip).Select(r => r.S("anahtar")).ToHashSet();
        var yeniler = new List<Satir>();
        foreach (var s in satirlar)
        {
            if (var_.Contains(s.Anahtar)) continue;
            db.Execute("INSERT OR IGNORE INTO uets_liste (anahtar, hesap_tipi, tarih, kurum, konu, esas_no, satir, durum) VALUES (?,?,?,?,?,?,?,?)",
                s.Anahtar, tip, s.Tarih, s.Kurum, s.Konu, s.Esas, s.Ham, ilk ? "Görüldü" : "Yeni");
            var_.Add(s.Anahtar);
            if (!ilk) yeniler.Add(s);
        }
        return (yeniler.Count, satirlar.Count, ilk, yeniler);
    }

    public static long YeniSayisi(Db db) => db.ScalarL("SELECT COUNT(*) FROM uets_liste WHERE hesap_tipi='Sahsi' AND durum='Yeni'");

    /// <summary>İndirilen/işlenen bir tebligat aynı esas numarasıyla listede "Yeni" duruyorsa birini "İşlendi" yapar.</summary>
    public static void Isle(Db db, string esasNo)
    {
        var e = Regex.Replace(esasNo ?? "", @"\s+", "");
        if (e.Length == 0) return;
        try
        {
            db.Execute("UPDATE uets_liste SET durum='İşlendi' WHERE id=(SELECT id FROM uets_liste WHERE durum='Yeni' AND esas_no=? ORDER BY id LIMIT 1)", e);
        }
        catch (Exception) { }
    }
}
