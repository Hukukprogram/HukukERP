using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HukukErp.Core;

namespace HukukErp.UI.Pages;

/// <summary>İcra dosyaları (Müvekkil sayfası düzeninde): solda aranabilir liste, sağda seçili dosyanın özeti, süreli görevleri ve notları.</summary>
public class IcraPage : BasePage
{
    static readonly string[] Durumlar = { "Derdest", "Haciz aşamasında", "Satış aşamasında", "Ödeme bekleniyor", "İtiraz / dava", "Kapandı" };
    static readonly string[] Turler = { "İlamsız", "İlamlı", "Kambiyo senedi", "Kira alacağı (tahliye)", "Rehin / ipotek", "Diğer" };
    static readonly string[] Siralar = { "Son eklenen", "Borçlu (A → Z)", "Müvekkil (A → Z)", "Kalan alacak (çoktan aza)", "Takip tarihi (yeni → eski)", "Yaklaşan görev" };
    static readonly string[] Palet = { "#5B8CFF", "#34D39E", "#F7B23B", "#FF6B7A", "#A78BFA", "#22C1DC", "#F472B6", "#94A3B8" };
    static readonly CultureInfo TrC = new("tr-TR");
    const int Sinir = 150;

    enum Fl { Derdest, Gorevli, Kapandi, Tumu }
    Fl _fl = Fl.Derdest;
    readonly Dictionary<Fl, TbBtn> _chip = new();

    // sol
    readonly TextBox _ara = new() { Margin = new Thickness(0, 0, 0, 8), ToolTip = "Borçlu, müvekkil, icra dairesi veya esas no ile ara  •  Enter: ilk sonucu aç  •  ↑ ↓: gez" };
    readonly ComboBox _sira = new() { MinWidth = 170 };
    readonly TextBlock _sayi = Ui.Muted("", false);
    readonly StackPanel _liste = new();
    readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = true };
    readonly System.Windows.Threading.DispatcherTimer _aramaT = new() { Interval = TimeSpan.FromMilliseconds(220) };
    List<Row> _gorunen = new();

    // sağ
    readonly ContentControl _sag = new();
    UIElement _detay = null!, _bos = null!;
    readonly TextBlock _avatar = Ui.Text("", 20, true), _dTitle = Ui.H2(""), _dSub = Ui.Muted("");
    static TextBlock KV(double b = 18) => Ui.Text("–", b, true);
    readonly TextBlock _kAlacak = KV(), _kTahsil = KV(), _kKalan = KV(), _kTarih = KV(14), _kGorev = KV(14);
    readonly TextBlock _bTop = KV(22), _bDer = KV(22), _bKalan = KV(18);
    readonly TextBlock _oMuv = Ui.Text("–", 13, false, "text", true), _oDaire = Ui.Text("–", 13, false, "text", true), _oTur = Ui.Text("–", 13, false, "text", true),
                       _oTc = Ui.Text("–", 13, false, "text", true), _oDurum = Ui.Text("–", 13, false, "text", true), _oKes = Ui.Text("–", 13, false, "text", true);
    readonly DataTable _tG = new(("Son Gün", "son_tarih", Fmt.Date), ("Kalan", "kalan_txt", null), ("Görev", "baslik", null), ("Durum", "durum", null));
    readonly TextBox _not = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(8) };
    readonly TextBlock _notDurum = Ui.Muted("");
    readonly TabControl _tabs = new();
    TabItem _tabG = null!;
    Row? _sel;
    long _sid;
    bool _busy, _notYukleniyor, _notKirli;

    public IcraPage(AppCtx ctx) : base(ctx)
    {
        _scroll.Content = _liste;
        _scroll.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down) { Gez(1); e.Handled = true; }
            else if (e.Key == Key.Up) { Gez(-1); e.Handled = true; }
        };
        _bos = BuildBos();
        _detay = BuildDetay();
        _sag.Content = _bos;
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
                _aramaT.Stop(); ListeYukle();
                if (_gorunen.Count > 0 && _ara.Text.Trim().Length > 0) Ac(_gorunen[0]);
                e.Handled = true;
            }
        };
        _tG.MouseDoubleClick += (_, _) => GorevDuzenle();
        _not.TextChanged += (_, _) => { if (!_notYukleniyor && _sid > 0) { _notKirli = true; _notDurum.Text = "kaydedilmedi…"; } };
        _not.LostFocus += (_, _) => NotYaz();
        Unloaded += (_, _) => NotYaz();
    }

    // ------------------------------------------------------------ yerleşim
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
        var yeni = Ui.Btn("＋ Yeni", "Primary", Yeni);
        DockPanel.SetDock(yeni, Dock.Right);
        var baslik = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        baslik.Children.Add(yeni); baslik.Children.Add(Ui.Text("İcra Dosyaları", 18, true));
        DockPanel.SetDock(baslik, Dock.Top);
        DockPanel.SetDock(_ara, Dock.Top);

        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var (f, ad) in new[] { (Fl.Derdest, "Derdest"), (Fl.Gorevli, "Görevi olan"), (Fl.Kapandi, "Kapananlar"), (Fl.Tumu, "Tümü") })
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

        dock.Children.Add(baslik); dock.Children.Add(_ara); dock.Children.Add(chips); dock.Children.Add(sr); dock.Children.Add(_scroll);
        var kap = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(14, 12, 10, 10), Child = dock };
        kap.SetResourceReference(Border.BackgroundProperty, "surface");
        kap.SetResourceReference(Border.BorderBrushProperty, "border");
        return kap;
    }

    UIElement BuildBos()
    {
        var sp = new StackPanel { Margin = new Thickness(24, 30, 24, 0), HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 640 };
        var t = Ui.Text("Soldan bir icra dosyası seçin", 20, true); t.HorizontalAlignment = HorizontalAlignment.Center;
        var a = Ui.Muted("Borçlu, müvekkil, icra dairesi ya da esas no yazıp Enter'a basarsanız ilk sonuç doğrudan açılır.");
        a.TextAlignment = TextAlignment.Center; a.Margin = new Thickness(0, 6, 0, 18);
        var kpi = new UniformGrid { Columns = 3 };
        kpi.Children.Add(Kart("Toplam dosya", _bTop)); kpi.Children.Add(Kart("Derdest", _bDer)); kpi.Children.Add(Kart("Kalan alacak (derdest)", _bKalan));
        sp.Children.Add(t); sp.Children.Add(a); sp.Children.Add(kpi);
        return sp;
    }

    UIElement BuildDetay()
    {
        var dock = new DockPanel();
        var av = new Border { Width = 58, Height = 58, CornerRadius = new CornerRadius(29), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 14, 0), Child = _avatar };
        av.SetResourceReference(Border.BackgroundProperty, "accent");
        _avatar.Foreground = Brushes.White; _avatar.HorizontalAlignment = HorizontalAlignment.Center; _avatar.VerticalAlignment = VerticalAlignment.Center;
        _dTitle.FontSize = 21; _dSub.TextWrapping = TextWrapping.Wrap;
        var info = new StackPanel(); info.Children.Add(_dTitle); info.Children.Add(_dSub);
        var eylem = Ui.WRow(Ui.Btn("✏ Düzenle", "", Duzenle));
        eylem.HorizontalAlignment = HorizontalAlignment.Right;
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
        kpi.Children.Add(Kart("Alacak", _kAlacak)); kpi.Children.Add(Kart("Tahsil edilen", _kTahsil)); kpi.Children.Add(Kart("Kalan", _kKalan));
        kpi.Children.Add(Kart("Takip / Kesinleşme", _kTarih)); kpi.Children.Add(Kart("Sıradaki görev", _kGorev));
        DockPanel.SetDock(kpi, Dock.Top);

        var diger = Ui.Btn("⋯ Diğer  ▾", "", null);
        diger.Click += (_, _) =>
        {
            if (_sel == null) return;
            var m = new ContextMenu { PlacementTarget = diger, Placement = PlacementMode.Top };
            void E(string h, Action a) { var mi = new MenuItem { Header = h }; mi.Click += (_, _) => a(); m.Items.Add(mi); }
            if (_sel.S("durum") == "Kapandı") E("↺ Dosyayı yeniden aç", () => DurumYap("Derdest")); else E("✔ Dosyayı kapat", () => DurumYap("Kapandı"));
            E("👤 Müvekkile git", () => Ctx.Navigate("muvekkil", null));
            E("⏱ İcra Görevleri sayfası", () => Ctx.Navigate("icra_gorev", null));
            m.Items.Add(new Separator());
            E("🗑 Dosyayı sil…", Sil);
            m.IsOpen = true;
        };
        var alt = Ui.FRow(Ui.Btn("⏱ Süreli görev ekle", "Primary", GorevEkle), Ui.Btn("＋ Tahsilat işle", "", Tahsilat), diger);
        alt.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(alt, Dock.Bottom);
        dock.Children.Add(hdr); dock.Children.Add(kpi); dock.Children.Add(alt);

        // Özet
        var ozet = new Card();
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) }); g.ColumnDefinitions.Add(new ColumnDefinition());
        int satir = 0;
        void Sat(string et, TextBlock v)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = Ui.Muted(et, false); l.Margin = new Thickness(0, 4, 8, 4); Grid.SetRow(l, satir);
            v.Margin = new Thickness(0, 4, 0, 4); Grid.SetRow(v, satir); Grid.SetColumn(v, 1);
            g.Children.Add(l); g.Children.Add(v); satir++;
        }
        Sat("Müvekkil", _oMuv); Sat("İcra dairesi / Esas", _oDaire); Sat("Takip türü", _oTur); Sat("Borçlu T.C./V.N.", _oTc); Sat("Durum", _oDurum); Sat("Kesinleşme", _oKes);
        ozet.Fill(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = g });
        _tabs.Items.Add(Ui.Tab("Özet", ozet));

        // Görevler
        var gd = new DockPanel();
        var gb = Ui.HRow(Ui.Btn("＋ Süreli görev", "Primary", GorevEkle), Ui.Btn("Düzenle", "", GorevDuzenle), Ui.Btn("✔ Tamamlandı", "Ok", () => GorevDurum("Tamamlandı")),
                         Ui.Btn("↺ Yeniden Aç", "", () => GorevDurum("Açık")), Ui.Btn("Sil", "Danger", GorevSil));
        gb.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(gb, Dock.Bottom);
        gd.Children.Add(gb); gd.Children.Add(_tG);
        _tabG = Ui.Tab("Süreli görevler", gd);
        _tabs.Items.Add(_tabG);

        // Notlar
        _not.SetResourceReference(Control.ForegroundProperty, "text");
        _not.SetResourceReference(Control.BackgroundProperty, "surface2");
        _not.SetResourceReference(Control.BorderBrushProperty, "border");
        var nd = new DockPanel();
        var nb = Ui.HRow(Ui.Btn("Notları Kaydet", "Primary", NotYaz), _notDurum);
        nb.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(nb, Dock.Bottom);
        nd.Children.Add(nb); nd.Children.Add(_not);
        _tabs.Items.Add(Ui.Tab("Notlar", nd));
        dock.Children.Add(_tabs);
        return dock;
    }

    // ------------------------------------------------------------ liste
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

    public override void Refresh()
    {
        ListeYukle();
    }

    void ListeYukle()
    {
        NotYaz();
        var tum = Db.Query("SELECT i.*, COALESCE(m.ad_soyad,'—') AS muvekkil, " +
            "(SELECT COUNT(*) FROM sureler s WHERE s.icra_id=i.id AND s.durum='Açık') AS gorev_say, " +
            "(SELECT MIN(s.son_tarih) FROM sureler s WHERE s.icra_id=i.id AND s.durum='Açık') AS gorev_son " +
            "FROM icra_takipleri i LEFT JOIN muvekkiller m ON m.id=i.muvekkil_id");
        foreach (var r in tum) r["kalan"] = r.D("alacak_tutari") - r.D("tahsil_edilen");
        bool Kap(Row r) => r.S("durum") == "Kapandı";

        _busy = true;
        _chip[Fl.Derdest].Metin($"Derdest {tum.Count(r => !Kap(r))}");
        _chip[Fl.Gorevli].Metin($"Görevi olan {tum.Count(r => !Kap(r) && r.L("gorev_say") > 0)}");
        _chip[Fl.Kapandi].Metin($"Kapananlar {tum.Count(Kap)}");
        _chip[Fl.Tumu].Metin($"Tümü {tum.Count}");
        foreach (var kv in _chip) kv.Value.Aktif(kv.Key == _fl);
        _bTop.Text = tum.Count.ToString(); _bDer.Text = tum.Count(r => !Kap(r)).ToString();
        _bKalan.Text = Utils.FmtMoney(tum.Where(r => !Kap(r)).Sum(r => r.D("kalan")));
        _busy = false;

        IEnumerable<Row> l = tum;
        l = _fl switch
        {
            Fl.Derdest => l.Where(r => !Kap(r)),
            Fl.Gorevli => l.Where(r => !Kap(r) && r.L("gorev_say") > 0),
            Fl.Kapandi => l.Where(Kap),
            _ => l,
        };
        var q = Utils.TrLower(_ara.Text.Trim());
        if (q.Length > 0)
            l = l.Where(r => Utils.TrLower($"{r.S("borclu")} {r.S("muvekkil")} {r.S("icra_dairesi")} {r.S("esas_no")} {r.S("borclu_tc")}").Contains(q));
        var cmp = StringComparer.Create(TrC, true);
        l = _sira.SelectedIndex switch
        {
            1 => l.OrderBy(r => r.S("borclu"), cmp),
            2 => l.OrderBy(r => r.S("muvekkil"), cmp).ThenBy(r => r.S("borclu"), cmp),
            3 => l.OrderByDescending(r => r.D("kalan")),
            4 => l.OrderByDescending(r => r.S("takip_tarihi"), StringComparer.Ordinal),
            5 => l.OrderBy(r => r.S("gorev_son").Length == 0 ? 1 : 0).ThenBy(r => r.S("gorev_son"), StringComparer.Ordinal),
            _ => l.OrderByDescending(r => r.L("id")),
        };
        _gorunen = l.ToList();
        _sayi.Text = $"{_gorunen.Count} / {tum.Count} dosya" + (_gorunen.Count > Sinir ? $" (ilk {Sinir} gösteriliyor)" : "");

        _liste.Children.Clear();
        if (_gorunen.Count == 0)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 40, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(Ui.Text(tum.Count == 0 ? "Henüz icra dosyası yok" : "Eşleşen dosya yok", 16, true));
            var alt = Ui.Muted(tum.Count == 0 ? "İlk icra dosyanızı ekleyerek başlayın." : "Aramayı veya süzgeci değiştirin.");
            alt.Margin = new Thickness(0, 6, 0, 12); alt.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(alt);
            if (tum.Count == 0) { var bt = Ui.Btn("＋ İcra dosyası ekle", "Primary", Yeni); bt.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(bt); }
            _liste.Children.Add(sp);
        }
        else foreach (var r in _gorunen.Take(Sinir)) _liste.Children.Add(SatirYap(r));

        var sec = _gorunen.FirstOrDefault(r => r.L("id") == _sid);
        if (sec != null) { _sel = sec; Vurgu(); DetayYukle(); }
        else if (_sid != 0 && _gorunen.Count == 0) { _sid = 0; _sel = null; _sag.Content = _bos; }
    }

    Border SatirYap(Row m)
    {
        var ad = m.S("borclu").Length > 0 ? m.S("borclu") : "?";
        var av = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = new SolidColorBrush(Renk(ad)), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = Bas(ad), Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var isim = Ui.Text(ad, 14, true); isim.TextTrimming = TextTrimming.CharacterEllipsis;
        var alt = Ui.Muted(string.Join(" • ", new[] { m.S("icra_dairesi"), m.S("esas_no") }.Where(x => x.Trim().Length > 0)), false);
        alt.FontSize = 12; alt.TextTrimming = TextTrimming.CharacterEllipsis;
        var orta = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        orta.Children.Add(isim); orta.Children.Add(alt);

        var sag = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var k = Utils.DaysLeft(m.S("gorev_son"));
        Pill pill;
        if (m.S("durum") == "Kapandı") pill = new Pill("Kapandı", "muted");
        else if (m.L("gorev_say") > 0 && k != null && k <= 7) pill = new Pill(k < 0 ? $"Görev {-k} gün geçti" : k == 0 ? "Görev bugün" : $"Görev {k} gün", k <= 3 ? "danger" : "warn");
        else if (m.D("kalan") > 0) pill = new Pill(Utils.FmtMoney(m.D("kalan")), "warn");
        else pill = new Pill("Tahsil edildi", "ok");
        pill.HorizontalAlignment = HorizontalAlignment.Right;
        var mv = Ui.Muted(m.S("muvekkil"), false);
        mv.FontSize = 11; mv.HorizontalAlignment = HorizontalAlignment.Right; mv.Margin = new Thickness(0, 2, 2, 0); mv.MaxWidth = 150; mv.TextTrimming = TextTrimming.CharacterEllipsis;
        sag.Children.Add(pill); sag.Children.Add(mv);

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
        b.MouseLeftButtonDown += (_, e) => { Ac(m); if (e.ClickCount >= 2) { e.Handled = true; Duzenle(); } };
        b.MouseRightButtonDown += (_, _) => Ac(m);
        var cm = new ContextMenu();
        void Ekle(string h, Action a) { var mi = new MenuItem { Header = h }; mi.Click += (_, _) => { Ac(m); a(); }; cm.Items.Add(mi); }
        Ekle("Düzenle", Duzenle); Ekle("Süreli görev ekle", GorevEkle); Ekle("Tahsilat işle", Tahsilat);
        cm.Items.Add(new Separator());
        Ekle("Sil…", Sil);
        b.ContextMenu = cm;
        return b;
    }

    void Vurgu()
    {
        foreach (var c in _liste.Children)
        {
            if (c is not Border b || b.Tag is not long id) continue;
            if (id == _sid) { b.SetResourceReference(Border.BackgroundProperty, "sel"); b.BringIntoView(); }
            else b.Background = Brushes.Transparent;
        }
    }

    void Gez(int d)
    {
        if (_gorunen.Count == 0) return;
        var i = _gorunen.FindIndex(r => r.L("id") == _sid);
        i = i < 0 ? (d > 0 ? 0 : _gorunen.Count - 1) : Math.Clamp(i + d, 0, Math.Min(_gorunen.Count, Sinir) - 1);
        Ac(_gorunen[i]);
    }

    void Ac(Row r)
    {
        if (_sid == r.L("id") && ReferenceEquals(_sag.Content, _detay)) return;
        NotYaz();
        _sel = r; _sid = r.L("id");
        Vurgu();
        DetayYukle();
    }

    // ------------------------------------------------------------ detay
    void DetayYukle()
    {
        var d = Db.One("SELECT i.*, COALESCE(m.ad_soyad,'—') AS muvekkil FROM icra_takipleri i LEFT JOIN muvekkiller m ON m.id=i.muvekkil_id WHERE i.id=?", _sid);
        if (d == null) { _sag.Content = _bos; _sid = 0; _sel = null; return; }
        _sel = d;
        _sag.Content = _detay;
        var ad = d.S("borclu").Length > 0 ? d.S("borclu") : "—";
        _avatar.Text = Bas(ad);
        _dTitle.Text = ad;
        _dSub.Text = $"{d.S("icra_dairesi")} {d.S("esas_no")}".Trim() + (d.S("muvekkil").Length > 0 ? $"   •   Müvekkil: {d.S("muvekkil")}" : "");
        var kalan = d.D("alacak_tutari") - d.D("tahsil_edilen");
        _kAlacak.Text = Utils.FmtMoney(d.D("alacak_tutari")); _kTahsil.Text = Utils.FmtMoney(d.D("tahsil_edilen")); _kKalan.Text = Utils.FmtMoney(kalan);
        _kTarih.Text = (d.S("takip_tarihi").Length > 0 ? Utils.FmtDate(d.S("takip_tarihi")) : "–") + " / " + (d.S("kesinlesme_tarihi").Length > 0 ? Utils.FmtDate(d.S("kesinlesme_tarihi")) : "–");
        _oMuv.Text = d.S("muvekkil"); _oDaire.Text = $"{d.S("icra_dairesi")} {d.S("esas_no")}".Trim();
        _oTur.Text = d.S("takip_turu"); _oTc.Text = d.S("borclu_tc").Length > 0 ? d.S("borclu_tc") : "–"; _oDurum.Text = d.S("durum");
        _oKes.Text = d.S("kesinlesme_tarihi").Length > 0 ? Utils.FmtDate(d.S("kesinlesme_tarihi")) : "–";

        var gs = Db.Query("SELECT * FROM sureler WHERE icra_id=? ORDER BY durum='Açık' DESC, son_tarih", _sid);
        foreach (var r in gs)
        {
            var k = Utils.DaysLeft(r.S("son_tarih"));
            r["kalan_txt"] = r.S("durum") == "Tamamlandı" ? "✔" : k == null ? "" : k < 0 ? $"{-k} gün geçti" : k == 0 ? "BUGÜN" : $"{k} gün";
        }
        _tG.Load(gs, new()
        {
            ["kalan_txt"] = r => r.S("durum") == "Tamamlandı" ? "ok" : Ui.DeadlineKind(r.S("son_tarih")),
            ["son_tarih"] = r => r.S("durum") == "Tamamlandı" ? "muted" : Ui.DeadlineKind(r.S("son_tarih")),
            ["durum"] = r => r.S("durum") == "Tamamlandı" ? "ok" : "warn",
        });
        var acik = gs.Where(x => x.S("durum") == "Açık").OrderBy(x => x.S("son_tarih")).ToList();
        _kGorev.Text = acik.Count == 0 ? "Yok" : $"{acik.Count} • {Utils.FmtDate(acik[0].S("son_tarih"))}";
        _tabG.Header = $"Süreli görevler ({acik.Count} açık)";

        _notYukleniyor = true;
        _not.Text = d.S("notlar"); _notKirli = false; _notDurum.Text = "";
        _notYukleniyor = false;
    }

    void NotYaz()
    {
        if (!_notKirli || _sid <= 0) return;
        _notKirli = false;
        Db.Update("icra_takipleri", _sid, new Dictionary<string, object?> { ["notlar"] = _not.Text });
        _notDurum.Text = "kaydedildi";
    }

    // ------------------------------------------------------------ işlemler
    List<Field> Fields() => new()
    {
        new("muvekkil_id", "Müvekkil", "combo", Secenek.Muvekkiller(Db), required: true),
        new("icra_dairesi", "İcra Dairesi", required: true),
        new("esas_no", "Esas No (örn. 2026/123)"),
        new("takip_turu", "Takip Türü", "combo", Field.Of(Turler), def: "İlamsız"),
        new("borclu", "Borçlu", required: true),
        new("borclu_tc", "Borçlu T.C. / Vergi No"),
        new("takip_tarihi", "Takip Tarihi", "date", def: Utils.TodayIso()),
        new("kesinlesme_tarihi", "Kesinleşme Tarihi (ilamlı takipte)", "date"),
        new("alacak_tutari", "Alacak Tutarı (asıl + işlemiş faiz vb.)", "money"),
        new("tahsil_edilen", "Tahsil Edilen", "money"),
        new("durum", "Durum", "combo", Field.Of(Durumlar), def: "Derdest"),
        new("notlar", "Notlar", "multiline"),
    };

    public void Yeni()
    {
        var v = FormDialog.Ask(this, "Yeni İcra Dosyası", Fields());
        if (v == null) return;
        _sid = Db.Insert("icra_takipleri", v);
        _fl = Fl.Tumu;
        Refresh();
    }

    void Duzenle()
    {
        if (_sid == 0) { Ui.Info(this, "Önce bir dosya seçin."); return; }
        var v = FormDialog.Ask(this, "İcra Dosyası", Fields(), Db.One("SELECT * FROM icra_takipleri WHERE id=?", _sid));
        if (v == null) return;
        _notKirli = false;
        Db.Update("icra_takipleri", _sid, v);
        Refresh();
    }

    void DurumYap(string durum)
    {
        if (_sid == 0) return;
        Db.Update("icra_takipleri", _sid, new Dictionary<string, object?> { ["durum"] = durum });
        Refresh();
    }

    void GorevEkle()
    {
        if (_sid == 0) { Ui.Info(this, "Önce bir icra dosyası seçin."); return; }
        if (IcraGorevPage.GorevEkle(this, Db, _sid)) { Refresh(); _tabs.SelectedItem = _tabG; }
    }

    long GorevId => _tG.Current?.L("id") ?? 0;

    void GorevDuzenle()
    {
        var id = GorevId;
        if (id == 0) { Ui.Info(this, "Önce bir görev seçin."); return; }
        var v = IsDialog.Goster(this, Db, Db.One("SELECT * FROM sureler WHERE id=?", id));
        if (v == null) return;
        Db.Update("sureler", id, v);
        Refresh();
    }

    void GorevDurum(string durum)
    {
        var id = GorevId;
        if (id == 0) { Ui.Info(this, "Önce bir görev seçin."); return; }
        Db.Update("sureler", id, new Dictionary<string, object?> { ["durum"] = durum });
        Refresh();
    }

    void GorevSil()
    {
        var id = GorevId;
        if (id == 0) { Ui.Info(this, "Önce bir görev seçin."); return; }
        if (Ui.Confirm(this, "Görev silinsin mi?")) { Db.Delete("sureler", id); Refresh(); }
    }

    void Tahsilat()
    {
        if (_sel == null) { Ui.Info(this, "Önce bir dosya seçin."); return; }
        var v = FormDialog.Ask(this, "Tahsilat İşle", new List<Field> { new("tutar", "Tahsil edilen tutar", "money", required: true) });
        if (v == null) return;
        var yeni = _sel.D("tahsil_edilen") + Convert.ToDouble(v["tutar"] ?? 0.0);
        Db.Update("icra_takipleri", _sid, new Dictionary<string, object?> { ["tahsil_edilen"] = yeni });
        Refresh();
    }

    void Sil()
    {
        if (_sid == 0) return;
        if (!Ui.Confirm(this, "İcra dosyası (ve bağlı süreli görevleri) silinsin mi?")) return;
        Db.Execute("DELETE FROM sureler WHERE icra_id=?", _sid);
        Db.Delete("icra_takipleri", _sid);
        _sid = 0; _sel = null; _sag.Content = _bos;
        Refresh();
    }
}
