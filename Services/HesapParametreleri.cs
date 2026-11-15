using System.Text.Json;
using HukukErp.Core;

namespace HukukErp.Services;

/// <summary>Faiz, harç ve AAÜT değerleri. Veri klasöründeki hesap_parametreleri.json dosyasından okunur;
/// dosya yoksa varsayılanlarla OLUŞTURULUR, varsa ASLA ezilmez. Değerler yılda bir değişir: dosyayı düzenleyip güncelleyin.</summary>
public sealed class HesapParametreleri
{
    public string Aciklama { get; set; } = "";
    public string GuncellemeTarihi { get; set; } = "";
    public List<FaizDonemi> YasalFaiz { get; set; } = new();
    public Harc Harclar { get; set; } = new();
    public Aaut Tarife { get; set; } = new();

    public sealed class FaizDonemi { public string Baslangic { get; set; } = ""; public double Yuzde { get; set; } public string Kaynak { get; set; } = ""; }
    public sealed class Harc
    {
        public string Kaynak { get; set; } = "";
        public double NispiBinde { get; set; }
        public double PesinOran { get; set; }
        public double BasvurmaSulh { get; set; }
        public double BasvurmaAsliye { get; set; }
        public double MaktuKararIlam { get; set; }
        public double VekaletHarci { get; set; }
    }
    public sealed class Aaut
    {
        public string Kaynak { get; set; } = "";
        public List<Dilim> Nispi { get; set; } = new();
        public List<Maktu> Maktular { get; set; } = new();
    }
    public sealed class Dilim { public double Tutar { get; set; } public double Yuzde { get; set; } }   // Tutar<=0: kalan tamamı
    public sealed class Maktu { public string Ad { get; set; } = ""; public double Tutar { get; set; } }

    public static string Yol(Config cfg) => Path.Combine(cfg.DataDir, "hesap_parametreleri.json");

    static readonly JsonSerializerOptions Js = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static HesapParametreleri Yukle(Config cfg)
    {
        var yol = Yol(cfg);
        try
        {
            if (!File.Exists(yol)) File.WriteAllText(yol, JsonSerializer.Serialize(Varsayilan(), Js));
            return JsonSerializer.Deserialize<HesapParametreleri>(File.ReadAllText(yol), Js) ?? Varsayilan();
        }
        catch (Exception) { return Varsayilan(); }
    }

    /// <summary>Kaynaklardan 13.11.2026'da derlenen değerler. Resmî metinle karşılaştırmadan kesin kabul etmeyin.</summary>
    public static HesapParametreleri Varsayilan() => new()
    {
        Aciklama = "DOĞRULAMA GEREKİR: değerler web kaynaklarından derlenmiştir; Resmî Gazete/TCMB metniyle karşılaştırın, değişince bu dosyayı güncelleyin.",
        GuncellemeTarihi = "2026-11-13",
        YasalFaiz = new()
        {
            new() { Baslangic = "2006-01-01", Yuzde = 9, Kaynak = "2005/9831 sayılı BKK" },
            new() { Baslangic = "2024-06-01", Yuzde = 24, Kaynak = "8485 sayılı Cumhurbaşkanı Kararı (RG 21.05.2024)" },
            new() { Baslangic = "2026-07-31", Yuzde = 31, Kaynak = "7589 sayılı Kanun m.10 (RG 31.07.2026) - kaynak sitede belirtilen; DOĞRULAYIN" },
        },
        Harclar = new()
        {
            Kaynak = "2026 Harçlar Kanunu (1) ve (2) sayılı tarifeler, 2026 yeniden değerleme (kpmgvergi.com özeti)",
            NispiBinde = 68.31, PesinOran = 0.25, BasvurmaSulh = 335.20, BasvurmaAsliye = 732.00, MaktuKararIlam = 732.00, VekaletHarci = 307.90,
        },
        Tarife = new()
        {
            Kaynak = "AAÜT 2026-2027, RG 03.11.2026 S.33419 (harbiyehukuk.com özeti) - iş mahkemesi vb. maktular eklenmemiştir",
            Nispi = new()
            {
                new() { Tutar = 800000, Yuzde = 16 }, new() { Tutar = 800000, Yuzde = 15 }, new() { Tutar = 1600000, Yuzde = 14 },
                new() { Tutar = 1600000, Yuzde = 13 }, new() { Tutar = 2400000, Yuzde = 11 }, new() { Tutar = 3200000, Yuzde = 8 },
                new() { Tutar = 4000000, Yuzde = 5 }, new() { Tutar = 4800000, Yuzde = 3 }, new() { Tutar = 5600000, Yuzde = 2 },
                new() { Tutar = 0, Yuzde = 1 },
            },
            Maktular = new()
            {
                new() { Ad = "Asliye Mahkemeleri", Tutar = 62000 }, new() { Ad = "Sulh Hukuk Mahkemeleri", Tutar = 40000 },
                new() { Ad = "Tüketici Mahkemeleri", Tutar = 30000 }, new() { Ad = "İcra daireleri (takip)", Tutar = 12500 },
                new() { Ad = "İcra mahkemeleri (işler)", Tutar = 15000 }, new() { Ad = "İcra mahkemeleri (dava ve duruşmalı işler)", Tutar = 23000 },
                new() { Ad = "Tahliyeye ilişkin icra takibi", Tutar = 26000 },
            },
        },
    };

    // ---------------- hesaplar
    public sealed record FaizSatir(DateTime Bas, DateTime Bit, int Gun, double Yuzde, double Tutar);

    /// <summary>Basit faiz, yıl=365 gün; oran dönemleri boyunca parçalanır. bas dahil, bit hariç.</summary>
    public List<FaizSatir> Faiz(double anapara, DateTime bas, DateTime bit, double? ozelYuzde)
    {
        var l = new List<FaizSatir>();
        if (bit <= bas) return l;
        var donem = YasalFaiz.Select(d => (T: Utils.ParseDate(d.Baslangic) ?? DateTime.MinValue, d.Yuzde)).OrderBy(x => x.T).ToList();
        var kes = new List<DateTime> { bas };
        if (ozelYuzde == null) kes.AddRange(donem.Select(d => d.T).Where(t => t > bas && t < bit));
        kes.Add(bit);
        for (int i = 0; i < kes.Count - 1; i++)
        {
            double y = ozelYuzde ?? donem.LastOrDefault(d => d.T <= kes[i]).Yuzde;
            int gun = (kes[i + 1] - kes[i]).Days;
            l.Add(new FaizSatir(kes[i], kes[i + 1], gun, y, anapara * y / 100.0 * gun / 365.0));
        }
        return l;
    }

    public double NispiVekalet(double deger, List<(double Tutar, double Yuzde, double Pay)>? ayrinti = null)
    {
        double kalan = deger, top = 0;
        foreach (var d in Tarife.Nispi)
        {
            if (kalan <= 0) break;
            double pay = d.Tutar <= 0 ? kalan : Math.Min(kalan, d.Tutar);
            top += pay * d.Yuzde / 100.0; ayrinti?.Add((pay, d.Yuzde, pay * d.Yuzde / 100.0)); kalan -= pay;
        }
        return top;
    }
}
