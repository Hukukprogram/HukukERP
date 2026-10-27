namespace HukukErp.Services;

/// <summary>Brüt/net ücret, gelir vergisi (kümülatif), damga vergisi, kıdem tavanı ve TRH-2010 yaşam tablosu. 2026 parametreleri.</summary>
public static class Bordro
{
    public const double SgkIsci = 0.14, Issizlik = 0.01, Damga = 0.00759;
    public const double SgkTavan = 297270.00;      // aylık SGK prim tavanı (2026)
    public const double AsgariBrut = 33030.00;     // 2026 brüt asgari ücret

    // 2026 ücret gelirleri tarifesi: (üst sınır, oran)
    static readonly (double Ust, double Oran)[] Dilim =
    {
        (190_000, 0.15), (400_000, 0.20), (1_500_000, 0.27), (5_300_000, 0.35), (double.PositiveInfinity, 0.40),
    };

    public static double GelirVergisi(double matrah)
    {
        double vergi = 0, alt = 0;
        foreach (var (ust, oran) in Dilim)
        {
            if (matrah <= alt) break;
            vergi += (Math.Min(matrah, ust) - alt) * oran;
            alt = ust;
        }
        return vergi;
    }

    public static double Marjinal(double kum)
    {
        foreach (var (ust, oran) in Dilim) if (kum < ust) return oran;
        return 0.40;
    }

    public record Ay(int No, double Brut, double Sgk, double Issiz, double Matrah, double KumMatrah, double Gv, double Damga, double Net);

    /// <summary>Bir ayın brütünden neti hesaplar. kumOnceki: önceki ayların kümülatif GV matrahı.</summary>
    public static Ay BrutAy(int ay, double brut, double kumOnceki, bool asgariIstisna = true)
    {
        var baz = Math.Min(brut, SgkTavan);
        var sgk = baz * SgkIsci;
        var iss = baz * Issizlik;
        var matrah = brut - sgk - iss;
        var gvHam = GelirVergisi(kumOnceki + matrah) - GelirVergisi(kumOnceki);
        double gvIst = 0, dvIst = 0;
        if (asgariIstisna)
        {
            var am = AsgariBrut * (1 - SgkIsci - Issizlik);
            gvIst = GelirVergisi(am * ay) - GelirVergisi(am * (ay - 1));
            dvIst = AsgariBrut * Damga;
        }
        var gv = Math.Max(0, gvHam - gvIst);
        var dv = Math.Max(0, brut * Damga - dvIst);
        return new Ay(ay, brut, sgk, iss, matrah, kumOnceki + matrah, gv, dv, brut - sgk - iss - gv - dv);
    }

    /// <summary>İstenen net için brütü bulur (ikiye bölme).</summary>
    public static Ay NetAy(int ay, double net, double kumOnceki, bool asgariIstisna = true)
    {
        double lo = net, hi = Math.Max(net * 4, 1000);
        for (int i = 0; i < 80; i++)
        {
            var mid = (lo + hi) / 2;
            if (BrutAy(ay, mid, kumOnceki, asgariIstisna).Net < net) lo = mid; else hi = mid;
        }
        return BrutAy(ay, (lo + hi) / 2, kumOnceki, asgariIstisna);
    }

    /// <summary>Ocak'tan Aralık'a 12 aylık tablo. net=true ise tutar aylık net, değilse aylık brüt.</summary>
    public static List<Ay> YillikTablo(double tutar, bool net, bool asgariIstisna)
    {
        var liste = new List<Ay>();
        double kum = 0;
        for (int m = 1; m <= 12; m++)
        {
            var a = net ? NetAy(m, tutar, kum, asgariIstisna) : BrutAy(m, tutar, kum, asgariIstisna);
            liste.Add(a);
            kum = a.KumMatrah;
        }
        return liste;
    }

    /// <summary>Kıdem tazminatı tavanı (aylık, brüt). Bilinmeyen dönem için 0 döner.</summary>
    public static double KidemTavani(DateTime cikis)
    {
        (DateTime Bas, double Tutar)[] t =
        {
            (new DateTime(2026, 7, 1), 73_729.87), (new DateTime(2026, 1, 1), 64_948.77),
            (new DateTime(2025, 7, 1), 53_919.68), (new DateTime(2025, 1, 1), 46_655.43),
            (new DateTime(2024, 7, 1), 41_828.42), (new DateTime(2024, 1, 1), 35_058.58),
            (new DateTime(2023, 7, 1), 23_489.83),
        };
        foreach (var (bas, tutar) in t) if (cikis >= bas) return tutar;
        return 0;
    }

    /// <summary>İki tarih arası yıl/ay/gün.</summary>
    public static (int Yil, int Ay, int Gun) Sure(DateTime bas, DateTime son)
    {
        int y = son.Year - bas.Year, m = son.Month - bas.Month, d = son.Day - bas.Day;
        if (d < 0) { m--; d += DateTime.DaysInMonth(son.Year, son.Month == 1 ? 12 : son.Month - 1) ; }
        if (m < 0) { y--; m += 12; }
        return (y, m, d);
    }

    public static int IhbarHaftasi(double toplamAy) => toplamAy < 6 ? 2 : toplamAy < 18 ? 4 : toplamAy < 36 ? 6 : 8;

    // ---- TRH-2010: yaşa göre beklenen kalan ömür (yıl), yaş 0..99 ----
    static readonly double[] ExErkek =
    {
        71.93, 72.35, 71.42, 70.47, 69.52, 68.57, 67.60, 66.63, 65.66, 64.68, 63.70, 62.72, 61.74, 60.76, 59.78, 58.80, 57.84, 56.87, 55.91, 54.95,
        53.99, 53.04, 52.09, 51.14, 50.19, 49.24, 48.28, 47.33, 46.37, 45.41, 44.45, 43.50, 42.54, 41.58, 40.62, 39.67, 38.72, 37.77, 36.81, 35.87,
        34.93, 33.99, 33.05, 32.12, 31.19, 30.27, 29.36, 28.46, 27.56, 26.67, 25.79, 24.93, 24.06, 23.21, 22.37, 21.54, 20.74, 19.94, 19.15, 18.38,
        17.62, 16.88, 16.14, 15.42, 14.72, 14.04, 13.37, 12.72, 12.08, 11.47, 10.87, 10.29, 9.73, 9.20, 8.68, 8.17, 7.69, 7.24, 6.81, 6.40,
        5.99, 5.59, 5.23, 4.90, 4.57, 4.25, 3.93, 3.64, 3.37, 3.12, 2.90, 2.66, 2.39, 2.10, 1.80, 1.55, 1.40, 1.23, 0.96, 0.50,
    };

    static readonly double[] ExKadin =
    {
        78.02, 77.66, 76.68, 75.70, 74.72, 73.73, 72.74, 71.75, 70.76, 69.76, 68.77, 67.78, 66.78, 65.79, 64.79, 63.80, 62.81, 61.82, 60.83, 59.84,
        58.85, 57.86, 56.88, 55.89, 54.90, 53.92, 52.93, 51.95, 50.97, 49.98, 49.00, 48.02, 47.04, 46.06, 45.08, 44.10, 43.12, 42.15, 41.17, 40.20,
        39.23, 38.26, 37.30, 36.34, 35.38, 34.43, 33.48, 32.54, 31.60, 30.67, 29.74, 28.82, 27.90, 26.98, 26.08, 25.18, 24.29, 23.40, 22.52, 21.65,
        20.79, 19.94, 19.09, 18.26, 17.43, 16.63, 15.85, 15.08, 14.32, 13.58, 12.87, 12.18, 11.51, 10.85, 10.22, 9.62, 9.05, 8.51, 8.00, 7.50,
        7.01, 6.55, 6.12, 5.71, 5.32, 4.93, 4.54, 4.20, 3.88, 3.59, 3.29, 2.97, 2.64, 2.32, 1.99, 1.67, 1.36, 1.05, 0.75, 0.50,
    };

    public static double BeklenenOmur(double yas, bool kadin)
    {
        var t = kadin ? ExKadin : ExErkek;
        var a = (int)Math.Clamp(Math.Floor(yas), 0, t.Length - 1);
        return t[a];
    }

    /// <summary>Aylık ödemenin bugünkü değeri: n ay, t0 ay sonra başlayan, yıllık teknik faiz i (0.0 = faizsiz).</summary>
    public static double Rant(double aylik, double n, double t0, double i)
    {
        if (n <= 0) return 0;
        if (Math.Abs(i) < 1e-12) return aylik * n;
        var r = Math.Pow(1 + i, 1.0 / 12);
        return aylik * Math.Pow(r, -t0) * (1 - Math.Pow(r, -n)) / (r - 1);
    }
}
