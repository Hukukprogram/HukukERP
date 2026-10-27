using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using UglyToad.PdfPig.Writer;

namespace HukukErp.Services;

/// <summary>Dosya dönüştürücüler: görsel→PDF, PDF→Word, Word→UDF, PDF→UDF. Yalnızca metin/görsel; sayfa düzeni birebir korunmaz.</summary>
public static class Donusum
{
    public static string BenzersizYol(string klasor, string ad, string uzanti)
    {
        var yol = Path.Combine(klasor, ad + uzanti);
        for (int n = 2; File.Exists(yol); n++) yol = Path.Combine(klasor, $"{ad} ({n}){uzanti}");
        return yol;
    }

    // ---------------------------------------------------------------- görsel → PDF
    public static string GorselleriPdfYap(IReadOnlyList<string> dosyalar, string cikis)
    {
        var b = new PdfDocumentBuilder();
        foreach (var f in dosyalar)
        {
            var (bytes, pw, ph) = JpegHazirla(f);
            double W = 595, H = 842;
            if (pw > ph) (W, H) = (H, W);
            var olcek = Math.Min(W / pw, H / ph);
            double w = pw * olcek, h = ph * olcek;
            var sayfa = b.AddPage(W, H);
            sayfa.AddJpeg(bytes, new PdfRectangle((W - w) / 2, (H - h) / 2, (W + w) / 2, (H + h) / 2));
        }
        File.WriteAllBytes(cikis, b.Build());
        return cikis;
    }

    /// <summary>JPEG baytlarını A4'e sığacak şekilde yeni sayfa olarak ekler.</summary>
    public static void SayfaOlarakEkle(PdfDocumentBuilder b, byte[] jpeg, int pw, int ph)
    {
        double W = 595, H = 842;
        if (pw > ph) (W, H) = (H, W);
        var olcek = Math.Min(W / pw, H / ph);
        double w = pw * olcek, h = ph * olcek;
        var sayfa = b.AddPage(W, H);
        sayfa.AddJpeg(jpeg, new PdfRectangle((W - w) / 2, (H - h) / 2, (W + w) / 2, (H + h) / 2));
    }

    static (byte[] Bytes, int W, int H) JpegHazirla(string yol)
    {
        var ext = Path.GetExtension(yol).ToLowerInvariant();
        using var ms = new MemoryStream(File.ReadAllBytes(yol));
        var kare = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        if (ext is ".jpg" or ".jpeg") return (File.ReadAllBytes(yol), kare.PixelWidth, kare.PixelHeight);
        var enc = new JpegBitmapEncoder { QualityLevel = 92 };
        enc.Frames.Add(kare);
        using var o = new MemoryStream();
        enc.Save(o);
        return (o.ToArray(), kare.PixelWidth, kare.PixelHeight);
    }

    /// <summary>Sayfa metinleri; metni olmayan (taranmış) sayfalar OCR ile okunur.</summary>
    public static List<string> SayfaMetinleri(string pdf, out int ocrSayfa, out string dil)
    {
        var sayfalar = Parser.PdfSayfalari(pdf);
        var bos = Enumerable.Range(0, sayfalar.Count).Where(i => sayfalar[i].Trim().Length < 20).ToList();
        ocrSayfa = 0; dil = "";
        if (bos.Count > 0)
        {
            var oku = Ocr.Oku(pdf, bos, out dil);
            foreach (var (no, sf) in oku) { sayfalar[no] = string.Join("\n", sf.Satirlar.Select(x => x.Metin)); ocrSayfa++; }
        }
        return sayfalar;
    }

    // ---------------------------------------------------------------- PDF → Word
    public static string PdfToWord(string pdf, string cikis, bool satirlariBirlestir, out int sayfa, out bool metinYok, out int ocrSayfa, out string dil)
    {
        var sayfalar = SayfaMetinleri(pdf, out ocrSayfa, out dil);
        sayfa = sayfalar.Count;
        metinYok = sayfalar.All(s => s.Trim().Length == 0);
        using var doc = WordprocessingDocument.Create(cikis, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;
        for (int i = 0; i < sayfalar.Count; i++)
        {
            if (i > 0) body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
            foreach (var par in Paragraflar(sayfalar[i], satirlariBirlestir))
            {
                var rp = new RunProperties(new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman" }, new FontSize { Val = "24" });
                body.Append(new Paragraph(new Run(rp, new Text(par) { Space = SpaceProcessingModeValues.Preserve })));
            }
        }
        main.Document.Save();
        return cikis;
    }

    static List<string> Paragraflar(string metin, bool birlestir)
    {
        var satirlar = metin.Split('\n').Select(x => x.TrimEnd('\r')).ToList();
        if (!birlestir) return satirlar;
        var sonuc = new List<string>();
        var cur = new StringBuilder();
        foreach (var s in satirlar)
        {
            if (s.Trim().Length == 0) { if (cur.Length > 0) { sonuc.Add(cur.ToString()); cur.Clear(); } sonuc.Add(""); continue; }
            if (cur.Length > 0) cur.Append(' ');
            cur.Append(s.Trim());
            if (Regex.IsMatch(s.TrimEnd(), @"[.:;!?)]$") || s.Trim().Length < 40) { sonuc.Add(cur.ToString()); cur.Clear(); }
        }
        if (cur.Length > 0) sonuc.Add(cur.ToString());
        return sonuc;
    }

    // ---------------------------------------------------------------- PDF → Word (tablo + görsel + sayfa düzeni)
    sealed class Satir
    {
        public double Top, Bottom, Left, Right, Boyut;
        public List<Word> Kelimeler = new();
    }

    sealed class Ogesi
    {
        public double Top, Bottom, Left;
        public Satir? Satir;
        public (byte[] Png, double W, double H)? Resim;
        public (double[] Xs, double[] Ys, List<Word> Kelimeler)? Tablo;
    }

    static List<Satir> SatirlaraBol(IEnumerable<Word> kelimeler)
    {
        var sirali = kelimeler.Where(w => !string.IsNullOrWhiteSpace(w.Text)).OrderByDescending(w => w.BoundingBox.Bottom).ToList();
        var satirlar = new List<Satir>();
        foreach (var w in sirali)
        {
            var son = satirlar.LastOrDefault();
            double h = Math.Max(1.0, w.BoundingBox.Height);
            if (son != null && Math.Abs(son.Kelimeler[0].BoundingBox.Bottom - w.BoundingBox.Bottom) < h * 0.5) son.Kelimeler.Add(w);
            else satirlar.Add(new Satir { Kelimeler = new List<Word> { w } });
        }
        foreach (var s in satirlar)
        {
            s.Kelimeler = s.Kelimeler.OrderBy(w => w.BoundingBox.Left).ToList();
            s.Top = s.Kelimeler.Max(w => w.BoundingBox.Top); s.Bottom = s.Kelimeler.Min(w => w.BoundingBox.Bottom);
            s.Left = s.Kelimeler.Min(w => w.BoundingBox.Left); s.Right = s.Kelimeler.Max(w => w.BoundingBox.Right);
            s.Boyut = s.Kelimeler.Select(w => w.Letters.Count > 0 ? w.Letters.Max(l => l.PointSize) : 12.0).DefaultIfEmpty(12.0).Max();
        }
        return satirlar;
    }

    /// <summary>Sayfadaki çizgi/dikdörtgenlerden tablo ızgaraları bulur.</summary>
    static List<(double[] Xs, double[] Ys)> TabloBul(UglyToad.PdfPig.Content.Page sayfa)
    {
        var yatay = new List<(double Y, double X1, double X2)>();
        var dikey = new List<(double X, double Y1, double Y2)>();
        foreach (var yol in sayfa.ExperimentalAccess.Paths)
            foreach (var sp in yol)
            {
                PdfRectangle? br = sp.GetBoundingRectangle();
                if (br is not { } r) continue;
                double w = r.Width, h = r.Height;
                if (w >= sayfa.Width * 0.95 && h >= sayfa.Height * 0.95) continue;
                if (h < 2 && w > 8) yatay.Add(((r.Bottom + r.Top) / 2, r.Left, r.Right));
                else if (w < 2 && h > 8) dikey.Add(((r.Left + r.Right) / 2, r.Bottom, r.Top));
                else if (w >= 8 && h >= 8)
                {
                    yatay.Add((r.Bottom, r.Left, r.Right)); yatay.Add((r.Top, r.Left, r.Right));
                    dikey.Add((r.Left, r.Bottom, r.Top)); dikey.Add((r.Right, r.Bottom, r.Top));
                }
            }
        // dokunan çizgileri aynı tabloya grupla
        var n = yatay.Count + dikey.Count;
        var ata = Enumerable.Range(0, n).ToArray();
        int Bul(int a) { while (ata[a] != a) a = ata[a] = ata[ata[a]]; return a; }
        const double T = 2.5;
        for (int i = 0; i < yatay.Count; i++)
            for (int j = 0; j < dikey.Count; j++)
            {
                var h = yatay[i]; var v = dikey[j];
                if (v.X >= h.X1 - T && v.X <= h.X2 + T && h.Y >= v.Y1 - T && h.Y <= v.Y2 + T)
                    ata[Bul(i)] = Bul(yatay.Count + j);
            }
        var sonuc = new List<(double[], double[])>();
        foreach (var grp in Enumerable.Range(0, n).GroupBy(Bul))
        {
            var xs = grp.Where(k => k >= yatay.Count).Select(k => dikey[k - yatay.Count].X).ToList();
            var ys = grp.Where(k => k < yatay.Count).Select(k => yatay[k].Y).ToList();
            if (xs.Count < 2 || ys.Count < 2) continue;
            double[] Kume(List<double> v)
            {
                var o = new List<double>();
                foreach (var d in v.OrderBy(x => x)) if (o.Count == 0 || d - o[^1] > 3) o.Add(d);
                return o.ToArray();
            }
            var cx = Kume(xs); var cy = Kume(ys);
            if (cx.Length >= 2 && cy.Length >= 2) sonuc.Add((cx, cy));
        }
        return sonuc;
    }

    static bool Icinde(Word w, double[] xs, double[] ys)
    {
        double cx = (w.BoundingBox.Left + w.BoundingBox.Right) / 2, cy = (w.BoundingBox.Bottom + w.BoundingBox.Top) / 2;
        return cx >= xs[0] - 1 && cx <= xs[^1] + 1 && cy >= ys[0] - 1 && cy <= ys[^1] + 1;
    }

    static Run KelimeRun(Word w, bool sonda)
    {
        var l = w.Letters.Count > 0 ? w.Letters[0] : null;
        var ad = l?.FontName ?? "";
        var rp = new RunProperties(new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman" },
            new FontSize { Val = Math.Max(2, (int)Math.Round((l?.PointSize ?? 12.0) * 2)).ToString() });
        if (ad.Contains("Bold", StringComparison.OrdinalIgnoreCase)) rp.Append(new Bold());
        if (ad.Contains("Italic", StringComparison.OrdinalIgnoreCase) || ad.Contains("Oblique", StringComparison.OrdinalIgnoreCase)) rp.Append(new Italic());
        return new Run(rp, new Text(w.Text + (sonda ? "" : " ")) { Space = SpaceProcessingModeValues.Preserve });
    }

    static Paragraph SatirParagrafi(Satir s, double solKenar, double icGenislik, double sayfaGenislik, double oncekiAlt, bool ilkTabloIci)
    {
        var pp = new ParagraphProperties();
        double bosluk = ilkTabloIci ? 0 : Math.Clamp(oncekiAlt - s.Top, 0, 40);
        var merkez = Math.Abs((s.Left + s.Right) / 2 - sayfaGenislik / 2) < 8 && s.Left - solKenar > 20;
        pp.Append(new SpacingBetweenLines { Before = ((int)(bosluk * 20)).ToString(), After = "0" });
        if (merkez) pp.Append(new Justification { Val = JustificationValues.Center });
        else
        {
            var ind = Math.Clamp(s.Left - solKenar, 0, icGenislik * 0.7);
            if (ind > 3) pp.Append(new Indentation { Left = ((int)(ind * 20)).ToString() });
        }
        var par = new Paragraph(pp);
        for (int i = 0; i < s.Kelimeler.Count; i++) par.Append(KelimeRun(s.Kelimeler[i], i == s.Kelimeler.Count - 1));
        return par;
    }

    static Paragraph ResimParagrafi(MainDocumentPart main, byte[] png, double wPt, double hPt, double solPt, ref uint no)
    {
        var part = main.AddImagePart(ImagePartType.Png);
        using (var ms = new MemoryStream(png)) part.FeedData(ms);
        var rid = main.GetIdOfPart(part);
        long cx = (long)(wPt * 12700), cy = (long)(hPt * 12700);
        no++;
        var inline = new DW.Inline(
            new DW.Extent { Cx = cx, Cy = cy },
            new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
            new DW.DocProperties { Id = no, Name = "Resim" + no },
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            new A.Graphic(new A.GraphicData(
                new PIC.Picture(
                    new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = 0U, Name = "resim" + no + ".png" }, new PIC.NonVisualPictureDrawingProperties()),
                    new PIC.BlipFill(new A.Blip { Embed = rid }, new A.Stretch(new A.FillRectangle())),
                    new PIC.ShapeProperties(new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = cx, Cy = cy }),
                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U };
        var pp = new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "0" });
        if (solPt > 3) pp.Append(new Indentation { Left = ((int)(solPt * 20)).ToString() });
        return new Paragraph(pp, new Run(new Drawing(inline)));
    }

    static Table TabloOlustur(double[] xs, double[] ys, List<Word> kelimeler, double sayfaGenislik)
    {
        var tbl = new Table();
        tbl.Append(new TableProperties(new TableBorders(
            new TopBorder { Val = BorderValues.Single, Size = 4 }, new BottomBorder { Val = BorderValues.Single, Size = 4 },
            new LeftBorder { Val = BorderValues.Single, Size = 4 }, new RightBorder { Val = BorderValues.Single, Size = 4 },
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 }, new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 }),
            new TableLayout { Type = TableLayoutValues.Fixed }));
        var grid = new TableGrid();
        for (int c = 0; c < xs.Length - 1; c++) grid.Append(new GridColumn { Width = ((int)((xs[c + 1] - xs[c]) * 20)).ToString() });
        tbl.Append(grid);
        for (int r = ys.Length - 1; r > 0; r--)          // üstten alta (Y yukarı doğru artar)
        {
            double ust = ys[r], alt = ys[r - 1];
            var row = new TableRow();
            for (int c = 0; c < xs.Length - 1; c++)
            {
                double sol = xs[c], sag = xs[c + 1];
                var hucre = kelimeler.Where(w =>
                {
                    double cx = (w.BoundingBox.Left + w.BoundingBox.Right) / 2, cy = (w.BoundingBox.Bottom + w.BoundingBox.Top) / 2;
                    return cx >= sol && cx < sag && cy >= alt && cy < ust;
                }).ToList();
                var tc = new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = ((int)((sag - sol) * 20)).ToString() }));
                var satirlar = SatirlaraBol(hucre);
                if (satirlar.Count == 0) tc.Append(new Paragraph());
                foreach (var s in satirlar) tc.Append(SatirParagrafi(s, s.Left, sag - sol, sayfaGenislik, 0, true));
                row.Append(tc);
            }
            tbl.Append(row);
        }
        return tbl;
    }

    public static string PdfToWordDuzenli(string pdf, string cikis, out int sayfaSayisi, out int atlananResim, out int tabloSayisi, out int ocrSayfa, out string dil)
    {
        atlananResim = 0; tabloSayisi = 0; ocrSayfa = 0; dil = "";
        using var pd = PdfDocument.Open(pdf);
        var ocrIstenen = new List<int>();
        for (int i = 1; i <= pd.NumberOfPages; i++)
            if (pd.GetPage(i).GetWords().Sum(w => w.Text.Length) < 20) ocrIstenen.Add(i - 1);
        var ocr = Ocr.Oku(pdf, ocrIstenen, out dil);
        ocrSayfa = ocr.Count;
        using var doc = WordprocessingDocument.Create(cikis, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;
        sayfaSayisi = pd.NumberOfPages;
        uint resimNo = 0;
        SectionProperties? sonSec = null;
        for (int pn = 1; pn <= pd.NumberOfPages; pn++)
        {
            var sayfa = pd.GetPage(pn);
            double W = sayfa.Width, H = sayfa.Height, kenar = 36;
            var sec = new SectionProperties(
                new DocumentFormat.OpenXml.Wordprocessing.PageSize { Width = (uint)(W * 20), Height = (uint)(H * 20), Orient = W > H ? PageOrientationValues.Landscape : PageOrientationValues.Portrait },
                new PageMargin { Top = (int)(kenar * 20), Bottom = (int)(kenar * 20), Left = (uint)(kenar * 20), Right = (uint)(kenar * 20), Header = 0U, Footer = 0U, Gutter = 0U });
            if (ocr.TryGetValue(pn - 1, out var os))
            {
                // taranmış sayfa: OCR ile okunan satırlar, konumlarına göre yazı olarak
                double ow = os.Genislik, oh = os.Yukseklik;
                var osec = new SectionProperties(
                    new DocumentFormat.OpenXml.Wordprocessing.PageSize { Width = (uint)(ow * 20), Height = (uint)(oh * 20), Orient = ow > oh ? PageOrientationValues.Landscape : PageOrientationValues.Portrait },
                    new PageMargin { Top = (int)(kenar * 20), Bottom = (int)(kenar * 20), Left = (uint)(kenar * 20), Right = (uint)(kenar * 20), Header = 0U, Footer = 0U, Gutter = 0U });
                double oncekiAlt = kenar;
                foreach (var sat in os.Satirlar)
                {
                    var pp = new ParagraphProperties();
                    pp.Append(new SpacingBetweenLines { Before = ((int)(Math.Clamp(sat.Ust - oncekiAlt, 0, 40) * 20)).ToString(), After = "0" });
                    var ind = Math.Clamp(sat.Sol - kenar, 0, (ow - 2 * kenar) * 0.7);
                    if (ind > 3) pp.Append(new Indentation { Left = ((int)(ind * 20)).ToString() });
                    var boyut = Math.Clamp((sat.Alt - sat.Ust) * 0.9, 9, 14);
                    var rp = new RunProperties(new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman" }, new FontSize { Val = ((int)Math.Round(boyut * 2)).ToString() });
                    body.Append(new Paragraph(pp, new Run(rp, new Text(sat.Metin) { Space = SpaceProcessingModeValues.Preserve })));
                    oncekiAlt = sat.Alt;
                }
                if (pn < pd.NumberOfPages) body.Append(new Paragraph(new ParagraphProperties(osec), new Run()));
                else sonSec = osec;
                continue;
            }
            var kelimeler = sayfa.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
            var ogeler = new List<Ogesi>();
            var tablolar = new List<(double[] Xs, double[] Ys)>();
            try { tablolar = TabloBul(sayfa); } catch (Exception) { tablolar = new(); }
            var tabloKelime = new HashSet<Word>();
            foreach (var (xs, ys) in tablolar)
            {
                var icerik = kelimeler.Where(w => Icinde(w, xs, ys)).ToList();
                foreach (var w in icerik) tabloKelime.Add(w);
                ogeler.Add(new Ogesi { Top = ys[^1], Bottom = ys[0], Left = xs[0], Tablo = (xs, ys, icerik) });
                tabloSayisi++;
            }
            foreach (var s in SatirlaraBol(kelimeler.Where(w => !tabloKelime.Contains(w))))
                ogeler.Add(new Ogesi { Top = s.Top, Bottom = s.Bottom, Left = s.Left, Satir = s });
            try
            {
                foreach (var im in sayfa.GetImages())
                {
                    if (im.TryGetPng(out var png) && png != null && png.Length > 0)
                    {
                        var b = im.Bounds;
                        ogeler.Add(new Ogesi { Top = b.Top, Bottom = b.Bottom, Left = b.Left, Resim = (png, b.Width, b.Height) });
                    }
                    else atlananResim++;
                }
            }
            catch (Exception) { atlananResim++; }

            double onceki = H - kenar;
            foreach (var o in ogeler.OrderByDescending(x => x.Top))
            {
                if (o.Tablo is { } t)
                {
                    body.Append(TabloOlustur(t.Xs, t.Ys, t.Kelimeler, W));
                    body.Append(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "0" })));
                }
                else if (o.Resim is { } rs)
                {
                    var w = Math.Min(rs.W, W - 2 * kenar); var h = rs.H * (w / Math.Max(rs.W, 1));
                    body.Append(ResimParagrafi(main, rs.Png, w, h, o.Left - kenar, ref resimNo));
                }
                else if (o.Satir != null)
                    body.Append(SatirParagrafi(o.Satir, kenar, W - 2 * kenar, W, onceki, false));
                onceki = o.Bottom;
            }
            if (pn < pd.NumberOfPages)
            {
                // sayfa sonu + bu sayfanın ölçüsünü bölümde sakla
                body.Append(new Paragraph(new ParagraphProperties(sec), new Run()));
            }
            else sonSec = sec;
        }
        if (sonSec != null) body.Append(sonSec);
        main.Document.Save();
        return cikis;
    }

    // ---------------------------------------------------------------- Word → metin
    public static string WordMetni(string docx)
    {
        using var doc = WordprocessingDocument.Open(docx, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return "";
        var sb = new StringBuilder();
        foreach (var p in body.Descendants<Paragraph>())
            sb.Append(string.Concat(p.Descendants<Text>().Select(t => t.Text))).Append('\n');
        return sb.ToString();
    }

    public static string WordToUdf(string docx, string cikis) => Documents.MakeUdf(WordMetni(docx).Replace("**", ""), cikis);

    public static string PdfToUdf(string pdf, string cikis, out bool metinYok, out int ocrSayfa, out string dil)
    {
        var sayfalar = SayfaMetinleri(pdf, out ocrSayfa, out dil);
        metinYok = sayfalar.All(s => s.Trim().Length == 0);
        return Documents.MakeUdf(string.Join("\n", sayfalar).Replace("**", ""), cikis);
    }
}
