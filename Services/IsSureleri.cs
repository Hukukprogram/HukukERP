using System.Text.Json;
using HukukErp.UI.Pages;

namespace HukukErp.Services;

/// <summary>Tebligat sürelerinin kataloğu (tebligat_sureleri.json). Veri klasöründeki dosya varsa o, yoksa programla gelen dosya kullanılır;
/// böylece yeni süre eklemek için JSON'a satır eklemek yeterlidir.</summary>
public static class IsSureleri
{
    public class SureTanim
    {
        public string Kod { get; set; } = "";
        public string Kategori { get; set; } = "";
        public List<string> AnahtarKelimeler { get; set; } = new();
        public string Islem { get; set; } = "";
        public int? Sure { get; set; }
        public string Birim { get; set; } = "gun";   // gun | hafta | ay | degisken
        public string Kanun { get; set; } = "";
        public string AdliTatil { get; set; } = "YOK"; // HMK | CMK | IYUK | YOK
        public string Not { get; set; } = "";
        public string Ad => $"{Kategori} – {Islem}" + (Sure != null ? $" ({Sure} {BirimAd(Birim)})" : " (süre elle)");
    }

    static string BirimAd(string b) => b switch { "gun" => "gün", "hafta" => "hafta", "ay" => "ay", _ => b };

    static List<SureTanim>? _liste;
    static int _eGun = 5;

    public static int ETebligatGunu { get { Yukle(); return _eGun; } }

    public static List<SureTanim> Liste(string? veriKlasoru = null)
    {
        if (_liste == null) Yukle(veriKlasoru);
        return _liste!;
    }

    static void Yukle(string? veriKlasoru = null)
    {
        if (_liste != null) return;
        var adaylar = new List<string>();
        if (!string.IsNullOrEmpty(veriKlasoru)) adaylar.Add(Path.Combine(veriKlasoru, "tebligat_sureleri.json"));
        adaylar.Add(Path.Combine(AppContext.BaseDirectory, "tebligat_sureleri.json"));
        foreach (var yol in adaylar)
        {
            try
            {
                if (!File.Exists(yol)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(yol), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                var root = doc.RootElement;
                if (root.TryGetProperty("hesapKurallari", out var hk) && hk.TryGetProperty("eTebligatTebligGunu", out var eg) && eg.TryGetInt32(out var n)) _eGun = n;
                var l = new List<SureTanim>();
                foreach (var e in root.GetProperty("sureler").EnumerateArray())
                {
                    var t = new SureTanim
                    {
                        Kod = Str(e, "kod"), Kategori = Str(e, "kategori"), Islem = Str(e, "islem"), Birim = Str(e, "birim"),
                        Kanun = Str(e, "kanun"), AdliTatil = Str(e, "adliTatil"), Not = Str(e, "not"),
                    };
                    if (e.TryGetProperty("sure", out var s) && s.ValueKind == JsonValueKind.Number) t.Sure = s.GetInt32();
                    if (e.TryGetProperty("anahtarKelimeler", out var ak) && ak.ValueKind == JsonValueKind.Array)
                        foreach (var k in ak.EnumerateArray()) t.AnahtarKelimeler.Add(k.GetString() ?? "");
                    if (t.AdliTatil.Length == 0) t.AdliTatil = "YOK";
                    l.Add(t);
                }
                _liste = l;
                return;
            }
            catch (Exception) { /* sıradaki adaya geç */ }
        }
        _liste = new List<SureTanim>();
    }

    static string Str(JsonElement e, string ad) => e.TryGetProperty(ad, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>Tebligat metnine (konu, kurum, ayrışan metin) göre anahtar kelime isabeti en yüksek süre; yoksa -1.</summary>
    public static int EnIyiEslesme(string metin)
    {
        var m = Core.Utils.TrLower(metin);
        int best = -1, bestPuan = 0;
        var l = Liste();
        for (int i = 0; i < l.Count; i++)
        {
            int puan = l[i].AnahtarKelimeler.Count(k => k.Length > 0 && m.Contains(Core.Utils.TrLower(k)));
            if (puan > bestPuan) { bestPuan = puan; best = i; }
        }
        return best;
    }

    /// <summary>Tebligat metnine uyan katalog süresine göre son cevap/işlem tarihi (yyyy-MM-dd). UETS'e ulaşma tarihine e-tebligat günü eklenir,
    /// hafta sonu/resmî tatil ve adli tatil uzatması uygulanır. Uygun süre bulunamazsa ya da süre elle girilecekse null döner.</summary>
    public static string? SonCevapTahmin(string metin, string uetsTarihiIso)
    {
        var bas = Core.Utils.ParseDate(uetsTarihiIso);
        if (bas == null) return null;
        var i = EnIyiEslesme(metin);
        if (i < 0) return null;
        var t = Liste()[i];
        if (t.Sure == null) return null;
        return Core.Utils.Iso(Hesapla(t, bas.Value, true).Son);
    }

    public record Sonuc(DateTime Tebligat, DateTime Son, bool AdliTatilUzatti, string Aciklama);

    /// <summary>baslangic: e-tebligatta UETS'e ulaşma tarihi (eTebligat=true) veya doğrudan tebliğ tarihi.
    /// degisken: birimi "degisken" olan süreler için kullanıcıdan gelen gün sayısı.</summary>
    public static Sonuc Hesapla(SureTanim s, DateTime baslangic, bool eTebligat, int degisken = 0)
    {
        Yukle();
        var teblig = eTebligat ? baslangic.AddDays(_eGun) : baslangic;
        int n = s.Sure ?? degisken;
        var ham = s.Birim switch
        {
            "hafta" => teblig.AddDays(7 * n),
            "ay" => teblig.AddMonths(n),
            _ => teblig.AddDays(n),   // gün ve değişken (gün)
        };
        var son = IsGunuyaKaydir(ham);
        bool uzadi = false;
        var (uzatma, ad) = AdliTatilUzatma(s.AdliTatil, son.Year);
        if (uzatma != null && son >= new DateTime(son.Year, 7, 20) && son <= new DateTime(son.Year, 8, 31))
        {
            son = IsGunuyaKaydir(uzatma.Value);
            uzadi = true;
        }
        var acik = $"Tebliğ: {teblig:dd.MM.yyyy}" + (eTebligat ? $" (UETS'e ulaşma {baslangic:dd.MM.yyyy} + {_eGun} gün)" : "") +
                   $" • Süre: {n} {BirimAd(s.Birim == "degisken" ? "gun" : s.Birim)}" + (uzadi ? $" • adli tatil uzatması uygulandı ({ad})" : "");
        return new Sonuc(teblig, son, uzadi, acik);
    }

    static (DateTime?, string) AdliTatilUzatma(string tur, int yil) => tur switch
    {
        "HMK" => (new DateTime(yil, 8, 31).AddDays(7), "HMK: 7 gün"),
        "IYUK" => (new DateTime(yil, 8, 31).AddDays(7), "İYUK: 7 gün"),
        "CMK" => (new DateTime(yil, 8, 31).AddDays(3), "CMK: 3 gün"),
        _ => (null, ""),
    };

    public static DateTime IsGunuyaKaydir(DateTime d)
    {
        while (TatilMi(d)) d = d.AddDays(1);
        return d;
    }

    public static bool TatilMi(DateTime d)
    {
        if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) return true;
        return TrTatil.Yil(d.Year).Any(t => t.Tarih.Date == d.Date);
    }
}
