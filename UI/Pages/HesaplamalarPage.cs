using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HukukErp.Core;
using HukukErp.Services;

namespace HukukErp.UI.Pages;

/// <summary>Hesaplamalar: İnfaz Yatar Hesaplama. Tamamen yerel (WPF) arayüz; belge tarzı kâğıt kartlar.</summary>
public class HesaplamalarPage : BasePage
{
    readonly InfazAraci _araci;

    public HesaplamalarPage(AppCtx ctx) : base(ctx, false)
    {
        _araci = new InfazAraci();
        Fill(_araci);
    }

    public override void Refresh() => _araci.Hesapla();
}

static class Pal
{
    static readonly Dictionary<string, string> Light = new()
    {
        ["bg"] = "#f2f3f5", ["paper"] = "#ffffff", ["ink"] = "#1b2433", ["muted"] = "#5d6675", ["line"] = "#d9dce2", ["accent"] = "#8a2232",
        ["accent_soft"] = "#f6e6e8", ["ds"] = "#2f6f55", ["ds_soft"] = "#e3f0ea", ["open"] = "#2f6a8f", ["open_soft"] = "#e2eef6",
        ["ks"] = "#9a6512", ["ks_soft"] = "#f7ecd9", ["end"] = "#33415c", ["end_soft"] = "#e4e8f0", ["warn"] = "#9a3412", ["warn_soft"] = "#fdecdf",
    };
    static readonly Dictionary<string, string> Dark = new()
    {
        ["bg"] = "#11141a", ["paper"] = "#191d25", ["ink"] = "#e7e9ee", ["muted"] = "#9aa2b1", ["line"] = "#2c323e", ["accent"] = "#e5808c",
        ["accent_soft"] = "#3a1f25", ["ds"] = "#7cc8a6", ["ds_soft"] = "#18302a", ["open"] = "#80b8dc", ["open_soft"] = "#172a38",
        ["ks"] = "#e5b467", ["ks_soft"] = "#33280f", ["end"] = "#a9b7d6", ["end_soft"] = "#1f2635", ["warn"] = "#f0a070", ["warn_soft"] = "#3a2416",
    };

    static Dictionary<string, string> P => Theme.Name == "dark" ? Dark : Light;
    public static Color C(string k) => (Color)ColorConverter.ConvertFromString(P[k]);
    public static SolidColorBrush B(string k) { var b = new SolidColorBrush(C(k)); b.Freeze(); return b; }
}

/// <summary>Tek ilam (ceza) girişi.</summary>
class IlamSatiri : Border
{
    public readonly ComboBox Tur = new(), Tk = new(), Cat = new();
    public readonly DateBox Sd = new();
    public readonly TextBox Y = new(), M = new(), D = new();
    readonly UIElement _dur;

    public IlamSatiri(Action onChange, Action<IlamSatiri> onRemove)
    {
        Background = Pal.B("paper");
        BorderBrush = Pal.B("line");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(14);

        var v = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var rm = Ui.Btn("Kaldır", "", () => onRemove(this));
        DockPanel.SetDock(rm, Dock.Right);
        var t = new TextBlock { Text = "İLAM", FontFamily = new FontFamily("Consolas"), FontSize = 12, Foreground = Pal.B("accent"), VerticalAlignment = VerticalAlignment.Center };
        head.Children.Add(rm); head.Children.Add(t);
        v.Children.Add(head);

        foreach (var (k, l) in new[] { ("sureli", "Süreli hapis"), ("mueb", "Müebbet hapis"), ("agir", "Ağırlaştırılmış müebbet") })
            Tur.Items.Add(new ComboItem { Value = k, Label = l });
        foreach (var (k, l) in new[] { ("0", "Yok"), ("1", "Mükerrir"), ("2", "İkinci kez mükerrir") })
            Tk.Items.Add(new ComboItem { Value = k, Label = l });
        foreach (var c in Infaz.Cats) Cat.Items.Add(new ComboItem { Value = c.Id, Label = c.L });
        Tur.SelectedIndex = 0; Tk.SelectedIndex = 0; Cat.SelectedIndex = 0;

        var r1 = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        for (int i = 0; i < 3; i++) r1.ColumnDefinitions.Add(new ColumnDefinition());
        void Put(Grid g, int col, string label, UIElement w)
        {
            var box = Lab(label, w);
            box.Margin = new Thickness(col == 0 ? 0 : 6, 0, col == g.ColumnDefinitions.Count - 1 ? 0 : 6, 0);
            Grid.SetColumn(box, col);
            g.Children.Add(box);
        }
        Put(r1, 0, "Ceza türü", Tur);
        Put(r1, 1, "Suç tarihi (GG.AA.YYYY)", Sd);
        Put(r1, 2, "Tekerrür", Tk);
        v.Children.Add(r1);

        var r2 = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        for (int i = 0; i < 3; i++) r2.ColumnDefinitions.Add(new ColumnDefinition());
        foreach (var tb in new[] { Y, M, D }) { tb.MaxLength = 3; tb.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit); }
        Put(r2, 0, "Yıl", Y); Put(r2, 1, "Ay", M); Put(r2, 2, "Gün", D);
        _dur = r2;
        v.Children.Add(r2);
        v.Children.Add(Lab("Suç türü", Cat));
        Child = v;

        foreach (var tb in new TextBox[] { Sd, Y, M, D }) tb.TextChanged += (_, _) => onChange();
        foreach (var cb in new[] { Tur, Tk, Cat }) cb.SelectionChanged += (_, _) => onChange();
        Tur.SelectionChanged += (_, _) => Sync();
        Sync();
    }

    void Sync() => _dur.Visibility = Val(Tur) == "sureli" ? Visibility.Visible : Visibility.Collapsed;

    static string Val(ComboBox c) => (c.SelectedItem as ComboItem)?.Value as string ?? "";

    public static StackPanel Lab(string text, UIElement w)
    {
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = text, FontSize = 13, Foreground = Pal.B("muted"), Margin = new Thickness(0, 0, 0, 5) });
        s.Children.Add(w);
        return s;
    }

    static int Int(TextBox t) => int.TryParse(t.Text.Trim(), out var n) ? Math.Max(0, n) : 0;

    public Ilam ToIlam() => new()
    {
        Tur = Val(Tur), Sd = InfazAraci.IsoToDate(Sd.Iso()), Tk = Val(Tk), Y = Int(Y), M = Int(M), D = Int(D), CatId = Val(Cat),
    };
}

/// <summary>Kapalı / açık / denetimli serbestlik / koşullu salıverilme zaman çizelgesi.</summary>
class Zaman : FrameworkElement
{
    ZamanInfo? _z;

    public Zaman() { Height = 64; MinHeight = 64; }

    public void SetData(ZamanInfo? z) { _z = z; InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        var z = _z;
        if (z == null) return;
        double w = Math.Max(1, ActualWidth - 8);
        double span = Math.Max(1, Infaz.Diff(z.Start, z.End));
        double X(DateTime d) => 4 + Math.Max(0, Math.Min(1, Infaz.Diff(z.Start, d) / span)) * w;
        void Bar(DateTime a, DateTime b, string col)
        {
            if (X(b) - X(a) > 0) dc.DrawRoundedRectangle(Pal.B(col), null, new Rect(X(a), 26, X(b) - X(a), 12), 3, 3);
        }
        Bar(z.Start, z.KapaliEnd, "accent");
        if (z.Acik != null) Bar(z.Acik.Value, z.Ds, "open");
        Bar(z.Ds, z.Ks, "ds");
        Bar(z.Ks, z.End, "ks");
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText Ft(string s) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), 11, Pal.B("muted"), dpi);
        dc.DrawText(Ft(Infaz.Fmt(z.Start)), new Point(4, 4));
        var t = Ft(Infaz.Fmt(z.End));
        dc.DrawText(t, new Point(ActualWidth - 4 - t.Width, 4));
        if (z.Start < z.Bugun && z.Bugun < z.End)
            dc.DrawLine(new Pen(Pal.B("accent"), 2), new Point(X(z.Bugun), 20), new Point(X(z.Bugun), 46));
    }
}

class InfazAraci : ScrollViewer
{
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    readonly List<IlamSatiri> _satirlar = new();
    readonly StackPanel _rows = new();
    readonly DateBox _start = new(), _birth = new();
    readonly TextBox _mahsup = new();
    readonly ComboBox _ozel = new(), _acikMod = new();
    readonly CheckBox _ihlal = new();
    readonly TextBlock _bugun = new() { FontSize = 13, Foreground = Pal.B("muted") };
    readonly TextBlock _uyari = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Foreground = Pal.B("warn") };
    readonly Border _uyariBox = new() { Background = Pal.B("warn_soft"), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10), Visibility = Visibility.Collapsed };
    readonly Grid _grid = new();
    readonly Zaman _zaman = new();
    readonly Grid _tbl = new();
    readonly StackPanel _adimlar = new();
    readonly TextBox _ozet = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 170, FontFamily = new FontFamily("Consolas"), FontSize = 12.5, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBlock _kopyaMsg = new() { FontSize = 13, Foreground = Pal.B("muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };

    public static DateTime? IsoToDate(string? s) =>
        DateTime.TryParseExact(s ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    static Border Panel(UIElement content)
    {
        var b = new Border
        {
            Background = Pal.B("paper"), BorderBrush = Pal.B("line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 22), Child = content,
        };
        return b;
    }

    static TextBlock H2s(string t) => new() { Text = t, FontFamily = new FontFamily("Georgia"), FontSize = 20, FontWeight = FontWeights.Medium, Foreground = Pal.B("ink"), Margin = new Thickness(0, 0, 0, 14) };

    public InfazAraci()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Background = Pal.B("bg");

        var col = new StackPanel { MaxWidth = 860, Margin = new Thickness(16, 28, 16, 56) };
        Content = new Grid { Children = { new Border { HorizontalAlignment = HorizontalAlignment.Center, Child = col, MaxWidth = 860 } } };
        // sütun, pencere genişliğine uyum sağlasın
        ((Border)((Grid)Content).Children[0]).HorizontalAlignment = HorizontalAlignment.Stretch;

        col.Children.Add(new TextBlock { Text = "5275 SAYILI CGTİHK · 7571 VE 7550 SAYILI KANUN DEĞİŞİKLİKLERİ DAHİL", FontFamily = new FontFamily("Consolas"), FontSize = 11, Foreground = Pal.B("accent") });
        col.Children.Add(new TextBlock { Text = "İnfaz Yatar Hesaplama", FontFamily = new FontFamily("Georgia"), FontSize = 36, FontWeight = FontWeights.Bold, Foreground = Pal.B("ink"), Margin = new Thickness(0, 4, 0, 6) });
        col.Children.Add(new TextBlock
        {
            Text = "Koşullu salıverilme, denetimli serbestlik ve hak ederek tahliye tarihlerini; suç tarihi, suç türü, tekerrür ve özel durumlara göre hesaplar. Birden fazla ilam içtima edilebilir.",
            TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = Pal.B("muted"), Margin = new Thickness(0, 0, 0, 22),
        });

        // --- cezalar
        var p1 = new StackPanel();
        p1.Children.Add(H2s("Cezalar"));
        _rows.Margin = new Thickness(0, 0, 0, 12);
        p1.Children.Add(_rows);
        var add = Ui.Btn("＋ Ceza ekle (içtima)", "", () => IlamEkle());
        add.HorizontalAlignment = HorizontalAlignment.Left;
        p1.Children.Add(add);
        col.Children.Add(Panel(p1));

        // --- infaz bilgileri
        foreach (var (k, l) in new[] { ("yok", "Yok"), ("kadin06", "0–6 yaş arası çocuğu olan kadın"), ("yas70", "70 yaşını bitirmiş"),
                                       ("agir", "Ağır hastalık / engellilik / yaşlılık (tek başına yaşayamayan)") })
            _ozel.Items.Add(new ComboItem { Value = k, Label = l });
        foreach (var (k, l) in new[] { ("auto", "Otomatik (Yönetmelik m.5–6’ya göre)"), ("dogrudan", "Doğrudan açık kurumda infaz"),
                                       ("kapali", "Açığa ayrılamaz (koşullu salıverilmesi geri alınan, birden fazla firar vb.)") })
            _acikMod.Items.Add(new ComboItem { Value = k, Label = l });
        _ozel.SelectedIndex = 0; _acikMod.SelectedIndex = 0;
        _mahsup.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        _mahsup.MaxLength = 5;
        _ihlal.Content = new TextBlock
        {
            Text = "Daha önce denetimli serbestlik ihlali, koşullu salıverilmenin geri alınması veya ikinci kez firar var (Geçici 10/6 ek 3 yıl uygulanmaz)",
            TextWrapping = TextWrapping.Wrap, Foreground = Pal.B("ink"),
        };
        _ihlal.Margin = new Thickness(0, 12, 0, 12);

        var g2 = new Grid();
        g2.ColumnDefinitions.Add(new ColumnDefinition()); g2.ColumnDefinitions.Add(new ColumnDefinition());
        g2.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g2.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void G(int r, int c, string label, UIElement w)
        {
            var b = IlamSatiri.Lab(label, w);
            b.Margin = new Thickness(c == 0 ? 0 : 6, r == 0 ? 0 : 12, c == 0 ? 6 : 0, 0);
            Grid.SetRow(b, r); Grid.SetColumn(b, c);
            g2.Children.Add(b);
        }
        G(0, 0, "İnfaz başlangıcı (yakalanma / teslim)", _start);
        G(0, 1, "Mahsup (gözaltı + tutukluluk, gün)", _mahsup);
        G(1, 0, "Doğum tarihi (çocuk hükümlü için)", _birth);
        G(1, 1, "Özel durum", _ozel);
        var p2 = new StackPanel();
        p2.Children.Add(H2s("İnfaz bilgileri"));
        p2.Children.Add(g2);
        p2.Children.Add(_ihlal);
        p2.Children.Add(IlamSatiri.Lab("Açık kuruma ayrılma", _acikMod));
        col.Children.Add(Panel(p2));
        foreach (var tb in new TextBox[] { _start, _mahsup, _birth }) tb.TextChanged += (_, _) => Degisti();
        foreach (var cb in new[] { _ozel, _acikMod }) cb.SelectionChanged += (_, _) => Degisti();
        _ihlal.Checked += (_, _) => Degisti();
        _ihlal.Unchecked += (_, _) => Degisti();

        // --- sonuç
        var p3 = new StackPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(_bugun, Dock.Right);
        var h3 = H2s("Sonuç"); h3.Margin = new Thickness(0);
        top.Children.Add(_bugun); top.Children.Add(h3);
        p3.Children.Add(top);
        _uyariBox.Child = _uyari; _uyariBox.Margin = new Thickness(0, 0, 0, 12);
        p3.Children.Add(_uyariBox);
        p3.Children.Add(_grid);
        _zaman.Margin = new Thickness(0, 14, 0, 0);
        p3.Children.Add(_zaman);
        var legend = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Pal.B("muted"), Margin = new Thickness(0, 6, 0, 0) };
        void Leg(string key, string text)
        {
            legend.Inlines.Add(new System.Windows.Documents.Run("■ ") { Foreground = Pal.B(key) });
            legend.Inlines.Add(new System.Windows.Documents.Run(text + "      "));
        }
        Leg("accent", "Kapalı cezaevi"); Leg("open", "Açık cezaevi"); Leg("ds", "Denetimli serbestlik"); Leg("ks", "Koşullu salıverilme denetim süresi");
        p3.Children.Add(legend);
        col.Children.Add(Panel(p3));

        // --- döküm
        var p4 = new StackPanel();
        p4.Children.Add(H2s("Hesap dökümü"));
        p4.Children.Add(_tbl);
        _adimlar.Margin = new Thickness(0, 12, 0, 0);
        p4.Children.Add(_adimlar);
        col.Children.Add(Panel(p4));

        // --- özet
        var p5 = new StackPanel();
        var top5 = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var kb = Ui.Btn("Metni kopyala", "Primary", Kopyala);
        DockPanel.SetDock(kb, Dock.Right); DockPanel.SetDock(_kopyaMsg, Dock.Right);
        var h5 = H2s("Dilekçe / not için özet"); h5.Margin = new Thickness(0);
        top5.Children.Add(kb); top5.Children.Add(_kopyaMsg); top5.Children.Add(h5);
        p5.Children.Add(top5);
        p5.Children.Add(_ozet);
        col.Children.Add(Panel(p5));

        // --- kurallar
        var p6 = new StackPanel();
        p6.Children.Add(H2s("Uygulanan kurallar"));
        foreach (var k in Kurallar.Liste)
        {
            var r = Ui.Rich(k, 13.5, "text", true);
            r.Margin = new Thickness(0, 0, 0, 8);
            p6.Children.Add(r);
        }
        col.Children.Add(Panel(p6));
        col.Children.Add(new TextBlock { Text = Kurallar.Uyari, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Pal.B("muted") });

        _timer.Tick += (_, _) => { _timer.Stop(); Hesapla(); };
        IlamEkle();
        Hesapla();
    }

    // ------------------------------------------------------------ satırlar
    void IlamEkle()
    {
        var s = new IlamSatiri(Degisti, IlamSil) { Margin = new Thickness(0, 0, 0, 12) };
        _satirlar.Add(s);
        _rows.Children.Add(s);
        Degisti();
    }

    void IlamSil(IlamSatiri s)
    {
        if (_satirlar.Count <= 1) return;
        _satirlar.Remove(s);
        _rows.Children.Remove(s);
        Degisti();
    }

    void Degisti() { _timer.Stop(); _timer.Start(); }

    // ------------------------------------------------------------ hesap
    static string Val(ComboBox c) => (c.SelectedItem as ComboItem)?.Value as string ?? "";

    Girdi BuildGirdi() => new()
    {
        Start = IsoToDate(_start.Iso()),
        Mahsup = int.TryParse(_mahsup.Text.Trim(), out var m) ? Math.Max(0, m) : 0,
        Birth = IsoToDate(_birth.Iso()),
        Ozel = Val(_ozel), Ihlal = _ihlal.IsChecked == true, AcikMod = Val(_acikMod),
        Ilamlar = _satirlar.Select(s => s.ToIlam()).ToList(), Bugun = DateTime.Today,
    };

    TextBlock Cell(string t, int col, bool header = false, bool mono = false)
    {
        var tb = new TextBlock
        {
            Text = t, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(10, 8, 10, 8),
            FontSize = header ? 11 : 13.5, FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = Pal.B(header ? "muted" : "ink"),
        };
        if (mono) tb.FontFamily = new FontFamily("Consolas");
        Grid.SetColumn(tb, col);
        return tb;
    }

    public void Hesapla()
    {
        var sonuc = Infaz.Hesapla(BuildGirdi());
        _grid.Children.Clear(); _grid.RowDefinitions.Clear(); _grid.ColumnDefinitions.Clear();
        _bugun.Text = $"Bugün: {Infaz.Fmt(DateTime.Today)}";
        if (sonuc.Eksik)
        {
            _grid.Children.Add(new TextBlock { Text = "Hesap için infaz başlangıcını ve her ilamın suç tarihini girin.", FontSize = 13, Foreground = Pal.B("muted") });
            _zaman.SetData(null);
            _tbl.Children.Clear(); _tbl.RowDefinitions.Clear(); _tbl.ColumnDefinitions.Clear();
            _adimlar.Children.Clear();
            _ozet.Text = "";
            _uyariBox.Visibility = Visibility.Collapsed;
            return;
        }
        _uyariBox.Visibility = sonuc.Uyarilar.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _uyari.Text = string.Join(" ", sonuc.Uyarilar);

        var renk = new Dictionary<string, (string fg, string soft)>
        {
            ["yatar"] = ("accent", "paper"), ["kapali"] = ("accent", "accent_soft"), ["acik"] = ("open", "open_soft"),
            ["ds"] = ("ds", "ds_soft"), ["ks"] = ("ks", "ks_soft"), ["end"] = ("end", "end_soft"),
        };
        _grid.ColumnDefinitions.Add(new ColumnDefinition()); _grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < sonuc.Kartlar.Count; i++)
        {
            var k = sonuc.Kartlar[i];
            var (fg, soft) = renk.TryGetValue(k.Cls, out var rr) ? rr : ("muted", "paper");
            var v = new StackPanel();
            v.Children.Add(new TextBlock { Text = k.K.ToUpper(new CultureInfo("tr-TR")), FontFamily = new FontFamily("Consolas"), FontSize = 11, Foreground = Pal.B(fg) });
            v.Children.Add(new TextBlock { Text = k.D, FontFamily = new FontFamily("Georgia"), FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Pal.B("ink"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
            v.Children.Add(new TextBlock { Text = k.S, FontSize = 13, Foreground = Pal.B("muted"), TextWrapping = TextWrapping.Wrap });
            if (k.Pill.Length > 0)
                v.Children.Add(new Border
                {
                    Background = Pal.B("paper"), BorderBrush = Pal.B("line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(9, 2, 9, 2), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock { Text = k.Pill, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Pal.B("ink") },
                });
            var card = new Border
            {
                Background = Pal.B(soft), BorderBrush = Pal.B("line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(i % 2 == 0 ? 0 : 6, 0, i % 2 == 0 ? 6 : 0, 12), Child = v,
            };
            int row = i / 2;
            while (_grid.RowDefinitions.Count <= row) _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(card, row); Grid.SetColumn(card, i % 2);
            _grid.Children.Add(card);
        }
        _zaman.SetData(sonuc.Zaman);

        // döküm tablosu
        _tbl.Children.Clear(); _tbl.RowDefinitions.Clear(); _tbl.ColumnDefinitions.Clear();
        var genis = new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star) };
        foreach (var gl in genis) _tbl.ColumnDefinitions.Add(new ColumnDefinition { Width = gl });
        var basliklar = new[] { "#", "SUÇ", "SUÇ TARİHİ", "CEZA", "GÜN", "KOŞULLU", "GEREKÇE" };
        _tbl.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int c = 0; c < 7; c++) _tbl.Children.Add(Cell(basliklar[c], c, true));
        for (int r = 0; r < sonuc.Tablo.Count; r++)
        {
            _tbl.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var line = new Border { BorderBrush = Pal.B("line"), BorderThickness = new Thickness(0, 1, 0, 0) };
            Grid.SetRow(line, r + 1); Grid.SetColumnSpan(line, 7);
            _tbl.Children.Add(line);
            for (int c = 0; c < 7; c++)
            {
                var cell = Cell(Convert.ToString(sonuc.Tablo[r][c], CultureInfo.InvariantCulture) ?? "", c, false, c >= 2 && c <= 5);
                Grid.SetRow(cell, r + 1);
                _tbl.Children.Add(cell);
            }
        }

        _adimlar.Children.Clear();
        foreach (var s in sonuc.Adimlar)
        {
            var r = Ui.Rich("•  " + s, 13.5, "text", true);
            r.Margin = new Thickness(0, 0, 0, 6);
            _adimlar.Children.Add(r);
        }
        _ozet.Text = sonuc.Ozet;
    }

    void Kopyala()
    {
        try { Clipboard.SetText(_ozet.Text); _kopyaMsg.Text = "Kopyalandı."; }
        catch (Exception) { _kopyaMsg.Text = "Kopyalanamadı."; }
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        t.Tick += (_, _) => { _kopyaMsg.Text = ""; t.Stop(); };
        t.Start();
    }
}
