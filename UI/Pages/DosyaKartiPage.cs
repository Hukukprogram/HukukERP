using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HukukErp.Core;
using HukukErp.Services;

namespace HukukErp.UI.Pages;

/// <summary>Dosya kartı: bir davaya bağlı her şey (aşamalar, süreler, tebligatlar, duruşma notları, müvekkil bilgilendirmeleri, zaman, tahsilat, masraf, emanet, evrak) tek ekranda ve zaman çizelgesinde.</summary>
public class DosyaKartiPage : BasePage
{
    /// <summary>Başka sayfalardan "Dosya Kartı" ile gelinirken açılacak dava.</summary>
    public static long Hedef;


    static readonly string[] Kanallar = { "Telefon", "WhatsApp", "SMS", "E-posta", "Yüz yüze", "Diğer" };
    static readonly string[] Gruplar = { "Tümü", "Süre ve duruşma", "Tebligat", "Mali", "Evrak", "Notlar" };
    static readonly (string Anahtar, string Ad)[] HmkAlan =
    {
        ("durusma_tarihi", "Duruşma"),
    };

    readonly ComboBox _combo = new() { MinWidth = 380, Margin = new Thickness(0, 0, 8, 0) };
    readonly SearchPick _mcombo = new() { MinWidth = 260, Margin = new Thickness(0, 0, 14, 0), Placeholder = "— Tüm müvekkiller —" };
    readonly StackPanel _serit = new() { Orientation = Orientation.Horizontal };
    readonly TextBlock _seritBaslik = Ui.Muted("", false);
    static TextBlock KV() => Ui.Text("—", 18, true);
    readonly TextBlock _kSure = KV(), _kDur = KV(), _kTeb = KV(), _kCari = KV(), _kMas = KV(), _kUcret = KV(), _kOde = KV();
    readonly TextBlock _dTitle = Ui.Text("", 22, true, "text", true), _dSub = Ui.Muted("");
    readonly WrapPanel _dEtiket = new() { Margin = new Thickness(0, 8, 0, 0) };
    readonly UniformGrid _step = new() { Columns = 8, Margin = new Thickness(0, 0, 0, 12) };
    readonly StackPanel _tl = new();
    readonly Dictionary<string, TbBtn> _chip = new();
    string _grup = "Tümü";
    List<Row> _kayitlar = new();
    Row? _sec; Border? _secB;
    bool _busy;

    readonly bool _gomulu;

    /// <param name="gomulu">true: Dosyalar sayfasında sekme olarak gömülür (üst başlık, müvekkil/dava seçici ve aşama çubuğu gösterilmez; dava dışarıdan AcDava ile verilir).</param>
    public DosyaKartiPage(AppCtx ctx, bool gomulu = false) : base(ctx, !gomulu)
    {
        _gomulu = gomulu;
        if (!gomulu) Top(Ui.PageHeader("Dosya Kartı", "Bir davaya bağlı her şey tek ekranda • evrakı pencereye sürükleyip bırakabilirsiniz"));
        _combo.SelectionChanged += (_, _) =>
        {
            if (_busy) return;
            _busy = true;
            try { SeritYaz(); } finally { _busy = false; }
            Yukle();
        };
        _mcombo.SelectionChanged += (_, _) => { if (!_busy) { _busy = true; try { DavaDoldur(0); } finally { _busy = false; } Yukle(); } };

        // ---- seçiciler
        var sec = Ui.WRow(Ui.Muted("Müvekkil:", false), _mcombo, Ui.Muted("Dava:", false), _combo);
        var seritScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _serit, Margin = new Thickness(0, 0, 0, 10) };

        // ---- başlık kartı (yalnızca tek başına sayfa olarak kullanılırken; gömülü modda hiç oluşturulmaz)
        var eylem = Ui.WRow(Ui.Btn("⚖ Duruşma Notu", "Primary", DurusmaNotu), Ui.Btn("＋ Süreli İş", "Ok", SureEkle), Ui.Btn("⋯ Git", "", GitMenu));
        Border? hdr = null;
        if (!gomulu)
        {
            var info = new StackPanel(); info.Children.Add(_dTitle); info.Children.Add(_dSub); info.Children.Add(_dEtiket);
            eylem.HorizontalAlignment = HorizontalAlignment.Right; eylem.MaxWidth = 420;
            var hg = new Grid();
            hg.ColumnDefinitions.Add(new ColumnDefinition()); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(eylem, 1);
            hg.Children.Add(info); hg.Children.Add(eylem);
            hdr = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 10), Child = hg };
            hdr.SetResourceReference(Border.BackgroundProperty, "surface");
            hdr.SetResourceReference(Border.BorderBrushProperty, "border");
        }

        // ---- araç şeridi
        var araclar = Ui.WRow(Ui.Btn("⚖ İstinaf Bilgisi", "", IstinafBilgi),
                              Ui.Btn("📎 Evrak Ekle…", "", EvrakSec), Ui.Btn("🔒 Dosyayı Kapat / Aç", "", KapatAc), Ui.Btn("Seçili Kaydı Sil", "Danger", KayitSil));
        araclar.Margin = new Thickness(0, 0, 0, 6);

        // ---- özet kartları
        var kpi = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -10, 10) };
        kpi.Children.Add(Mini("Açık süre", _kSure)); kpi.Children.Add(Mini("Sonraki duruşma", _kDur)); kpi.Children.Add(Mini("Tebligat", _kTeb));
        kpi.Children.Add(Mini("Masraf • emanet", _kMas));

        // ---- zaman çizelgesi süzgeçleri
        var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 10) };
        foreach (var g in Gruplar)
        {
            var gg = g;
            var c = new TbBtn("", g, () => { _grup = gg; Cizelge(); });
            _chip[g] = c; chips.Children.Add(c);
        }
        var tlBaslik = Ui.H2("Zaman çizelgesi"); tlBaslik.Margin = new Thickness(2, 6, 0, 4);

        var sayfa = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        if (gomulu)
        {
            eylem.HorizontalAlignment = HorizontalAlignment.Left; eylem.MaxWidth = double.PositiveInfinity; eylem.Margin = new Thickness(0, 8, 0, 6);
            foreach (var e in new UIElement[] { eylem, araclar, kpi, tlBaslik, chips, _tl }) sayfa.Children.Add(e);
        }
        else
            foreach (var e in new UIElement[] { sec, seritScroll, hdr!, _step, araclar, kpi, tlBaslik, chips, _tl }) sayfa.Children.Add(e);
        var kaydir = new ScrollViewer { Content = sayfa, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Fill(kaydir);

        AllowDrop = true;
        PreviewDragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        PreviewDrop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] f) EvrakEkle(f);
            e.Handled = true;
        };
    }

    static Border Mini(string etiket, TextBlock deger)
    {
        var b = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 10, 0), BorderThickness = new Thickness(1) };
        b.SetResourceReference(Border.BackgroundProperty, "surface");
        b.SetResourceReference(Border.BorderBrushProperty, "border");
        var sp = new StackPanel();
        sp.Children.Add(Ui.Muted(etiket, false));
        deger.Margin = new Thickness(0, 3, 0, 0);
        deger.TextTrimming = TextTrimming.CharacterEllipsis;
        sp.Children.Add(deger);
        b.Child = sp;
        return b;
    }

    static void Boya(TextBlock t, string brush) => t.SetResourceReference(TextBlock.ForegroundProperty, brush);

    static Pill Etiket(string t, string kind) { var p = new Pill(t, kind); p.Margin = new Thickness(0, 0, 6, 4); return p; }

    long Secili => (_combo.SelectedItem as ComboItem)?.Value is { } v ? Convert.ToInt64(v) : 0;

    long MSecili => (_mcombo.SelectedItem as ComboItem)?.Value is { } v ? Convert.ToInt64(v) : 0;

    void GitMenu()
    {
        var m = new ContextMenu();
        void Ekle(string h, Action a) { var mi = new MenuItem { Header = h }; mi.Click += (_, _) => a(); m.Items.Add(mi); }
        Ekle("İş Listesi", () => Ctx.Navigate("is_listesi", null));
        if (!_gomulu) Ekle("Dava Takibi", () => { DavaPage.Hedef = Secili; Ctx.Navigate("dava", null); });
        Ekle("Emanet / Masraf", () => Ctx.Navigate("emanet", null));
        Ekle("Müvekkil sayfası", () => Ctx.Navigate("muvekkil", null));
        m.IsOpen = true;
    }

    /// <summary>Gömülü kullanımda: verilen davanın kartını yükler.</summary>
    public void AcDava(long id)
    {
        _busy = true;
        try
        {
            _combo.Items.Clear();
            if (id > 0) { _combo.Items.Add(new ComboItem { Value = id, Label = "" }); _combo.SelectedIndex = 0; }
            else _combo.SelectedIndex = -1;
        }
        finally { _busy = false; }
        Yukle();
    }

    public override void Refresh()
    {
        if (_gomulu) { Yukle(); return; }
        _busy = true;
        try
        {
            var onceki = Hedef > 0 ? Hedef : Secili;
            var mid = MSecili;
            if (onceki > 0 && Db.One("SELECT muvekkil_id FROM davalar WHERE id=?", onceki) is { } dm)
            {
                if (Hedef > 0) mid = dm.L("muvekkil_id");            // başka sayfadan gelindi: o davanın müvekkiline geç
                else if (mid > 0 && dm.L("muvekkil_id") != mid) onceki = 0;
            }
            Hedef = 0;
            _mcombo.Items.Clear();
            _mcombo.Items.Add(new ComboItem { Value = 0L, Label = "— Tüm müvekkiller —" });
            var cmp = StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true);
            foreach (var m in Db.Query("SELECT id, ad_soyad, tc_no FROM muvekkiller").OrderBy(x => x.S("ad_soyad"), cmp))
                _mcombo.Items.Add(new ComboItem { Value = m.L("id"), Label = $"{m.S("ad_soyad")} ({m.S("tc_no")})" });
            var mi = _mcombo.Items.Cast<ComboItem>().ToList().FindIndex(x => Convert.ToInt64(x.Value) == mid);
            _mcombo.SelectedIndex = mi >= 0 ? mi : 0;
            DavaDoldur(onceki);
        }
        finally { _busy = false; }
        Yukle();
    }

    /// <summary>Seçili müvekkilin davalarını dava kutusuna ve dosya şeridine doldurur.</summary>
    void DavaDoldur(long sec)
    {
        var mid = MSecili;
        var rows = mid > 0
            ? Db.Query("SELECT d.*, m.ad_soyad FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id WHERE d.muvekkil_id=? ORDER BY d.id DESC", mid)
            : Db.Query("SELECT d.*, m.ad_soyad FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id ORDER BY d.id DESC");
        _seritRows = rows;
        _combo.Items.Clear();
        foreach (var d in rows)
            _combo.Items.Add(new ComboItem { Value = d.L("id"), Label = $"{d.S("mahkeme_adi")} {d.S("esas_no")} — {d.S("ad_soyad")}".Trim(' ', '—', ' ') });
        var i = _combo.Items.Cast<ComboItem>().ToList().FindIndex(x => Convert.ToInt64(x.Value) == sec);
        _combo.SelectedIndex = i >= 0 ? i : (_combo.Items.Count > 0 ? 0 : -1);
        SeritYaz();
    }

    List<Row> _seritRows = new();

    /// <summary>Müvekkilin dava dosyalarını yatay çiplerle gösterir; seçili olan vurgulanır.</summary>
    void SeritYaz()
    {
        _serit.Children.Clear();
        var secili = Secili;
        foreach (var d in _seritRows)
        {
            var id = d.L("id");
            var kapali = d.S("kapanis_tarihi").Length > 0;
            var sp = new StackPanel();
            var t1 = Ui.Text($"{d.S("esas_no")}  {d.S("mahkeme_adi")}", 12.5, true); t1.MaxWidth = 260; t1.TextTrimming = TextTrimming.CharacterEllipsis;
            var t2 = Ui.Muted((MSecili == 0 && d.S("ad_soyad").Length > 0 ? d.S("ad_soyad") + " • " : "") + (kapali ? "🔒 kapalı" : d.S("asama_durum")), false);
            t2.FontSize = 11.5; t2.MaxWidth = 260; t2.TextTrimming = TextTrimming.CharacterEllipsis;
            sp.Children.Add(t1); sp.Children.Add(t2);
            var b = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0), Child = sp, Cursor = Cursors.Hand };
            bool s = id == secili;
            b.SetResourceReference(Border.BackgroundProperty, s ? "sel" : "surface");
            b.SetResourceReference(Border.BorderBrushProperty, s ? "accent" : "border");
            b.MouseLeftButtonUp += (_, _) => DosyaGec(id);
            _serit.Children.Add(b);
        }
        if (_seritRows.Count == 0) _serit.Children.Add(Ui.Muted("Bu müvekkilin dava dosyası yok. Önce Dosyalar sayfasında dava ekleyin."));
    }

    void DosyaGec(long id)
    {
        if (id == Secili) return;
        var it = _combo.Items.Cast<ComboItem>().FirstOrDefault(x => Convert.ToInt64(x.Value) == id);
        if (it == null) return;
        _busy = true;
        try { _combo.SelectedItem = it; SeritYaz(); } finally { _busy = false; }
        Yukle();
    }

    static Row Satir(string tarih, string tur, string aciklama, string tutar = "", string tablo = "", long kid = 0, string yol = "") => new()
    {
        ["tarih"] = tarih, ["tur"] = tur, ["aciklama"] = aciklama, ["tutar"] = tutar, ["tablo"] = tablo, ["kid"] = kid, ["yol"] = yol,
    };

    static string Sure(long dk) => dk >= 60 ? $"{dk / 60} sa {dk % 60} dk" : $"{dk} dk";

    static int AsamaNo(string a) { if (a == DavaPage.Kapali) return DavaPage.Asamalar.Length; var i = Array.IndexOf(DavaPage.Asamalar, a); return i < 0 ? 0 : i; }

    static string GunMetni(int? k) => k == null ? "" : k < 0 ? $"{-k} gün geçti" : k == 0 ? "bugün" : k == 1 ? "yarın" : $"{k} gün sonra";

    static string Grup(string tur)
    {
        if (tur.StartsWith("SÜRE") || tur.StartsWith("Süre") || tur is "Duruşma" or "Aşama" or "Kapanış" or "Keşif" or "Toplantı" or "Diğer" or "Ajanda") return "Süre ve duruşma";
        if (tur == "Tebligat") return "Tebligat";
        if (tur is "Tahsilat" or "Masraf" || tur.StartsWith("Emanet")) return "Mali";
        if (tur == "Evrak") return "Evrak";
        return "Notlar";
    }

    /// <summary>tür → (simge, tema rengi anahtarı)</summary>
    static (string Simge, string Renk) Gorunum(string tur)
    {
        if (tur.StartsWith("SÜRE")) return ("⏱", "danger");
        if (tur.StartsWith("Süre")) return ("✔", "ok");
        if (tur.StartsWith("Emanet")) return ("🏦", "muted");
        return tur switch
        {
            "Duruşma" => ("⚖", "warn"), "Aşama" => ("▣", "accent"), "Kapanış" => ("🔒", "muted"), "Tebligat" => ("📬", "accent"),
            "Duruşma notu" => ("📝", "accent"), "Müvekkil bilgi" => ("💬", "accent"), "Zaman" => ("⏳", "muted"), "Evrak" => ("📎", "accent"),
            "Tahsilat" => ("💰", "ok"), "Masraf" => ("🧾", "warn"), "Keşif" or "Toplantı" or "Diğer" or "Ajanda" => ("📅", "accent"),
            _ => ("•", "muted"),
        };
    }

    void Yukle()
    {
        var id = Secili;
        _sec = null; _secB = null;
        if (id == 0)
        {
            _dTitle.Text = "Dava seçin"; _dSub.Text = "Üstten müvekkil ve dava seçin ya da önce Dosyalar sayfasında dava ekleyin.";
            _dEtiket.Children.Clear(); _step.Children.Clear(); _kayitlar = new();
            _kSure.Text = _kDur.Text = _kTeb.Text = _kCari.Text = _kMas.Text = _kUcret.Text = _kOde.Text = "—";
            Cizelge();
            return;
        }
        var d = Db.One("SELECT d.*, m.ad_soyad, m.telefon FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id WHERE d.id=?", id);
        if (d == null) return;
        var kapali = d.S("kapanis_tarihi").Length > 0;
        _dTitle.Text = $"{d.S("mahkeme_adi")} • {d.S("esas_no")}";
        _dSub.Text = $"Müvekkil: {d.S("ad_soyad")}" + (d.S("telefon").Length > 0 ? $" • {d.S("telefon")}" : "") +
                     (d.S("karsi_taraf").Length > 0 ? $"   •   Karşı taraf: {d.S("karsi_taraf")}" : "");
        _dEtiket.Children.Clear();
        _dEtiket.Children.Add(Etiket(d.S("asama_durum"), d.S("asama_durum") == DavaPage.Kapali ? "ok" : "info"));
        var ist = DavaPage.IstinafOzet(d);
        if (ist.Length > 0) _dEtiket.Children.Add(Etiket(ist, "info"));
        if (d.S("dava_turu").Length > 0) _dEtiket.Children.Add(Etiket(d.S("dava_turu"), "muted"));
        if (kapali) _dEtiket.Children.Add(Etiket($"🔒 DOSYA KAPALI {Utils.FmtDate(d.S("kapanis_tarihi"))} • saklama bitişi {Utils.FmtDate(d.S("saklama_bitis"))}", "warn"));
        StepYaz(d);

        var l = new List<Row>();
        foreach (var (k, ad) in HmkAlan)
            if (d.S(k).Length > 0) l.Add(Satir(d.S(k), k == "durusma_tarihi" ? "Duruşma" : "Aşama", ad));
        if (d.S("istinaf_basvuru_tarihi").Length > 0) l.Add(Satir(d.S("istinaf_basvuru_tarihi"), "Aşama", $"İstinaf başvurusu • {d.S("istinaf_mahkemesi")} {d.S("istinaf_esas_no")}".Trim()));
        if (d.S("istinaf_karar_tarihi").Length > 0) l.Add(Satir(d.S("istinaf_karar_tarihi"), "Aşama", $"İstinaf kararı • {d.S("istinaf_sonuc")}".Trim(' ', '•')));
        if (kapali) l.Add(Satir(d.S("kapanis_tarihi"), "Kapanış", d.S("kapanis_notu").Length > 0 ? d.S("kapanis_notu") : "Dosya kapatıldı"));
        foreach (var r in Db.Query("SELECT * FROM ajanda WHERE dava_id=?", id))
            l.Add(Satir(r.S("tarih"), r.S("tur").Length > 0 ? r.S("tur") : "Ajanda", $"{r.S("saat")} {r.S("baslik")}".Trim()));
        int acik = 0; string enYakin = "";
        foreach (var r in Db.Query("SELECT * FROM sureler WHERE dava_id=? ORDER BY son_tarih", id))
        {
            if (r.S("durum") == "Açık") { acik++; if (enYakin.Length == 0) enYakin = r.S("son_tarih"); }
            l.Add(Satir(r.S("son_tarih"), r.S("durum") == "Açık" ? "SÜRE (açık)" : "Süre (tamam)", $"{r.S("baslik")} • {r.S("dayanak")}"));
        }
        int teb = 0;
        foreach (var r in Db.Query("SELECT * FROM genel_tebligatlar WHERE dava_id=?", id))
        {
            teb++;
            l.Add(Satir(r.S("teblig_tarihi"), "Tebligat", $"{r.S("gonderen_kurum")} – {r.S("konu")} ({r.S("durum")})"));
        }
        foreach (var r in Db.Query("SELECT * FROM durusma_notlari WHERE dava_id=?", id))
            l.Add(Satir(r.S("tarih"), "Duruşma notu", r.S("ozet") + (r.S("sonraki_durusma").Length > 0 ? $" → sonraki: {Utils.FmtDate(r.S("sonraki_durusma"))}" : ""), "", "durusma_notlari", r.L("id")));
        foreach (var r in Db.Query("SELECT * FROM muvekkil_bilgilendirme WHERE dava_id=?", id))
            l.Add(Satir(r.S("tarih"), "Müvekkil bilgi", $"{r.S("kanal")}: {r.S("konu")} {r.S("ozet")}".Trim(), "", "muvekkil_bilgilendirme", r.L("id")));
        long dk = 0;
        foreach (var r in Db.Query("SELECT * FROM zaman_kayitlari WHERE dava_id=?", id))
        {
            dk += r.L("dakika");
            l.Add(Satir(r.S("tarih"), "Zaman", r.S("aciklama"), Sure(r.L("dakika")), "zaman_kayitlari", r.L("id")));
        }
        foreach (var r in Db.Query("SELECT * FROM dava_evraklari WHERE dava_id=?", id))
            l.Add(Satir(r.S("eklenme"), "Evrak", r.S("ad"), "", "dava_evraklari", r.L("id"), Workflows.DosyaYolu(Cfg, r.S("yol"))));
        foreach (var r in Db.Query("SELECT h.tarih, h.tutar, h.aciklama FROM tahsilat_hareketleri h JOIN finans_tahsilat f ON f.id=h.finans_id WHERE f.dava_id=?", id))
            l.Add(Satir(r.S("tarih"), "Tahsilat", r.S("aciklama"), Utils.FmtMoney(r.D("tutar"))));
        double masraf = 0;
        foreach (var r in Db.Query("SELECT * FROM dava_masraflari WHERE dava_id=?", id))
        {
            masraf += r.D("tutar");
            l.Add(Satir(r.S("tarih"), "Masraf", r.S("masraf_kalemi"), Utils.FmtMoney(r.D("tutar"))));
        }
        double emanet = 0;
        foreach (var r in Db.Query("SELECT * FROM emanet_hareketleri WHERE dava_id=?", id))
        {
            emanet += r.S("tur") == "Giriş" ? r.D("tutar") : -r.D("tutar");
            l.Add(Satir(r.S("tarih"), "Emanet " + r.S("tur"), r.S("aciklama"), Utils.FmtMoney(r.D("tutar"))));
        }
        var f = Db.One("SELECT SUM(toplam_ucret) t, SUM(alinan_tutar) a, SUM(kalan_bakiye) k FROM finans_tahsilat WHERE dava_id=?", id);

        _kSure.Text = acik == 0 ? "Yok" : $"{acik} • {Utils.FmtDate(enYakin)}"; Boya(_kSure, acik > 0 ? "danger" : "ok");
        var durGun = Utils.DaysLeft(d.S("durusma_tarihi"));
        _kDur.Text = d.S("durusma_tarihi").Length == 0 ? "Yok" : durGun >= 0 ? $"{Utils.FmtDate(d.S("durusma_tarihi"))} ({GunMetni(durGun)})" : $"{Utils.FmtDate(d.S("durusma_tarihi"))} (geçti)";
        Boya(_kDur, durGun != null && durGun >= 0 && durGun <= 7 ? "warn" : "text");
        _kTeb.Text = teb.ToString(); Boya(_kTeb, "text");
        var kalan = f?.D("k") ?? 0; var ucret = f?.D("t") ?? 0; var odenen = f?.D("a") ?? 0;
        _kUcret.Text = ucret > 0 ? Utils.FmtMoney(ucret) : "Girilmedi"; Boya(_kUcret, ucret > 0 ? "text" : "muted");
        _kOde.Text = Utils.FmtMoney(odenen) + (ucret > 0 ? $"  (%{Math.Min(100, Math.Round(100 * odenen / ucret))})" : ""); Boya(_kOde, "ok");
        _kCari.Text = Utils.FmtMoney(kalan); Boya(_kCari, kalan > 0 ? "warn" : "ok");
        _kMas.Text = $"{Utils.FmtMoney(masraf)} • {Utils.FmtMoney(emanet)}"; Boya(_kMas, "text");

        _kayitlar = l.OrderByDescending(x => x.S("tarih")).ToList();
        Cizelge();
    }

    // ------------------------------------------------------------------ aşama çubuğu
    void StepYaz(Row d)
    {
        _step.Children.Clear();
        var A = DavaPage.Asamalar;
        _step.Columns = A.Length;
        int no = AsamaNo(d.S("asama_durum"));
        for (int i = 0; i < A.Length; i++)
        {
            int ii = i;
            var num = new TextBlock { Text = (i + 1).ToString(), FontSize = 14, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
            var ad = new TextBlock { Text = A[i], FontSize = 10.5, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
            var sp = new StackPanel(); sp.Children.Add(num); sp.Children.Add(ad);
            var b = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(4, 6, 4, 6), Margin = new Thickness(0, 0, i == A.Length - 1 ? 0 : 5, 0), Child = sp, Cursor = Cursors.Hand, ToolTip = "Aşamayı “" + A[i] + "” yap" };
            if (i < no) { b.Background = Theme.Alpha("ok", 40); b.SetResourceReference(Border.BorderBrushProperty, "ok"); num.SetResourceReference(TextBlock.ForegroundProperty, "ok"); ad.SetResourceReference(TextBlock.ForegroundProperty, "muted"); }
            else if (i == no) { b.SetResourceReference(Border.BackgroundProperty, "accent"); b.SetResourceReference(Border.BorderBrushProperty, "accent"); num.Foreground = Brushes.White; ad.Foreground = Brushes.White; }
            else { b.SetResourceReference(Border.BackgroundProperty, "surface2"); b.SetResourceReference(Border.BorderBrushProperty, "border"); num.SetResourceReference(TextBlock.ForegroundProperty, "muted"); ad.SetResourceReference(TextBlock.ForegroundProperty, "muted"); }
            b.MouseLeftButtonUp += (_, _) => AsamaDegistir(ii);
            _step.Children.Add(b);
        }
    }

    void AsamaDegistir(int i)
    {
        var id = Secili;
        if (id == 0) return;
        var A = DavaPage.Asamalar;
        var mevcut = Db.One("SELECT asama_durum FROM davalar WHERE id=?", id)?.S("asama_durum") ?? "";
        if (mevcut == A[i]) return;
        Db.Update("davalar", id, new Dictionary<string, object?> { ["asama_durum"] = A[i] });
        Ctx.Status($"Aşama güncellendi: {A[i]}");
        if (A[i] == "İstinaf")
        {
            var dd = Db.One("SELECT * FROM davalar WHERE id=?", id);
            if (dd != null && dd.S("istinaf_basvuru_tarihi").Length == 0 && dd.S("istinaf_mahkemesi").Length == 0) DavaPage.IstinafBilgisi(this, Db, id);
        }
        _busy = true; try { DavaDoldur(id); } finally { _busy = false; }
        Yukle();
    }

    // ------------------------------------------------------------------ zaman çizelgesi
    void Cizelge()
    {
        foreach (var kv in _chip)
        {
            kv.Value.Aktif(kv.Key == _grup);
        }
        _tl.Children.Clear();
        _sec = null; _secB = null;
        if (Secili == 0) return;
        var liste = _grup == "Tümü" ? _kayitlar : _kayitlar.Where(r => Grup(r.S("tur")) == _grup).ToList();
        if (liste.Count == 0)
        {
            var bos = Ui.Muted(_kayitlar.Count == 0 ? "Bu dosyada henüz kayıt yok. Duruşma notu, süreli iş, zaman ya da evrak ekleyerek başlayın." : "Bu başlıkta kayıt yok.");
            bos.Margin = new Thickness(4, 10, 0, 0);
            _tl.Children.Add(bos);
            return;
        }
        var bugun = Utils.TodayIso();
        bool cizdi = false;
        for (int i = 0; i < liste.Count; i++)
        {
            var r = liste[i];
            if (!cizdi && string.CompareOrdinal(r.S("tarih"), bugun) <= 0) { _tl.Children.Add(BugunCizgisi()); cizdi = true; }
            _tl.Children.Add(Olay(r, i == liste.Count - 1));
        }
        if (!cizdi) _tl.Children.Add(BugunCizgisi());
    }

    static UIElement BugunCizgisi()
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        var t = Ui.Text($"BUGÜN • {DateTime.Today.ToString("d MMMM yyyy, dddd", Utils.Tr)}", 11.5, true, "accent");
        t.Margin = new Thickness(0, 0, 10, 0);
        var line = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Center };
        line.SetResourceReference(Border.BackgroundProperty, "accent");
        Grid.SetColumn(line, 1);
        g.Children.Add(t); g.Children.Add(line);
        return g;
    }

    Grid Olay(Row r, bool son)
    {
        var (simge, renk) = Gorunum(r.S("tur"));
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        g.ColumnDefinitions.Add(new ColumnDefinition());

        var gelecek = string.CompareOrdinal(r.S("tarih"), Utils.TodayIso()) > 0;
        var dt = Ui.Text(Utils.FmtDate(r.S("tarih")), 12.5, true, gelecek ? "accent" : "muted");
        dt.HorizontalAlignment = HorizontalAlignment.Right; dt.VerticalAlignment = VerticalAlignment.Top; dt.Margin = new Thickness(0, 10, 4, 0);

        var line = new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = son ? VerticalAlignment.Top : VerticalAlignment.Stretch, Height = son ? 16 : double.NaN, Margin = new Thickness(0, 14, 0, 0) };
        line.SetResourceReference(Border.BackgroundProperty, "border");
        var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 0, 0) };
        dot.SetResourceReference(Border.BackgroundProperty, renk);
        Grid.SetColumn(line, 1); Grid.SetColumn(dot, 1);

        var tur = new TextBlock { Text = $"{simge}  {r.S("tur")}", FontSize = 12, FontWeight = FontWeights.SemiBold };
        tur.SetResourceReference(TextBlock.ForegroundProperty, renk);
        var ust = new DockPanel();
        if (r.S("tutar").Length > 0)
        {
            var tt = Ui.Text(r.S("tutar"), 12.5, true); DockPanel.SetDock(tt, Dock.Right); ust.Children.Add(tt);
        }
        ust.Children.Add(tur);
        var ac = Ui.Text(r.S("aciklama").Length > 0 ? r.S("aciklama") : "—", 13, false, "text", true);
        ac.Margin = new Thickness(0, 3, 0, 0);
        var sp = new StackPanel(); sp.Children.Add(ust); sp.Children.Add(ac);
        var kart = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 12, 9), Margin = new Thickness(0, 0, 0, 8), Child = sp, Cursor = Cursors.Hand };
        kart.SetResourceReference(Border.BackgroundProperty, "surface");
        kart.SetResourceReference(Border.BorderBrushProperty, "border");
        Grid.SetColumn(kart, 2);
        kart.MouseLeftButtonDown += (_, e) =>
        {
            KayitSec(kart, r);
            if (e.ClickCount >= 2 && r.S("tur") == "Evrak" && r.S("yol").Length > 0)
            {
                if (File.Exists(r.S("yol"))) Ui.OpenExternal(r.S("yol")); else Ui.Warn(this, "Dosya bulunamadı:\n" + r.S("yol"));
            }
        };
        kart.MouseRightButtonDown += (_, _) => KayitSec(kart, r);
        var cm = new ContextMenu();
        if (r.S("tur") == "Evrak" && r.S("yol").Length > 0)
        {
            var a = new MenuItem { Header = "Evrakı aç" }; a.Click += (_, _) => { if (File.Exists(r.S("yol"))) Ui.OpenExternal(r.S("yol")); }; cm.Items.Add(a);
            var k = new MenuItem { Header = "Klasörde göster" }; k.Click += (_, _) => Ui.RevealInFolder(r.S("yol")); cm.Items.Add(k);
        }
        if (r.S("tablo").Length > 0)
        {
            var s = new MenuItem { Header = "Kaydı sil…" }; s.Click += (_, _) => { KayitSec(kart, r); KayitSil(); }; cm.Items.Add(s);
        }
        if (cm.Items.Count > 0) kart.ContextMenu = cm;
        g.Children.Add(dt); g.Children.Add(line); g.Children.Add(dot); g.Children.Add(kart);
        return g;
    }

    void KayitSec(Border b, Row r)
    {
        if (_secB != null) { _secB.SetResourceReference(Border.BackgroundProperty, "surface"); _secB.SetResourceReference(Border.BorderBrushProperty, "border"); }
        _sec = r; _secB = b;
        b.SetResourceReference(Border.BackgroundProperty, "sel");
        b.SetResourceReference(Border.BorderBrushProperty, "accent");
    }

    // ------------------------------------------------------------------ süreli iş
    void SureEkle()
    {
        var id = Secili;
        if (id == 0) { Ui.Info(this, "Önce bir dava seçin."); return; }
        var v = IsListesiPage.Sor(this, Db, new Dictionary<string, object?> { ["dava_id"] = id });
        if (v == null) return;
        Db.Insert("sureler", v);
        Yukle();
    }

    // ------------------------------------------------------------------ duruşma notu
    void DurusmaNotu()
    {
        var id = Secili;
        if (id == 0) { Ui.Info(this, "Önce bir dava seçin."); return; }
        var mevcut = Db.One("SELECT asama_durum FROM davalar WHERE id=?", id)?.S("asama_durum") ?? "";
        var v = FormDialog.Ask(this, "Duruşma Sonrası", new List<Field>
        {
            new("tarih", "Duruşma tarihi", "date", def: Utils.TodayIso()),
            new("ozet", "Duruşmada ne oldu?", "multiline", required: true),
            new("sonraki_durusma", "Sonraki duruşma tarihi", "date"),
            new("kesin_sure_gun", "Verilen kesin süre (gün) – yoksa 0", "int", def: 0),
            new("kesin_sure_aciklama", "Kesin sürenin konusu (ör. bilirkişi ücreti, belge sunma)"),
            new("asama", "Dava aşaması (duruşma sonrası)", "combo", Field.Of(DavaPage.Asamalar), def: mevcut.Length > 0 ? mevcut : DavaPage.Asamalar[0]),
            new("gorev", "Yapılacak iş / görev (yoksa boş bırakın)"),
            new("gorev_tarih", "Görev son tarihi", "date"),
            new("metin", "Müvekkil bilgilendirme metnini panoya kopyala", "check", def: 1L),
        });
        if (v == null) return;
        var asama = Convert.ToString(v["asama"]) ?? "";
        var gorev = (Convert.ToString(v["gorev"]) ?? "").Trim();
        var gorevTarih = Convert.ToString(v["gorev_tarih"]) ?? "";
        var tarih = Convert.ToString(v["tarih"]) ?? "";
        var ozet = Convert.ToString(v["ozet"]) ?? "";
        var sonraki = Convert.ToString(v["sonraki_durusma"]) ?? "";
        int gun = Convert.ToInt32(v["kesin_sure_gun"] ?? 0);
        var aciklama = Convert.ToString(v["kesin_sure_aciklama"]) ?? "";
        Db.Insert("durusma_notlari", new Dictionary<string, object?>
        {
            ["dava_id"] = id, ["tarih"] = tarih, ["ozet"] = ozet, ["sonraki_durusma"] = sonraki,
            ["kesin_sure_gun"] = gun, ["kesin_sure_aciklama"] = aciklama,
        });
        if (sonraki.Length > 0) Db.Update("davalar", id, new Dictionary<string, object?> { ["durusma_tarihi"] = sonraki });
        string sureMetni = "";
        if (asama.Length > 0 && asama != mevcut) Db.Update("davalar", id, new Dictionary<string, object?> { ["asama_durum"] = asama });
        if (gorev.Length > 0)
        {
            Db.Insert("gorevler", new Dictionary<string, object?> { ["baslik"] = gorev, ["son_tarih"] = gorevTarih, ["aciklama"] = $"Duruşma sonrası ({Utils.FmtDate(tarih)})" });
            sureMetni += "\nGörev eklendi: " + gorev;
        }
        var bas = Utils.ParseDate(tarih);
        if (gun > 0 && bas != null)
        {
            var son = IsSureleri.IsGunuyaKaydir(bas.Value.AddDays(gun));
            Db.Insert("sureler", new Dictionary<string, object?>
            {
                ["baslik"] = "Kesin süre – " + (aciklama.Length > 0 ? aciklama : "duruşma tutanağı"),
                ["dayanak"] = $"HMK 94 • duruşma tutanağı ({Utils.FmtDate(tarih)})", ["sure_turu"] = "Mahkeme Verdiği Süre",
                ["baslangic"] = tarih, ["son_tarih"] = Utils.Iso(son), ["dava_id"] = id, ["durum"] = "Açık",
                ["notlar"] = $"{gun} gün kesin süre. Tutanaktaki süre başlangıcını ve adli tatil etkisini kontrol edin.",
            });
            sureMetni += $"\n\nSüreli iş açıldı: son gün {son:dd.MM.yyyy}.";
        }
        if (Convert.ToInt64(v["metin"] ?? 0) == 1)
        {
            var d = Db.One("SELECT d.mahkeme_adi, d.esas_no, m.ad_soyad FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id WHERE d.id=?", id);
            var metin = $"Sayın {d?.S("ad_soyad")},\n{d?.S("mahkeme_adi")} {d?.S("esas_no")} sayılı dosyanızın {Utils.FmtDate(tarih)} tarihli duruşması yapılmıştır.\n{ozet}\n" +
                        (sonraki.Length > 0 ? $"Bir sonraki duruşma tarihi: {Utils.FmtDate(sonraki)}.\n" : "") +
                        (gun > 0 ? $"Mahkemece {gun} gün süre verilmiştir{(aciklama.Length > 0 ? " (" + aciklama + ")" : "")}.\n" : "") +
                        "Bilgilerinize sunarım.";
            try { Clipboard.SetText(metin); sureMetni += "\nMüvekkil bilgilendirme metni panoya kopyalandı."; } catch (Exception) { }
        }
        Yukle();
        if (sureMetni.Length > 0) Ui.Info(this, "Duruşma notu kaydedildi." + sureMetni);
    }

    // ------------------------------------------------------------------ müvekkil bilgilendirme kaydı
    void Bilgilendirme()
    {
        var id = Secili;
        if (id == 0) { Ui.Info(this, "Önce bir dava seçin."); return; }
        var v = FormDialog.Ask(this, "Müvekkile Bilgi Verildi", new List<Field>
        {
            new("tarih", "Tarih", "date", def: Utils.TodayIso()),
            new("kanal", "Kanal", "combo", Field.Of(Kanallar), def: "Telefon"),
            new("konu", "Konu", required: true),
            new("ozet", "Ayrıntı", "multiline"),
        });
        if (v == null) return;
        var mid = Db.One("SELECT muvekkil_id FROM davalar WHERE id=?", id)?.L("muvekkil_id") ?? 0;
        v["dava_id"] = id; v["muvekkil_id"] = mid;
        Db.Insert("muvekkil_bilgilendirme", v);
        Yukle();
    }

    // ------------------------------------------------------------------ zaman
    // ------------------------------------------------------------------ ücret / tahsilat
    /// <summary>Bu dosya için anlaşılan vekalet ücretini girer / günceller.</summary>
    void UcretAnlasmasi()
    {
        var id = Secili;
        if (id == 0) { Ui.Info(this, "Önce bir dava seçin."); return; }
        var d = Db.One("SELECT * FROM davalar WHERE id=?", id);
        if (d == null) return;
        if (d.L("muvekkil_id") == 0) { Ui.Info(this, "Bu dosyanın müvekkili seçili değil; önce Dosyalar sayfasında müvekkili seçin."); return; }
        var fin = Db.One("SELECT * FROM finans_tahsilat WHERE dava_id=? ORDER BY id LIMIT 1", id);
        var v = FormDialog.Ask(this, "Vekalet Ücreti Anlaşması", new List<Field>
        {
            new("toplam_ucret", "Anlaşılan toplam ücret", "money", required: true),
            new("sonraki_tahsilat_tarihi", "Sonraki tahsilat tarihi", "date"),
            new("notlar", "Not (ödeme planı vb.)", "multiline"),
        }, fin);
        if (v == null) return;
        long fid;
        if (fin == null)
        {
            v["muvekkil_id"] = d.L("muvekkil_id"); v["dava_id"] = id;
            v["dosya_konusu"] = $"{d.S("mahkeme_adi")} {d.S("esas_no")}".Trim();
            v["kalan_bakiye"] = v["toplam_ucret"];
            fid = Db.Insert("finans_tahsilat", v);
        }
        else { fid = fin.L("id"); Db.Update("finans_tahsilat", fid, v); }
        Workflows.TahsilatYenidenHesapla(Db, fid);
        Yukle();
    }

    /// <summary>Bu dosyaya tahsilat (ödeme) işler; ücret anlaşması yoksa önce onu sorar.</summary>
    void TahsilatGir()
    {
        var id = Secili;
        if (id == 0) { Ui.Info(this, "Önce bir dava seçin."); return; }
        var fin = Db.One("SELECT * FROM finans_tahsilat WHERE dava_id=? ORDER BY id LIMIT 1", id);
        if (fin == null)
        {
            UcretAnlasmasi();
            fin = Db.One("SELECT * FROM finans_tahsilat WHERE dava_id=? ORDER BY id LIMIT 1", id);
            if (fin == null) return;
        }
        var v = FormDialog.Ask(this, "Tahsilat Gir", new List<Field>
        {
            new("tarih", "Tahsilat Tarihi", "date", def: Utils.TodayIso()),
            new("tutar", "Tutar", "money", required: true),
            new("aciklama", "Açıklama (nakit / havale / çek…)"),
        });
        if (v == null || Convert.ToDouble(v["tutar"] ?? 0.0) <= 0) return;
        v["finans_id"] = fin.L("id");
        Db.Insert("tahsilat_hareketleri", v);
        Workflows.TahsilatYenidenHesapla(Db, fin.L("id"));
        Yukle();
    }

    void IstinafBilgi()
    {
        var id = Secili;
        if (id == 0) { Ui.Info(this, "Önce bir dava seçin."); return; }
        if (DavaPage.IstinafBilgisi(this, Db, id)) Yukle();
    }

    void ZamanEkle()
    {
        var id = Secili;
        if (id == 0) { Ui.Info(this, "Önce bir dava seçin."); return; }
        var v = FormDialog.Ask(this, "Zaman Kaydı", new List<Field>
        {
            new("tarih", "Tarih", "date", def: Utils.TodayIso()),
            new("dakika", "Süre (dakika)", "int", def: 30, required: true),
            new("aciklama", "Ne yapıldı?"),
        });
        if (v == null) return;
        v["dava_id"] = id;
        Db.Insert("zaman_kayitlari", v);
        Yukle();
    }

    // ------------------------------------------------------------------ evrak
    void EvrakSec()
    {
        if (Secili == 0) { Ui.Info(this, "Önce bir dava seçin."); return; }
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Dosyaya eklenecek evrak", Multiselect = true };
        if (dlg.ShowDialog() == true) EvrakEkle(dlg.FileNames);
    }

    void EvrakEkle(string[] dosyalar)
    {
        var id = Secili;
        if (id == 0) { Ui.Info(this, "Önce bir dava seçin."); return; }
        int n = 0;
        var klasor = Cfg.Subdir("Dosyalar", "Dava", id.ToString());
        foreach (var f in dosyalar.Where(File.Exists))
        {
            var yeni = Db.CopyFile(f, klasor, $"{DateTime.Now:yyyyMMdd_HHmmss}_{Path.GetFileName(f)}");
            Db.Insert("dava_evraklari", new Dictionary<string, object?>
            {
                ["dava_id"] = id, ["ad"] = Path.GetFileName(f), ["yol"] = Path.GetRelativePath(Cfg.DataDir, yeni),
            });
            n++;
        }
        if (n > 0) { Yukle(); Ctx.Status($"{n} evrak eklendi."); }
    }

    // ------------------------------------------------------------------ kapanış
    void KapatAc()
    {
        var id = Secili;
        if (id == 0) { Ui.Info(this, "Önce bir dava seçin."); return; }
        var d = Db.One("SELECT * FROM davalar WHERE id=?", id);
        if (d == null) return;
        if (d.S("kapanis_tarihi").Length > 0)
        {
            if (!Ui.Confirm(this, "Dosya yeniden açılsın mı? Kapanış ve saklama bilgisi silinir.")) return;
            Db.Update("davalar", id, new Dictionary<string, object?>
            {
                ["kapanis_tarihi"] = "", ["saklama_bitis"] = "", ["kapanis_notu"] = "", ["asama_durum"] = "Karar",
            });
            Yukle();
            return;
        }
        var sorunlar = new List<string>();
        var acik = Db.ScalarL("SELECT COUNT(*) FROM sureler WHERE dava_id=? AND durum='Açık'", id);
        if (acik > 0) sorunlar.Add($"{acik} açık süreli iş var");
        var kalan = Db.ScalarD("SELECT COALESCE(SUM(kalan_bakiye),0) FROM finans_tahsilat WHERE dava_id=?", id);
        if (kalan > 0.009) sorunlar.Add($"tahsil edilmemiş vekalet ücreti: {Utils.FmtMoney(kalan)}");
        var em = Db.ScalarD("SELECT COALESCE(SUM(CASE WHEN tur='Giriş' THEN tutar ELSE -tutar END),0) FROM emanet_hareketleri WHERE dava_id=?", id);
        if (Math.Abs(em) > 0.009) sorunlar.Add($"emanet bakiyesi sıfır değil: {Utils.FmtMoney(em)}");
        if (sorunlar.Count > 0 && !Ui.Confirm(this, "Kapatmadan önce dikkat:\n• " + string.Join("\n• ", sorunlar) + "\n\nYine de dosya kapatılsın mı?")) return;

        var v = FormDialog.Ask(this, "Dosyayı Kapat", new List<Field>
        {
            new("kapanis_tarihi", "Kapanış tarihi", "date", def: Utils.TodayIso(), required: true),
            new("yil", "Saklama süresi (yıl) – mevzuatınıza göre belirleyin", "int", def: 10, required: true),
            new("k1", "Vekalet ücreti ve masraflar tamamlandı", "check", def: sorunlar.Count == 0 ? 1L : 0L),
            new("k2", "Emanet bakiyesi kapatıldı / iade edildi", "check", def: Math.Abs(em) < 0.01 ? 1L : 0L),
            new("k3", "Asıl evraklar müvekkile iade edildi", "check"),
            new("k4", "Müvekkile kapanış bilgisi verildi", "check"),
            new("not", "Kapanış notu (sonuç, kesinleşme tarihi vb.)", "multiline"),
        });
        if (v == null) return;
        var eksik = new[] { ("k1", "ücret/masraf"), ("k2", "emanet"), ("k3", "evrak iadesi"), ("k4", "müvekkil bilgisi") }
            .Where(x => Convert.ToInt64(v[x.Item1] ?? 0) != 1).Select(x => x.Item2).ToList();
        var kt = Utils.ParseDate(Convert.ToString(v["kapanis_tarihi"])) ?? DateTime.Today;
        var not = Convert.ToString(v["not"]) ?? "";
        if (eksik.Count > 0) not += (not.Length > 0 ? "\n" : "") + "Kapanışta tamamlanmayanlar: " + string.Join(", ", eksik);
        Db.Update("davalar", id, new Dictionary<string, object?>
        {
            ["kapanis_tarihi"] = Utils.Iso(kt), ["saklama_bitis"] = Utils.Iso(kt.AddYears(Math.Max(1, Convert.ToInt32(v["yil"] ?? 10)))),
            ["kapanis_notu"] = not, ["asama_durum"] = "Karara Çıktı",
        });
        Yukle();
    }

    void KayitSil()
    {
        var c = _sec;
        if (c == null || c.S("tablo").Length == 0) { Ui.Info(this, "Silinebilen bir kayıt seçin (duruşma notu, müvekkil bilgi, zaman, evrak)."); return; }
        var ek = c.S("tablo") == "dava_evraklari" ? "\n(Dosyanın kendisi diskte kalır.)" : "";
        if (!Ui.Confirm(this, "Seçili kayıt silinsin mi?" + ek)) return;
        Db.Delete(c.S("tablo"), c.L("kid"));
        Yukle();
    }
}
