using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HukukErp.Core;

namespace HukukErp.UI.Pages;

/// <summary>İcra dosyaları — önce müvekkil: solda müvekkiller (icra dosyası sayısı, kalan alacak, yaklaşan görev), sağda seçili müvekkilin borçlu icra dosyaları;
/// bir dosya seçilince altında özet, süreli görevler ve notlar açılır.</summary>
public class IcraPage : BasePage
{
    static readonly string[] Durumlar = { "Derdest", "Haciz aşamasında", "Satış aşamasında", "Ödeme bekleniyor", "İtiraz / dava", "Kapandı" };
    static readonly string[] Turler = { "İlamsız", "İlamlı", "Kambiyo senedi", "Kira alacağı (tahliye)", "Rehin / ipotek", "Diğer" };
    static readonly string[] Siralar = { "Ad Soyad (A → Z)", "Kalan alacak (çoktan aza)", "Dosya sayısı (çoktan aza)", "Yaklaşan görev" };
    static readonly string[] Palet = { "#5B8CFF", "#34D39E", "#F7B23B", "#FF6B7A", "#A78BFA", "#22C1DC", "#F472B6", "#94A3B8" };
    static readonly CultureInfo TrC = new("tr-TR");
    const int Sinir = 150;

    enum Fl { Dosyali, Gorevli, Tumu }
    Fl _fl = Fl.Dosyali;
    readonly Dictionary<Fl, TbBtn> _chip = new();

    // sol: müvekkiller
    readonly TextBox _ara = new() { Margin = new Thickness(0, 0, 0, 8), ToolTip = "Müvekkil, borçlu, icra dairesi veya esas no ile ara  •  Enter: ilk sonucu aç  •  ↑ ↓: gez" };
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
    readonly TextBlock _kDos = KV(), _kAlacak = KV(), _kTahsil = KV(), _kKalan = KV(), _kGorev = KV(14);
    readonly TextBlock _bTop = KV(22), _bDer = KV(22), _bKalan = KV(18);
    readonly DataTable _tF = new(("Borçlu", "borclu", null), ("İcra Dairesi", "icra_dairesi", null), ("Esas No", "esas_no", null), ("Takip", "takip_tarihi", Fmt.Date),
                                 ("Kesinleşme", "kesinlesme_tarihi", Fmt.Date), ("Alacak", "alacak_tutari", Fmt.Money), ("Tahsil", "tahsil_edilen", Fmt.Money),
                                 ("Kalan", "kalan", Fmt.Money), ("Durum", "durum", null), ("Açık Görev", "gorev_txt", null));
    readonly CheckBox _kapaliGoster = new() { Content = "Kapananları da göster", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };

    // alt: seçili dosya
    readonly TextBlock _fBaslik = Ui.H2("");
    readonly TextBlock _oDaire = Ui.Text("–", 13, false, "text", true), _oTur = Ui.Text("–", 13, false, "text", true), _oTc = Ui.Text("–", 13, false, "text", true),
                       _oDurum = Ui.Text("–", 13, false, "text", true), _oTakip = Ui.Text("–", 13, false, "text", true), _oKes = Ui.Text("–", 13, false, "text", true);
    readonly DataTable _tG = new(("Son Gün", "son_tarih", Fmt.Date), ("Kalan", "kalan_txt", null), ("Görev", "baslik", null), ("Durum", "durum", null));
    readonly TextBox _not = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(8) };
    readonly TextBlock _notDurum = Ui.Muted("");
    readonly TabControl _tabs = new();
    readonly ContentControl _altIcerik = new();
    UIElement _altBos = null!, _altDetay = null!;
    TabItem _tabG = null!;

    Row? _mSel;
    long _mid, _fid;
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
        Fill(Ui.HSplit(BuildSol(), _sag, 4, 8));

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
        _tF.SelectionChanged += (_, _) => { if (!_busy) DosyaSecildi(); };
        _tF.MouseDoubleClick += (_, _) => Duzenle();
        _tG.MouseDoubleClick += (_, _) => GorevDuzenle();
        _kapaliGoster.Checked += (_, _) => DosyalariYukle(); _kapaliGoster.Unchecked += (_, _) => DosyalariYukle();
        _not.TextChanged += (_, _) => { if (!_notYukleniyor && _fid > 0) { _notKirli = true; _notDurum.Text = "kaydedilmedi…"; } };
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
        yeni.ToolTip = "Yeni icra dosyası (seçili müvekkil için)";
        DockPanel.SetDock(yeni, Dock.Right);
        var baslik = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        baslik.Children.Add(yeni); baslik.Children.Add(Ui.Text("İcra – Müvekkiller", 18, true));
        DockPanel.SetDock(baslik, Dock.Top);
        DockPanel.SetDock(_ara, Dock.Top);

        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var (f, ad) in new[] { (Fl.Dosyali, "İcra dosyası olanlar"), (Fl.Gorevli, "Görevi yaklaşan"), (Fl.Tumu, "Tüm müvekkiller") })
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
        var t = Ui.Text("Soldan bir müvekkil seçin", 20, true); t.HorizontalAlignment = HorizontalAlignment.Center;
        var a = Ui.Muted("Seçtiğiniz müvekkilin borçlu icra dosyaları burada listelenir. Borçlu, icra dairesi ya da esas no yazıp Enter'a basarsanız ilk sonuç açılır.");
        a.TextAlignment = TextAlignment.Center; a.Margin = new Thickness(0, 6, 0, 18);
        var kpi = new UniformGrid { Columns = 3 };
        kpi.Children.Add(Kart("Toplam icra dosyası", _bTop)); kpi.Children.Add(Kart("Derdest dosya", _bDer)); kpi.Children.Add(Kart("Kalan alacak (derdest)", _bKalan));
        sp.Children.Add(t); sp.Children.Add(a); sp.Children.Add(kpi);
        return sp;
    }

    UIElement BuildDetay()
    {
        var dock = new DockPanel();
        // müvekkil başlığı
        var av = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(26), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 14, 0), Child = _avatar };
        av.SetResourceReference(Border.BackgroundProperty, "accent");
        _avatar.Foreground = Brushes.White; _avatar.HorizontalAlignment = HorizontalAlignment.Center; _avatar.VerticalAlignment = VerticalAlignment.Center;
        _dTitle.FontSize = 20; _dSub.TextWrapping = TextWrapping.Wrap;
        var info = new StackPanel(); info.Children.Add(_dTitle); info.Children.Add(_dSub);
        var eylem = Ui.WRow(Ui.Btn("👤 Müvekkil kartı", "", () => Ctx.Navigate("muvekkil", null)), Ui.Btn("⏱ İcra Görevleri", "", () => Ctx.Navigate("icra_gorev", null)));
        eylem.HorizontalAlignment = HorizontalAlignment.Right;
        var hg = new Grid();
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hg.ColumnDefinitions.Add(new ColumnDefinition());
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(info, 1); Grid.SetColumn(eylem, 2);
        hg.Children.Add(av); hg.Children.Add(info); hg.Children.Add(eylem);
        var hdr = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(18, 12, 18, 12), Margin = new Thickness(0, 0, 0, 10), Child = hg };
        hdr.SetResourceReference(Border.BackgroundProperty, "surface");
        hdr.SetResourceReference(Border.BorderBrushProperty, "border");
        DockPanel.SetDock(hdr, Dock.Top);

        var kpi = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, -10, 10) };
        kpi.Children.Add(Kart("Derdest dosya", _kDos)); kpi.Children.Add(Kart("Toplam alacak", _kAlacak)); kpi.Children.Add(Kart("Tahsil edilen", _kTahsil));
        kpi.Children.Add(Kart("Kalan", _kKalan)); kpi.Children.Add(Kart("Sıradaki görev", _kGorev));
        DockPanel.SetDock(kpi, Dock.Top);
        dock.Children.Add(hdr); dock.Children.Add(kpi);

        // üst: borçlu icra dosyaları + araç çubuğu
        var diger = Ui.Btn("⋯ Diğer  ▾", "", null);
        diger.Click += (_, _) =>
        {
            var f = _tF.Current;
            if (f == null) { Ui.Info(this, "Önce listeden bir icra dosyası seçin."); return; }
            var m = new ContextMenu { PlacementTarget = diger, Placement = PlacementMode.Bottom };
            void E(string h, Action a) { var mi = new MenuItem { Header = h }; mi.Click += (_, _) => a(); m.Items.Add(mi); }
            if (f.S("durum") == "Kapandı") E("↺ Dosyayı yeniden aç", () => DurumYap("Derdest")); else E("✔ Dosyayı kapat", () => DurumYap("Kapandı"));
            m.Items.Add(new Separator());
            E("🗑 Dosyayı sil…", Sil);
            m.IsOpen = true;
        };
        var bar = Ui.WRow(Ui.Btn("＋ Yeni İcra Dosyası", "Primary", Yeni), Ui.Btn("✏ Düzenle", "", Duzenle), Ui.Btn("⏱ Süreli görev ekle", "", GorevEkle), Ui.Btn("＋ Tahsilat işle", "", Tahsilat), diger, _kapaliGoster);
        bar.Margin = new Thickness(0, 0, 0, 8);
        var ust = new Card();
        ust.Top(Ui.Text("Borçlu icra dosyaları", 14, true), 6);
        ust.Top(bar, 0);
        ust.Fill(_tF);

        // alt: seçili dosya
        var bosAlt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var ba = Ui.Muted("Yukarıdan bir icra dosyası seçin: özeti, süreli görevleri ve notları burada açılır."); ba.TextAlignment = TextAlignment.Center;
        bosAlt.Children.Add(ba);
        _altBos = bosAlt;

        var ozet = new Card();
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) }); g.ColumnDefinitions.Add(new ColumnDefinition());
        int satir = 0;
        void Sat(string et, TextBlock v)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = Ui.Muted(et, false); l.Margin = new Thickness(0, 3, 8, 3); Grid.SetRow(l, satir);
            v.Margin = new Thickness(0, 3, 0, 3); Grid.SetRow(v, satir); Grid.SetColumn(v, 1);
            g.Children.Add(l); g.Children.Add(v); satir++;
        }
        Sat("İcra dairesi / Esas", _oDaire); Sat("Takip türü", _oTur); Sat("Borçlu T.C./V.N.", _oTc); Sat("Takip tarihi", _oTakip); Sat("Kesinleşme", _oKes); Sat("Durum", _oDurum);
        ozet.Fill(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = g });
        _tabs.Items.Add(Ui.Tab("Özet", ozet));

        var gd = new DockPanel();
        var gb = Ui.HRow(Ui.Btn("＋ Süreli görev", "Primary", GorevEkle), Ui.Btn("Düzenle", "", GorevDuzenle), Ui.Btn("✔ Tamamlandı", "Ok", () => GorevDurum("Tamamlandı")),
                         Ui.Btn("↺ Yeniden Aç", "", () => GorevDurum("Açık")), Ui.Btn("Sil", "Danger", GorevSil));
        gb.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(gb, Dock.Bottom);
        gd.Children.Add(gb); gd.Children.Add(_tG);
        _tabG = Ui.Tab("Süreli görevler", gd);
        _tabs.Items.Add(_tabG);

        _not.SetResourceReference(Control.ForegroundProperty, "text");
        _not.SetResourceReference(Control.BackgroundProperty, "surface2");
        _not.SetResourceReference(Control.BorderBrushProperty, "border");
        var nd = new DockPanel();
        var nb = Ui.HRow(Ui.Btn("Notları Kaydet", "Primary", NotYaz), _notDurum);
        nb.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(nb, Dock.Bottom);
        nd.Children.Add(nb); nd.Children.Add(_not);
        _tabs.Items.Add(Ui.Tab("Notlar", nd));

        var altKart = new Border { Child = _altIcerik };
        _altDetay = _tabs;
        _altIcerik.Content = _altBos;
        dock.Children.Add(Ui.VSplit(ust, _altIcerik, 3, 2));
        return dock;
    }

    // ------------------------------------------------------------ sol liste (müvekkiller)
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

    public override void Refresh() => ListeYukle();

    void ListeYukle()
    {
        NotYaz();
        var tum = Db.Query(
            "SELECT m.id, m.ad_soyad, m.tc_no, m.telefon, " +
            "(SELECT COUNT(*) FROM icra_takipleri i WHERE i.muvekkil_id=m.id) AS isay, " +
            "(SELECT COUNT(*) FROM icra_takipleri i WHERE i.muvekkil_id=m.id AND i.durum<>'Kapandı') AS dsay, " +
            "(SELECT COALESCE(SUM(i.alacak_tutari-i.tahsil_edilen),0) FROM icra_takipleri i WHERE i.muvekkil_id=m.id AND i.durum<>'Kapandı') AS kalan, " +
            "(SELECT MIN(s.son_tarih) FROM sureler s JOIN icra_takipleri i ON i.id=s.icra_id WHERE i.muvekkil_id=m.id AND s.durum='Açık' AND i.durum<>'Kapandı') AS gorev_son, " +
            "(SELECT group_concat(COALESCE(i.borclu,'') || ' ' || COALESCE(i.esas_no,'') || ' ' || COALESCE(i.icra_dairesi,'') || ' ' || COALESCE(i.borclu_tc,''), ' ') FROM icra_takipleri i WHERE i.muvekkil_id=m.id) AS aranan " +
            "FROM muvekkiller m");

        // genel özet (boş sağ panel)
        var ic = Db.One("SELECT COUNT(*) AS n, COALESCE(SUM(CASE WHEN durum<>'Kapandı' THEN 1 ELSE 0 END),0) AS d, " +
                        "COALESCE(SUM(CASE WHEN durum<>'Kapandı' THEN alacak_tutari-tahsil_edilen ELSE 0 END),0) AS k FROM icra_takipleri");
        _bTop.Text = (ic?.L("n") ?? 0).ToString(); _bDer.Text = (ic?.L("d") ?? 0).ToString(); _bKalan.Text = Utils.FmtMoney(ic?.D("k") ?? 0);

        bool Yakin(Row r) { var k = Utils.DaysLeft(r.S("gorev_son")); return r.S("gorev_son").Length > 0 && k != null && k <= 7; }
        _busy = true;
        _chip[Fl.Dosyali].Metin($"İcra dosyası olanlar {tum.Count(r => r.L("isay") > 0)}");
        _chip[Fl.Gorevli].Metin($"Görevi yaklaşan {tum.Count(Yakin)}");
        _chip[Fl.Tumu].Metin($"Tüm müvekkiller {tum.Count}");
        foreach (var kv in _chip) kv.Value.Aktif(kv.Key == _fl);
        _busy = false;

        IEnumerable<Row> l = tum;
        l = _fl switch
        {
            Fl.Dosyali => l.Where(r => r.L("isay") > 0),
            Fl.Gorevli => l.Where(Yakin),
            _ => l,
        };
        var q = Utils.TrLower(_ara.Text.Trim());
        if (q.Length > 0)
            l = l.Where(r => Utils.TrLower($"{r.S("ad_soyad")} {r.S("tc_no")} {r.S("aranan")}").Contains(q));
        var cmp = StringComparer.Create(TrC, true);
        l = _sira.SelectedIndex switch
        {
            1 => l.OrderByDescending(r => r.D("kalan")).ThenBy(r => r.S("ad_soyad"), cmp),
            2 => l.OrderByDescending(r => r.L("isay")).ThenBy(r => r.S("ad_soyad"), cmp),
            3 => l.OrderBy(r => r.S("gorev_son").Length == 0 ? 1 : 0).ThenBy(r => r.S("gorev_son"), StringComparer.Ordinal).ThenBy(r => r.S("ad_soyad"), cmp),
            _ => l.OrderBy(r => r.S("ad_soyad"), cmp),
        };
        _gorunen = l.ToList();
        _sayi.Text = $"{_gorunen.Count} / {tum.Count} müvekkil" + (_gorunen.Count > Sinir ? $" (ilk {Sinir} gösteriliyor)" : "");

        _liste.Children.Clear();
        if (_gorunen.Count == 0)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 40, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(Ui.Text(tum.Count == 0 ? "Henüz müvekkil yok" : "Eşleşen müvekkil yok", 16, true));
            var alt = Ui.Muted(tum.Count == 0 ? "Önce Müvekkil sayfasından müvekkil ekleyin." : _fl == Fl.Dosyali ? "İcra dosyası olmayan müvekkiller için \"Tüm müvekkiller\"e geçin." : "Aramayı veya süzgeci değiştirin.");
            alt.Margin = new Thickness(0, 6, 0, 12); alt.HorizontalAlignment = HorizontalAlignment.Center; alt.TextAlignment = TextAlignment.Center;
            sp.Children.Add(alt);
            _liste.Children.Add(sp);
        }
        else foreach (var r in _gorunen.Take(Sinir)) _liste.Children.Add(SatirYap(r));

        var sec = _gorunen.FirstOrDefault(r => r.L("id") == _mid);
        if (sec != null) { _mSel = sec; Vurgu(); DetayYukle(); }
        else if (_mid != 0 && _gorunen.Count == 0) { _mid = 0; _mSel = null; _fid = 0; _sag.Content = _bos; }
    }

    Border SatirYap(Row m)
    {
        var ad = m.S("ad_soyad");
        var av = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = new SolidColorBrush(Renk(ad)), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = Bas(ad), Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var isim = Ui.Text(ad, 14, true); isim.TextTrimming = TextTrimming.CharacterEllipsis;
        var alt = Ui.Muted(m.L("isay") == 0 ? "icra dosyası yok" : $"{m.L("dsay")} derdest / {m.L("isay")} icra dosyası", false);
        alt.FontSize = 12; alt.TextTrimming = TextTrimming.CharacterEllipsis;
        var orta = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        orta.Children.Add(isim); orta.Children.Add(alt);

        var sag = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var k = Utils.DaysLeft(m.S("gorev_son"));
        Pill pill;
        if (m.S("gorev_son").Length > 0 && k != null && k <= 7) pill = new Pill(k < 0 ? $"Görev {-k} gün geçti" : k == 0 ? "Görev bugün" : $"Görev {k} gün", k <= 3 ? "danger" : "warn");
        else if (m.D("kalan") > 0) pill = new Pill(Utils.FmtMoney(m.D("kalan")), "warn");
        else if (m.L("isay") > 0) pill = new Pill("Alacak yok", "ok");
        else pill = new Pill("Dosya yok", "muted");
        pill.HorizontalAlignment = HorizontalAlignment.Right;
        sag.Children.Add(pill);

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(orta, 1); Grid.SetColumn(sag, 2);
        g.Children.Add(av); g.Children.Add(orta); g.Children.Add(sag);

        var b = new Border { Padding = new Thickness(10, 8, 10, 8), BorderThickness = new Thickness(0, 0, 0, 1), Child = g, Cursor = Cursors.Hand, Tag = m.L("id"), Background = Brushes.Transparent };
        b.SetResourceReference(Border.BorderBrushProperty, "border");
        if (m.L("id") == _mid) b.SetResourceReference(Border.BackgroundProperty, "sel");
        b.MouseEnter += (_, _) => { if ((long)b.Tag != _mid) b.SetResourceReference(Border.BackgroundProperty, "surface2"); };
        b.MouseLeave += (_, _) => { if ((long)b.Tag != _mid) b.Background = Brushes.Transparent; };
        b.MouseLeftButtonDown += (_, _) => Ac(m);
        var cm = new ContextMenu();
        var mi = new MenuItem { Header = "Yeni icra dosyası" }; mi.Click += (_, _) => { Ac(m); Yeni(); }; cm.Items.Add(mi);
        b.ContextMenu = cm;
        return b;
    }

    void Vurgu()
    {
        foreach (var c in _liste.Children)
        {
            if (c is not Border b || b.Tag is not long id) continue;
            if (id == _mid) { b.SetResourceReference(Border.BackgroundProperty, "sel"); b.BringIntoView(); }
            else b.Background = Brushes.Transparent;
        }
    }

    void Gez(int d)
    {
        if (_gorunen.Count == 0) return;
        var i = _gorunen.FindIndex(r => r.L("id") == _mid);
        i = i < 0 ? (d > 0 ? 0 : _gorunen.Count - 1) : Math.Clamp(i + d, 0, Math.Min(_gorunen.Count, Sinir) - 1);
        Ac(_gorunen[i]);
    }

    void Ac(Row r)
    {
        if (_mid == r.L("id") && ReferenceEquals(_sag.Content, _detay)) return;
        NotYaz();
        _mSel = r; _mid = r.L("id"); _fid = 0;
        Vurgu();
        DetayYukle();
    }

    // ------------------------------------------------------------ sağ: müvekkilin dosyaları
    void DetayYukle()
    {
        var m = Db.One("SELECT * FROM muvekkiller WHERE id=?", _mid);
        if (m == null) { _sag.Content = _bos; _mid = 0; _mSel = null; return; }
        _mSel = m;
        _sag.Content = _detay;
        _avatar.Text = Bas(m.S("ad_soyad"));
        _dTitle.Text = m.S("ad_soyad");
        _dSub.Text = string.Join("   •   ", new[] { m.S("tc_no"), m.S("telefon") }.Where(x => x.Trim().Length > 0));
        DosyalariYukle();
    }

    void DosyalariYukle()
    {
        if (_mid == 0) return;
        NotYaz();
        var onceki = _fid;
        var rows = Db.Query(
            "SELECT i.*, " +
            "(SELECT COUNT(*) FROM sureler s WHERE s.icra_id=i.id AND s.durum='Açık') AS gorev_say, " +
            "(SELECT MIN(s.son_tarih) FROM sureler s WHERE s.icra_id=i.id AND s.durum='Açık') AS gorev_son " +
            "FROM icra_takipleri i WHERE i.muvekkil_id=? ORDER BY i.durum='Kapandı', i.id DESC", _mid);
        double alacak = 0, tahsil = 0; int derdest = 0; string enYakin = "";
        foreach (var r in rows)
        {
            r["kalan"] = r.D("alacak_tutari") - r.D("tahsil_edilen");
            r["gorev_txt"] = r.L("gorev_say") == 0 ? "" : $"{r.L("gorev_say")} • {Utils.FmtDate(r.S("gorev_son"))}";
            if (r.S("durum") == "Kapandı") continue;
            derdest++; alacak += r.D("alacak_tutari"); tahsil += r.D("tahsil_edilen");
            if (r.S("gorev_son").Length > 0 && (enYakin.Length == 0 || string.CompareOrdinal(r.S("gorev_son"), enYakin) < 0)) enYakin = r.S("gorev_son");
        }
        _kDos.Text = derdest.ToString(); _kAlacak.Text = Utils.FmtMoney(alacak); _kTahsil.Text = Utils.FmtMoney(tahsil); _kKalan.Text = Utils.FmtMoney(alacak - tahsil);
        _kGorev.Text = enYakin.Length == 0 ? "Yok" : Utils.FmtDate(enYakin);
        var goster = _kapaliGoster.IsChecked == true ? rows : rows.Where(r => r.S("durum") != "Kapandı").ToList();

        _busy = true;
        try
        {
            _tF.Load(goster, new()
            {
                ["durum"] = r => r.S("durum") == "Kapandı" ? "muted" : "warn",
                ["kalan"] = r => r.D("kalan") > 0 && r.S("durum") != "Kapandı" ? "danger" : "ok",
            });
            if (onceki > 0)
            {
                var ix = _tF.Rows.FindIndex(r => r.L("id") == onceki);
                if (ix >= 0) _tF.SelectedIndex = ix;
            }
        }
        finally { _busy = false; }
        DosyaSecildi();
    }

    // ------------------------------------------------------------ alt: seçili dosya
    void DosyaSecildi()
    {
        var c = _tF.Current;
        if (c == null) { NotYaz(); _fid = 0; _altIcerik.Content = _altBos; return; }
        if (c.L("id") != _fid) NotYaz();
        _fid = c.L("id");
        var d = Db.One("SELECT * FROM icra_takipleri WHERE id=?", _fid);
        if (d == null) { _fid = 0; _altIcerik.Content = _altBos; return; }
        _altIcerik.Content = _altDetay;
        _oDaire.Text = $"{d.S("icra_dairesi")} {d.S("esas_no")}".Trim(); _oTur.Text = d.S("takip_turu");
        _oTc.Text = d.S("borclu_tc").Length > 0 ? d.S("borclu_tc") : "–"; _oDurum.Text = d.S("durum");
        _oTakip.Text = d.S("takip_tarihi").Length > 0 ? Utils.FmtDate(d.S("takip_tarihi")) : "–";
        _oKes.Text = d.S("kesinlesme_tarihi").Length > 0 ? Utils.FmtDate(d.S("kesinlesme_tarihi")) : "–";

        var gs = Db.Query("SELECT * FROM sureler WHERE icra_id=? ORDER BY durum='Açık' DESC, son_tarih", _fid);
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
        _tabG.Header = $"Süreli görevler ({gs.Count(x => x.S("durum") == "Açık")} açık)";
        _notYukleniyor = true;
        _not.Text = d.S("notlar"); _notKirli = false; _notDurum.Text = "";
        _notYukleniyor = false;
    }

    void NotYaz()
    {
        if (!_notKirli || _fid <= 0) return;
        _notKirli = false;
        Db.Update("icra_takipleri", _fid, new Dictionary<string, object?> { ["notlar"] = _not.Text });
        _notDurum.Text = "kaydedildi";
    }

    // ------------------------------------------------------------ işlemler
    List<Field> Fields() => new()
    {
        new("muvekkil_id", "Müvekkil", "combo", Secenek.Muvekkiller(Db), required: true),
        new("borclu", "Borçlu", required: true),
        new("borclu_tc", "Borçlu T.C. / Vergi No"),
        new("icra_dairesi", "İcra Dairesi", required: true),
        new("esas_no", "Esas No (örn. 2026/123)"),
        new("takip_turu", "Takip Türü", "combo", Field.Of(Turler), def: "İlamsız"),
        new("takip_tarihi", "Takip Tarihi", "date", def: Utils.TodayIso()),
        new("kesinlesme_tarihi", "Kesinleşme Tarihi (ilamlı takipte)", "date"),
        new("alacak_tutari", "Alacak Tutarı (asıl + işlemiş faiz vb.)", "money"),
        new("tahsil_edilen", "Tahsil Edilen", "money"),
        new("durum", "Durum", "combo", Field.Of(Durumlar), def: "Derdest"),
        new("notlar", "Notlar", "multiline"),
    };

    /// <summary>Seçili müvekkil için yeni icra dosyası (müvekkil hazır gelir; seçili değilse listeden seçilir).</summary>
    public void Yeni()
    {
        var v = FormDialog.Ask(this, "Yeni İcra Dosyası", Fields(), _mid > 0 ? new Dictionary<string, object?> { ["muvekkil_id"] = _mid } : null);
        if (v == null) return;
        var id = Db.Insert("icra_takipleri", v);
        _mid = Convert.ToInt64(v["muvekkil_id"]); _fid = id;
        _fl = Fl.Dosyali;
        Refresh();
    }

    void Duzenle()
    {
        if (_fid == 0) { Ui.Info(this, "Önce listeden bir icra dosyası seçin."); return; }
        var v = FormDialog.Ask(this, "İcra Dosyası", Fields(), Db.One("SELECT * FROM icra_takipleri WHERE id=?", _fid));
        if (v == null) return;
        _notKirli = false;
        Db.Update("icra_takipleri", _fid, v);
        _mid = Convert.ToInt64(v["muvekkil_id"]);
        Refresh();
    }

    void DurumYap(string durum)
    {
        if (_fid == 0) return;
        Db.Update("icra_takipleri", _fid, new Dictionary<string, object?> { ["durum"] = durum });
        Refresh();
    }

    void GorevEkle()
    {
        if (_fid == 0) { Ui.Info(this, "Önce listeden bir icra dosyası seçin."); return; }
        if (IcraGorevPage.GorevEkle(this, Db, _fid)) { Refresh(); _tabs.SelectedItem = _tabG; }
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
        var c = _tF.Current;
        if (c == null) { Ui.Info(this, "Önce listeden bir icra dosyası seçin."); return; }
        var v = FormDialog.Ask(this, "Tahsilat İşle", new List<Field> { new("tutar", "Tahsil edilen tutar", "money", required: true) });
        if (v == null) return;
        var yeni = c.D("tahsil_edilen") + Convert.ToDouble(v["tutar"] ?? 0.0);
        Db.Update("icra_takipleri", c.L("id"), new Dictionary<string, object?> { ["tahsil_edilen"] = yeni });
        Refresh();
    }

    void Sil()
    {
        if (_fid == 0) return;
        if (!Ui.Confirm(this, "İcra dosyası (ve bağlı süreli görevleri) silinsin mi?")) return;
        Db.Execute("DELETE FROM sureler WHERE icra_id=?", _fid);
        Db.Delete("icra_takipleri", _fid);
        _fid = 0;
        Refresh();
    }
}
