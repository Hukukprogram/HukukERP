using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HukukErp.Core;
using HukukErp.Services;

namespace HukukErp.UI.Pages;

/// <summary>Dava Takibi: sol tarafta filtrelenebilir dava kartları, sağda seçili davanın aşama çubuğu, süreleri, masrafları ve notları.</summary>
public class DavaPage : BasePage
{
    public static readonly string[] Asamalar = { "Dava Dilekçesi", "Cevap Dilekçesi", "Cevaba Cevap", "İkinci Cevap", "Tahkikat / Delil", "Bilirkişi", "Karar Aşaması", "Karara Çıktı" };
    static readonly (string Key, string Ad, string Dayanak)[] Hmk =
    {
        ("dava_dilekcesi_tarih", "Dava Dilekçesi", "HMK 119"), ("cevap_dilekcesi_tarih", "Cevap Dilekçesi", "HMK 127 (2 hafta)"),
        ("cevaba_cevap_tarih", "Cevaba Cevap", "HMK 136 (2 hafta)"), ("ikinci_cevap_tarih", "İkinci Cevap", "HMK 136 (2 hafta)"),
    };
    static readonly string[] Siralar = { "Duruşma tarihi (yakın → uzak)", "Müvekkil (A → Z)", "Mahkeme (A → Z)", "Aşama (ilerlemiş önce)", "Son eklenen" };
    enum Fl { Derdest, Durusma, Sure, Karar, Tumu }

    /// <summary>Başka sayfalardan (ör. Ajanda) belirli davayı açmak için.</summary>
    public static long Hedef;

    static readonly System.Globalization.CultureInfo Tr = new("tr-TR");

    // liste
    readonly TextBox _ara = new() { Width = 300, ToolTip = "Mahkeme, esas no, müvekkil, karşı taraf veya dava türü ile ara" };
    readonly SearchPick _mc = new() { MinWidth = 210, Placeholder = "Tüm müvekkiller" };
    readonly ComboBox _sira = new() { MinWidth = 220 };
    readonly Dictionary<Fl, TbBtn> _chip = new();
    readonly StackPanel _kartlar = new() { Margin = new Thickness(0, 0, 6, 0) };
    readonly ScrollViewer _kScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = true };
    readonly TextBlock _sayi = Ui.Muted("");
    readonly TextBlock _kDerdest = Ui.Text("–", 22, true), _kDur = Ui.Text("–", 22, true), _kSure = Ui.Text("–", 22, true), _kKarar = Ui.Text("–", 22, true);
    readonly List<long> _mIds = new();
    List<Row> _rows = new();
    Fl _fl = Fl.Derdest;
    long _curId;
    Row? _cur;
    Border? _curB;

    // detay
    readonly ContentControl _sag = new();
    readonly TextBlock _dTitle = Ui.Text("", 20, true, "text", true), _dSub = Ui.Muted("");
    readonly UniformGrid _step = new() { Columns = 8, Margin = new Thickness(0, 0, 0, 8), ToolTip = "Dava aşaması — bir aşamaya tıklayarak güncelleyin" };
    readonly TextBlock _dkDur = Ui.Text("–", 16, true), _dkSure = Ui.Text("–", 16, true), _dkMasraf = Ui.Text("–", 16, true), _dkTur = Ui.Text("–", 16, true);
    readonly DataTable _tTl = new(("Aşama", "asama", null), ("Tarih", "tarih", Fmt.Date), ("Dayanak", "dayanak", null));
    readonly DataTable _tSu = new(("Son Gün", "son_tarih", Fmt.Date), ("Süre / İş", "baslik", null), ("Dayanak", "dayanak", null), ("Durum", "durum", null));
    readonly DataTable _tM = new(("Tarih", "tarih", Fmt.Date), ("Masraf Kalemi", "masraf_kalemi", null), ("Tutar", "tutar", Fmt.Money));
    readonly TextBlock _mSum = Ui.H2("");
    readonly TextBlock _notDurum = Ui.Muted("");
    readonly TextBox _notes = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalContentAlignment = VerticalAlignment.Top,
    };
    TabItem _tabS = null!, _tabM = null!, _tabKart = null!;
    readonly TabControl _tabs = new();
    readonly DosyaKartiPage _kart;
    long _kartId;
    UIElement _detay = null!, _bos = null!;
    long _notId;
    bool _notKirli, _notYukleniyor, _busy;

    public DavaPage(AppCtx ctx) : base(ctx)
    {
        _kart = new DosyaKartiPage(ctx, true);
        Top(Ui.PageHeader("Dosyalar", "Soldan dosya seçin (↑ ↓ ile gezin, Ctrl+F ile ara); sağda Dosya Kartı, aşama, süreler, masraflar ve notlar açılır"));

        // özet kartları
        var kpi = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -10, 12) };
        kpi.Children.Add(Kart("Derdest dava", _kDerdest)); kpi.Children.Add(Kart("7 gün içinde duruşma", _kDur));
        kpi.Children.Add(Kart("Süresi yaklaşan / geciken", _kSure)); kpi.Children.Add(Kart("Karara çıkan", _kKarar));
        // (üstteki özet kartları kaldırıldı: sayılar süzgeç düğmelerinin üzerinde)

        // araç çubuğu
        _sira.Items.Clear(); foreach (var s in Siralar) _sira.Items.Add(s);
        _sira.SelectedIndex = 0;
        var bar = Ui.WRow(Ui.Btn("＋ Dava", "Primary", AddD), Ui.Muted("Ara:", false), _ara, Ui.Muted("Müvekkil:", false), _mc, Ui.Muted("Sırala:", false), _sira);
        Top(bar, 2);
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (f, ad) in new[] { (Fl.Derdest, "Derdest"), (Fl.Durusma, "Duruşması 14 gün içinde"), (Fl.Sure, "Süresi 7 gün içinde"), (Fl.Karar, "Karara çıkanlar"), (Fl.Tumu, "Tümü") })
        {
            var ff = f;
            var c = new TbBtn("", ad, () => { _fl = ff; Yukle(); });
            _chip[f] = c; chips.Children.Add(c);
        }
        Top(chips, 0);

        // sol: kartlar
        _kScroll.Content = _kartlar;
        _kScroll.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter && _cur != null) { DosyaKarti(); e.Handled = true; } };
        var left = new Card();
        left.Bottom(_sayi, 6);
        left.Fill(_kScroll);

        // sağ: detay
        _detay = BuildDetay();
        var bos = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        bos.Children.Add(Ui.Text("Bir dava seçin", 18, true));
        var bm = Ui.Muted("Soldaki listeden bir dava kartına tıklayın."); bm.HorizontalAlignment = HorizontalAlignment.Center; bm.Margin = new Thickness(0, 6, 0, 0);
        bos.Children.Add(bm);
        _bos = bos;
        _sag.Content = _bos;

        Fill(Ui.HSplit(left, _sag, 2, 3));

        _ara.TextChanged += (_, _) => { if (!_busy) Yukle(); };
        _ara.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { _ara.Text = ""; e.Handled = true; }
            else if (e.Key == Key.Down) { Gez(1, true); e.Handled = true; }
            else if (e.Key == Key.Up) { Gez(-1, true); e.Handled = true; }
        };
        _kScroll.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down) { Gez(1, false); e.Handled = true; }
            else if (e.Key == Key.Up) { Gez(-1, false); e.Handled = true; }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { _ara.Focus(); _ara.SelectAll(); e.Handled = true; }
        };
        _mc.SelectionChanged += (_, _) => { if (!_busy) Yukle(); };
        _sira.SelectionChanged += (_, _) => { if (!_busy) Yukle(); };
        _tSu.MouseDoubleClick += (_, _) => Ctx.Navigate("is_listesi", null);
    }

    // ------------------------------------------------------------ ortak bileşenler
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

    static Pill Etiket(string t, string kind) { var p = new Pill(t, kind); p.Margin = new Thickness(0, 0, 6, 4); return p; }

    static int AsamaNo(string a) { var i = Array.IndexOf(Asamalar, a); return i < 0 ? 0 : i; }

    static string GunMetni(int? k) => k == null ? "" : k < 0 ? $"{-k} gün geçti" : k == 0 ? "bugün" : k == 1 ? "yarın" : $"{k} gün sonra";

    static string Kind(int? k) => k == null ? "muted" : k < 0 ? "danger" : k <= 3 ? "danger" : k <= 14 ? "warn" : "info";

    // ------------------------------------------------------------ detay arayüzü
    UIElement BuildDetay()
    {
        var dock = new DockPanel();

        // başlık kartı
        var info = new StackPanel(); info.Children.Add(_dTitle); info.Children.Add(_dSub);
        var eylem = Ui.WRow(Ui.Btn("✏ Düzenle", "", EditD), Ui.Btn("Sil", "Danger", DelD));
        eylem.HorizontalAlignment = HorizontalAlignment.Right; eylem.MaxWidth = 300;
        var hg = new Grid();
        hg.ColumnDefinitions.Add(new ColumnDefinition()); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(eylem, 1);
        hg.Children.Add(info); hg.Children.Add(eylem);
        var hdr = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 10), Child = hg };
        hdr.SetResourceReference(Border.BackgroundProperty, "surface");
        hdr.SetResourceReference(Border.BorderBrushProperty, "border");
        DockPanel.SetDock(hdr, Dock.Top);

        // aşama çubuğu
        var stepBaslik = Ui.Muted("Dava aşaması — bir aşamaya tıklayarak güncelleyin", false);
        stepBaslik.Margin = new Thickness(2, 0, 0, 4);
        DockPanel.SetDock(stepBaslik, Dock.Top);
        DockPanel.SetDock(_step, Dock.Top);

        // mini kartlar
        var kpi = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -10, 10) };
        kpi.Children.Add(Kart("Sonraki duruşma", _dkDur)); kpi.Children.Add(Kart("Açık süre", _dkSure));
        kpi.Children.Add(Kart("Toplam masraf", _dkMasraf)); kpi.Children.Add(Kart("Dava türü", _dkTur));
        DockPanel.SetDock(kpi, Dock.Top);

        dock.Children.Add(hdr); dock.Children.Add(_step); dock.Children.Add(kpi);

        var tabs = _tabs;
        // Dosya Kartı (zaman çizelgesi, evrak, mali kayıtlar): yalnızca bu sekme açıkken yüklenir
        _tabKart = Ui.Tab("Dosya Kartı", _kart);
        tabs.Items.Add(_tabKart);
        tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, tabs)) KartYukle(); };
        // HMK
        var tw = new DockPanel();
        var upd = Ui.Btn("Aşama Tarihlerini Güncelle", "", EditStages);
        upd.HorizontalAlignment = HorizontalAlignment.Left; upd.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(upd, Dock.Bottom);
        tw.Children.Add(upd); tw.Children.Add(_tTl);
        tabs.Items.Add(Ui.Tab("HMK Aşamaları", tw));
        // süreler
        var sw = new DockPanel();
        var sb = Ui.HRow(Ui.Btn("⏱ İş Listesine git", "", () => Ctx.Navigate("is_listesi", null)), Ui.Muted("Süre eklemek ve hesaplamak için İş Listesi sayfasını kullanın.", false));
        sb.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(sb, Dock.Bottom);
        sw.Children.Add(sb); sw.Children.Add(_tSu);
        _tabS = Ui.Tab("Süreler", sw); tabs.Items.Add(_tabS);
        // masraflar
        var mw = new DockPanel();
        var mb = Ui.HRow(Ui.Btn("＋ Masraf", "Primary", AddM), Ui.Btn("Düzenle", "", EditM), Ui.Btn("Sil", "Danger", DelM));
        mb.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(mb, Dock.Bottom);
        DockPanel.SetDock(_mSum, Dock.Bottom); _mSum.Margin = new Thickness(0, 8, 0, 0);
        mw.Children.Add(mb); mw.Children.Add(_mSum); mw.Children.Add(_tM);
        _tabM = Ui.Tab("Masraflar", mw); tabs.Items.Add(_tabM);
        // notlar
        var nw = new DockPanel();
        var nbar = Ui.HRow(Ui.Btn("Notları Kaydet", "Primary", SaveNotes), _notDurum);
        nbar.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(nbar, Dock.Bottom);
        // tema renkleri açıkça verilir (yazılan not her zaman okunur olsun)
        _notes.SetResourceReference(Control.ForegroundProperty, "text");
        _notes.SetResourceReference(Control.BackgroundProperty, "surface2");
        _notes.SetResourceReference(Control.BorderBrushProperty, "border");
        _notes.SetResourceReference(TextBox.CaretBrushProperty, "text");
        _notes.Padding = new Thickness(8);
        _notes.TextChanged += (_, _) => { if (!_notYukleniyor && _notId > 0) { _notKirli = true; _notDurum.Text = "kaydedilmedi…"; } };
        _notes.LostFocus += (_, _) => NotlariYaz();
        Unloaded += (_, _) => NotlariYaz();
        nw.Children.Add(nbar); nw.Children.Add(_notes);
        tabs.Items.Add(Ui.Tab("Genel Notlar", nw));
        dock.Children.Add(tabs);
        return dock;
    }

    // ------------------------------------------------------------ yükleme
    public override void Refresh()
    {
        NotlariYaz();
        if (Hedef == 0 && DosyaKartiPage.Hedef > 0) { Hedef = DosyaKartiPage.Hedef; DosyaKartiPage.Hedef = 0; }
        if (Hedef > 0)
        {
            _curId = Hedef; Hedef = 0;
            _fl = Fl.Tumu; _busy = true; _ara.Text = ""; _mc.SelectedIndex = _mc.Items.Count > 0 ? 0 : -1; _busy = false;
        }
        MuvekkilleriDoldur();
        Yukle();
    }

    void MuvekkilleriDoldur()
    {
        var onceki = _mc.SelectedIndex >= 0 && _mc.SelectedIndex < _mIds.Count ? _mIds[_mc.SelectedIndex] : 0;
        _busy = true;
        try
        {
            _mc.Items.Clear(); _mIds.Clear();
            _mc.Items.Add(new ComboItem { Value = 0L, Label = "Tüm müvekkiller" }); _mIds.Add(0);
            var cmp = StringComparer.Create(Tr, true);
            foreach (var m in Db.Query("SELECT id, ad_soyad FROM muvekkiller").OrderBy(x => x.S("ad_soyad"), cmp))
            { _mc.Items.Add(new ComboItem { Value = m.L("id"), Label = m.S("ad_soyad") }); _mIds.Add(m.L("id")); }
            var ix = _mIds.IndexOf(onceki);
            _mc.SelectedIndex = ix < 0 ? 0 : ix;
        }
        finally { _busy = false; }
    }

    void Yukle()
    {
        NotlariYaz();
        _rows = Db.Query(
            "SELECT d.*, COALESCE(m.ad_soyad,'—') AS muvekkil, " +
            "(SELECT MIN(s.son_tarih) FROM sureler s WHERE s.dava_id=d.id AND s.durum='Açık') AS sure_son, " +
            "(SELECT COUNT(*) FROM sureler s WHERE s.dava_id=d.id AND s.durum='Açık') AS sure_say, " +
            "(SELECT COALESCE(SUM(x.tutar),0) FROM dava_masraflari x WHERE x.dava_id=d.id) AS masraf " +
            "FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id");

        bool Acik(Row r) => r.S("asama_durum") != "Karara Çıktı";
        int? DurK(Row r) => Utils.DaysLeft(r.S("durusma_tarihi"));
        bool DurGelecek(Row r) => r.S("durusma_tarihi").Length > 0 && DurK(r) >= 0;
        // özet kartları (süzgeçten bağımsız)
        _kDerdest.Text = _rows.Count(Acik).ToString();
        _kDur.Text = _rows.Count(r => Acik(r) && DurGelecek(r) && DurK(r) <= 7).ToString();
        _kSure.Text = _rows.Count(r => Acik(r) && r.S("sure_son").Length > 0 && Utils.DaysLeft(r.S("sure_son")) <= 7).ToString();
        _kKarar.Text = _rows.Count(r => !Acik(r)).ToString();
        _chip[Fl.Derdest].Metin($"Derdest {_kDerdest.Text}");
        _chip[Fl.Durusma].Metin($"Duruşması 14 gün içinde {_rows.Count(r => Acik(r) && DurGelecek(r) && DurK(r) <= 14)}");
        _chip[Fl.Sure].Metin($"Süresi 7 gün içinde {_kSure.Text}");
        _chip[Fl.Karar].Metin($"Karara çıkanlar {_kKarar.Text}");
        _chip[Fl.Tumu].Metin($"Tümü {_rows.Count}");
        foreach (var kv in _chip) kv.Value.Aktif(kv.Key == _fl);

        var list = _rows.AsEnumerable();
        var mid = _mc.SelectedIndex >= 0 && _mc.SelectedIndex < _mIds.Count ? _mIds[_mc.SelectedIndex] : 0;
        if (mid > 0) list = list.Where(r => r.L("muvekkil_id") == mid);
        list = _fl switch
        {
            Fl.Derdest => list.Where(Acik),
            Fl.Durusma => list.Where(r => Acik(r) && DurGelecek(r) && DurK(r) <= 14),
            Fl.Sure => list.Where(r => Acik(r) && r.S("sure_son").Length > 0 && Utils.DaysLeft(r.S("sure_son")) <= 7),
            Fl.Karar => list.Where(r => !Acik(r)),
            _ => list,
        };
        var q = Utils.TrLower(_ara.Text.Trim());
        if (q.Length > 0)
            list = list.Where(r => Utils.TrLower($"{r.S("mahkeme_adi")} {r.S("esas_no")} {r.S("muvekkil")} {r.S("karsi_taraf")} {r.S("dava_turu")}").Contains(q));
        var cmp = StringComparer.Create(Tr, true);
        var sirali = _sira.SelectedIndex switch
        {
            1 => list.OrderBy(r => r.S("muvekkil"), cmp).ThenBy(r => r.S("mahkeme_adi"), cmp),
            2 => list.OrderBy(r => r.S("mahkeme_adi"), cmp).ThenBy(r => r.S("esas_no"), cmp),
            3 => list.OrderByDescending(r => AsamaNo(r.S("asama_durum"))).ThenBy(r => r.S("mahkeme_adi"), cmp),
            4 => list.OrderByDescending(r => r.L("id")),
            // duruşması gelecekte olanlar önce (yakın → uzak), sonra tarihsizler, en sonda geçmiş duruşmalar
            _ => list.OrderBy(r => r.S("durusma_tarihi").Length == 0 ? 2 : (DurGelecek(r) ? 0 : 1)).ThenBy(r => r.S("durusma_tarihi"), StringComparer.Ordinal),
        };
        var goster = sirali.ToList();
        _sayi.Text = $"{goster.Count} / {_rows.Count} dava gösteriliyor";

        _kartlar.Children.Clear();
        _curB = null;
        if (goster.Count == 0)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 40, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(Ui.Text(_rows.Count == 0 ? "Henüz dava yok" : "Bu süzgeçle eşleşen dava yok", 18, true));
            var alt = Ui.Muted(_rows.Count == 0 ? "İlk davanızı ekleyerek başlayın." : "Aramayı, müvekkili veya üstteki süzgeci değiştirin.");
            alt.Margin = new Thickness(0, 6, 0, 12); alt.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(alt);
            if (_rows.Count == 0) { var bt = Ui.Btn("＋ Dava ekle", "Primary", AddD); bt.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(bt); }
            _kartlar.Children.Add(sp);
        }
        else
            foreach (var r in goster) _kartlar.Children.Add(KartYap(r));

        // seçimi koru; kalmadıysa ilkini seç
        var sec = goster.FirstOrDefault(r => r.L("id") == _curId) ?? goster.FirstOrDefault();
        if (sec != null)
        {
            _cur = sec; _curId = sec.L("id");
            _curB = _kartlar.Children.OfType<Border>().FirstOrDefault(b => b.Tag is long t && t == _curId);
            Vurgula();
            DetayYukle();
        }
        else { _cur = null; _curId = 0; DetayYukle(); }
    }

    // ------------------------------------------------------------ dava kartı
    Border KartYap(Row r)
    {
        var id = r.L("id");
        var ust = new Grid();
        ust.ColumnDefinitions.Add(new ColumnDefinition()); ust.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ad = Ui.Text(r.S("mahkeme_adi"), 14, true); ad.TextTrimming = TextTrimming.CharacterEllipsis;
        var es = Ui.Muted(r.S("esas_no"), false); es.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(es, 1);
        ust.Children.Add(ad); ust.Children.Add(es);

        var taraf = $"{r.S("muvekkil")}" + (r.S("karsi_taraf").Length > 0 ? $"   ⚔   {r.S("karsi_taraf")}" : "");
        var tl = Ui.Muted(taraf, false); tl.TextTrimming = TextTrimming.CharacterEllipsis; tl.Margin = new Thickness(0, 3, 0, 8);

        // aşama ilerleme çubuğu
        int no = AsamaNo(r.S("asama_durum"));
        var pg = new UniformGrid { Columns = Asamalar.Length, Height = 5, Margin = new Thickness(0, 0, 0, 8) };
        for (int i = 0; i < Asamalar.Length; i++)
        {
            var seg = new Border { CornerRadius = new CornerRadius(2.5), Margin = new Thickness(0, 0, i == Asamalar.Length - 1 ? 0 : 3, 0) };
            seg.SetResourceReference(Border.BackgroundProperty, i <= no ? (no == Asamalar.Length - 1 ? "ok" : "accent") : "border");
            pg.Children.Add(seg);
        }

        var et = new WrapPanel();
        et.Children.Add(Etiket(r.S("asama_durum"), r.S("asama_durum") == "Karara Çıktı" ? "ok" : "info"));
        var dk = Utils.DaysLeft(r.S("durusma_tarihi"));
        if (r.S("durusma_tarihi").Length > 0 && dk != null && dk >= 0) et.Children.Add(Etiket($"Duruşma {Utils.FmtDate(r.S("durusma_tarihi"))} • {GunMetni(dk)}", Kind(dk)));
        if (r.L("sure_say") > 0)
        {
            var sk = Utils.DaysLeft(r.S("sure_son"));
            et.Children.Add(Etiket($"{r.L("sure_say")} açık süre • en yakını {GunMetni(sk)}", sk != null && sk <= 3 ? "danger" : (sk != null && sk <= 7 ? "warn" : "muted")));
        }

        var sp = new StackPanel();
        sp.Children.Add(ust); sp.Children.Add(tl); sp.Children.Add(pg); sp.Children.Add(et);
        var b = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(14, 12, 14, 8), Margin = new Thickness(0, 0, 0, 10), Child = sp, Cursor = Cursors.Hand, Tag = id };
        b.SetResourceReference(Border.BackgroundProperty, "surface");
        b.SetResourceReference(Border.BorderBrushProperty, "border");
        b.MouseEnter += (_, _) => { if (!ReferenceEquals(_curB, b)) b.SetResourceReference(Border.BorderBrushProperty, "muted"); };
        b.MouseLeave += (_, _) => { if (!ReferenceEquals(_curB, b)) b.SetResourceReference(Border.BorderBrushProperty, "border"); };
        b.MouseLeftButtonDown += (_, e) => { Sec(b, r); if (e.ClickCount >= 2) { e.Handled = true; DosyaKarti(); } };
        b.MouseRightButtonDown += (_, _) => Sec(b, r);
        b.ContextMenu = Menu(r);
        return b;
    }

    ContextMenu Menu(Row r)
    {
        var cm = new ContextMenu();
        void Ekle(string h, Action a) { var mi = new MenuItem { Header = h }; mi.Click += (_, _) => { if (_curId == r.L("id")) a(); }; cm.Items.Add(mi); }
        Ekle("Dosya Kartını aç", DosyaKarti);
        Ekle("Düzenle", EditD);
        Ekle("Aşama tarihlerini güncelle", EditStages);
        Ekle("Masraf ekle", AddM);
        cm.Items.Add(new Separator());
        Ekle("Sil…", DelD);
        return cm;
    }

    void Gez(int yon, bool aramaOdakta)
    {
        var l = _kartlar.Children.OfType<Border>().Where(x => x.Tag is long).ToList();
        if (l.Count == 0) return;
        int i = _curB == null ? 0 : l.IndexOf(_curB);
        i = Math.Clamp(i < 0 ? 0 : i + yon, 0, l.Count - 1);
        var b = l[i];
        var r = _rows.FirstOrDefault(x => x.L("id") == (long)b.Tag);
        if (r == null) return;
        Sec(b, r);
        b.BringIntoView();
        if (aramaOdakta) _ara.Focus();
    }

    void Vurgula()
    {
        foreach (var b in _kartlar.Children.OfType<Border>().Where(x => x.Tag is long))
        {
            bool sec = ReferenceEquals(b, _curB);
            b.SetResourceReference(Border.BackgroundProperty, sec ? "sel" : "surface");
            b.SetResourceReference(Border.BorderBrushProperty, sec ? "accent" : "border");
        }
    }

    void Sec(Border b, Row r)
    {
        if (_curId == r.L("id") && ReferenceEquals(_curB, b)) { _kScroll.Focus(); return; }
        NotlariYaz();
        _curB = b; _cur = r; _curId = r.L("id");
        Vurgula();
        DetayYukle();
        _kScroll.Focus();
    }

    // ------------------------------------------------------------ detay
    void DetayYukle()
    {
        NotlariYaz();   // önceki davanın yazılmamış notu varsa önce kaydedilir
        var d = _cur == null ? null : Db.One("SELECT d.*, COALESCE(m.ad_soyad,'—') AS muvekkil FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id WHERE d.id=?", _cur.L("id"));
        _notYukleniyor = true;
        try
        {
            if (d == null)
            {
                _sag.Content = _bos;
                _notId = 0; _notes.Text = ""; _notDurum.Text = "";
                return;
            }
            _cur = d;
            _sag.Content = _detay;
            _dTitle.Text = $"{d.S("mahkeme_adi")} • {d.S("esas_no")}";
            _dSub.Text = $"Müvekkil: {d.S("muvekkil")}" + (d.S("karsi_taraf").Length > 0 ? $"   •   Karşı taraf: {d.S("karsi_taraf")}" : "");
            StepYaz(d);

            var dk = Utils.DaysLeft(d.S("durusma_tarihi"));
            _dkDur.Text = d.S("durusma_tarihi").Length == 0 ? "Yok" : $"{Utils.FmtDate(d.S("durusma_tarihi"))} ({(dk >= 0 ? GunMetni(dk) : "geçti")})";
            _dkTur.Text = d.S("dava_turu").Length > 0 ? d.S("dava_turu") : "—";

            _tTl.Load(Hmk.Select(h => new Row { ["asama"] = h.Ad, ["tarih"] = d.S(h.Key), ["dayanak"] = h.Dayanak }).ToList());
            var su = Db.Query("SELECT * FROM sureler WHERE dava_id=? ORDER BY durum='Açık' DESC, son_tarih", d.L("id"));
            _tSu.Load(su, new() { ["son_tarih"] = r => r.S("durum") == "Açık" ? Ui.DeadlineKind(r.S("son_tarih")) : "muted" });
            var acik = su.Where(x => x.S("durum") == "Açık").OrderBy(x => x.S("son_tarih")).ToList();
            _dkSure.Text = acik.Count == 0 ? "Yok" : $"{acik.Count} • {Utils.FmtDate(acik[0].S("son_tarih"))}";
            _tabS.Header = $"Süreler ({acik.Count} açık)";
            var ms = Db.Query("SELECT * FROM dava_masraflari WHERE dava_id=? ORDER BY tarih DESC, id DESC", d.L("id"));
            _tM.Load(ms);
            var top = ms.Sum(m => m.D("tutar"));
            _mSum.Text = $"Toplam masraf: {Utils.FmtMoney(top)}";
            _dkMasraf.Text = Utils.FmtMoney(top);
            _tabM.Header = $"Masraflar ({ms.Count})";
            _notId = d.L("id");
            _notes.Text = d.S("genel_notlar");
            _notKirli = false; _notDurum.Text = "";
            _kartId = 0;
            KartYukle();
        }
        finally { _notYukleniyor = false; }
    }

    void StepYaz(Row d)
    {
        _step.Children.Clear();
        int no = AsamaNo(d.S("asama_durum"));
        for (int i = 0; i < Asamalar.Length; i++)
        {
            int ii = i;
            var num = new TextBlock { Text = (i + 1).ToString(), FontSize = 15, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
            var ad = new TextBlock { Text = Asamalar[i], FontSize = 10.5, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
            var sp = new StackPanel(); sp.Children.Add(num); sp.Children.Add(ad);
            var b = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(4, 7, 4, 7), Margin = new Thickness(0, 0, i == Asamalar.Length - 1 ? 0 : 5, 0), Child = sp, Cursor = Cursors.Hand, ToolTip = "Aşamayı “" + Asamalar[i] + "” yap" };
            if (i < no) { b.Background = Theme.Alpha("ok", 40); b.SetResourceReference(Border.BorderBrushProperty, "ok"); num.SetResourceReference(TextBlock.ForegroundProperty, "ok"); ad.SetResourceReference(TextBlock.ForegroundProperty, "muted"); }
            else if (i == no) { b.SetResourceReference(Border.BackgroundProperty, "accent"); b.SetResourceReference(Border.BorderBrushProperty, "accent"); num.Foreground = Brushes.White; ad.Foreground = Brushes.White; }
            else { b.SetResourceReference(Border.BackgroundProperty, "surface2"); b.SetResourceReference(Border.BorderBrushProperty, "border"); num.SetResourceReference(TextBlock.ForegroundProperty, "muted"); ad.SetResourceReference(TextBlock.ForegroundProperty, "muted"); }
            b.MouseLeftButtonUp += (_, _) => AsamaDegistir(ii);
            _step.Children.Add(b);
        }
    }

    void AsamaDegistir(int i)
    {
        var d = _cur;
        if (d == null || AsamaNo(d.S("asama_durum")) == i && d.S("asama_durum") == Asamalar[i]) return;
        if (i == Asamalar.Length - 1 && !Ui.Confirm(this, "Dava “Karara Çıktı” olarak işaretlensin mi? (Derdest listesinden çıkar.)")) return;
        Db.Update("davalar", d.L("id"), new Dictionary<string, object?> { ["asama_durum"] = Asamalar[i] });
        Ctx.Status($"Aşama güncellendi: {Asamalar[i]}");
        Yukle();
    }

    /// <summary>Yazılmış ama kaydedilmemiş notu veritabanına yazar.</summary>
    void NotlariYaz()
    {
        if (!_notKirli || _notId <= 0) return;
        _notKirli = false;
        var txt = _notes.Text;
        Db.Update("davalar", _notId, new Dictionary<string, object?> { ["genel_notlar"] = txt });
        foreach (var r in _rows) if (r.L("id") == _notId) r["genel_notlar"] = txt;
        if (_cur != null && _cur.L("id") == _notId) _cur["genel_notlar"] = txt;
        _notDurum.Text = $"✔ Kaydedildi {DateTime.Now:HH:mm}";
    }

    // ------------------------------------------------------------ dava işlemleri
    List<Field> DFields()
    {
        var ms = new List<(object?, string)> { (null, "— seçilmedi —") };
        ms.AddRange(Db.Query("SELECT id, ad_soyad, tc_no FROM muvekkiller ORDER BY ad_soyad").Select(m => ((object?)m.L("id"), $"{m.S("ad_soyad")} ({m.S("tc_no")})")));
        return new()
        {
            new("muvekkil_id", "Müvekkil", "combo", ms), new("mahkeme_adi", "Mahkeme", required: true), new("esas_no", "Esas No", required: true),
            new("dava_turu", "Dava Türü"), new("karsi_taraf", "Karşı Taraf"), new("durusma_tarihi", "Duruşma Tarihi", "date"),
            new("asama_durum", "Aşama", "combo", Field.Of(Asamalar)),
        };
    }

    public void YeniDava() => AddD();

    /// <summary>Seçili dosyanın Dosya Kartı sekmesini açar.</summary>
    void DosyaKarti()
    {
        if (_cur == null) { Ui.Info(this, "Önce bir dosya seçin."); return; }
        _tabs.SelectedItem = _tabKart;
        KartYukle();
    }

    /// <summary>Dışarıdan (ör. Dosya Kartı bağlantılarından) gelinirken Dosya Kartı sekmesini seçer.</summary>
    public void KartSekmesi() { if (_cur != null) _tabs.SelectedItem = _tabKart; KartYukle(); }

    void KartYukle()
    {
        if (_cur == null || !ReferenceEquals(_tabs.SelectedItem, _tabKart)) return;
        var id = _cur.L("id");
        if (id == _kartId) return;
        _kartId = id;
        _kart.AcDava(id);
    }

    void AddD()
    {
        var mid0 = _mc.SelectedIndex >= 0 && _mc.SelectedIndex < _mIds.Count ? _mIds[_mc.SelectedIndex] : 0;
        var v = FormDialog.Ask(this, "Yeni Dava", DFields(), mid0 > 0 ? new Dictionary<string, object?> { ["muvekkil_id"] = mid0 } : null);
        if (v == null) return;
        long mid = v.TryGetValue("muvekkil_id", out var mv) && mv != null ? Convert.ToInt64(mv) : 0;
        var ad = mid > 0 ? Db.One("SELECT ad_soyad FROM muvekkiller WHERE id=?", mid)?.S("ad_soyad") : null;
        var uyari = DosyaEsleme.Catisma(Db, ad, v.TryGetValue("karsi_taraf", out var kt) ? kt as string : null, mid);
        if (uyari.Count > 0 && !Ui.Confirm(this, "Olası çıkar çatışması:\n• " + string.Join("\n• ", uyari) + "\n\nYine de kaydedilsin mi?")) return;
        var yeni = Db.Insert("davalar", v);
        _curId = yeni;
        if (_fl == Fl.Karar) _fl = Fl.Derdest;
        Yukle();
    }

    void EditD()
    {
        var d = _cur;
        if (d == null) return;
        var v = FormDialog.Ask(this, "Davayı Düzenle", DFields(), d);
        if (v != null) { Db.Update("davalar", d.L("id"), v); Yukle(); }
    }

    void DelD()
    {
        var d = _cur;
        if (d != null && Ui.Confirm(this, "Dava ve masrafları silinsin mi? (Çöp Kutusu'ndan geri alınabilir)")) { Db.Delete("davalar", d.L("id")); _curId = 0; Yukle(); }
    }

    void EditStages()
    {
        var d = _cur;
        if (d == null) { Ui.Info(this, "Önce dava seçin."); return; }
        var fields = Hmk.Select(h => new Field(h.Key, h.Ad, "date")).ToList();
        fields.Add(new Field("asama_durum", "Güncel Aşama", "combo", Field.Of(Asamalar)));
        var v = FormDialog.Ask(this, "HMK Aşamaları", fields, d);
        if (v != null) { Db.Update("davalar", d.L("id"), v); Yukle(); }
    }

    void SaveNotes()
    {
        if (_cur == null) { Ui.Info(this, "Önce bir dava seçin."); return; }
        _notKirli = true;
        NotlariYaz();
        Ctx.Status("Notlar kaydedildi");
    }

    // ---- masraf
    void AddM()
    {
        var d = _cur;
        if (d == null) { Ui.Info(this, "Önce dava seçin."); return; }
        var v = FormDialog.Ask(this, "Yeni Masraf", new List<Field>
        {
            new("tarih", "Tarih", "date", def: Utils.TodayIso()), new("masraf_kalemi", "Masraf Kalemi", required: true), new("tutar", "Tutar", "money"),
        });
        if (v == null) return;
        v["dava_id"] = d.L("id");
        Db.Insert("dava_masraflari", v);
        DetayYukle();
    }

    void EditM()
    {
        var m = _tM.Current;
        if (m == null) return;
        var v = FormDialog.Ask(this, "Masrafı Düzenle", new List<Field>
        {
            new("tarih", "Tarih", "date"), new("masraf_kalemi", "Masraf Kalemi", required: true), new("tutar", "Tutar", "money"),
        }, m);
        if (v != null) { Db.Update("dava_masraflari", m.L("id"), v); DetayYukle(); }
    }

    void DelM()
    {
        var m = _tM.Current;
        if (m != null && Ui.Confirm(this, "Masraf silinsin mi?")) { Db.Delete("dava_masraflari", m.L("id")); DetayYukle(); }
    }
}
