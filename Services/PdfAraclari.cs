using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using Windows.Storage;
using Windows.Storage.Streams;
using WPdf = Windows.Data.Pdf;

namespace HukukErp.Services;

/// <summary>PDF sıkıştırma (sayfaları JPEG olarak yeniden kodlama) ve bölme (sayfa sayısı / aralık / MB sınırı).</summary>
public sealed class SayfaOgesi
{
    public string Dosya = "";
    public int No;                 // 0 tabanlı
    public bool Tiff;
    public BitmapSource? Kucuk;
}

public static class PdfAraclari
{
    public static readonly (string Ad, int Dpi, int Kalite)[] Seviyeler =
    {
        ("Hafif (200 dpi, yüksek kalite)", 200, 75),
        ("Orta (150 dpi)", 150, 60),
        ("Güçlü (110 dpi)", 110, 45),
        ("Çok güçlü (90 dpi, düşük kalite)", 90, 35),
    };

    // ---------------------------------------------------------------- sıkıştırma
    /// <summary>hedefMb &gt; 0 ise, seçilen seviyeden başlayıp hedefe inene kadar daha güçlü seviyeleri dener.</summary>
    public static (string Yol, long Once, long Sonra, int Gorsellesen, string Seviye)? Sikistir(string pdf, string cikis, int seviye, double hedefMb, bool metinliSayfalariDa)
    {
        long once = new FileInfo(pdf).Length;
        using var kaynak = PdfDocument.Open(pdf);
        int n = kaynak.NumberOfPages;
        var metinli = new HashSet<int>();
        if (!metinliSayfalariDa)
            for (int i = 1; i <= n; i++)
                if (kaynak.GetPage(i).GetWords().Sum(w => w.Text.Length) >= 20) metinli.Add(i);
        var raster = Enumerable.Range(1, n).Where(i => !metinli.Contains(i)).Select(i => i - 1).ToList();
        if (raster.Count == 0) return null;

        byte[]? en = null; string ad = "";
        for (int s = seviye; s < Seviyeler.Length; s++)
        {
            var (sad, dpi, kalite) = Seviyeler[s];
            var jpegler = Task.Run(() => SayfalariJpegle(pdf, raster, dpi, kalite)).GetAwaiter().GetResult();
            var b = new PdfDocumentBuilder();
            for (int i = 1; i <= n; i++)
            {
                if (metinli.Contains(i)) { b.AddPage(kaynak, i); continue; }
                var (jpeg, w, h) = jpegler[i - 1];
                var sayfa = b.AddPage(w, h);
                sayfa.AddJpeg(jpeg, new PdfRectangle(0, 0, w, h));
            }
            en = b.Build(); ad = sad;
            if (hedefMb <= 0 || en.Length <= hedefMb * 1024 * 1024) break;
        }
        if (en == null || en.Length >= once) return (pdf, once, en?.Length ?? once, 0, "küçülmedi");
        File.WriteAllBytes(cikis, en);
        return (cikis, once, en.Length, raster.Count, ad);
    }

    static async Task<Dictionary<int, (byte[] Jpeg, double W, double H)>> SayfalariJpegle(string pdf, List<int> sayfalar, int dpi, int kalite)
    {
        var sonuc = new Dictionary<int, (byte[], double, double)>();
        var dosya = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdf));
        var belge = await WPdf.PdfDocument.LoadFromFileAsync(dosya);
        foreach (var no in sayfalar)
        {
            using var p = belge.GetPage((uint)no);
            var opts = new WPdf.PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(100, Math.Round(p.Size.Width * dpi / 96.0)),
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
            };
            using var akim = new InMemoryRandomAccessStream();
            await p.RenderToStreamAsync(akim, opts);
            var bytes = new byte[akim.Size];
            using (var rd = new DataReader(akim.GetInputStreamAt(0)))
            {
                await rd.LoadAsync((uint)akim.Size);
                rd.ReadBytes(bytes);
            }
            using var ms = new MemoryStream(bytes);
            var kare = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var enc = new JpegBitmapEncoder { QualityLevel = kalite };
            enc.Frames.Add(kare);
            using var o = new MemoryStream();
            enc.Save(o);
            sonuc[no] = (o.ToArray(), p.Size.Width * 0.75, p.Size.Height * 0.75);
        }
        return sonuc;
    }

    // ---------------------------------------------------------------- bölme
    static byte[] Yaz(PdfDocument kaynak, IEnumerable<int> sayfalar)
    {
        var b = new PdfDocumentBuilder();
        foreach (var p in sayfalar) b.AddPage(kaynak, p);
        return b.Build();
    }

    static string Kaydet(byte[] veri, string klasor, string ad, int no)
    {
        var yol = Donusum.BenzersizYol(klasor, $"{ad}_bolum_{no:00}", ".pdf");
        File.WriteAllBytes(yol, veri);
        return yol;
    }

    /// <summary>Her N sayfada bir yeni dosya.</summary>
    public static List<string> BolSayfaSayisi(string pdf, int n)
    {
        using var k = PdfDocument.Open(pdf);
        var klasor = Path.GetDirectoryName(pdf)!; var ad = Path.GetFileNameWithoutExtension(pdf);
        n = Math.Max(1, n);
        var sonuc = new List<string>();
        for (int bas = 1, no = 1; bas <= k.NumberOfPages; bas += n, no++)
            sonuc.Add(Kaydet(Yaz(k, Enumerable.Range(bas, Math.Min(n, k.NumberOfPages - bas + 1))), klasor, ad, no));
        return sonuc;
    }

    /// <summary>"1-3, 5, 7-9" gibi aralıkların her biri ayrı dosya olur.</summary>
    public static List<string> BolAraliklar(string pdf, string aralik)
    {
        using var k = PdfDocument.Open(pdf);
        var klasor = Path.GetDirectoryName(pdf)!; var ad = Path.GetFileNameWithoutExtension(pdf);
        var sonuc = new List<string>();
        int no = 1;
        foreach (var parca in aralik.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var m = Regex.Match(parca.Trim(), @"^(\d+)\s*(?:-\s*(\d+))?$");
            if (!m.Success) throw new InvalidOperationException($"Geçersiz aralık: '{parca.Trim()}' (örnek: 1-3, 5, 7-9)");
            int a = int.Parse(m.Groups[1].Value), b = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : a;
            if (a < 1 || b < a || b > k.NumberOfPages)
                throw new InvalidOperationException($"'{parca.Trim()}' aralığı geçersiz; belgede {k.NumberOfPages} sayfa var.");
            sonuc.Add(Kaydet(Yaz(k, Enumerable.Range(a, b - a + 1)), klasor, ad, no++));
        }
        return sonuc;
    }

    /// <summary>Her dosya en çok mb MB olacak şekilde bölünür (tek sayfa sınırı aşarsa o sayfa tek başına kalır).</summary>
    public static List<string> BolBoyut(string pdf, double mb, out List<int> asanSayfalar)
    {
        using var k = PdfDocument.Open(pdf);
        var klasor = Path.GetDirectoryName(pdf)!; var ad = Path.GetFileNameWithoutExtension(pdf);
        long limit = (long)(Math.Max(0.05, mb) * 1024 * 1024);
        int toplam = k.NumberOfPages;
        asanSayfalar = new List<int>();
        var tek = new long[toplam + 1];
        for (int i = 1; i <= toplam; i++) tek[i] = Yaz(k, new[] { i }).LongLength;

        var sonuc = new List<string>();
        int bas = 1, no = 1;
        while (bas <= toplam)
        {
            // tahminle başla (tek sayfa boyutları toplamı), sonra gerçek boyutla düzelt
            int son = bas; long tahmin = tek[bas];
            while (son < toplam && tahmin + tek[son + 1] <= limit) { son++; tahmin += tek[son]; }
            var veri = Yaz(k, Enumerable.Range(bas, son - bas + 1));
            while (veri.LongLength > limit && son > bas)
            {
                son--;
                veri = Yaz(k, Enumerable.Range(bas, son - bas + 1));
            }
            // ortak kaynaklar sayesinde gerçek boyut tahminden küçükse sığdığı kadar sayfa ekle
            while (son < toplam)
            {
                var deneme = Yaz(k, Enumerable.Range(bas, son - bas + 2));
                if (deneme.LongLength > limit) break;
                veri = deneme; son++;
            }
            if (veri.LongLength > limit) asanSayfalar.Add(bas);
            sonuc.Add(Kaydet(veri, klasor, ad, no++));
            bas = son + 1;
        }
        return sonuc;
    }

    // ---------------------------------------------------------------- sayfa ayırma / birleştirme (PDF + TIFF)
    static BitmapSource Dondur(BitmapSource kaynak)
    {
        var w = new WriteableBitmap(kaynak);
        w.Freeze();
        return w;
    }

    static BitmapSource Kucult(BitmapSource kare, int genislik = 220)
    {
        var fmt = new FormatConvertedBitmap(kare, System.Windows.Media.PixelFormats.Bgr24, null, 0);
        double o = genislik / (double)Math.Max(1, fmt.PixelWidth);
        return Dondur(new TransformedBitmap(fmt, new System.Windows.Media.ScaleTransform(o, o)));
    }

    /// <summary>PDF veya TIFF dosyasının her sayfası için küçük önizleme üretir.</summary>
    public static List<SayfaOgesi> SayfalariYukle(string yol)
    {
        var liste = new List<SayfaOgesi>();
        var ext = Path.GetExtension(yol).ToLowerInvariant();
        if (ext is ".tif" or ".tiff")
        {
            using var ms = new MemoryStream(File.ReadAllBytes(yol));
            var dec = new TiffBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            for (int i = 0; i < dec.Frames.Count; i++)
                liste.Add(new SayfaOgesi { Dosya = yol, No = i, Tiff = true, Kucuk = Kucult(dec.Frames[i]) });
            return liste;
        }
        var kucukler = Task.Run(() => PdfKucukleri(yol)).GetAwaiter().GetResult();
        for (int i = 0; i < kucukler.Count; i++)
            liste.Add(new SayfaOgesi { Dosya = yol, No = i, Tiff = false, Kucuk = kucukler[i] });
        return liste;
    }

    static async Task<List<BitmapSource>> PdfKucukleri(string yol)
    {
        var dosya = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(yol));
        var belge = await WPdf.PdfDocument.LoadFromFileAsync(dosya);
        var sonuc = new List<BitmapSource>();
        for (uint i = 0; i < belge.PageCount; i++)
        {
            using var p = belge.GetPage(i);
            var opts = new WPdf.PdfPageRenderOptions
            {
                DestinationWidth = 220,
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
            };
            using var akim = new InMemoryRandomAccessStream();
            await p.RenderToStreamAsync(akim, opts);
            var bytes = new byte[akim.Size];
            using (var rd = new DataReader(akim.GetInputStreamAt(0)))
            {
                await rd.LoadAsync((uint)akim.Size);
                rd.ReadBytes(bytes);
            }
            using var ms = new MemoryStream(bytes);
            sonuc.Add(Dondur(BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad)));
        }
        return sonuc;
    }

    /// <summary>Seçilen sayfaları (verilen sırayla) tek PDF'te birleştirir. PDF sayfaları aynen, TIFF sayfaları A4'e sığdırılmış JPEG olarak eklenir.</summary>
    public static string SayfalariBirlestir(IReadOnlyList<SayfaOgesi> secim, string cikis)
    {
        var b = new PdfDocumentBuilder();
        var pdfler = new Dictionary<string, PdfDocument>(StringComparer.OrdinalIgnoreCase);
        var tifler = new Dictionary<string, TiffBitmapDecoder>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var o in secim)
            {
                if (!o.Tiff)
                {
                    if (!pdfler.TryGetValue(o.Dosya, out var d)) pdfler[o.Dosya] = d = PdfDocument.Open(o.Dosya);
                    b.AddPage(d, o.No + 1);
                    continue;
                }
                if (!tifler.TryGetValue(o.Dosya, out var dec))
                {
                    using var ms = new MemoryStream(File.ReadAllBytes(o.Dosya));
                    tifler[o.Dosya] = dec = new TiffBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                }
                var kare = new FormatConvertedBitmap(dec.Frames[o.No], System.Windows.Media.PixelFormats.Bgr24, null, 0);
                var enc = new JpegBitmapEncoder { QualityLevel = 90 };
                enc.Frames.Add(BitmapFrame.Create(kare));
                using var cik = new MemoryStream();
                enc.Save(cik);
                Donusum.SayfaOlarakEkle(b, cik.ToArray(), kare.PixelWidth, kare.PixelHeight);
            }
            File.WriteAllBytes(cikis, b.Build());
        }
        finally { foreach (var d in pdfler.Values) d.Dispose(); }
        return cikis;
    }
}
