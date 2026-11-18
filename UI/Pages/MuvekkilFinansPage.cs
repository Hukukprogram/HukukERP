using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HukukErp.Core;
using HukukErp.Services;
using Microsoft.Data.Sqlite;

namespace HukukErp.UI.Pages;

/// <summary>Müvekkil: liste (arama + sıralama) → müvekkile tıklayınca cari hesap, vekaletnameler ve dava dosyaları.</summary>
public class MuvekkilFinansPage : BasePage
{
    static readonly string[] Siralar =
        { "Ad Soyad (A → Z)", "Ad Soyad (Z → A)", "T.C. No", "Kalan bakiye (çoktan aza)", "Dava sayısı (çoktan aza)", "Son eklenen", "Sonraki duruşma (yakın → uzak)" };

    readonly DataTable _tM = new(("T.C. No", "tc_no", null), ("Ad Soyad", "ad_soyad", null), ("Telefon", "telefon", null), ("E-posta", "eposta", null),
        ("Dava", "dsay", null), ("Sonraki Duruşma", "durusma", Fmt.Date), ("Vekaletname", "vek", null), ("Kalan Bakiye", "kalan", Fmt.Money));
    readonly DataTable _tF = new(("Dava Dosyası", "dava_etiket", null), ("Dosya Konusu", "dosya_konusu", null), ("Toplam Ücret", "toplam_ucret", Fmt.Money),
        ("Alınan", "alinan_tutar", Fmt.Money), ("Kalan Bakiye", "kalan_bakiye", Fmt.Money), ("Sonraki Tahsilat", "sonraki_tahsilat_tarihi", Fmt.Date),
        ("Makbuz", "makbuz_kesildi", v => v != null && Convert.ToInt64(v) != 0 ? "Kesildi" : "Bekliyor"));
    readonly DataTable _tV = new(("Tarih", "tarih", Fmt.Date), ("Tür", "tur", null), ("Bağlı Dava", "dava", null), ("Açıklama", "aciklama", null), ("Dosya", "dosya", null));
    readonly DataTable _tDv = new(("Mahkeme", "mahkeme_adi", null), ("Esas No", "esas_no", null), ("Tür", "dava_turu", null),
        ("Karşı Taraf", "karsi_taraf", null), ("Duruşma", "durusma_tarihi", Fmt.Date), ("Aşama", "asama_durum", null));
    readonly TextBlock _vTitle = Ui.H2("Vekaletnameler");
    readonly TextBlock _fSum = Ui.Muted("");
    readonly TextBlock _hTitle = Ui.H2("Tahsilat hareketleri — cari kaydı seçin");
    readonly DataTable _tH = new(("Tahsilat Tarihi", "tarih", Fmt.Date), ("Tutar", "tutar", Fmt.Money), ("Açıklama", "aciklama", null));
    readonly TextBlock _hSum = Ui.Muted("");
    readonly TextBlock _dTitle = Ui.H2("");
    readonly TextBlock _dSub = Ui.Muted("");
    readonly TextBlock _sayi = Ui.Muted("");
    readonly TextBox _ara = new() { Width = 300, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Ad soyad, T.C. no, telefon veya e-posta ile ara" };
    readonly ComboBox _sira = new() { MinWidth = 220, Margin = new Thickness(0, 0, 8, 0) };
    readonly ContentControl _host = new();
    readonly TabControl _tabs = new();
    enum Fl { Tumu, Bakiye, VekYok, Davali, DavasiYok }
    Fl _fl = Fl.Tumu;
    readonly Dictionary<Fl, TbBtn> _chip = new();
    static TextBlock KV(double boyut = 22) => Ui.Text("–", boyut, true);
    readonly TextBlock _kTop = KV(), _kBak = KV(18), _kVek = KV(), _kDav = KV();
    readonly TextBlock _dkTop = KV(18), _dkAl = KV(18), _dkKal = KV(18), _dkDav = KV(18), _dkDur = KV(18);
    readonly TextBlock _avatar = Ui.Text("", 20, true);
    readonly TextBlock _dNot = Ui.Text("", 12, false, "warn", true);
    readonly DataTable _tS = new(("Son Gün", "son_tarih", Fmt.Date), ("Süre / İş", "baslik", null), ("Dava", "dava", null), ("Durum", "durum", null));
    TabItem _tabF = null!, _tabV = null!, _tabD = null!, _tabS = null!;
    UIElement _liste = null!, _detay = null!;
    Row? _mSel;
    long _sid;
    bool _busy;

    public MuvekkilFinansPage(AppCtx ctx) : base(ctx)
    {
        Top(Ui.PageHeader("Müvekkil", "Listede müvekkile çift tıklayın: cari hesap, vekaletnameler ve dava dosyaları açılır"));
        _liste = BuildListe();
        _detay = BuildDetay();
        _host.Content = _liste;
        Fill(_host);

        foreach (var s in Siralar) _sira.Items.Add(s);
        _sira.SelectedIndex = 0;
        _sira.SelectionChanged += (_, _) => { if (!_busy) ListeYukle(); };
        _ara.TextChanged += (_, _) => { if (!_busy) ListeYukle(); };
        _ara.KeyDown += (_, e) => { if (e.Key == Key.Escape) { _ara.Text = ""; e.Handled = true; } };
        _tS.MouseDoubleClick += (_, _) => Ctx.Navigate("is_listesi", null);
        // Esc: müvekkil detayından listeye dön (yazı yazarken değil)
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && ReferenceEquals(_host.Content, _detay) && Keyboard.FocusedElement is not TextBox) { Geri(); e.Handled = true; }
        };
        ListeMenusu();

        _tM.MouseDoubleClick += (_, e) => { if (SatirdaMi(e.OriginalSource as DependencyObject)) Ac(_tM.Current); };
        _tM.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { Ac(_tM.Current); e.Handled = true; } };
        _tF.SelectionChanged += (_, _) => { if (!_busy) LoadHareket(); };
        _tV.MouseDoubleClick += (_, _) => VekAc();
        _tDv.MouseDoubleClick += (_, _) => DosyaKartinaGit();
    }

    static bool SatirdaMi(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is DataGridRow) return true;
            if (d is System.Windows.Controls.Primitives.DataGridColumnHeader || d is System.Windows.Controls.Primitives.ScrollBar) return false;
            d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
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
        Ac(Db.One("SELECT * FROM muvekkiller WHERE id=?", id));
        _tabs.SelectedIndex = 0;
        if (_tF.Current == null && _tF.Rows.Count > 0) _tF.SelectFirst();
        if (_tF.Current != null) Tahsilat(); else AddF();
    }

    static List<Row> TrSirala(List<Row> l)
    {
        var cmp = StringComparer.Create(new CultureInfo("tr-TR"), true);
        return l.OrderBy(x => x.S("ad_soyad"), cmp).ToList();
    }

    // ------------------------------------------------------------ liste
    static Border Kart(string et, TextBlock v)
    {
        var sp = new StackPanel();
        sp.Children.Add(Ui.Muted(et, false));
        v.Margin = new Thickness(0, 4, 0, 0);
        sp.Children.Add(v);
        var b = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 10, 0), Child = sp };
        b.SetResourceReference(Border.BackgroundProperty, "surface");
        b.SetResourceReference(Border.BorderBrushProperty, "border");
        return b;
    }

    UIElement BuildListe()
    {
        var dock = new DockPanel();
        var kpi = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -10, 12) };
        kpi.Children.Add(Kart("Toplam müvekkil", _kTop));
        kpi.Children.Add(Kart("Bakiyesi olan", _kBak));
        kpi.Children.Add(Kart("Vekaletnamesi eksik", _kVek));
        kpi.Children.Add(Kart("Davası olan", _kDav));
        DockPanel.SetDock(kpi, Dock.Top);

        var bar = Ui.WRow(Ui.Btn("＋ Müvekkil", "Primary", AddM), Ui.Muted("Ara:", false), _ara, Ui.Muted("Sırala:", false), _sira);
        DockPanel.SetDock(bar, Dock.Top);

        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (f, ad) in new[] { (Fl.Tumu, "Tümü"), (Fl.Bakiye, "Bakiyesi olanlar"), (Fl.VekYok, "Vekaletnamesi eksik"), (Fl.Davali, "Davası olanlar"), (Fl.DavasiYok, "Davası olmayanlar") })
        {
            var ff = f;
            var c = new TbBtn("", ad, () => { _fl = ff; ListeYukle(); });
            _chip[f] = c; chips.Children.Add(c);
        }
        DockPanel.SetDock(chips, Dock.Top);
        DockPanel.SetDock(_sayi, Dock.Bottom); _sayi.Margin = new Thickness(0, 6, 0, 0);
        dock.Children.Add(kpi); dock.Children.Add(bar); dock.Children.Add(chips); dock.Children.Add(_sayi); dock.Children.Add(_tM);
        return dock;
    }

    // ---- satıra sağ tık menüsü
    void ListeMenusu()
    {
        var cm = new ContextMenu();
        void Ekle(string h, Action<Row> a)
        {
            var mi = new MenuItem { Header = h };
            mi.Click += (_, _) => { var r = _tM.Current; if (r != null) a(r); };
            cm.Items.Add(mi);
        }
        Ekle("Aç (cari, vekalet, davalar)", r => Ac(r));
        Ekle("Tahsilat ekle", r => { Ac(r); TahsilatEkle(); });
        Ekle("Düzenle", r => Gecici(r, EditM));
        cm.Items.Add(new Separator());
        Ekle("WhatsApp'ta yaz", r => WhatsApp(r));
        Ekle("E-posta yaz", r => Eposta(r));
        Ekle("Telefonu kopyala", r => Kopyala(r.S("telefon"), "Telefon"));
        Ekle("T.C. numarasını kopyala", r => Kopyala(r.S("tc_no"), "T.C. no"));
        cm.Items.Add(new Separator());
        Ekle("Sil…", r => Gecici(r, DelM));
        _tM.ContextMenu = cm;
        _tM.ContextMenuOpening += (_, e) => { if (_tM.Current == null) e.Handled = true; };
        _tM.PreviewMouseRightButtonDown += (_, e) =>
        {
            var d = e.OriginalSource as DependencyObject;
            while (d != null && d is not DataGridRow) d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            if (d is DataGridRow row) row.IsSelected = true;
        };
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
        foreach (var m in rows) m["vek"] = m.L("vsay") > 0 ? $"✔ {m.L("vsay")} adet" : "—";

        // özet kartları (filtreden bağımsız)
        var bakiyeli = rows.Where(m => m.D("kalan") > 0).ToList();
        _kTop.Text = toplam.ToString();
        _kBak.Text = bakiyeli.Count == 0 ? "Yok" : $"{bakiyeli.Count} • {Utils.FmtMoney(bakiyeli.Sum(m => m.D("kalan")))}";
        _kVek.Text = rows.Count(m => m.L("vsay") == 0).ToString();
        _kDav.Text = rows.Count(m => m.L("dsay") > 0).ToString();
        foreach (var kv in _chip) kv.Value.Aktif(kv.Key == _fl);

        rows = _fl switch
        {
            Fl.Bakiye => bakiyeli,
            Fl.VekYok => rows.Where(m => m.L("vsay") == 0).ToList(),
            Fl.Davali => rows.Where(m => m.L("dsay") > 0).ToList(),
            Fl.DavasiYok => rows.Where(m => m.L("dsay") == 0).ToList(),
            _ => rows,
        };
        var q = Utils.TrLower(_ara.Text.Trim());
        if (q.Length > 0)
            rows = rows.Where(m => Utils.TrLower($"{m.S("ad_soyad")} {m.S("tc_no")} {m.S("telefon")} {m.S("eposta")}").Contains(q)).ToList();
        var cmp = StringComparer.Create(new CultureInfo("tr-TR"), true);
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
        _tM.Load(rows, new() { ["kalan"] = r => r.D("kalan") > 0 ? "warn" : "muted", ["durusma"] = r => Ui.DeadlineKind(r.S("durusma")) });
        _sayi.Text = (q.Length > 0 || _fl != Fl.Tumu) ? $"{rows.Count} / {toplam} müvekkil gösteriliyor" : $"{toplam} müvekkil";
    }

    // ------------------------------------------------------------ detay (müvekkile tıklayınca)
    UIElement BuildDetay()
    {
        var dock = new DockPanel();
        var geri = Ui.Btn("← Müvekkil listesi  (Esc)", "", Geri);
        geri.HorizontalAlignment = HorizontalAlignment.Left; geri.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(geri, Dock.Top);

        // başlık kartı: avatar + kimlik + hızlı eylemler
        var av = new Border { Width = 58, Height = 58, CornerRadius = new CornerRadius(29), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 14, 0), Child = _avatar };
        av.SetResourceReference(Border.BackgroundProperty, "accent");
        _avatar.Foreground = Brushes.White; _avatar.HorizontalAlignment = HorizontalAlignment.Center;
        _dTitle.FontSize = 21; _dSub.TextWrapping = TextWrapping.Wrap; _dNot.Margin = new Thickness(0, 4, 0, 0);
        var info = new StackPanel(); info.Children.Add(_dTitle); info.Children.Add(_dSub); info.Children.Add(_dNot);
        var eylem = Ui.WRow(Ui.Btn("💬 WhatsApp", "", () => { if (_mSel != null) WhatsApp(_mSel); }),
                            Ui.Btn("✉ E-posta", "", () => { if (_mSel != null) Eposta(_mSel); }),
                            Ui.Btn("Düzenle", "", EditM), Ui.Btn("Sil", "Danger", DelM));
        eylem.HorizontalAlignment = HorizontalAlignment.Right; eylem.MaxWidth = 440;
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
        kpi.Children.Add(Kart("Toplam ücret", _dkTop)); kpi.Children.Add(Kart("Alınan", _dkAl)); kpi.Children.Add(Kart("Kalan bakiye", _dkKal));
        kpi.Children.Add(Kart("Açık dava", _dkDav)); kpi.Children.Add(Kart("Sonraki duruşma", _dkDur));
        DockPanel.SetDock(kpi, Dock.Top);

        dock.Children.Add(geri); dock.Children.Add(hdr); dock.Children.Add(kpi);
        _tabF = Ui.Tab("Cari Hesap & Tahsilat", BuildFinans());
        _tabV = Ui.Tab("Vekaletnameler", BuildVekalet());
        _tabD = Ui.Tab("Dava Dosyaları", BuildDavalar());
        var sb = new DockPanel(); sb.Children.Add(_tS);
        _tabS = Ui.Tab("Süreler", sb);
        _tabs.Items.Add(_tabF); _tabs.Items.Add(_tabD); _tabs.Items.Add(_tabS); _tabs.Items.Add(_tabV);
        dock.Children.Add(_tabs);
        return dock;
    }

    void Ac(Row? m)
    {
        if (m == null) return;
        _mSel = Db.One("SELECT * FROM muvekkiller WHERE id=?", m.L("id"));
        if (_mSel == null) return;
        _sid = _mSel.L("id");
        _host.Content = _detay;
        DetayYukle();
    }

    void Geri()
    {
        _sid = 0; _mSel = null;
        _host.Content = _liste;
        Refresh();
    }

    void DetayYukle()
    {
        if (_mSel == null) return;
        var ad = _mSel.S("ad_soyad").Trim();
        var parcalar = ad.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _avatar.Text = parcalar.Length == 0 ? "?" : (parcalar.Length == 1 ? parcalar[0][..1] : parcalar[0][..1] + parcalar[^1][..1]).ToUpper(new CultureInfo("tr-TR"));
        _dTitle.Text = ad;
        _dSub.Text = string.Join("   •   ", new[] { "T.C. " + _mSel.S("tc_no"), _mSel.S("telefon"), _mSel.S("eposta"), _mSel.S("adres") }.Where(s => s.Length > 0 && s != "T.C. "));
        _dNot.Text = _mSel.S("notlar").Trim().Length > 0 ? "📌 " + _mSel.S("notlar").Trim() : "";
        _dNot.Visibility = _dNot.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        LoadVekalet();
        LoadCari();
        LoadHareket();
        LoadDavalar();
        LoadSureler();
        Ozet();
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
        _tabF.Header = $"Cari Hesap & Tahsilat ({_tF.Rows.Count})";
        _tabD.Header = $"Dava Dosyaları ({_tDv.Rows.Count})";
        _tabS.Header = $"Süreler ({_tS.Rows.Count(r => r.S("durum") == "Açık")} açık)";
        _tabV.Header = $"Vekaletnameler ({_tV.Rows.Count})";
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
        var b = Ui.HRow(Ui.Btn("📂 Dosya Kartı", "Ok", DosyaKartinaGit), Ui.Btn("📁 Dava Takibi", "", () => Ctx.Navigate("dava", null)));
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
        if (ReferenceEquals(_host.Content, _liste)) _ara.Focus();
        if (_sid > 0 && ReferenceEquals(_host.Content, _detay))
        {
            _mSel = Db.One("SELECT * FROM muvekkiller WHERE id=?", _sid);
            if (_mSel == null) { _sid = 0; _host.Content = _liste; }
            else DetayYukle();
        }
    }
}
