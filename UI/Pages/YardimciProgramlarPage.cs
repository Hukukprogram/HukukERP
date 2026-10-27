using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HukukErp.Core;
using HukukErp.Services;
using Microsoft.Win32;

namespace HukukErp.UI.Pages;

/// <summary>Yardımcı programlar: sürükle-bırak dosya dönüştürücüler.</summary>
public class YardimciProgramlarPage : BasePage
{
    readonly StackPanel _sonuclar = new();
    readonly CheckBox _tekPdf = new() { Content = "Birden çok görseli tek PDF'te birleştir (ad sırasına göre)", IsChecked = true, Margin = new Thickness(0, 8, 0, 0) };
    readonly CheckBox _birlestir = new() { Content = "Tablo, görsel ve sayfa düzenini koru (kapalıysa yalnızca düz metin)", IsChecked = true, Margin = new Thickness(0, 8, 0, 0) };

    readonly ComboBox _sikSeviye = new() { Margin = new Thickness(0, 4, 0, 0) };
    readonly TextBox _sikHedef = new() { Text = "0", Width = 80, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
    readonly CheckBox _sikMetinli = new() { Content = "Metinli sayfaları da görüntüye çevirip sıkıştır (metin seçilemez olur)", Margin = new Thickness(0, 8, 0, 0) };
    readonly ComboBox _bolMod = new() { Margin = new Thickness(0, 4, 0, 0) };
    readonly TextBox _bolDeger = new() { Text = "10", Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
    readonly TextBlock _bolIpucu = Ui.Muted("Her kaç sayfada bir yeni dosya (örn. 10)");

    // sayfa ayırma
    readonly WrapPanel _kucukler = new();
    readonly List<SayfaOgesi> _sayfalar = new();
    readonly List<int> _secim = new();
    readonly List<(Border Kutu, TextBlock Rozet)> _kutular = new();
    readonly CheckBox _sirayla = new() { Content = "Seçim sırasına göre birleştir (kapalıysa sayfa sırasıyla)", Margin = new Thickness(0, 8, 0, 0) };
    readonly TextBlock _sayfaBilgi = Ui.Muted("Henüz sayfa yüklenmedi.");

    sealed class Birakma : Border
    {
        readonly TextBlock _yazi;

        public Birakma(string baslik, string aciklama, string filtre, string[] uzantilar, Action<string[]> islem)
        {
            AllowDrop = true;
            MinHeight = 96;
            BorderThickness = new Thickness(2);
            CornerRadius = new CornerRadius(12);
            Padding = new Thickness(12);
            Cursor = Cursors.Hand;
            SetResourceReference(BorderBrushProperty, "border");
            SetResourceReference(BackgroundProperty, "surface2");
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(Ui.Text(baslik, 14, true));
            _yazi = Ui.Muted(aciklama);
            sp.Children.Add(_yazi);
            Child = sp;

            void Vurgu(bool on) => SetResourceReference(BorderBrushProperty, on ? "accent" : "border");
            string[] Sec(string[] f) => f.Where(x => uzantilar.Contains(Path.GetExtension(x).ToLowerInvariant())).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            DragEnter += (_, e) => { Vurgu(true); e.Effects = DragDropEffects.Copy; e.Handled = true; };
            DragOver += (_, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
            DragLeave += (_, _) => Vurgu(false);
            Drop += (_, e) =>
            {
                Vurgu(false);
                if (e.Data.GetData(DataFormats.FileDrop) is not string[] f) return;
                var uygun = Sec(f);
                if (uygun.Length == 0) { Ui.Warn(this, $"Bu alan yalnızca şu dosyaları kabul eder: {string.Join(", ", uzantilar)}"); return; }
                islem(uygun);
            };
            MouseLeftButtonUp += (_, _) =>
            {
                var dlg = new OpenFileDialog { Filter = filtre, Multiselect = true, Title = baslik };
                if (dlg.ShowDialog() == true) islem(Sec(dlg.FileNames));
            };
        }
    }

    public YardimciProgramlarPage(AppCtx ctx) : base(ctx)
    {
        Top(Ui.PageHeader("Yardımcı Programlar", "Dosyayı kutuya sürükleyip bırakın (veya kutuya tıklayıp seçin). Çıktı, kaynak dosyanın yanına kaydedilir."));

        var jpg = new Birakma("JPEG / görsel → PDF", "JPG, PNG, BMP, TIF dosyalarını sürükleyin. Sayfa A4'e sığdırılır.",
            "Görseller|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|Tümü|*.*", new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" }, GorselPdf);
        var word = new Birakma("PDF → Word", "PDF dosyasını sürükleyin. Taranmış (resim) sayfalar OCR ile yazıya çevrilir.",
            "PDF|*.pdf", new[] { ".pdf" }, PdfWord);
        var udf1 = new Birakma("Word → UDF", "Word (.docx) dosyasını sürükleyin. UYAP Doküman Editörü için .udf üretilir.",
            "Word|*.docx", new[] { ".docx" }, WordUdf);
        var udf2 = new Birakma("PDF → UDF", "PDF dosyasını sürükleyin. Taranmış (resim) sayfalar OCR ile yazıya çevrilir.",
            "PDF|*.pdf", new[] { ".pdf" }, PdfUdf);

        Card Kart(Birakma b, UIElement? ek = null)
        {
            var c = new Card { Margin = new Thickness(0, 0, 0, 12) };
            c.Top(b, 0);
            if (ek != null) c.Top(ek, 0);
            return c;
        }

        var sol = new StackPanel(); var sag = new StackPanel();
        sol.Children.Add(Kart(jpg, _tekPdf)); sol.Children.Add(Kart(udf1));
        sag.Children.Add(Kart(word, _birlestir)); sag.Children.Add(Kart(udf2));
        var grid = Ui.HSplit(sol, sag, 1, 1);

        foreach (var sv in PdfAraclari.Seviyeler) _sikSeviye.Items.Add(sv.Ad);
        _sikSeviye.SelectedIndex = 1;
        var sik = new Birakma("PDF sıkıştır", "PDF'leri sürükleyin. Taranmış/resimli sayfalar yeniden kodlanarak küçültülür.",
            "PDF|*.pdf", new[] { ".pdf" }, PdfSikistir);
        var sikKart = new Card { Margin = new Thickness(0, 0, 0, 12) };
        sikKart.Top(sik, 8);
        sikKart.Top(Ui.Muted("Sıkıştırma düzeyi", false), 0); sikKart.Top(_sikSeviye, 8);
        sikKart.Top(Ui.Muted("Hedef boyut (MB; 0 = yalnızca seçilen düzey). Hedefe inene kadar daha güçlü düzeyler denenir.", false), 0);
        sikKart.Top(_sikHedef, 0);
        sikKart.Top(_sikMetinli, 8);
        sikKart.Fill(Ui.Muted("Çıktı '<ad>_kucuk.pdf' olarak kaydedilir. Küçülmüyorsa dosya oluşturulmaz. Sıkıştırma kayıplıdır; imzalı (e-imzalı) PDF'lerde imza geçersiz olur."));

        _bolMod.Items.Add("Sayfa sayısına göre (her N sayfada bir)");
        _bolMod.Items.Add("Sayfa aralıklarına göre (1-3, 5, 7-9)");
        _bolMod.Items.Add("Boyuta göre (her dosya en çok X MB)");
        _bolMod.SelectedIndex = 0;
        _bolMod.SelectionChanged += (_, _) =>
        {
            _bolIpucu.Text = _bolMod.SelectedIndex switch { 0 => "Her kaç sayfada bir yeni dosya (örn. 10)", 1 => "Her aralık ayrı dosya olur (örn. 1-3, 5, 7-9)", _ => "Her dosyanın en büyük boyutu, MB (örn. 5 — e-posta/UYAP sınırı için)" };
            _bolDeger.Text = _bolMod.SelectedIndex switch { 0 => "10", 1 => "1-3, 4-6", _ => "5" };
        };
        var bol = new Birakma("PDF böl", "PDF'leri sürükleyin. Aşağıdaki seçime göre parçalara ayrılır.",
            "PDF|*.pdf", new[] { ".pdf" }, PdfBol);
        var bolKart = new Card { Margin = new Thickness(0, 0, 0, 12) };
        bolKart.Top(bol, 8);
        bolKart.Top(Ui.Muted("Bölme yöntemi", false), 0); bolKart.Top(_bolMod, 8);
        bolKart.Top(_bolIpucu, 0); bolKart.Top(_bolDeger, 0);
        bolKart.Fill(Ui.Muted("Parçalar '<ad>_bolum_01.pdf' gibi kaynak dosyanın yanına kaydedilir."));
        var pdfGrid = Ui.HSplit(sikKart, bolKart, 1, 1);

        var sayfaZone = new Birakma("PDF / TIFF sayfa ayırma", "PDF veya TIFF dosyalarını sürükleyin; her sayfa aşağıda görünür. Birden çok dosya aynı listeye eklenir.",
            "PDF ve TIFF|*.pdf;*.tif;*.tiff", new[] { ".pdf", ".tif", ".tiff" }, SayfaYukle);
        var sayfaBar = Ui.HRow(Ui.Btn("Tümünü seç", "", () => SecimAyarla(true)), Ui.Btn("Seçimi kaldır", "", () => SecimAyarla(false)),
            Ui.Btn("Listeyi temizle", "", ListeTemizle), Ui.Btn("Seçilenleri birleştir → tek PDF", "Primary", SecilenleriBirlestir),
            Ui.Btn("Seçilenleri ayrı PDF'lere böl", "", SecilenleriAyir));
        var sayfaUst = new StackPanel();
        sayfaUst.Children.Add(sayfaZone);
        sayfaUst.Children.Add(_sirayla);
        sayfaBar.Margin = new Thickness(0, 8, 0, 0);
        sayfaUst.Children.Add(sayfaBar);
        _sayfaBilgi.Margin = new Thickness(0, 6, 0, 6);
        sayfaUst.Children.Add(_sayfaBilgi);
        DockPanel.SetDock(sayfaUst, Dock.Top);
        var sayfaDock = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        sayfaDock.Children.Add(sayfaUst);
        sayfaDock.Children.Add(new ScrollViewer { Content = _kucukler, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _sirayla.Checked += (_, _) => Gorunum();
        _sirayla.Unchecked += (_, _) => Gorunum();

        var tabs = new TabControl();
        tabs.Items.Add(Ui.Tab("Dönüştürücüler", new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }));
        tabs.Items.Add(Ui.Tab("PDF Sıkıştır / Böl", new ScrollViewer { Content = pdfGrid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }));
        tabs.Items.Add(Ui.Tab("Sayfa Ayırma / Birleştirme", sayfaDock));

        var sonuc = new Card { Height = 190, Margin = new Thickness(0, 8, 0, 0) };
        sonuc.Top(Ui.H2("Sonuçlar"), 8);
        sonuc.Fill(new ScrollViewer { Content = _sonuclar, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        DockPanel.SetDock(sonuc, Dock.Bottom);
        Root.Children.Add(sonuc);
        Fill(tabs);
        Log("Hazır. Dosyaları yukarıdaki kutulara bırakın.", null);
    }

    // ------------------------------------------------------------------ sonuç listesi
    void Log(string metin, string? dosya, bool hata = false)
    {
        var satir = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        if (dosya != null)
        {
            var ac = Ui.Btn("Aç", "", () => Ui.OpenExternal(dosya));
            var kl = Ui.Btn("Klasörde göster", "", () => Ui.RevealInFolder(dosya));
            var bt = Ui.HRow(ac, kl);
            DockPanel.SetDock(bt, Dock.Right);
            satir.Children.Add(bt);
        }
        satir.Children.Add(Ui.Text((hata ? "✖ " : dosya != null ? "✔ " : "") + metin, 13, false, hata ? "danger" : "text", true));
        _sonuclar.Children.Insert(0, satir);
    }

    void Calistir(string ad, Func<(string Yol, string Mesaj)> is_)
    {
        Ctx.Status($"{ad} çalışıyor…");
        Ui.RunAsync(is_, r => { Ctx.Status($"{ad} tamamlandı"); Log(r.Mesaj, r.Yol); },
            m => { Ctx.Status(""); Log($"{ad}: {m}", null, true); });
    }

    // ------------------------------------------------------------------ dönüştürmeler
    void GorselPdf(string[] f)
    {
        var tek = _tekPdf.IsChecked == true;
        Calistir("Görsel → PDF", () =>
        {
            var klasor = Path.GetDirectoryName(f[0])!;
            if (tek || f.Length == 1)
            {
                var yol = Donusum.BenzersizYol(klasor, Path.GetFileNameWithoutExtension(f[0]) + (f.Length > 1 ? "_birlesik" : ""), ".pdf");
                Donusum.GorselleriPdfYap(f, yol);
                return (yol, $"{f.Length} görsel PDF'e çevrildi: {Path.GetFileName(yol)}");
            }
            string son = "";
            foreach (var x in f)
            {
                son = Donusum.BenzersizYol(Path.GetDirectoryName(x)!, Path.GetFileNameWithoutExtension(x), ".pdf");
                Donusum.GorselleriPdfYap(new[] { x }, son);
            }
            return (son, $"{f.Length} görsel ayrı ayrı PDF'e çevrildi (son dosya: {Path.GetFileName(son)})");
        });
    }

    void PdfWord(string[] f)
    {
        var birl = _birlestir.IsChecked == true;
        foreach (var x in f)
            Calistir($"PDF → Word ({Path.GetFileName(x)})", () =>
            {
                var yol = Donusum.BenzersizYol(Path.GetDirectoryName(x)!, Path.GetFileNameWithoutExtension(x), ".docx");
                if (birl)
                {
                    Donusum.PdfToWordDuzenli(x, yol, out var sp, out var atl, out var tbl, out var ocrN, out var dilW);
                    return (yol, $"{sp} sayfa Word'e aktarıldı ({tbl} tablo bulundu" + (atl > 0 ? $", {atl} görsel aktarılamadı" : "") + (ocrN > 0 ? $", {ocrN} taranmış sayfa OCR ile yazıya çevrildi [{dilW}]" : "") + $"): {Path.GetFileName(yol)}");
                }
                Donusum.PdfToWord(x, yol, true, out var sayfa, out var yok, out var ocrN2, out var dilW2);
                return (yol, yok ? $"{Path.GetFileName(yol)} oluşturuldu ama OCR dahil hiç yazı bulunamadı (sayfalar boş ya da okunaksız olabilir)."
                              : $"{sayfa} sayfa Word'e aktarıldı{(ocrN2 > 0 ? $" ({ocrN2} sayfa OCR ile, {dilW2})" : "")}: {Path.GetFileName(yol)}");
            });
    }

    void WordUdf(string[] f)
    {
        foreach (var x in f)
            Calistir($"Word → UDF ({Path.GetFileName(x)})", () =>
            {
                var yol = Donusum.BenzersizYol(Path.GetDirectoryName(x)!, Path.GetFileNameWithoutExtension(x), ".udf");
                Donusum.WordToUdf(x, yol);
                return (yol, $"UDF oluşturuldu: {Path.GetFileName(yol)} (biçimlendirme taşınmaz, yalnızca metin)");
            });
    }

    void PdfUdf(string[] f)
    {
        foreach (var x in f)
            Calistir($"PDF → UDF ({Path.GetFileName(x)})", () =>
            {
                var yol = Donusum.BenzersizYol(Path.GetDirectoryName(x)!, Path.GetFileNameWithoutExtension(x), ".udf");
                Donusum.PdfToUdf(x, yol, out var yok, out var ocrN3, out var dilW3);
                return (yol, yok ? $"{Path.GetFileName(yol)} oluşturuldu ama OCR dahil hiç yazı bulunamadı (sayfalar boş ya da okunaksız olabilir)."
                                 : $"UDF oluşturuldu: {Path.GetFileName(yol)} (yalnızca metin{(ocrN3 > 0 ? $"; {ocrN3} sayfa OCR ile, {dilW3}" : "")})");
            });
    }

    // ------------------------------------------------------------------ PDF sıkıştır / böl
    static string MB(long b) => (b / 1048576.0).ToString("0.##") + " MB";

    void PdfSikistir(string[] f)
    {
        var seviye = Math.Max(0, _sikSeviye.SelectedIndex);
        double.TryParse(_sikHedef.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hedef);
        var metinli = _sikMetinli.IsChecked == true;
        foreach (var x in f)
            Calistir($"PDF sıkıştır ({Path.GetFileName(x)})", () =>
            {
                var yol = Donusum.BenzersizYol(Path.GetDirectoryName(x)!, Path.GetFileNameWithoutExtension(x) + "_kucuk", ".pdf");
                var r = PdfAraclari.Sikistir(x, yol, seviye, hedef, metinli);
                if (r == null) throw new InvalidOperationException("Bu PDF'in tüm sayfaları metinli; sıkıştırılacak görüntü yok. 'Metinli sayfaları da görüntüye çevir' seçeneğini açabilirsiniz.");
                var (cikis, once, sonra, n, ad) = r.Value;
                if (cikis == x) throw new InvalidOperationException($"Küçülmedi ({MB(once)} → {MB(sonra)}); dosya zaten optimize olabilir. Daha güçlü düzey deneyin.");
                var hedefNot = hedef > 0 && sonra > hedef * 1048576 ? $" — hedef {hedef:0.##} MB'a inilemedi" : "";
                return (cikis, $"{Path.GetFileName(x)}: {MB(once)} → {MB(sonra)} ({n} sayfa yeniden kodlandı, {ad}){hedefNot}");
            });
    }

    void PdfBol(string[] f)
    {
        var mod = _bolMod.SelectedIndex;
        var deger = _bolDeger.Text.Trim();
        foreach (var x in f)
            Calistir($"PDF böl ({Path.GetFileName(x)})", () =>
            {
                List<string> parcalar;
                string ek = "";
                if (mod == 0)
                {
                    if (!int.TryParse(deger, out var n) || n < 1) throw new InvalidOperationException("Sayfa sayısı için 1 veya daha büyük bir tam sayı girin.");
                    parcalar = PdfAraclari.BolSayfaSayisi(x, n);
                }
                else if (mod == 1) parcalar = PdfAraclari.BolAraliklar(x, deger);
                else
                {
                    if (!double.TryParse(deger.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mb) || mb <= 0)
                        throw new InvalidOperationException("Boyut için MB cinsinden pozitif bir sayı girin.");
                    parcalar = PdfAraclari.BolBoyut(x, mb, out var asan);
                    if (asan.Count > 0) ek = $" — {asan.Count} parça sınırı aşıyor (tek sayfa bile büyük; önce sıkıştırın)";
                }
                return (parcalar[^1], $"{Path.GetFileName(x)}: {parcalar.Count} parçaya bölündü ({string.Join(", ", parcalar.Take(3).Select(q => Path.GetFileName(q)))}{(parcalar.Count > 3 ? ", …" : "")}){ek}");
            });
    }

    // ------------------------------------------------------------------ sayfa ayırma / birleştirme
    void SayfaYukle(string[] dosyalar)
    {
        Ctx.Status("Sayfalar yükleniyor…");
        Ui.RunAsync(() => dosyalar.SelectMany(d => PdfAraclari.SayfalariYukle(d)).ToList(),
            liste =>
            {
                Ctx.Status("");
                foreach (var o in liste) SayfaEkle(o);
                _sayfaBilgi.Text = $"{_sayfalar.Count} sayfa listede. Birleştirmek istediklerinize tıklayarak seçin.";
                Gorunum();
            },
            m => { Ctx.Status(""); Log($"Sayfalar yüklenemedi: {m}", null, true); });
    }

    void SayfaEkle(SayfaOgesi o)
    {
        int idx = _sayfalar.Count;
        _sayfalar.Add(o);
        var resim = new Image { Source = o.Kucuk, Height = 210, Stretch = System.Windows.Media.Stretch.Uniform };
        var rozet = new TextBlock { FontWeight = FontWeights.Bold, FontSize = 16, Margin = new Thickness(4), Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        rozet.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        var g = new Grid();
        g.Children.Add(resim); g.Children.Add(rozet);
        var ad = Ui.Muted($"{Path.GetFileName(o.Dosya)} – s.{o.No + 1}", false);
        ad.TextTrimming = TextTrimming.CharacterEllipsis;
        var sp = new StackPanel();
        sp.Children.Add(g); sp.Children.Add(ad);
        var kutu = new Border { Width = 180, Margin = new Thickness(6), Padding = new Thickness(6), BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(10), Child = sp, Cursor = Cursors.Hand };
        kutu.MouseLeftButtonUp += (_, _) => Degistir(idx);
        _kutular.Add((kutu, rozet));
        _kucukler.Children.Add(kutu);
    }

    void Degistir(int idx)
    {
        if (!_secim.Remove(idx)) _secim.Add(idx);
        Gorunum();
    }

    List<int> CikisSirasi() => _sirayla.IsChecked == true ? _secim.ToList() : _secim.OrderBy(x => x).ToList();

    void Gorunum()
    {
        var sira = CikisSirasi();
        for (int i = 0; i < _kutular.Count; i++)
        {
            var (kutu, rozet) = _kutular[i];
            var sec = _secim.Contains(i);
            kutu.SetResourceReference(Border.BorderBrushProperty, sec ? "accent" : "border");
            kutu.SetResourceReference(Border.BackgroundProperty, sec ? "sel" : "surface2");
            rozet.Visibility = sec ? Visibility.Visible : Visibility.Collapsed;
            if (sec) rozet.Text = "✔ " + (sira.IndexOf(i) + 1);
        }
        if (_sayfalar.Count > 0) _sayfaBilgi.Text = $"{_sayfalar.Count} sayfa listede, {_secim.Count} seçili.";
    }

    void SecimAyarla(bool hepsi)
    {
        _secim.Clear();
        if (hepsi) _secim.AddRange(Enumerable.Range(0, _sayfalar.Count));
        Gorunum();
    }

    void ListeTemizle()
    {
        _sayfalar.Clear(); _secim.Clear(); _kutular.Clear(); _kucukler.Children.Clear();
        _sayfaBilgi.Text = "Liste temizlendi.";
    }

    void SecilenleriBirlestir()
    {
        if (_secim.Count == 0) { Ui.Info(this, "Önce birleştirilecek sayfaları seçin."); return; }
        var secili = CikisSirasi().Select(i => _sayfalar[i]).ToList();
        Calistir("Sayfa birleştirme", () =>
        {
            var yol = Donusum.BenzersizYol(Path.GetDirectoryName(secili[0].Dosya)!, Path.GetFileNameWithoutExtension(secili[0].Dosya) + "_secili", ".pdf");
            PdfAraclari.SayfalariBirlestir(secili, yol);
            return (yol, $"{secili.Count} sayfa birleştirildi: {Path.GetFileName(yol)}");
        });
    }

    void SecilenleriAyir()
    {
        if (_secim.Count == 0) { Ui.Info(this, "Önce ayrılacak sayfaları seçin."); return; }
        var secili = CikisSirasi().Select(i => _sayfalar[i]).ToList();
        Calistir("Sayfaları ayırma", () =>
        {
            string son = "";
            foreach (var o in secili)
            {
                son = Donusum.BenzersizYol(Path.GetDirectoryName(o.Dosya)!, $"{Path.GetFileNameWithoutExtension(o.Dosya)}_sayfa_{o.No + 1}", ".pdf");
                PdfAraclari.SayfalariBirlestir(new[] { o }, son);
            }
            return (son, $"{secili.Count} sayfa ayrı ayrı PDF yapıldı (son dosya: {Path.GetFileName(son)})");
        });
    }
}
