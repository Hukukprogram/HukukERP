using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HukukErp.Core;
using HukukErp.Services;
using Microsoft.Data.Sqlite;

namespace HukukErp.UI.Pages;

/// <summary>Müvekkil: solda aranabilir liste (harf çubuğu, filtreler), sağda seçili müvekkilin özeti, dosyaları, cari hesabı, süreleri, evrakı ve notları.</summary>
public class MuvekkilFinansPage : BasePage
{
    static readonly string[] Siralar =
        { "Ad Soyad (A → Z)", "Ad Soyad (Z → A)", "T.C. No", "Kalan bakiye (çoktan aza)", "Dava sayısı (çoktan aza)", "Son eklenen", "Sonraki duruşma (yakın → uzak)" };
    static readonly string[] Harfler = { "A", "B", "C", "Ç", "D", "E", "F", "G", "Ğ", "H", "I", "İ", "J", "K", "L", "M", "N", "O", "Ö", "P", "R", "S", "Ş", "T", "U", "Ü", "V", "Y", "Z" };
    static readonly CultureInfo TrC = new("tr-TR");

    // ---- sekme tabloları
    readonly DataTable _tF = new(("Dava Dosyası", "dava_etiket", null), ("Dosya Konusu", "dosya_konusu", null), ("Toplam Ücret", "toplam_ucret", Fmt.Money),
        ("Alınan", "alinan_tutar", Fmt.Money), ("Kalan Bakiye", "kalan_bakiye", Fmt.Money), ("Sonraki Tahsilat", "sonraki_tahsilat_tarihi", Fmt.Date),
        ("Makbuz", "makbuz_kesildi", v => v != null && Convert.ToInt64(v) != 0 ? "Kesildi" : "Bekliyor"));
    readonly DataTable _tV = new(("Tarih", "tarih", Fmt.Date), ("Tür", "tur", null), ("Bağlı Dava", "dava", null), ("Açıklama", "aciklama", null), ("Dosya", "dosya", null));
    readonly DataTable _tDv = new(("Mahkeme", "mahkeme_adi", null), ("Esas No", "esas_no", null), ("Tür", "dava_turu", null),
        ("Karşı Taraf", "karsi_taraf", null), ("Duruşma", "durusma_tarihi", Fmt.Date), ("Aşama", "asama_durum", null));
    readonly DataTable _tH = new(("Tahsilat Tarihi", "tarih", Fmt.Date), ("Tutar", "tutar", Fmt.Money), ("Açıklama", "aciklama", null));
    readonly DataTable _tS = new(("Son Gün", "son_tarih", Fmt.Date), ("Süre / İş", "baslik", null), ("Dava", "dava", null), ("Durum", "durum", null));
    readonly DataTable _tOzD = new(("Mahkeme", "mahkeme_adi", null), ("Esas", "esas_no", null), ("Aşama", "asama_durum", null), ("Duruşma", "durusma_tarihi", Fmt.Date));
    readonly DataTable _tOzC = new(("Tarih", "tarih", Fmt.Date), ("Açıklama", "aciklama", null), ("Tutar", "tutar", Fmt.Money));
    readonly TextBlock _vTitle = Ui.H2("Evrak ve vekaletnameler");
    readonly TextBlock _fSum = Ui.Muted("");
    readonly TextBlock _hTitle = Ui.H2("Tahsilat hareketleri — cari kaydı seçin");
    readonly TextBlock _hSum = Ui.Muted("");

    // ---- sol liste
    readonly TextBlock _sayi = Ui.Muted("", false);
    readonly TextBox _ara = new() { Margin = new Thickness(0, 0, 0, 8), ToolTip = "Ad soyad, T.C. no, telefon veya e-posta ile ara  •  Enter: ilk sonucu aç  •  ↑ ↓: sonuçlar arasında gez" };
    readonly ComboBox _sira = new() { MinWidth = 170 };
    readonly StackPanel _kWrap = new();
    readonly ScrollViewer _kScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = true };
    readonly Dictionary<string, TextBlock> _harfT = new();
    string _harf = "";
    static readonly string[] Palet = { "#5B8CFF", "#34D39E", "#F7B23B", "#FF6B7A", "#A78BFA", "#22C1DC", "#F472B6", "#94A3B8" };
    enum Fl { Tumu, Bakiye, VekYok, Davali, DavasiYok }
    Fl _fl = Fl.Tumu;
    readonly Dictionary<Fl, TbBtn> _chip = new();
    const int KartSayfa = 60;                 // binlerce müvekkilde arayüzü yavaşlatmamak için satırlar parça parça çizilir
    int _kLimit = KartSayfa;
    List<Row> _kRows = new(); int _kToplam;
    List<Row> _gorunen = new();
    readonly System.Windows.Threading.DispatcherTimer _aramaT = new() { Interval = TimeSpan.FromMilliseconds(220) };

    // ---- sağ detay
    readonly ContentControl _sag = new();
    readonly TabControl _tabs = new();
    static TextBlock KV(double boyut = 22) => Ui.Text("–", boyut, true);
    readonly TextBlock _kTop = KV(), _kBak = KV(18), _kVek = KV(), _kDav = KV();
    readonly TextBlock _dkTop = KV(18), _dkAl = KV(18), _dkKal = KV(18), _dkDav = KV(18), _dkDur = KV(18), _dkSure = KV(18);
    readonly TextBlock _avatar = Ui.Text("", 20, true);
    readonly TextBlock _dTitle = Ui.H2("");
    readonly TextBlock _dSub = Ui.Muted("");
    readonly TextBlock _dNot = Ui.Text("", 12, false, "warn", true);
    readonly TextBlock _oTel = Ui.Text("–", 13, false, "text", true), _oMail = Ui.Text("–", 13, false, "text", true), _oAdres = Ui.Text("–", 13, false, "text", true), _oNot = Ui.Text("–", 13, false, "text", true);
    readonly TextBox _notBox = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top };
    TabItem _tabO = null!, _tabF = null!, _tabV = null!, _tabD = null!, _tabS = null!, _tabN = null!;
    UIElement _detay = null!, _bosSag = null!;
    Row? _mSel;
    long _sid;
    bool _busy;

    public MuvekkilFinansPage(AppCtx ctx) : base(ctx)
    {
        _kScroll.Content = _kWrap;
        _kScroll.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down) { Gez(1); e.Handled = true; }
            else if (e.Key == Key.Up) { Gez(-1); e.Handled = true; }
        };
        _bosSag = BuildBosSag();
        _detay = BuildDetay();
        _sag.Content = _bosSag;
        Fill(Ui.HSplit(BuildSol(), _sag, 4, 7));

        foreach (var s in Siralar) _sira.Items.Add(s);
        _sira.SelectedIndex = 0;
        _sira.SelectionChanged += (_, _) => { if (!_busy) ListeYukle(); };
        _aramaT.Tick += (_, _) => { _aramaT.Stop(); ListeYukle(); };
        _ara.TextChanged += (_, _) => { if (_busy) return; _aramaT.Stop(); _aramaT.Start(); };
        _ara.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { _ara.Text = ""; e.Handled = true; }
            else if (e.Key == Key.Down) { Gez(1); e.Handled = true; }
            else if (e.Key == Key.Up) { Gez(-1); e.Handled = true; }
            else if (e.Key == Key.Enter)
            {
                // Enter: yazılanı hemen uygula ve ilk sonucu aç (T.C. no / telefon ile hızlı erişim)
                _aramaT.Stop(); ListeYukle();
                if (_gorunen.Count > 0 && _ara.Text.Trim().Length > 0) Ac(_gorunen[0]);
                e.Handled = true;
            }
        };
        _tS.MouseDoubleClick += (_, _) => Ctx.Navigate("is_listesi", null);
        _tF.SelectionChanged += (_, _) => { if (!_busy) LoadHareket(); };
        _tV.MouseDoubleClick += (_, _) => VekAc();
        _tDv.MouseDoubleClick += (_, _) => DosyaKartinaGit();
        _tOzD.MouseDoubleClick += (_, _) =>
        {
            if (_tOzD.Current is { } c) { DosyaKartiPage.Hedef = c.L("id"); Ctx.Navigate("dosya_karti", null); }
        };
    }

    public void YeniMuvekkil() { Geri(); AddM(); }

    /// <summary>Kısayoldan gelinir: müvekkil seçtirir, cari hesabını açar, tahsilat ekler.</summary>
    public void TahsilatEkle()
    {
        if (Db.ScalarL("SELECT COUNT(*) FROM muvekkiller") == 0) { Ui.Info(this, "Önce müvekkil ekleyin."); return; }
        long id = _sid;
        if (id == 0)
        {
            var ms = TrSirala(Db.Query("SELECT id, ad_soyad, tc_no FROM muvekkiller"));
            var v = FormDialog.Ask(this, "Tahsilat girişi — müvekkil seçin", new List<Field>
            {
                new("muvekkil_id", "Müvekkil", "combo", ms.Select(m => ((object?)m.L("id"), $"{m.S("ad_soyad")} ({m.S("tc_no")})")), required: true),
            });
            if (v == null) return;
            id = Convert.ToInt64(v["muvekkil_id"]);
        }
        if (id != _sid) Ac(Db.One("SELECT * FROM muvekkiller WHERE id=?", id));
        _tabs.SelectedItem = _tabF;
        if (_tF.Current == null && _tF.Rows.Count > 0) _tF.SelectFirst();
        if (_tF.Current != null) Tahsilat(); else AddF();
    }

    static List<Row> TrSirala(List<Row> l)
    {
        var cmp = StringComparer.Create(TrC, true);
        return l.OrderBy(x => x.S("ad_soyad"), cmp).ToList();
    }

    // ------------------------------------------------------------ sol panel: liste
    static Border Kart(string et, TextBlock v)
    {
        var sp = new StackPanel();
        sp.Children.Add(Ui.Muted(et, false));
        v.Margin = new Thickness(0, 4, 0, 0);
        sp.Children.Add(v);
        var b = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 10, 0), Child = sp };
        b.SetResourceReference(Border.BackgroundProperty, "surface");
        b.SetResourceReference(Border.BorderBrushProperty, "border");
        return b;
    }

    UIElement BuildSol()
    {
        var dock = new DockPanel();

        var yeni = Ui.Btn("＋ Yeni", "Primary", AddM);
        DockPanel.SetDock(yeni, Dock.Right);
        var baslik = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        baslik.Children.Add(yeni); baslik.Children.Add(Ui.Text("Müvekkiller", 18, true));
        DockPanel.SetDock(baslik, Dock.Top);

        DockPanel.SetDock(_ara, Dock.Top);

        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var (f, ad) in new[] { (Fl.Tumu, "Tümü"), (Fl.Bakiye, "Bakiyesi olan"), (Fl.Davali, "Davası olan"), (Fl.DavasiYok, "Davası olmayan") })
        {
            var ff = f;
            var c = new TbBtn("", ad, () => { _fl = ff; ListeYukle(); });
            _chip[f] = c; chips.Children.Add(c);
        }
        DockPanel.SetDock(chips, Dock.Top);

        var sr = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(_sira, Dock.Right);
        sr.Children.Add(_sira); sr.Children.Add(_sayi);
        DockPanel.SetDock(sr, Dock.Top);

        var rail = HarfSerit();
        DockPanel.SetDock(rail, Dock.Right);

        dock.Children.Add(baslik); dock.Children.Add(_ara); dock.Children.Add(chips); dock.Children.Add(sr);
        dock.Children.Add(rail); dock.Children.Add(_kScroll);

        var kap = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(14, 12, 8, 10), Child = dock };
        kap.SetResourceReference(Border.BackgroundProperty, "surface");
        kap.SetResourceReference(Border.BorderBrushProperty, "border");
        return kap;
    }

    Border HarfSerit()
    {
        var g = new UniformGrid { Columns = 1 };
        foreach (var h in Harfler)
        {
            var t = new TextBlock { Text = h, FontSize = 10, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Stretch, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, ToolTip = $"{h} ile başlayanlar" };
            t.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            var hh = h;
            t.MouseLeftButtonDown += (_, _) => { _harf = _harf == hh ? "" : hh; ListeYukle(); };
            _harfT[h] = t; g.Children.Add(t);
        }
        return new Border { Width = 22, Margin = new Thickness(4, 0, 0, 0), Child = g };
    }

    // ---- sağ boş durum (müvekkil seçilmediğinde)
    UIElement BuildBosSag()
    {
        var sp = new StackPanel { Margin = new Thickness(24, 30, 24, 0), HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 640 };
        var t = Ui.Text("Soldan bir müvekkil seçin", 20, true); t.HorizontalAlignment = HorizontalAlignment.Center;
        var a = Ui.Muted("Ad, T.C. no ya da telefon yazıp Enter'a basarsanız ilk sonuç doğrudan açılır. Kenardaki harflerle de gezebilirsiniz.");
        a.TextAlignment = TextAlignment.Center; a.Margin = new Thickness(0, 6, 0, 18);
        var kpi = new UniformGrid { Columns = 3 };
        kpi.Children.Add(Kart("Toplam müvekkil", _kTop));
        kpi.Children.Add(Kart("Bakiyesi olan", _kBak));
        kpi.Children.Add(Kart("Davası olan", _kDav));
        sp.Children.Add(t); sp.Children.Add(a); sp.Children.Add(kpi);
        return sp;
    }

    // ---- satıra sağ tık menüsü
    ContextMenu MenuYap(Func<Row?> cur)
    {
        var cm = new ContextMenu();
        void Ekle(string h, Action<Row> a)
        {
            var mi = new MenuItem { Header = h };
            mi.Click += (_, _) => { var r = cur(); if (r != null) a(r); };
            cm.Items.Add(mi);
        }
        Ekle("Aç", r => Ac(r));
        Ekle("Tahsilat ekle", r => { Ac(r); TahsilatEkle(); });
        Ekle("Düzenle", r => Gecici(r, EditM));
        cm.Items.Add(new Separator());
        Ekle("WhatsApp'ta yaz", r => WhatsApp(r));
        Ekle("E-posta yaz", r => Eposta(r));
        Ekle("Telefonu kopyala", r => Kopyala(r.S("telefon"), "Telefon"));
        Ekle("T.C. numarasını kopyala", r => Kopyala(r.S("tc_no"), "T.C. no"));
        cm.Items.Add(new Separator());
        Ekle("Sil…", r => Gecici(r, DelM));
        return cm;
    }

    static string Bas(string ad)
    {
        var p = ad.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return p.Length == 0 ? "?" : (p.Length == 1 ? p[0][..1] : p[0][..1] + p[^1][..1]).ToUpper(TrC);
    }

    static Color Renk(string ad)
    {
        int h = 0; foreach (var ch in ad) h = (h * 31 + ch) & 0x7fffffff;
        return (Color)ColorConverter.ConvertFromString(Palet[h % Palet.Length]);
    }

    // ---- liste satırı
    Border SatirYap(Row m)
    {
        var ad = m.S("ad_soyad");
        var av = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = new SolidColorBrush(Renk(ad)), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = Bas(ad), Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var isim = Ui.Text(ad, 14, true); isim.TextTrimming = TextTrimming.CharacterEllipsis;
        var alt = Ui.Muted(string.Join(" • ", new[] { m.S("tc_no"), m.S("telefon") }.Where(x => x.Trim().Length > 0)), false);
        alt.FontSize = 12; alt.TextTrimming = TextTrimming.CharacterEllipsis;
        var orta = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        orta.Children.Add(isim); orta.Children.Add(alt);

        var sag = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var k = Utils.DaysLeft(m.S("durusma"));
        Pill pill;
        if (k != null && k <= 7) pill = new Pill(k == 0 ? "Duruşma bugün" : $"Duruşma {k} gün", k <= 3 ? "danger" : "warn");
        else if (m.D("kalan") > 0) pill = new Pill(Utils.FmtMoney(m.D("kalan")) + " bakiye", "warn");
        else if (m.L("dsay") > 0) pill = new Pill("Borcu yok", "ok");
        else pill = new Pill("Dava yok", "muted");
        pill.HorizontalAlignment = HorizontalAlignment.Right;
        var dsay = Ui.Muted(m.L("dsay") > 0 ? $"{m.L("dsay")} dava" : "", false);
        dsay.FontSize = 11; dsay.HorizontalAlignment = HorizontalAlignment.Right; dsay.Margin = new Thickness(0, 2, 2, 0);
        sag.Children.Add(pill); sag.Children.Add(dsay);

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(orta, 1); Grid.SetColumn(sag, 2);
        g.Children.Add(av); g.Children.Add(orta); g.Children.Add(sag);

        var b = new Border { Padding = new Thickness(10, 8, 10, 8), BorderThickness = new Thickness(0, 0, 0, 1), Child = g, Cursor = Cursors.Hand, Tag = m.L("id"), Background = Brushes.Transparent };
        b.SetResourceReference(Border.BorderBrushProperty, "border");
        if (m.L("id") == _sid) b.SetResourceReference(Border.BackgroundProperty, "sel");
        b.MouseEnter += (_, _) => { if ((long)b.Tag != _sid) b.SetResourceReference(Border.BackgroundProperty, "surface2"); };
        b.MouseLeave += (_, _) => { if ((long)b.Tag != _sid) b.Background = Brushes.Transparent; };
        b.MouseLeftButtonDown += (_, _) => Ac(m);
        b.ContextMenu = MenuYap(() => m);
        return b;
    }

    void Vurgu()
    {
        foreach (var c in _kWrap.Children)
        {
            if (c is not Border b || b.Tag is not long id) continue;
            if (id == _sid) { b.SetResourceReference(Border.BackgroundProperty, "sel"); b.BringIntoView(); }
            else b.Background = Brushes.Transparent;
        }
    }

    /// <summary>Klavyeyle (↑ ↓) listede bir sonraki / önceki müvekkile geçer.</summary>
    void Gez(int d)
    {
        if (_gorunen.Count == 0) return;
        var i = _gorunen.FindIndex(r => r.L("id") == _sid);
        i = i < 0 ? (d > 0 ? 0 : _gorunen.Count - 1) : Math.Clamp(i + d, 0, _gorunen.Count - 1);
        if (i >= _kLimit) { _kLimit = i + KartSayfa; KartlariYaz(_kRows, _kToplam); }
        Ac(_gorunen[i]);
    }

    void KartlariYaz(List<Row> rows, int toplam)
    {
        _kWrap.Children.Clear();
        if (rows.Count == 0)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 40, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(Ui.Text(toplam == 0 ? "Henüz müvekkil yok" : "Eşleşen müvekkil yok", 16, true));
            var alt = Ui.Muted(toplam == 0 ? "İlk müvekkilinizi ekleyerek başlayın." : "Aramayı, harfi ya da filtreyi değiştirin.");
            alt.Margin = new Thickness(0, 6, 0, 12); alt.HorizontalAlignment = HorizontalAlignment.Center; alt.TextAlignment = TextAlignment.Center;
            sp.Children.Add(alt);
            if (toplam == 0) { var bt = Ui.Btn("＋ Müvekkil ekle", "Primary", AddM); bt.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(bt); }
            _kWrap.Children.Add(sp);
            return;
        }
        _kRows = rows; _kToplam = toplam;
        bool harfBaslik = _sira.SelectedIndex is 0 or 1;
        string son = "";
        foreach (var m in rows.Take(_kLimit))
        {
            if (harfBaslik && m.S("ad_soyad").Length > 0)
            {
                var h = m.S("ad_soyad")[..1].ToUpper(TrC);
                if (h != son)
                {
                    son = h;
                    var hb = new Border { Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 4, 0, 0), Child = Ui.Text(h, 11, true, "muted") };
                    hb.SetResourceReference(Border.BackgroundProperty, "surface2");
                    _kWrap.Children.Add(hb);
                }
            }
            _kWrap.Children.Add(SatirYap(m));
        }
        if (rows.Count > _kLimit)
        {
            var kalan = rows.Count - _kLimit;
            var daha = Ui.Btn($"＋ Daha fazla göster ({kalan} müvekkil daha)", "", () => { _kLimit += KartSayfa * 2; KartlariYaz(_kRows, _kToplam); });
            daha.Margin = new Thickness(8, 10, 8, 16); daha.Padding = new Thickness(18, 8, 18, 8);
            daha.ToolTip = "Aramayı daraltmak daha hızlıdır: ad, T.C. no, telefon ya da harf seçin";
            _kWrap.Children.Add(daha);
        }
    }

    /// <summary>Listedeki satır için, detay açmadan, müvekkil seçili gibi işlem yapar.</summary>
    void Gecici(Row r, Action a)
    {
        var s0 = _sid; var m0 = _mSel;
        _sid = r.L("id"); _mSel = Db.One("SELECT * FROM muvekkiller WHERE id=?", _sid);
        try { a(); } finally { _sid = s0; _mSel = m0; }
    }

    static string Numara(string tel)
    {
        var d = new string(tel.Where(char.IsDigit).ToArray());
        if (d.StartsWith("00")) d = d[2..];
        else if (d.StartsWith("0")) d = "90" + d[1..];
        else if (d.Length == 10) d = "90" + d;
        return d;
    }

    void WhatsApp(Row m)
    {
        var n = Numara(m.S("telefon"));
        if (n.Length < 10) { Ui.Info(this, "Bu müvekkilin geçerli bir telefon numarası yok. Düzenle ile ekleyin."); return; }
        Ui.OpenExternal("https://wa.me/" + n);
    }

    void Eposta(Row m)
    {
        if (m.S("eposta").Trim().Length == 0) { Ui.Info(this, "Bu müvekkilin e-posta adresi yok. Düzenle ile ekleyin."); return; }
        Ui.OpenExternal("mailto:" + m.S("eposta").Trim());
    }

    void Kopyala(string metin, string ad)
    {
        if (metin.Length == 0) { Ui.Info(this, $"{ad} kayıtlı değil."); return; }
        try { Clipboard.SetText(metin); Ctx.Status($"{ad} panoya kopyalandı: {metin}"); } catch (Exception) { }
    }

    void ListeYukle()
    {
        var rows = Db.Query("SELECT m.*, (SELECT COUNT(*) FROM vekaletnameler v WHERE v.muvekkil_id=m.id) AS vsay, " +
                            "(SELECT COUNT(*) FROM davalar d WHERE d.muvekkil_id=m.id) AS dsay, " +
                            "(SELECT MIN(d.durusma_tarihi) FROM davalar d WHERE d.muvekkil_id=m.id AND d.durusma_tarihi!='' AND d.durusma_tarihi>=date('now') AND d.asama_durum!='Karara Çıktı') AS durusma, " +
                            "(SELECT COALESCE(SUM(f.kalan_bakiye),0) FROM finans_tahsilat f WHERE f.muvekkil_id=m.id) AS kalan FROM muvekkiller m");
        var toplam = rows.Count;

        // sayılar (filtreden bağımsız)
        var bakiyeli = rows.Where(m => m.D("kalan") > 0).ToList();
        var davali = rows.Count(m => m.L("dsay") > 0);
        _kTop.Text = toplam.ToString();
        _kBak.Text = bakiyeli.Count == 0 ? "Yok" : $"{bakiyeli.Count} • {Utils.FmtMoney(bakiyeli.Sum(m => m.D("kalan")))}";
        _kVek.Text = rows.Count(m => m.L("vsay") == 0).ToString();
        _kDav.Text = davali.ToString();
        _chip[Fl.Tumu].Metin($"Tümü {toplam}");
        _chip[Fl.Bakiye].Metin($"Bakiyesi olan {bakiyeli.Count}");
        _chip[Fl.Davali].Metin($"Davası olan {davali}");
        _chip[Fl.DavasiYok].Metin($"Davası olmayan {toplam - davali}");
        foreach (var kv in _chip) kv.Value.Aktif(kv.Key == _fl);
        foreach (var kv in _harfT)
        {
            bool on = kv.Key == _harf;
            if (on) { kv.Value.SetResourceReference(TextBlock.BackgroundProperty, "accent"); kv.Value.Foreground = Brushes.White; }
            else { kv.Value.Background = Brushes.Transparent; kv.Value.SetResourceReference(TextBlock.ForegroundProperty, "muted"); }
        }

        rows = _fl switch
        {
            Fl.Bakiye => bakiyeli,
            Fl.VekYok => rows.Where(m => m.L("vsay") == 0).ToList(),
            Fl.Davali => rows.Where(m => m.L("dsay") > 0).ToList(),
            Fl.DavasiYok => rows.Where(m => m.L("dsay") == 0).ToList(),
            _ => rows,
        };
        if (_harf.Length > 0)
        {
            var hl = Utils.TrLower(_harf);
            rows = rows.Where(m => Utils.TrLower(m.S("ad_soyad").TrimStart()).StartsWith(hl)).ToList();
        }
        var q = Utils.TrLower(_ara.Text.Trim());
        if (q.Length > 0)
            rows = rows.Where(m => Utils.TrLower($"{m.S("ad_soyad")} {m.S("tc_no")} {m.S("telefon")} {m.S("eposta")}").Contains(q)).ToList();
        var cmp = StringComparer.Create(TrC, true);
        rows = _sira.SelectedIndex switch
        {
            1 => rows.OrderByDescending(x => x.S("ad_soyad"), cmp).ToList(),
            2 => rows.OrderBy(x => x.S("tc_no"), StringComparer.Ordinal).ToList(),
            3 => rows.OrderByDescending(x => x.D("kalan")).ThenBy(x => x.S("ad_soyad"), cmp).ToList(),
            4 => rows.OrderByDescending(x => x.L("dsay")).ThenBy(x => x.S("ad_soyad"), cmp).ToList(),
            5 => rows.OrderByDescending(x => x.L("id")).ToList(),
            6 => rows.OrderBy(x => x.S("durusma").Length == 0).ThenBy(x => x.S("durusma"), StringComparer.Ordinal).ThenBy(x => x.S("ad_soyad"), cmp).ToList(),
            _ => rows.OrderBy(x => x.S("ad_soyad"), cmp).ToList(),
        };
        _gorunen = rows;
        _kLimit = KartSayfa;
        KartlariYaz(rows, toplam);
        bool filtre = q.Length > 0 || _fl != Fl.Tumu || _harf.Length > 0;
        _sayi.Text = filtre ? $"{rows.Count} / {toplam} müvekkil" : $"{toplam} müvekkil";
    }

    // ------------------------------------------------------------ sağ panel: seçili müvekkil
    UIElement BuildDetay()
    {
        var dock = new DockPanel();

        // başlık kartı: avatar + kimlik + hızlı eylemler
        var av = new Border { Width = 58, Height = 58, CornerRadius = new CornerRadius(29), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 14, 0), Child = _avatar };
        av.SetResourceReference(Border.BackgroundProperty, "accent");
        _avatar.Foreground = Brushes.White; _avatar.HorizontalAlignment = HorizontalAlignment.Center; _avatar.VerticalAlignment = VerticalAlignment.Center;
        _dTitle.FontSize = 21; _dSub.TextWrapping = TextWrapping.Wrap; _dNot.Margin = new Thickness(0, 4, 0, 0);
        var info = new StackPanel(); info.Children.Add(_dTitle); info.Children.Add(_dSub); info.Children.Add(_dNot);

        var kop = Ui.Btn("📋 Kopyala  ▾", "", null);
        kop.Click += (_, _) =>
        {
            if (_mSel == null) return;
            var m = new ContextMenu { PlacementTarget = kop, Placement = PlacementMode.Bottom };
            void K(string h, string v, string ad) { var mi = new MenuItem { Header = h }; mi.Click += (_, _) => Kopyala(v, ad); m.Items.Add(mi); }
            K("Telefon", _mSel.S("telefon"), "Telefon"); K("T.C. kimlik no", _mSel.S("tc_no"), "T.C. no");
            K("E-posta", _mSel.S("eposta"), "E-posta"); K("Adres", _mSel.S("adres"), "Adres");
            m.IsOpen = true;
        };
        var eylem = Ui.WRow(Ui.Btn("💬 WhatsApp", "", () => { if (_mSel != null) WhatsApp(_mSel); }),
                            Ui.Btn("✉ E-posta", "", () => { if (_mSel != null) Eposta(_mSel); }), kop,
                            Ui.Btn("✏ Düzenle", "", EditM));
        eylem.HorizontalAlignment = HorizontalAlignment.Right; eylem.MaxWidth = 420;
        var hg = new Grid();
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hg.ColumnDefinitions.Add(new ColumnDefinition());
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(info, 1); Grid.SetColumn(eylem, 2);
        hg.Children.Add(av); hg.Children.Add(info); hg.Children.Add(eylem);
        var hdr = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 10), Child = hg };
        hdr.SetResourceReference(Border.BackgroundProperty, "surface");
        hdr.SetResourceReference(Border.BorderBrushProperty, "border");
        DockPanel.SetDock(hdr, Dock.Top);

        var kpi = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, -10, 10) };
        kpi.Children.Add(Kart("Açık dava", _dkDav)); kpi.Children.Add(Kart("Kalan bakiye", _dkKal)); kpi.Children.Add(Kart("Toplam alınan", _dkAl));
        kpi.Children.Add(Kart("Sıradaki süre", _dkSure)); kpi.Children.Add(Kart("Sonraki duruşma", _dkDur));
        DockPanel.SetDock(kpi, Dock.Top);

        // alt işlem çubuğu
        var diger = Ui.Btn("⋯ Diğer  ▾", "", null);
        diger.Click += (_, _) =>
        {
            var m = new ContextMenu { PlacementTarget = diger, Placement = PlacementMode.Top };
            void E(string h, Action a) { var mi = new MenuItem { Header = h }; mi.Click += (_, _) => a(); m.Items.Add(mi); }
            E("📄 Cari ekstre (Word)", EkstreYaz);
            E("✉ Tahsilat hatırlatma metni", HatirlatmaMetni);
            m.Items.Add(new Separator());
            E("🗑 Müvekkili sil…", DelM);
            m.IsOpen = true;
        };
        Button B(string t, string ipucu, string stil, Action a) { var d = Ui.Btn(t, stil, a); d.ToolTip = ipucu; return d; }
        var alt = Ui.FRow(B("＋ Yeni dava", "Dosyalar sayfasında yeni dava açar", "Primary", () => Ctx.Navigate("dava", "YeniDava")),
                          B("💰 Tahsilat gir", "Cari hesaba tahsilat ekler", "", TahsilatEkle),
                          B("📎 Evrak ekle", "Vekaletname/evrak kaydı ekler", "", () => { _tabs.SelectedItem = _tabV; VekEkle(); }),
                          B("📝 Dilekçe hazırla", "Evrak ekranında şablondan belge üretir", "", () => Ctx.Navigate("evrak", null)), diger);
        alt.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(alt, Dock.Bottom);

        dock.Children.Add(hdr); dock.Children.Add(kpi); dock.Children.Add(alt);

        _tabO = Ui.Tab("Özet", BuildOzet());
        _tabD = Ui.Tab("Dosyalar", BuildDavalar());
        _tabF = Ui.Tab("Cari hesap", BuildFinans());
        var sb = new DockPanel(); sb.Children.Add(_tS);
        _tabS = Ui.Tab("Süreler", sb);
        _tabV = Ui.Tab("Evrak", BuildVekalet());
        _tabN = Ui.Tab("Notlar", BuildNotlar());
        _tabs.Items.Add(_tabO); _tabs.Items.Add(_tabD); _tabs.Items.Add(_tabF); _tabs.Items.Add(_tabS); _tabs.Items.Add(_tabV); _tabs.Items.Add(_tabN);
        dock.Children.Add(_tabs);
        return dock;
    }

    UIElement BuildOzet()
    {
        var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition());

        var c1 = new Card { Margin = new Thickness(0, 0, 12, 10) };
        c1.Top(Ui.H2("Dosyaları")); c1.Fill(_tOzD);
        var c2 = new Card { Margin = new Thickness(0, 0, 12, 0) };
        c2.Top(Ui.H2("Son tahsilatlar")); c2.Fill(_tOzC);
        Grid.SetRow(c1, 0); Grid.SetRow(c2, 1);

        var ic = new StackPanel();
        ic.Children.Add(Ui.H2("İletişim"));
        void Satir(string et, TextBlock v) { var e = Ui.Muted(et, false); e.FontSize = 11; e.Margin = new Thickness(0, 10, 0, 0); ic.Children.Add(e); ic.Children.Add(v); }
        Satir("Telefon", _oTel); Satir("E-posta", _oMail); Satir("Adres", _oAdres); Satir("Not", _oNot);
        var c3 = new Card();
        c3.Fill(new ScrollViewer { Content = ic, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetColumn(c3, 1); Grid.SetRowSpan(c3, 2);
        g.Children.Add(c1); g.Children.Add(c2); g.Children.Add(c3);
        return g;
    }

    UIElement BuildNotlar()
    {
        var d = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var info = Ui.Muted("Bu müvekkile ait genel notlar (yalnızca iç kullanım). Başlıkta 📌 ile görünür.");
        info.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(info, Dock.Top);
        var kaydet = Ui.Btn("Notu kaydet", "Primary", NotKaydet);
        kaydet.HorizontalAlignment = HorizontalAlignment.Left; kaydet.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(kaydet, Dock.Bottom);
        d.Children.Add(info); d.Children.Add(kaydet); d.Children.Add(_notBox);
        return d;
    }

    void NotKaydet()
    {
        if (_mSel == null) return;
        Db.Update("muvekkiller", _mSel.L("id"), new Dictionary<string, object?> { ["notlar"] = _notBox.Text.Trim() });
        Ctx.Status("Not kaydedildi.");
        _mSel = Db.One("SELECT * FROM muvekkiller WHERE id=?", _sid);
        DetayYukle();
    }

    void Ac(Row? m)
    {
        if (m == null) return;
        var yeni = Db.One("SELECT * FROM muvekkiller WHERE id=?", m.L("id"));
        if (yeni == null) return;
        _mSel = yeni;
        bool ayni = _sid == _mSel.L("id") && ReferenceEquals(_sag.Content, _detay);
        _sid = _mSel.L("id");
        _sag.Content = _detay;
        Vurgu();
        if (!ayni) _tabs.SelectedItem = _tabO;
        DetayYukle();
    }

    void Geri()
    {
        _sid = 0; _mSel = null;
        _sag.Content = _bosSag;
        Refresh();
    }

    void DetayYukle()
    {
        if (_mSel == null) return;
        var ad = _mSel.S("ad_soyad").Trim();
        _avatar.Text = Bas(ad);
        _dTitle.Text = ad;
        _dSub.Text = string.Join("   •   ", new[] { "T.C. " + _mSel.S("tc_no"), _mSel.S("telefon"), _mSel.S("eposta") }.Where(s => s.Length > 0 && s != "T.C. "));
        _dNot.Text = _mSel.S("notlar").Trim().Length > 0 ? "📌 " + _mSel.S("notlar").Trim() : "";
        _dNot.Visibility = _dNot.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        string V(string s) => s.Trim().Length > 0 ? s.Trim() : "—";
        _oTel.Text = V(_mSel.S("telefon")); _oMail.Text = V(_mSel.S("eposta")); _oAdres.Text = V(_mSel.S("adres")); _oNot.Text = V(_mSel.S("notlar"));
        _notBox.Text = _mSel.S("notlar");
        LoadVekalet();
        LoadCari();
        LoadHareket();
        LoadDavalar();
        LoadSureler();
        LoadOzet();
        Ozet();
    }

    void LoadOzet()
    {
        _tOzD.Load(Db.Query("SELECT * FROM davalar WHERE muvekkil_id=? ORDER BY CASE WHEN asama_durum='Karara Çıktı' THEN 1 ELSE 0 END, id DESC LIMIT 8", _sid),
            new() { ["durusma_tarihi"] = r => Ui.DeadlineKind(r.S("durusma_tarihi")) });
        _tOzC.Load(Db.Query("SELECT h.* FROM tahsilat_hareketleri h JOIN finans_tahsilat f ON f.id=h.finans_id WHERE f.muvekkil_id=? ORDER BY h.tarih DESC, h.id DESC LIMIT 8", _sid));
    }

    void Ozet()
    {
        var f = Db.One("SELECT COALESCE(SUM(toplam_ucret),0) AS t, COALESCE(SUM(alinan_tutar),0) AS a, COALESCE(SUM(kalan_bakiye),0) AS k FROM finans_tahsilat WHERE muvekkil_id=?", _sid);
        _dkTop.Text = Utils.FmtMoney(f?.D("t") ?? 0);
        _dkAl.Text = Utils.FmtMoney(f?.D("a") ?? 0);
        _dkKal.Text = Utils.FmtMoney(f?.D("k") ?? 0);
        _dkKal.SetResourceReference(TextBlock.ForegroundProperty, (f?.D("k") ?? 0) > 0 ? "warn" : "ok");
        _dkDav.Text = Db.ScalarL("SELECT COUNT(*) FROM davalar WHERE muvekkil_id=? AND asama_durum!='Karara Çıktı'", _sid).ToString();
        var dur = Db.One("SELECT MIN(durusma_tarihi) AS d FROM davalar WHERE muvekkil_id=? AND durusma_tarihi!='' AND durusma_tarihi>=? AND asama_durum!='Karara Çıktı'", _sid, Utils.TodayIso())?.S("d") ?? "";
        var k = Utils.DaysLeft(dur);
        _dkDur.Text = dur.Length == 0 ? "Yok" : $"{Utils.FmtDate(dur)} ({(k == 0 ? "bugün" : k + " gün")})";
        var sure = Db.One("SELECT MIN(sr.son_tarih) AS d FROM sureler sr JOIN davalar dd ON dd.id=sr.dava_id WHERE dd.muvekkil_id=? AND sr.durum='Açık' AND sr.son_tarih!=''", _sid)?.S("d") ?? "";
        var ks = Utils.DaysLeft(sure);
        _dkSure.Text = sure.Length == 0 ? "Yok" : $"{Utils.FmtDate(sure)} ({(ks == 0 ? "bugün" : ks + " gün")})";
        _dkSure.SetResourceReference(TextBlock.ForegroundProperty, sure.Length > 0 && ks != null && ks <= 3 ? "danger" : "text");
        _tabF.Header = $"Cari hesap ({_tF.Rows.Count})";
        _tabD.Header = $"Dosyalar ({_tDv.Rows.Count})";
        _tabS.Header = $"Süreler ({_tS.Rows.Count(r => r.S("durum") == "Açık")} açık)";
        _tabV.Header = $"Evrak ({_tV.Rows.Count})";
    }

    void LoadSureler()
    {
        _tS.Load(Db.Query("SELECT sr.*, TRIM(COALESCE(d.mahkeme_adi,'') || ' ' || COALESCE(d.esas_no,'')) AS dava FROM sureler sr " +
                          "JOIN davalar d ON d.id=sr.dava_id WHERE d.muvekkil_id=? ORDER BY sr.durum='Açık' DESC, sr.son_tarih", _sid),
            new() { ["son_tarih"] = r => r.S("durum") == "Açık" ? Ui.DeadlineKind(r.S("son_tarih")) : "muted" });
    }

    UIElement BuildVekalet()
    {
        var bot = new DockPanel();
        DockPanel.SetDock(_vTitle, Dock.Top); _vTitle.Margin = new Thickness(0, 0, 0, 6);
        var vb = Ui.HRow(Ui.Btn("＋ Vekalet Ekle", "Ok", VekEkle), Ui.Btn("Aç", "", VekAc), Ui.Btn("Düzenle", "", VekDuzenle), Ui.Btn("Sil", "Danger", VekSil));
        vb.Margin = new Thickness(0, 6, 0, 0);
        DockPanel.SetDock(vb, Dock.Bottom);
        bot.Children.Add(_vTitle); bot.Children.Add(vb); bot.Children.Add(_tV);
        return bot;
    }

    UIElement BuildDavalar()
    {
        var d = new DockPanel();
        var b = Ui.HRow(Ui.Btn("📂 Dosya Kartı", "Ok", DosyaKartinaGit), Ui.Btn("📁 Dosyalar", "", () => Ctx.Navigate("dava", null)));
        b.Margin = new Thickness(0, 6, 0, 0);
        DockPanel.SetDock(b, Dock.Bottom);
        d.Children.Add(b); d.Children.Add(_tDv);
        return d;
    }

    void LoadDavalar()
    {
        _tDv.Load(Db.Query("SELECT * FROM davalar WHERE muvekkil_id=? ORDER BY id DESC", _sid),
            new() { ["durusma_tarihi"] = r => Ui.DeadlineKind(r.S("durusma_tarihi")) });
    }

    void DosyaKartinaGit()
    {
        var c = _tDv.Current;
        if (c == null) { Ui.Info(this, "Önce listeden bir dava seçin."); return; }
        DosyaKartiPage.Hedef = c.L("id");
        Ctx.Navigate("dosya_karti", null);
    }

    // ---- vekaletnameler (müvekkil başına birden fazla)
    static readonly string[] VekTurleri = { "Genel vekaletname", "Özel vekaletname", "Dava vekaletnamesi", "Diğer" };

    List<Field> VFields(long muvekkilId)
    {
        var dv = new List<(object? Value, string Label)> { (0L, "— Dava seçilmedi —") };
        foreach (var d in Db.Query("SELECT id, mahkeme_adi, esas_no FROM davalar WHERE muvekkil_id=? ORDER BY id DESC", muvekkilId))
            dv.Add((d.L("id"), $"{d.S("mahkeme_adi")} {d.S("esas_no")}".Trim()));
        return new List<Field>
        {
            new("tur", "Tür", "combo", Field.Of(VekTurleri), def: VekTurleri[0]),
            new("tarih", "Vekalet tarihi", "date", def: Utils.TodayIso()),
            new("dava_id", "Bağlı dava (müvekkilin davaları)", "combo", dv, def: 0L),
            new("aciklama", "Açıklama (noter, yevmiye no, yetkiler…)"),
            new("pdf", "Vekaletname (PDF)", "file"),
        };
    }

    void VekEkle()
    {
        var m = _mSel;
        if (m == null) { Ui.Info(this, "Önce müvekkil seçin."); return; }
        var v = FormDialog.Ask(this, $"Vekalet ekle — {m.S("ad_soyad")}", VFields(m.L("id")));
        if (v == null) return;
        VekKaydet(null, m, v);
    }

    void VekDuzenle()
    {
        var m = _mSel; var r = _tV.Current;
        if (m == null || r == null) return;
        var v = FormDialog.Ask(this, "Vekaleti düzenle", VFields(m.L("id")), r);
        if (v == null) return;
        VekKaydet(r.L("id"), m, v);
    }

    void VekKaydet(long? id, Row m, Dictionary<string, object?> v)
    {
        var pdf = Convert.ToString(v["pdf"]) ?? "";
        v.Remove("pdf");
        v["dava_id"] = Convert.ToInt64(v["dava_id"] ?? 0L);
        if (pdf.Length > 0) v["dosya_yolu"] = Workflows.VekaletnameKaydet(Cfg, m.S("ad_soyad"), pdf);
        if (id == null) { v["muvekkil_id"] = m.L("id"); Db.Insert("vekaletnameler", v); }
        else Db.Update("vekaletnameler", id.Value, v);
        Refresh();
    }

    void VekAc()
    {
        var r = _tV.Current;
        if (r == null) return;
        if (r.S("dosya_yolu").Length == 0) { Ui.Info(this, "Bu vekalet kaydına PDF eklenmemiş. Düzenle ile PDF ekleyebilirsiniz."); return; }
        var p = Workflows.DosyaYolu(Cfg, r.S("dosya_yolu"));
        if (System.IO.File.Exists(p)) Ui.OpenExternal(p); else Ui.Warn(this, $"Dosya bulunamadı:\n{p}");
    }

    void VekSil()
    {
        var r = _tV.Current;
        if (r != null && Ui.Confirm(this, "Vekalet kaydı silinsin mi? (PDF dosyası bilgisayarda kalır)")) { Db.Delete("vekaletnameler", r.L("id")); Refresh(); }
    }

    void LoadVekalet()
    {
        var m = _mSel;
        if (m == null) { _vTitle.Text = "Vekaletnameler — müvekkil seçin"; _tV.Load(new()); return; }
        var rows = Db.Query("SELECT v.*, TRIM(COALESCE(d.mahkeme_adi,'') || ' ' || COALESCE(d.esas_no,'')) AS dava FROM vekaletnameler v " +
                            "LEFT JOIN davalar d ON d.id=v.dava_id WHERE v.muvekkil_id=? ORDER BY v.tarih DESC, v.id DESC", m.L("id"));
        foreach (var r in rows) r["dosya"] = r.S("dosya_yolu").Length > 0 ? "✔ PDF" : "—";
        _vTitle.Text = $"Vekaletnameler — {m.S("ad_soyad")} ({rows.Count})";
        _tV.Load(rows);
    }

    static List<Field> MFields(bool withPdf)
    {
        var f = new List<Field>
        {
            new("tc_no", "T.C. Kimlik No", required: true), new("ad_soyad", "Ad Soyad", required: true),
            new("telefon", "Telefon"), new("eposta", "E-posta"), new("adres", "Adres", "multiline"), new("notlar", "Not (yalnızca iç kullanım)", "multiline"),
        };
        if (withPdf) f.Add(new Field("pdf", "Vekaletname (PDF)", "file"));
        return f;
    }

    static bool Unique(SqliteException e) => e.SqliteErrorCode == 19;

    void AddM()
    {
        var v = FormDialog.Ask(this, "Yeni Müvekkil", MFields(true));
        if (v == null) return;
        if (!Utils.ValidTc(v["tc_no"] as string) && !Ui.Confirm(this, "T.C. kimlik numarası doğrulamadan geçmedi. Yine de kaydedilsin mi?")) return;
        var catisma = DosyaEsleme.Catisma(Db, v["ad_soyad"] as string, null);
        if (catisma.Count > 0 && !Ui.Confirm(this, "Olası çıkar çatışması:\n• " + string.Join("\n• ", catisma) + "\n\nYine de kaydedilsin mi?")) return;
        var pdf = (v["pdf"] as string) ?? "";
        v.Remove("pdf");
        try
        {
            var mid = Db.Insert("muvekkiller", v);
            if (pdf.Length > 0)
                Db.Insert("vekaletnameler", new Dictionary<string, object?>
                {
                    ["muvekkil_id"] = mid, ["tur"] = VekTurleri[0], ["tarih"] = Utils.TodayIso(),
                    ["dosya_yolu"] = Workflows.VekaletnameKaydet(Cfg, (string)v["ad_soyad"]!, pdf),
                });
        }
        catch (SqliteException e) when (Unique(e)) { Ui.Warn(this, "Bu T.C. kimlik numarası ile kayıtlı müvekkil zaten var."); }
        Refresh();
    }

    void EditM()
    {
        var m = _mSel;
        if (m == null) return;
        var v = FormDialog.Ask(this, "Müvekkili Düzenle", MFields(false), m);
        if (v == null) return;
        try { Db.Update("muvekkiller", m.L("id"), v); }
        catch (SqliteException e) when (Unique(e)) { Ui.Warn(this, "Bu T.C. kimlik numarası başka bir müvekkilde kayıtlı."); }
        Refresh();
    }

    void DelM()
    {
        var m = _mSel;
        if (m != null && Ui.Confirm(this, $"{m.S("ad_soyad")} ve cari kayıtları silinsin mi?")) { Db.Delete("muvekkiller", m.L("id")); Geri(); }
    }

    // ------------------------------------------------------------ finans
    UIElement BuildFinans()
    {
        var top = new DockPanel();
        var tb = Ui.HRow(Ui.Btn("＋ Cari Kayıt", "Primary", AddF), Ui.Btn("Düzenle", "", EditF), Ui.Btn("Sil", "Danger", DelF),
                         Ui.Btn("📄 Cari Ekstre (Word)", "", EkstreYaz), Ui.Btn("✉ Hatırlatma Metni", "", HatirlatmaMetni));
        tb.Margin = new Thickness(0, 6, 0, 0);
        DockPanel.SetDock(tb, Dock.Bottom);
        DockPanel.SetDock(_fSum, Dock.Bottom); _fSum.Margin = new Thickness(0, 6, 0, 0);
        top.Children.Add(tb); top.Children.Add(_fSum); top.Children.Add(_tF);

        var bot = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(_hTitle, Dock.Top); _hTitle.Margin = new Thickness(0, 0, 0, 6);
        var hb = Ui.HRow(Ui.Btn("＋ Tahsilat Ekle", "Ok", Tahsilat), Ui.Btn("Düzenle", "", EditH), Ui.Btn("Sil", "Danger", DelH));
        hb.Margin = new Thickness(0, 6, 0, 0);
        DockPanel.SetDock(hb, Dock.Bottom);
        DockPanel.SetDock(_hSum, Dock.Bottom); _hSum.Margin = new Thickness(0, 6, 0, 0);
        bot.Children.Add(_hTitle); bot.Children.Add(hb); bot.Children.Add(_hSum); bot.Children.Add(_tH);
        return Ui.VSplit(top, bot, 3, 2);
    }

    List<Field> FFields(bool yeni)
    {
        var ms = Db.Query("SELECT id, ad_soyad, tc_no FROM muvekkiller ORDER BY ad_soyad");
        var dv = new List<(object? Value, string Label)> { (0L, "— Dava dosyası yok (genel cari) —") };
        foreach (var d in Db.Query("SELECT d.id, d.mahkeme_adi, d.esas_no, m.ad_soyad FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id ORDER BY d.id DESC"))
            dv.Add((d.L("id"), $"{d.S("mahkeme_adi")} {d.S("esas_no")}".Trim() + (d.S("ad_soyad").Length > 0 ? $" — {d.S("ad_soyad")}" : "")));
        var f = new List<Field>
        {
            new("muvekkil_id", "Müvekkil", "combo", ms.Select(m => ((object?)m.L("id"), $"{m.S("ad_soyad")} ({m.S("tc_no")})")), required: true),
            new("dava_id", "Bağlı dava dosyası", "combo", dv, def: 0L),
            new("dosya_konusu", "Dosya Konusu (boşsa davadan alınır)"), new("toplam_ucret", "Toplam Vekalet Ücreti", "money"),
            new("sonraki_tahsilat_tarihi", "Sonraki Tahsilat Tarihi", "date"),
            new("makbuz_kesildi", "Makbuz kesildi", "check"), new("notlar", "Notlar", "multiline"),
        };
        if (yeni)
            f.InsertRange(4, new[]
            {
                new Field("ilk_tutar", "İlk tahsilat tutarı (varsa)", "money"),
                new Field("ilk_tarih", "İlk tahsilat tarihi", "date", def: Utils.TodayIso()),
            });
        return f;
    }

    /// <summary>Dava seçildiyse konuyu/müvekkili tamamlar. Geçersizse false.</summary>
    bool DavaUyumla(Dictionary<string, object?> v)
    {
        var davaId = Convert.ToInt64(v["dava_id"] ?? 0L);
        v["dava_id"] = davaId;
        if (davaId > 0)
        {
            var d = Db.One("SELECT * FROM davalar WHERE id=?", davaId);
            if (d != null)
            {
                if (Convert.ToString(v["dosya_konusu"])?.Trim().Length is null or 0)
                    v["dosya_konusu"] = $"{d.S("mahkeme_adi")} {d.S("esas_no")}".Trim() + (d.S("dava_turu").Length > 0 ? $" — {d.S("dava_turu")}" : "");
                var mid = d.L("muvekkil_id");
                if (mid > 0 && mid != Convert.ToInt64(v["muvekkil_id"]) &&
                    !Ui.Confirm(this, "Seçilen dava dosyası başka bir müvekkile ait görünüyor. Yine de bu müvekkil için kaydedilsin mi?"))
                    return false;
            }
        }
        if (Convert.ToString(v["dosya_konusu"])?.Trim().Length is null or 0)
        {
            Ui.Warn(this, "Dosya konusu boş. Bir dava dosyası seçin veya konuyu yazın.");
            return false;
        }
        return true;
    }

    void AddF()
    {
        if (Db.ScalarL("SELECT COUNT(*) FROM muvekkiller") == 0) { Ui.Info(this, "Önce müvekkil ekleyin."); return; }
        var ilk = new Dictionary<string, object?>();
        if (_sid > 0) ilk["muvekkil_id"] = _sid;
        var alanlar = FFields(true);
        var v = FormDialog.Ask(this, "Yeni Cari Kayıt", alanlar, ilk);
        while (v != null && !DavaUyumla(v)) v = FormDialog.Ask(this, "Yeni Cari Kayıt", alanlar, v);
        if (v == null) return;
        var ilkT = Convert.ToDouble(v["ilk_tutar"]); var ilkD = (string)v["ilk_tarih"]!;
        v.Remove("ilk_tutar"); v.Remove("ilk_tarih");
        v["kalan_bakiye"] = v["toplam_ucret"];
        var fid = Db.Insert("finans_tahsilat", v);
        if (ilkT > 0)
            Db.Insert("tahsilat_hareketleri", new Dictionary<string, object?> { ["finans_id"] = fid, ["tarih"] = ilkD, ["tutar"] = ilkT, ["aciklama"] = "İlk tahsilat" });
        Workflows.TahsilatYenidenHesapla(Db, fid);
        Refresh();
        _tF.SelectId(fid);
    }

    void EditF()
    {
        var r = _tF.Current;
        if (r == null) return;
        var alanlar = FFields(false);
        var v = FormDialog.Ask(this, "Cari Kaydı Düzenle", alanlar, r);
        while (v != null && !DavaUyumla(v)) v = FormDialog.Ask(this, "Cari Kaydı Düzenle", alanlar, v);
        if (v == null) return;
        Db.Update("finans_tahsilat", r.L("id"), v);
        Workflows.TahsilatYenidenHesapla(Db, r.L("id"));
        Refresh();
    }

    void EkstreYaz()
    {
        if (_mSel == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog { Title = "Cari ekstreyi kaydet", Filter = "Word (*.docx)|*.docx", FileName = $"Cari_Ekstre_{_mSel.S("ad_soyad").Replace(' ', '_')}_{DateTime.Today:yyyyMMdd}.docx" };
        if (dlg.ShowDialog() != true) return;
        Documents.MakeDocx(Ekstre.Metin(Db, _mSel, Cfg.GetString("buro_adi")), dlg.FileName);
        if (Ui.Confirm(this, "Ekstre kaydedildi. Şimdi açılsın mı? (PDF için Word'de Farklı Kaydet → PDF)")) Ui.OpenExternal(dlg.FileName);
    }

    void HatirlatmaMetni()
    {
        if (_mSel == null) return;
        var r = _tF.Current;
        double kalan = r != null ? r.D("kalan_bakiye") : _tF.Rows.Sum(x => x.D("kalan_bakiye"));
        if (kalan <= 0) { Ui.Info(this, "Bu müvekkilin kalan bakiyesi yok."); return; }
        var metin = Ekstre.Hatirlatma(_mSel, kalan, r?.S("sonraki_tahsilat_tarihi") ?? "", r?.S("dosya_konusu") ?? "", Cfg.GetString("buro_adi"));
        try { Clipboard.SetText(metin); } catch (Exception) { }
        Ui.Info(this, "Metin panoya kopyalandı (WhatsApp, SMS ya da e-postaya yapıştırın):\n\n" + metin, "Tahsilat hatırlatma");
    }

    void DelF()
    {
        var r = _tF.Current;
        if (r != null && Ui.Confirm(this, "Cari kayıt ve tüm tahsilat hareketleri silinsin mi?")) { Db.Delete("finans_tahsilat", r.L("id")); Refresh(); }
    }

    void LoadCari()
    {
        if (_sid == 0) { _tF.Load(new()); _fSum.Text = ""; return; }
        var fs = Db.Query("SELECT f.*, TRIM(COALESCE(d.mahkeme_adi,'') || ' ' || COALESCE(d.esas_no,'')) AS dava_etiket " +
                          "FROM finans_tahsilat f LEFT JOIN davalar d ON d.id=f.dava_id WHERE f.muvekkil_id=? " +
                          "ORDER BY f.sonraki_tahsilat_tarihi='' , f.sonraki_tahsilat_tarihi", _sid);
        foreach (var f in fs) if (f.S("dava_etiket").Length == 0) f["dava_etiket"] = "— genel cari —";
        var bak = _busy;
        _busy = true;
        try
        {
            _tF.Load(fs, new() { ["sonraki_tahsilat_tarihi"] = r => r.D("kalan_bakiye") > 0 ? Ui.DeadlineKind(r.S("sonraki_tahsilat_tarihi")) : "muted" });
            if (_tF.Current == null) _tF.SelectFirst();
        }
        finally { _busy = bak; }
        var masraf = Db.ScalarD("SELECT COALESCE(SUM(x.tutar),0) FROM dava_masraflari x JOIN davalar dd ON dd.id=x.dava_id WHERE dd.muvekkil_id=?", _sid);
        _fSum.Text = $"Toplam ücret: {Utils.FmtMoney(fs.Sum(r => r.D("toplam_ucret")))}  •  Alınan: {Utils.FmtMoney(fs.Sum(r => r.D("alinan_tutar")))}  •  " +
                     $"Kalan: {Utils.FmtMoney(fs.Sum(r => r.D("kalan_bakiye")))}" + (masraf > 0 ? $"  •  Dava masrafları: {Utils.FmtMoney(masraf)}" : "");
    }

    void LoadHareket()
    {
        var r = _tF.Current;
        if (r == null)
        {
            _hTitle.Text = "Tahsilat hareketleri — cari kaydı seçin";
            _tH.Load(new()); _hSum.Text = "";
            return;
        }
        _hTitle.Text = $"Tahsilat hareketleri — {_mSel?.S("ad_soyad")} • {r.S("dosya_konusu")}";
        var rows = Db.Query("SELECT * FROM tahsilat_hareketleri WHERE finans_id=? ORDER BY tarih='' , tarih DESC, id DESC", r.L("id"));
        _tH.Load(rows);
        _hSum.Text = $"{rows.Count} tahsilat • Toplam alınan: {Utils.FmtMoney(rows.Sum(x => x.D("tutar")))} • Kalan bakiye: {Utils.FmtMoney(r.D("kalan_bakiye"))}";
    }

    static List<Field> HFields() => new()
    {
        new("tarih", "Tahsilat Tarihi", "date"), new("tutar", "Tutar", "money", required: true), new("aciklama", "Açıklama (nakit / havale / çek…)"),
    };

    void Tahsilat()
    {
        var r = _tF.Current;
        if (r == null) { Ui.Info(this, "Önce yukarıdan bir cari kayıt seçin."); return; }
        var fields = new List<Field>
        {
            new("tarih", "Tahsilat Tarihi", "date", def: Utils.TodayIso()), new("tutar", "Tutar", "money", required: true),
            new("aciklama", "Açıklama (nakit / havale / çek…)"), new("sonraki", "Sonraki tahsilat tarihi", "date", def: r.S("sonraki_tahsilat_tarihi")),
        };
        var v = FormDialog.Ask(this, $"Tahsilat — {_mSel?.S("ad_soyad")}", fields);
        if (v == null || Convert.ToDouble(v["tutar"]) <= 0) return;
        var sonraki = (string)v["sonraki"]!;
        v.Remove("sonraki");
        v["finans_id"] = r.L("id");
        Db.Insert("tahsilat_hareketleri", v);
        Db.Update("finans_tahsilat", r.L("id"), new Dictionary<string, object?> { ["sonraki_tahsilat_tarihi"] = sonraki });
        Workflows.TahsilatYenidenHesapla(Db, r.L("id"));
        Refresh();
    }

    void EditH()
    {
        var h = _tH.Current;
        if (h == null) return;
        var v = FormDialog.Ask(this, "Tahsilatı Düzenle", HFields(), h);
        if (v == null) return;
        Db.Update("tahsilat_hareketleri", h.L("id"), v);
        Workflows.TahsilatYenidenHesapla(Db, h.L("finans_id"));
        Refresh();
    }

    void DelH()
    {
        var h = _tH.Current;
        if (h == null || !Ui.Confirm(this, "Bu tahsilat kaydı silinsin mi?")) return;
        Db.Delete("tahsilat_hareketleri", h.L("id"));
        Workflows.TahsilatYenidenHesapla(Db, h.L("finans_id"));
        Refresh();
    }

    // ------------------------------------------------------------ yenile
    public override void Refresh()
    {
        _busy = true;
        try
        {
            // eski tek-PDF alanını vekaletnameler listesine bir kez taşı
            Db.Execute("INSERT INTO vekaletnameler (muvekkil_id, tur, tarih, dosya_yolu) SELECT m.id, 'Genel vekaletname', m.kayit_tarihi, m.vekaletname_yolu " +
                       "FROM muvekkiller m WHERE m.vekaletname_yolu!='' AND NOT EXISTS (SELECT 1 FROM vekaletnameler v WHERE v.muvekkil_id=m.id)");
            ListeYukle();
        }
        finally { _busy = false; }
        if (_sid == 0) _ara.Focus();
        if (_sid > 0 && ReferenceEquals(_sag.Content, _detay))
        {
            _mSel = Db.One("SELECT * FROM muvekkiller WHERE id=?", _sid);
            if (_mSel == null) { _sid = 0; _sag.Content = _bosSag; }
            else DetayYukle();
        }
    }
}
