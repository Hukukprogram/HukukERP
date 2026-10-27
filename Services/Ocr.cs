using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace HukukErp.Services;

public record OcrSatir(string Metin, double Sol, double Ust, double Alt, double Sag);

public record OcrSayfa(double Genislik, double Yukseklik, List<OcrSatir> Satirlar);

/// <summary>Windows'un yerleşik OCR motoru (Windows.Media.Ocr) ile taranmış PDF sayfalarını yazıya çevirir. Ek kurulum gerekmez; Türkçe için Windows'ta Türkçe dil paketi yüklü olmalıdır.</summary>
public static class Ocr
{
    const double Dpi = 300.0;

    /// <summary>İstenen sayfaları (0 tabanlı) okur. Sonuçta sayfa ölçüleri ve satırlar punto (pt) cinsindendir.</summary>
    public static Dictionary<int, OcrSayfa> Oku(string pdfYolu, IEnumerable<int> sayfalar, out string dil)
    {
        var sonuc = new Dictionary<int, OcrSayfa>();
        var istenen = sayfalar.ToList();
        dil = "";
        if (istenen.Count == 0) return sonuc;
        var (s, d) = Task.Run(() => OkuAsync(pdfYolu, istenen)).GetAwaiter().GetResult();
        dil = d;
        return s;
    }

    static async Task<(Dictionary<int, OcrSayfa>, string)> OkuAsync(string pdfYolu, List<int> sayfalar)
    {
        var motor = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("tr-TR"));
        string dil = "Türkçe";
        if (motor == null)
        {
            motor = OcrEngine.TryCreateFromUserProfileLanguages();
            dil = motor?.RecognizerLanguage.DisplayName ?? "";
        }
        if (motor == null)
            throw new InvalidOperationException("OCR için Windows'ta Türkçe dil paketi gerekli: Ayarlar > Zaman ve dil > Dil ve bölge > Türkçe ekleyin (OCR/Metin tanıma özelliği işaretli olsun).");

        var dosya = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdfYolu));
        var belge = await PdfDocument.LoadFromFileAsync(dosya);
        var sonuc = new Dictionary<int, OcrSayfa>();
        foreach (var no in sayfalar)
        {
            if (no < 0 || no >= belge.PageCount) continue;
            using var sayfa = belge.GetPage((uint)no);
            double dipW = sayfa.Size.Width, dipH = sayfa.Size.Height;          // 1/96 inç
            double olcek = Dpi / 96.0;
            var opts = new PdfPageRenderOptions { DestinationWidth = (uint)Math.Min(Math.Round(dipW * olcek), OcrEngine.MaxImageDimension) };
            using var akim = new InMemoryRandomAccessStream();
            await sayfa.RenderToStreamAsync(akim, opts);
            var dec = await BitmapDecoder.CreateAsync(akim);
            using var bmp = await dec.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var okunan = await motor.RecognizeAsync(bmp);
            double ptPx = (dipW * 0.75) / bmp.PixelWidth;                       // piksel → pt
            var satirlar = new List<OcrSatir>();
            foreach (var l in okunan.Lines)
            {
                if (l.Words.Count == 0) continue;
                double sol = l.Words.Min(w => w.BoundingRect.Left), sag = l.Words.Max(w => w.BoundingRect.Right);
                double ust = l.Words.Min(w => w.BoundingRect.Top), alt = l.Words.Max(w => w.BoundingRect.Bottom);
                satirlar.Add(new OcrSatir(l.Text, sol * ptPx, ust * ptPx, alt * ptPx, sag * ptPx));
            }
            sonuc[no] = new OcrSayfa(dipW * 0.75, dipH * 0.75, satirlar);
        }
        return (sonuc, dil);
    }
}
