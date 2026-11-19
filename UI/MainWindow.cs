using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HukukErp.Core;
using HukukErp.Services;
using HukukErp.UI.Pages;

namespace HukukErp.UI;

public class NavButton : Border
{
    readonly TextBlock _lbl;
    readonly Border _badge = new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 1, 8, 1), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _badgeText = new() { FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x1b, 0x1b, 0x1b)) };
    bool _checked;
    bool _hover;
    public event Action? Clicked;
    public string Text => _lbl.Text;

    public NavButton(string text)
    {
        Height = 40;
        CornerRadius = new CornerRadius(10);
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        _lbl = new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(0xcb, 0xd5, 0xe4)), VerticalAlignment = VerticalAlignment.Center };
        _badge.Child = _badgeText;
        var dock = new DockPanel { Margin = new Thickness(12, 0, 10, 0), LastChildFill = true };
        DockPanel.SetDock(_badge, Dock.Right);
        dock.Children.Add(_badge);
        dock.Children.Add(_lbl);
        Child = dock;
        MouseEnter += (_, _) => { _hover = true; Paint(); };
        MouseLeave += (_, _) => { _hover = false; Paint(); };
        MouseLeftButtonUp += (_, _) => Clicked?.Invoke();
    }

    public void SetChecked(bool on) { _checked = on; Paint(); }
    public void SetText(string text) => _lbl.Text = text;

    void Paint()
    {
        if (_checked)
        {
            SetResourceReference(BackgroundProperty, "accent");
            _lbl.Foreground = Brushes.White;
            _lbl.FontWeight = FontWeights.SemiBold;
        }
        else
        {
            ClearValue(BackgroundProperty);
            Background = _hover ? new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)) : Brushes.Transparent;
            _lbl.Foreground = new SolidColorBrush(Color.FromRgb(0xcb, 0xd5, 0xe4));
            _lbl.FontWeight = FontWeights.Normal;
        }
    }

    public void SetBadge(long n, string kind)
    {
        if (n <= 0) { _badge.Visibility = Visibility.Collapsed; return; }
        _badgeText.Text = n.ToString();
        string key = kind == "info" ? "accent" : kind;
        _badge.SetResourceReference(Border.BackgroundProperty, key);
        _badgeText.Foreground = kind == "info" ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x1b, 0x1b, 0x1b));
        _badge.Visibility = Visibility.Visible;
    }
}

public class MainWindow : Window
{
    readonly Db _db;
    readonly Config _cfg;
    readonly AppCtx _ctx;
    readonly List<BasePage> _pages = new();
    readonly List<string> _keys = new();
    readonly Dictionary<string, NavButton> _nav = new();
    readonly List<NavButton> _navList = new();
    readonly Dictionary<string, Action> _ac = new();   // kapalı menü grubunu açan eylemler
    TextBox? _araKutu;
    readonly ContentControl _host = new();
    readonly TextBlock _lblDb = Ui.Muted("", false);
    readonly ContentControl _tgHolder = new();
    readonly TextBlock _status = Ui.Muted("", false);
    readonly Border _banner = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(18, 8, 18, 8) };
    Action? _guncelleGoster;
    void OnGuncelleme() => Dispatcher.Invoke(() => _guncelleGoster?.Invoke());
    int _current;
    DispatcherTimer? _tRem, _tChrome, _tStatus;

    public MainWindow(Db db, Config cfg)
    {
        _db = db; _cfg = cfg;
        Title = "HukukERP — Hukuk Bürosu Yönetimi";
        Width = 1480; Height = 920;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // Not: Styles.xaml'deki "Window" stili türetilmiş pencerelere (MainWindow) uygulanmaz; renkleri burada bağlıyoruz.
        SetResourceReference(BackgroundProperty, "bg");
        SetResourceReference(ForegroundProperty, "text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        _ctx = new AppCtx
        {
            Db = db, Cfg = cfg,
            SetTheme = name => ApplyTheme(name),
            Status = Status,
            RefreshChrome = () => UpdateChrome(),
            Navigate = Navigate,
        };
        Build(0);
        try { _db.CopuTemizle(); } catch (Exception) { }
        Timers();
        Closing += (_, _) => { try { Yedekleme.Yap(_db, _cfg); } catch { } };
        PreviewKeyDown += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (e.Key == Key.K) { e.Handled = true; KomutPaletiAc(); }
            else if (e.Key == Key.F) { e.Handled = true; _araKutu?.Focus(); _araKutu?.SelectAll(); }
            else if (e.Key == Key.N) { e.Handled = true; HizliEkleAc(null); }
        };
        Loaded += (_, _) => IlkKurulum();
    }

    // ---------------------------------------------------------------- hızlı ekle ve üst arama
    void HizliEkleAc(FrameworkElement? hedef)
    {
        var m = new ContextMenu();
        void Ekle(string ad, string sayfa, string eylem)
        {
            var mi = new MenuItem { Header = ad };
            mi.Click += (_, _) => Navigate(sayfa, eylem);
            m.Items.Add(mi);
        }
        Ekle("Yeni müvekkil", "muvekkil", "YeniMuvekkil");
        Ekle("Yeni dava", "dava", "YeniDava");
        Ekle("Yeni icra dosyası", "icra", "Yeni");
        Ekle("Yeni icra görevi (süreli)", "icra_gorev", "Yeni");
        Ekle("Yeni görev", "kanban", "YeniGorev");
        Ekle("Tahsilat girişi", "muvekkil", "TahsilatEkle");
        if (hedef != null) { m.PlacementTarget = hedef; m.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; }
        else { m.Placement = System.Windows.Controls.Primitives.PlacementMode.Center; m.PlacementTarget = _host; }
        m.IsOpen = true;
    }

    UIElement UstArama()
    {
        var kutu = new TextBox { Width = 380, Padding = new Thickness(10, 6, 10, 6), VerticalContentAlignment = VerticalAlignment.Center,
                                 ToolTip = "Müvekkil, T.C., esas no, kurum, konu, not… (Ctrl+F)" };
        _araKutu = kutu;
        var ipucu = Ui.Muted("🔎  Müvekkil, esas no, kurum ara…   Ctrl+F", false);
        ipucu.IsHitTestVisible = false;
        ipucu.Margin = new Thickness(12, 0, 0, 0);
        ipucu.VerticalAlignment = VerticalAlignment.Center;
        kutu.TextChanged += (_, _) => ipucu.Visibility = kutu.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        kutu.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            var q = kutu.Text.Trim();
            if (q.Length < 2) return;
            AramaPage.Sorgu = q;
            kutu.Clear();
            Navigate("arama", null);
            e.Handled = true;
        };
        var g = new Grid();
        g.Children.Add(kutu);
        g.Children.Add(ipucu);
        return g;
    }

    // ---------------------------------------------------------------- komut paleti
    void KomutPaletiAc()
    {
        var k = new List<(string Ad, Action Calistir)>();
        for (int i = 0; i < _keys.Count; i++)
        {
            int ix = i;
            k.Add(("Git: " + _nav[_keys[i]].Text.Trim(), () => Goto(ix)));
        }
        void Is(string ad, string sayfa, string eylem) => k.Add((ad, () => Navigate(sayfa, eylem)));
        Is("Yeni icra görevi", "icra_gorev", "Yeni");
        Is("Yeni dava", "dava", "YeniDava");
        Is("Yeni müvekkil", "muvekkil", "YeniMuvekkil");
        Is("Yeni görev", "kanban", "YeniGorev");
        Is("Tahsilat girişi ekle", "muvekkil", "TahsilatEkle");
        Is("UETS tarayıcısını aç", "avukat_uets", "ShowBrowser");
        k.Add(("Şimdi yedek al", () => Status(Yedekleme.Yap(_db, _cfg) != null ? "Yedek alındı." : "Yedek alınamadı.")));
        k.Add(("Koyu tema", () => ApplyTheme("dark")));
        k.Add(("Açık tema", () => ApplyTheme("light")));
        KomutPaleti.Ac(this, k);
    }

    // ---------------------------------------------------------------- ilk kurulum
    void IlkKurulum()
    {
        if (_cfg.GetInt("kurulum_tamam", 0) == 1) return;
        _cfg.Set("kurulum_tamam", 1);   // yalnızca bir kez sorulur
        if (_db.ScalarL("SELECT COUNT(*) FROM muvekkiller") > 0 || _db.ScalarL("SELECT COUNT(*) FROM davalar") > 0) return;
        var v = FormDialog.Ask(this, "HukukERP'ye Hoş Geldiniz – İlk Kurulum", new List<Field>
        {
            new("buro", "Avukat / büro adı"),
            new("yedek", "İkinci yedek klasörü (harici disk / bulut klasörü, isteğe bağlı)"),
            new("token", "Telegram bot token (isteğe bağlı)"),
            new("chat", "Telegram chat id (isteğe bağlı)"),
            new("ornek", "Denemek için örnek veri ekle (sonradan silebilirsiniz)", "check", def: 0L),
        });
        if (v == null) return;
        var buro = Convert.ToString(v["buro"]) ?? "";
        if (buro.Length > 0) _cfg.Set("buro_adi", buro);
        var yedek = Convert.ToString(v["yedek"]) ?? "";
        if (yedek.Length > 0) _cfg.Set("yedek_ikinci_konum", yedek);
        var ay = new Dictionary<string, object?>();
        if ((Convert.ToString(v["token"]) ?? "").Length > 0) ay["telegram_bot_token"] = v["token"];
        if ((Convert.ToString(v["chat"]) ?? "").Length > 0) ay["telegram_kisisel_chat_id"] = v["chat"];
        if (ay.Count > 0) _db.SaveSettings(ay);
        if (Convert.ToInt64(v["ornek"] ?? 0) == 1)
        {
            var mid = _db.Insert("muvekkiller", new Dictionary<string, object?> { ["tc_no"] = "10000000146", ["ad_soyad"] = "ÖRNEK MÜVEKKİL", ["telefon"] = "0500 000 00 00" });
            var did = _db.Insert("davalar", new Dictionary<string, object?>
            {
                ["muvekkil_id"] = mid, ["mahkeme_adi"] = "ÖRNEK 1. Asliye Hukuk Mahkemesi", ["esas_no"] = "2026/1", ["dava_turu"] = "Örnek dava",
                ["karsi_taraf"] = "ÖRNEK KARŞI TARAF", ["durusma_tarihi"] = Utils.Iso(DateTime.Today.AddDays(10)),
            });
            _db.Insert("sureler", new Dictionary<string, object?>
            {
                ["baslik"] = "ÖRNEK – cevap dilekçesi", ["dayanak"] = "HMK 127", ["sure_turu"] = "Hukuk", ["dava_id"] = did,
                ["son_tarih"] = Utils.Iso(DateTime.Today.AddDays(7)), ["durum"] = "Açık",
            });
            Ui.Info(this, "Örnek müvekkil, dava ve süre eklendi. 'ÖRNEK' ile başlayan kayıtları dilediğinizde silebilirsiniz.");
        }
        Goto(_current);
    }

    void Status(string msg)
    {
        _status.Text = msg;
        _tStatus?.Stop();
        _tStatus = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _tStatus.Tick += (_, _) => { _status.Text = ""; _tStatus?.Stop(); };
        _tStatus.Start();
    }

    // ---------------------------------------------------------------- tema
    void ApplyTheme(string name)
    {
        if (name == Theme.Name) return;
        _cfg.Set("theme", name);
        Theme.Apply(name);
        Build(_current);
    }

    // ---------------------------------------------------------------- yapı
    /// <summary>Elemanı bulunduğu üst öğeden ayırır (tema değişince arayüz yeniden kurulurken "mantıksal alt öğe" hatasını önler).</summary>
    static void Ayir(FrameworkElement e)
    {
        switch (e.Parent)
        {
            case Panel pn: pn.Children.Remove(e); break;
            case Decorator d: d.Child = null; break;
            case ContentControl c: c.Content = null; break;
        }
    }

    void Build(int start)
    {
        _pages.Clear(); _keys.Clear(); _nav.Clear(); _navList.Clear();
        _host.Content = null;
        foreach (var e in new FrameworkElement[] { _tgHolder, _status, _lblDb, _banner, _host }) Ayir(e);

        var outer = new DockPanel { LastChildFill = true };

        // üst çubuk: arama + hızlı ekle + durum
        var top = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(18, 8, 18, 8) };
        top.SetResourceReference(Border.BackgroundProperty, "surface");
        top.SetResourceReference(Border.BorderBrushProperty, "border");
        var topDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_tgHolder, Dock.Right);
        topDock.Children.Add(_tgHolder);
        DockPanel.SetDock(_status, Dock.Right);
        _status.Margin = new Thickness(0, 0, 16, 0);
        topDock.Children.Add(_status);
        var arama = UstArama();
        DockPanel.SetDock(arama, Dock.Left);
        topDock.Children.Add(arama);
        Button? hizli = null;
        hizli = Ui.Btn("＋  Hızlı ekle", "Primary", () => HizliEkleAc(hizli));
        hizli.Margin = new Thickness(10, 0, 0, 0);
        hizli.ToolTip = "Yeni müvekkil, dava, süre, görev, tahsilat (Ctrl+N)";
        DockPanel.SetDock(hizli, Dock.Left);
        topDock.Children.Add(hizli);
        _lblDb.Margin = new Thickness(16, 0, 0, 0);
        _lblDb.VerticalAlignment = VerticalAlignment.Center;
        topDock.Children.Add(_lblDb);
        top.Child = topDock;
        DockPanel.SetDock(top, Dock.Top);
        outer.Children.Add(top);

        // güncelleme uyarısı
        _banner.SetResourceReference(Border.BackgroundProperty, "accent");
        var bTxt = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold };
        var bBtn = new Button { Content = "Şimdi güncelle ve yeniden başlat", Margin = new Thickness(16, 0, 0, 0), Padding = new Thickness(14, 4, 14, 4) };
        bBtn.Click += (_, _) => Guncelleme.Uygula();
        var bKapat = new Button { Content = "Sonra", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 4, 14, 4) };
        bKapat.Click += (_, _) => _banner.Visibility = Visibility.Collapsed;
        _banner.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { bTxt, bBtn, bKapat } };
        void Goster()
        {
            var v = Guncelleme.HazirSurum;
            if (v == null) return;
            bTxt.Text = $"🔔  Yeni sürüm var: {v}. Güncel sürüm indirildi; kurmak için yeniden başlatın (veriler korunur).";
            _banner.Visibility = Visibility.Visible;
        }
        Guncelleme.Degisti -= OnGuncelleme;
        _guncelleGoster = Goster;
        Guncelleme.Degisti += OnGuncelleme;
        Goster();
        DockPanel.SetDock(_banner, Dock.Top);
        outer.Children.Add(_banner);

        // sol menü: ana bölüm açık, diğerleri katlanabilir
        _ac.Clear();
        var side = new Border { Width = 250 };
        side.SetResourceReference(Border.BackgroundProperty, "sidebar");
        var sideStack = new StackPanel { Margin = new Thickness(12) };
        var brand = new TextBlock { Text = "⚖  HukukERP", Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(4, 6, 4, 6) };
        sideStack.Children.Add(brand);

        var sections = new (string Title, bool Acik, (string Key, string Text, BasePage Page)[] Items)[]
        {
            ("ÇALIŞMA", true, new (string, string, BasePage)[]
            {
                ("ofis_dash", "▦  Ofis Dashboard", new DashboardPage(_ctx, "avukat")),
                ("muvekkil", "💼  Müvekkil", new MuvekkilFinansPage(_ctx)),
                ("dava", "📁  Dosyalar", new DavaPage(_ctx)),
                ("icra", "⚖  İcra Dosyaları", new IcraPage(_ctx)),
                ("icra_gorev", "⏱  İcra Görevleri", new IcraGorevPage(_ctx)),
                ("ajanda", "📅  Ajanda", new AjandaPage(_ctx)),
                ("is_listesi", "⏱  İş Listesi (Süreler)", new IsListesiPage(_ctx)),
                ("kanban", "✅  Görevler", new KanbanPage(_ctx)),
                ("avukat_uets", "📬  Bireysel UETS", new UetsCenterPage(_ctx, "Sahsi")),
                ("emanet", "🏦  Emanet Hesabı", new EmanetPage(_ctx)),
            }),
            ("ARAMA VE RAPORLAR", false, new (string, string, BasePage)[]
            {
                ("arama", "🔎  Genel Arama", new AramaPage(_ctx)),
                ("emsal", "📚  Emsal ve Notlar", new EmsalPage(_ctx)),
                ("rapor", "📊  Raporlar", new RaporPage(_ctx)),
            }),
            ("HESAPLAMALAR", false, new (string, string, BasePage)[]
            {
                ("hesap_araclari", "🧮  Faiz • Harç • Vekalet", new HesapAraclariPage(_ctx)),
                ("hesap_iscilik", "👷  İşçilik Alacakları", HesaplamaMerkeziPage.Iscilik(_ctx)),
                ("hesap_kaza", "🚗  Kaza Tazminatı", HesaplamaMerkeziPage.Kaza(_ctx)),
                ("hesap_netbrut", "💱  Net ↔ Brüt", HesaplamaMerkeziPage.NetBrut(_ctx)),
                ("hesaplamalar", "⛓  İnfaz Hesaplama", new HesaplamalarPage(_ctx)),
            }),
            ("ARAÇLAR VE BELGELER", false, new (string, string, BasePage)[]
            {
                ("evrak", "📄  Sözleşme ve Evrak", new EvrakPage(_ctx)),
                ("yardimci", "🛠  Dosya Dönüştürücüler", new YardimciProgramlarPage(_ctx)),
                ("veri", "🗃  Veri Aktarımı ve KVKK", new VeriAktarimPage(_ctx)),
            }),
            ("SİSTEM", true, new (string, string, BasePage)[]
            {
                ("cop", "🗑  Çöp Kutusu", new CopKutusuPage(_ctx)),
                ("yardim", "❓  Kullanım Kılavuzu", new YardimPage(_ctx)),
                ("ayarlar", "⚙  Ayarlar", new AyarlarPage(_ctx)),
            }),
        };

        var grey = new SolidColorBrush(Color.FromRgb(0x7d, 0x8a, 0xa3));
        int idx = 0;
        foreach (var (title, acik, items) in sections)
        {
            var body = new StackPanel { Visibility = acik ? Visibility.Visible : Visibility.Collapsed };
            var chev = new TextBlock { Text = acik ? "▾" : "▸", Foreground = grey, FontSize = 11, Margin = new Thickness(0, 0, 6, 0) };
            var lab = new TextBlock { Text = title, Foreground = grey, FontSize = 11, FontWeight = FontWeights.Bold };
            var head = new Border { Cursor = Cursors.Hand, Background = Brushes.Transparent, Margin = new Thickness(4, 14, 4, 4),
                                    Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { chev, lab } } };
            void Ayarla(bool ac) { body.Visibility = ac ? Visibility.Visible : Visibility.Collapsed; chev.Text = ac ? "▾" : "▸"; }
            head.MouseLeftButtonUp += (_, _) => Ayarla(body.Visibility != Visibility.Visible);
            sideStack.Children.Add(head);
            sideStack.Children.Add(body);
            foreach (var (key, text, page) in items)
            {
                var b = new NavButton(text) { Margin = new Thickness(0, 1, 0, 1) };
                int i = idx;
                b.Clicked += () => Goto(i);
                body.Children.Add(b);
                _ac[key] = () => Ayarla(true);
                _pages.Add(page); _keys.Add(key); _nav[key] = b; _navList.Add(b);
                idx++;
            }
        }
        side.Child = new ScrollViewer { Content = sideStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(side, Dock.Left);
        outer.Children.Add(side);
        outer.Children.Add(_host);
        Content = outer;
        Goto(Math.Min(start, _pages.Count - 1));
    }

    // ---------------------------------------------------------------- gezinme
    void Goto(int i)
    {
        _current = i;
        for (int k = 0; k < _navList.Count; k++) _navList[k].SetChecked(k == i);
        if (_ac.TryGetValue(_keys[i], out var ac)) ac();
        _host.Content = _pages[i];
        _pages[i].Refresh();
        UpdateChrome();
    }

    /// <summary>Her sayfanın ekran görüntüsünü PNG olarak klasöre yazar (GitHub Actions ekran modu).</summary>
    public async Task EkranGoruntuleriAl(string klasor)
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        for (int i = 0; i < _keys.Count; i++)
        {
            try
            {
                Goto(i);
                UpdateLayout();
                await Task.Delay(700);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                var w = (int)ActualWidth; var h = (int)ActualHeight;
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render((Visual)Content);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using var fs = File.Create(System.IO.Path.Combine(klasor, $"{i + 1:00}_{_keys[i]}.png"));
                enc.Save(fs);
            }
            catch (Exception ex) { File.AppendAllText(System.IO.Path.Combine(klasor, "hatalar.txt"), _keys[i] + ": " + ex.Message + "\n"); }
        }
    }

    void Navigate(string key, string? action)
    {
        // Dosya Kartı artık Dosyalar sayfasının sekmesi: eski bağlantılar oraya yönlenir
        if (key == "dosya_karti") { key = "dava"; if (string.IsNullOrEmpty(action)) action = "KartSekmesi"; }
        int i = _keys.IndexOf(key);
        if (i < 0) return;
        Goto(i);
        if (!string.IsNullOrEmpty(action))
            _pages[i].GetType().GetMethod(action)?.Invoke(_pages[i], null);
    }

    void UpdateChrome()
    {
        _lblDb.Text = "Yedekleme: otomatik";
        var s = _db.GetSettings();
        string tok = s.S("telegram_bot_token"), kisisel = s.S("telegram_kisisel_chat_id");
        Pill p = tok.Length > 0 && kisisel.Length > 0 ? new Pill("🌐  Telegram Aktif", "ok") : new Pill("Telegram Pasif", "muted");
        _tgHolder.Content = p;

        _nav["avukat_uets"].SetBadge(_db.ScalarL("SELECT COUNT(*) FROM genel_tebligatlar WHERE hesap_tipi='Sahsi' AND durum=?", Workflows.QAvukat) + UetsEsitleme.YeniSayisi(_db), "info");
        _nav["dava"].SetBadge(_db.ScalarL("SELECT COUNT(*) FROM davalar WHERE asama_durum!='Karara Çıktı'"), "ok");
        _nav["is_listesi"].SetBadge(_db.ScalarL("SELECT COUNT(*) FROM sureler WHERE tebligat_id>0 AND durum='Açık' AND son_tarih<=date('now','+7 day')"), "danger");
        _nav["icra_gorev"].SetBadge(_db.ScalarL("SELECT COUNT(*) FROM sureler WHERE icra_id>0 AND durum='Açık' AND son_tarih<=date('now','+7 day')"), "danger");
        _nav["kanban"].SetBadge(_db.ScalarL("SELECT COUNT(*) FROM gorevler WHERE durum!='Tamamlandı'"), "warn");
    }

    // ---------------------------------------------------------------- zamanlayıcılar
    void Timers()
    {
        _tRem = new DispatcherTimer { Interval = TimeSpan.FromMinutes(Math.Max(1, _cfg.GetInt("hatirlatma_kontrol_dk", 30))) };
        _tRem.Tick += (_, _) => Hatirlat();
        _tRem.Start();
        var once = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        once.Tick += (_, _) => { once.Stop(); Hatirlat(); };
        once.Start();

        _tChrome = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _tChrome.Tick += (_, _) => UpdateChrome();
        _tChrome.Start();
    }

    void Hatirlat()
    {
        Ui.RunAsync(() => Workflows.HatirlatmalariGonder(_db, _cfg), n => { if (n > 0) Status($"{n} hatırlatma gönderildi"); });
        Ui.RunAsync(() => Workflows.SabahOzeti(_db, _cfg), gonderildi => { if (gonderildi) _cfg.Set("sabah_ozeti_gun", Utils.TodayIso()); });
    }
}
