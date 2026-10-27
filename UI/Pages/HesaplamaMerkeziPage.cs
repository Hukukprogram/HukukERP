using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using HukukErp.Core;
using HukukErp.Services;

namespace HukukErp.UI.Pages;

/// <summary>Hesaplamalar: İşçilik alacakları, kaza tazminatı, net↔brüt ve (en altta) infaz hesaplama.</summary>
public sealed class HesaplamaMerkeziPage : BasePage
{
    const string Not = "2026 parametreleri; sonuçlar bilgi amaçlıdır, bilirkişi/mahkeme hesabının yerini tutmaz.";

    HesaplamaMerkeziPage(AppCtx ctx, string baslik, string alt, UIElement icerik) : base(ctx)
    {
        Top(Ui.PageHeader(baslik, alt));
        Fill(icerik);
    }

    public static BasePage Iscilik(AppCtx ctx) => new HesaplamaMerkeziPage(ctx, "İşçilik Alacakları Hesabı", "Kıdem, ihbar, yıllık izin, UBGT, hafta tatili, fazla mesai. " + Not, new IscilikSekmesi());
    public static BasePage Kaza(AppCtx ctx) => new HesaplamaMerkeziPage(ctx, "Kaza Tazminatı Hesabı", "Sürekli iş göremezlik (maluliyet) kaynaklı maddi tazminat, TRH-2010. " + Not, new KazaSekmesi());
    public static BasePage NetBrut(AppCtx ctx) => new HesaplamaMerkeziPage(ctx, "Net ↔ Brüt Ücret Hesabı", "Kümülatif gelir vergisi, SGK ve damga vergisi. " + Not, new NetBrutSekmesi());

    // ================================================================== ortak yardımcılar
    sealed class Girdi
    {
        public StackPanel Panel { get; } = new();

        public T Ekle<T>(string etiket, T kontrol) where T : FrameworkElement
        {
            var l = Ui.Muted(etiket, false);
            l.Margin = new Thickness(0, 6, 0, 3);
            Panel.Children.Add(l);
            Panel.Children.Add(kontrol);
            return kontrol;
        }
    }

    static TextBox Sayi(string varsayilan) => new() { Text = varsayilan, MinWidth = 150, HorizontalAlignment = HorizontalAlignment.Left };

    static double Num(TextBox t) => Utils.ParseMoney(t.Text);

    static string N2(double v) => Utils.FmtMoney(v);

    static string Para(object? v) => v == null ? "" : Utils.FmtMoney(Convert.ToDouble(v, CultureInfo.InvariantCulture));

    static Row Satir(string kalem, double? brut = null, double? kesinti = null, double? net = null, string not = "")
    {
        var r = new Row { ["kalem"] = kalem, ["not"] = not };
        r["brut"] = brut; r["kesinti"] = kesinti; r["net"] = net;
        return r;
    }

    static FrameworkElement Duzen(Girdi g, Button hesapla, UIElement sonuc, UIElement alt)
    {
        var sol = new Card();
        sol.Bottom(hesapla, 0);
        sol.Fill(new ScrollViewer { Content = g.Panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var sag = new Card();
        sag.Bottom(alt, 0);
        sag.Fill(sonuc);
        var s = Ui.HSplit(sol, sag, 4, 6);
        s.Margin = new Thickness(0, 10, 0, 0);
        return s;
    }

    static TextBlock NotMetni(string t) { var x = Ui.Muted(t); x.Margin = new Thickness(0, 8, 0, 0); return x; }

    // ================================================================== 1) İşçilik alacakları
    sealed class IscilikSekmesi : UserControl
    {
        readonly DateBox _giris = new(), _cikis = new();
        readonly TextBox _ucret = Sayi("33030"), _yan = Sayi("0"), _tavan = Sayi("0"), _kum = Sayi("0");
        readonly CheckBox _kidem = new() { Content = "Kıdem tazminatına hak kazanıyor", IsChecked = true };
        readonly CheckBox _ihbar = new() { Content = "İhbar tazminatına hak kazanıyor", IsChecked = true };
        readonly TextBox _izin = Sayi("0"), _ubgtGun = Sayi("0"), _ubgtKat = Sayi("1"), _htGun = Sayi("0"), _htKat = Sayi("1,5");
        readonly TextBox _fmSaat = Sayi("0"), _fmHafta = Sayi("0"), _fmInd = Sayi("0"), _diger = Sayi("0");
        readonly DataTable _t = new(("Kalem", "kalem", null), ("Brüt", "brut", Para), ("Kesinti", "kesinti", Para), ("Net", "net", Para), ("Açıklama", "not", null));
        readonly TextBlock _ozet = Ui.Muted("");

        public IscilikSekmesi()
        {
            var g = new Girdi();
            g.Ekle("İşe giriş tarihi  GG.AA.YYYY", _giris);
            g.Ekle("İşten çıkış tarihi  GG.AA.YYYY", _cikis);
            g.Ekle("Aylık brüt ücret (₺)", _ucret);
            g.Ekle("Aylık yan haklar, brüt (yol, yemek, prim… giydirilmiş ücret için) (₺)", _yan);
            g.Panel.Children.Add(_kidem); g.Panel.Children.Add(_ihbar);
            _kidem.Margin = new Thickness(0, 8, 0, 0); _ihbar.Margin = new Thickness(0, 4, 0, 0);
            g.Ekle("Kıdem tavanı (0 = çıkış tarihinden otomatik)", _tavan);
            g.Ekle("Yıl başından beri GV matrahı (çıkış yılı, ₺)", _kum);
            g.Ekle("Kullanılmayan yıllık izin (gün)", _izin);
            g.Ekle("Çalışılan UBGT günü / katsayı", _ubgtGun); g.Panel.Children.Add(_ubgtKat);
            g.Ekle("Çalışılan hafta tatili günü / katsayı", _htGun); g.Panel.Children.Add(_htKat);
            g.Ekle("Haftalık fazla mesai (saat)", _fmSaat);
            g.Ekle("Fazla mesai yapılan hafta sayısı", _fmHafta);
            g.Ekle("Fazla mesaide hakkaniyet indirimi (%)", _fmInd);
            g.Ekle("Diğer brüt alacaklar (ödenmemiş ücret, ikramiye… ₺)", _diger);
            var b = Ui.Btn("Hesapla", "Primary", Hesapla);
            b.HorizontalAlignment = HorizontalAlignment.Left;
            var alt = new StackPanel();
            alt.Children.Add(_ozet);
            alt.Children.Add(NotMetni("Tahmini hesaptır. Kıdem: giydirilmiş brüt × çalışma yılı (tavanla sınırlı), yalnızca damga vergisi kesilir. İhbar: GV + damga. " +
                "Diğer kalemlerde SGK+işsizlik %15, GV ve damga düşülür. Fazla mesai saatlik ücreti: aylık/225 × 1,5. UBGT/hafta tatili katsayıları (aylık ücretli işçide ücret zaten ödendiği için " +
                "UBGT'de 1, hafta tatilinde 1,5 varsayıldı) içtihada göre değiştirilebilir. Kıdem tavanı tabloları elle değiştirilebilir."));
            Content = Duzen(g, b, _t, alt);
        }

        void Hesapla()
        {
            var gir = Utils.ParseDate(_giris.Text); var cik = Utils.ParseDate(_cikis.Text);
            if (gir == null || cik == null || cik < gir) { Ui.Warn(this, "Geçerli giriş ve çıkış tarihi girin."); return; }
            var ucret = Num(_ucret); var giydirilmis = ucret + Num(_yan);
            var gunluk = ucret / 30.0; var gGun = giydirilmis / 30.0;
            var kum = Num(_kum);
            var rows = new List<Row>();
            double tBrut = 0, tKes = 0, tNet = 0;
            void Ekle(Row r)
            {
                rows.Add(r);
                tBrut += Convert.ToDouble(r["brut"] ?? 0.0); tKes += Convert.ToDouble(r["kesinti"] ?? 0.0); tNet += Convert.ToDouble(r["net"] ?? 0.0);
            }

            var (y, m, d) = Bordro.Sure(gir.Value, cik.Value.AddDays(1));
            var yil = y + m / 12.0 + d / 365.0;
            var toplamAy = y * 12 + m + d / 30.0;
            var tavan = Num(_tavan) > 0 ? Num(_tavan) : Bordro.KidemTavani(cik.Value);
            var tavanNot = tavan > 0 ? $"tavan {N2(tavan)}" : "tavan bilinmiyor (sınırsız hesaplandı)";

            if (_kidem.IsChecked == true)
            {
                var esas = tavan > 0 ? Math.Min(giydirilmis, tavan) : giydirilmis;
                var brut = esas * yil; var dv = brut * Bordro.Damga;
                Ekle(Satir("Kıdem tazminatı", brut, dv, brut - dv, $"{y} yıl {m} ay {d} gün; {tavanNot}"));
            }
            if (_ihbar.IsChecked == true)
            {
                var hafta = Bordro.IhbarHaftasi(toplamAy);
                var brut = gGun * 7 * hafta;
                var gv = Bordro.GelirVergisi(kum + brut) - Bordro.GelirVergisi(kum);
                var dv = brut * Bordro.Damga;
                kum += brut;
                Ekle(Satir("İhbar tazminatı", brut, gv + dv, brut - gv - dv, $"{hafta} hafta"));
            }

            // ücret niteliğindeki kalemler (SGK + GV + damga)
            var kalemler = new List<(string Ad, double Brut, string Not)>();
            var izin = Num(_izin);
            if (izin > 0) kalemler.Add(("Yıllık izin ücreti", gGun * izin, $"{izin:0.##} gün"));
            if (Num(_ubgtGun) > 0) kalemler.Add(("UBGT ücreti", gunluk * Num(_ubgtGun) * Num(_ubgtKat), $"{Num(_ubgtGun):0.##} gün × {Num(_ubgtKat):0.##}"));
            if (Num(_htGun) > 0) kalemler.Add(("Hafta tatili ücreti", gunluk * Num(_htGun) * Num(_htKat), $"{Num(_htGun):0.##} gün × {Num(_htKat):0.##}"));
            if (Num(_fmSaat) > 0 && Num(_fmHafta) > 0)
            {
                var ind = Math.Clamp(Num(_fmInd), 0, 100) / 100.0;
                var saat = Num(_fmSaat) * Num(_fmHafta);
                kalemler.Add(("Fazla mesai ücreti", saat * (ucret / 225.0) * 1.5 * (1 - ind), $"{saat:0.##} saat, %{Num(_fmInd):0.##} indirim"));
            }
            if (Num(_diger) > 0) kalemler.Add(("Diğer alacaklar", Num(_diger), ""));
            foreach (var (ad, brut, not) in kalemler)
            {
                var sgk = brut * (Bordro.SgkIsci + Bordro.Issizlik);
                var matrah = brut - sgk;
                var gv = Bordro.GelirVergisi(kum + matrah) - Bordro.GelirVergisi(kum);
                kum += matrah;
                var dv = brut * Bordro.Damga;
                Ekle(Satir(ad, brut, sgk + gv + dv, brut - sgk - gv - dv, not));
            }

            rows.Add(Satir("TOPLAM", tBrut, tKes, tNet));
            _t.Load(rows);
            _ozet.Text = $"Çalışma süresi: {y} yıl {m} ay {d} gün • Toplam brüt: {N2(tBrut)} • Tahmini net: {N2(tNet)}";
        }
    }

    // ================================================================== 2) Kaza tazminatı (sürekli iş göremezlik / sakatlık)
    sealed class KazaSekmesi : UserControl
    {
        readonly DateBox _dogum = new(), _kaza = new(), _hesap = new(DateTime.Today.ToString("yyyy-MM-dd"));
        readonly ComboBox _cins = new() { MinWidth = 150, HorizontalAlignment = HorizontalAlignment.Left };
        readonly TextBox _oran = Sayi("10"), _gelir = Sayi("28075,50"), _pasif = Sayi("28075,50"), _aktifYas = Sayi("60");
        readonly TextBox _faiz = Sayi("0"), _kusur = Sayi("0"), _gecici = Sayi("0"), _mahsup = Sayi("0"), _omur = Sayi("0");
        readonly DataTable _t = new(("Dönem / kalem", "kalem", null), ("Süre", "not", null), ("Tutar", "brut", Para));
        readonly TextBlock _ozet = Ui.Muted("");

        public KazaSekmesi()
        {
            _cins.Items.Add(new ComboItem { Value = false, Label = "Erkek" });
            _cins.Items.Add(new ComboItem { Value = true, Label = "Kadın" });
            _cins.SelectedIndex = 0;
            var g = new Girdi();
            g.Ekle("Doğum tarihi  GG.AA.YYYY", _dogum);
            g.Ekle("Cinsiyet", _cins);
            g.Ekle("Kaza tarihi  GG.AA.YYYY", _kaza);
            g.Ekle("Hesap tarihi  GG.AA.YYYY", _hesap);
            g.Ekle("Maluliyet (sürekli iş göremezlik) oranı (%)", _oran);
            g.Ekle("Geçici iş göremezlik süresi (ay, %100 kabul edilir)", _gecici);
            g.Ekle("Kazalının aylık net geliri (aktif dönem, ₺)", _gelir);
            g.Ekle("Pasif dönem aylık geliri (60 yaş sonrası, ₺)", _pasif);
            g.Ekle("Aktif dönem sonu yaşı", _aktifYas);
            g.Ekle("Teknik faiz (% yıllık; 0 = faizsiz/progresif rant)", _faiz);
            g.Ekle("Beklenen ömür, yıl (0 = TRH-2010 tablosundan otomatik)", _omur);
            g.Ekle("Kusur / müterafik kusur indirimi (%)", _kusur);
            g.Ekle("SGK'nın bağladığı gelirin peşin sermaye değeri (mahsup, ₺)", _mahsup);
            var b = Ui.Btn("Hesapla", "Primary", Hesapla);
            b.HorizontalAlignment = HorizontalAlignment.Left;
            var alt = new StackPanel();
            alt.Children.Add(_ozet);
            alt.Children.Add(NotMetni("Yalnızca yaralanma/maluliyet kaynaklı maddi tazminatı hesaplar (geçici + bilinen + aktif + pasif dönem). Ölüm/destekten yoksun kalma bu ekranda yoktur. " +
                "Beklenen ömür TRH-2010 tablosundan alınır (kontrol edip gerekirse elle girin). Teknik faiz ve gelir artışı uygulaması mahkeme/bilirkişi tercihine göre değişir; " +
                "SGK mahsubu için kurumun hesapladığı peşin sermaye değerini girin."));
            Content = Duzen(g, b, _t, alt);
        }

        void Hesapla()
        {
            var dg = Utils.ParseDate(_dogum.Text); var kz = Utils.ParseDate(_kaza.Text); var hs = Utils.ParseDate(_hesap.Text);
            if (dg == null || kz == null || hs == null || kz < dg || hs < kz) { Ui.Warn(this, "Doğum ≤ kaza ≤ hesap tarihi olacak şekilde geçerli tarihler girin."); return; }
            var kadin = (_cins.SelectedItem as ComboItem)?.Value is true;
            var oran = Math.Clamp(Num(_oran), 0, 100) / 100.0;
            var gelir = Num(_gelir); var pasif = Num(_pasif);
            var i = Num(_faiz) / 100.0;
            var yas = (hs.Value - dg.Value).TotalDays / 365.25;
            var ex = Num(_omur) > 0 ? Num(_omur) : Bordro.BeklenenOmur(yas, kadin);
            var omurYas = yas + ex;
            var aktifSon = Math.Max(yas, Num(_aktifYas));

            var rows = new List<Row>();
            double toplam = 0;
            void Ekle(string ad, string sure, double tutar) { rows.Add(Satir(ad, tutar, null, null, sure)); toplam += tutar; }

            var bilinenAy = (hs.Value - kz.Value).TotalDays / 30.4375;
            var gecAy = Math.Min(Math.Max(0, Num(_gecici)), bilinenAy);
            if (gecAy > 0) Ekle("Geçici iş göremezlik (%100)", $"{gecAy:0.##} ay", gecAy * gelir);
            Ekle("Bilinen dönem (kaza → hesap tarihi)", $"{bilinenAy - gecAy:0.##} ay", Math.Max(0, bilinenAy - gecAy) * gelir * oran);

            var aktifAy = Math.Max(0, Math.Min(aktifSon, omurYas) - yas) * 12;
            var pasifAy = Math.Max(0, omurYas - Math.Max(yas, aktifSon)) * 12;
            Ekle("Aktif dönem (hesap tarihi → aktif dönem sonu)", $"{aktifAy:0.##} ay", Bordro.Rant(gelir * oran, aktifAy, 0, i));
            Ekle("Pasif dönem (aktif dönem sonu → beklenen ömür)", $"{pasifAy:0.##} ay", Bordro.Rant(pasif * oran, pasifAy, aktifAy, i));

            rows.Add(Satir("Toplam zarar", toplam));
            var kusur = Math.Clamp(Num(_kusur), 0, 100) / 100.0;
            var kusurlu = toplam * (1 - kusur);
            if (kusur > 0) rows.Add(Satir($"Kusur indirimi sonrası (%{Num(_kusur):0.##})", kusurlu));
            var sonuc = Math.Max(0, kusurlu - Num(_mahsup));
            if (Num(_mahsup) > 0) rows.Add(Satir("SGK mahsubu sonrası hükmedilecek tutar", sonuc));
            _t.Load(rows);
            _ozet.Text = $"Hesap tarihindeki yaş: {yas:0.0} • Beklenen kalan ömür: {ex:0.00} yıl (TRH-2010) • Sonuç: {N2(sonuc)}";
        }
    }

    // ================================================================== 3) Net ↔ Brüt
    sealed class NetBrutSekmesi : UserControl
    {
        readonly ComboBox _yon = new() { MinWidth = 150, HorizontalAlignment = HorizontalAlignment.Left };
        readonly TextBox _tutar = Sayi("33030");
        readonly CheckBox _ist = new() { Content = "Asgari ücret GV + damga vergisi istisnasını uygula", IsChecked = true };
        readonly DataTable _t = new(("Ay", "ay", null), ("Brüt", "brut", Para), ("SGK (%14)", "sgk", Para), ("İşsizlik (%1)", "iss", Para),
            ("Gelir vergisi", "gv", Para), ("Damga", "dv", Para), ("Net", "net", Para), ("Küm. matrah", "kum", Para));
        readonly TextBlock _ozet = Ui.Muted("");
        static readonly string[] Aylar = { "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık" };

        public NetBrutSekmesi()
        {
            _yon.Items.Add(new ComboItem { Value = false, Label = "Brüt → Net" });
            _yon.Items.Add(new ComboItem { Value = true, Label = "Net → Brüt" });
            _yon.SelectedIndex = 0;
            var g = new Girdi();
            g.Ekle("Yön", _yon);
            g.Ekle("Aylık tutar (₺) — brüt veya net", _tutar);
            g.Panel.Children.Add(_ist);
            _ist.Margin = new Thickness(0, 10, 0, 0);
            var b = Ui.Btn("Hesapla", "Primary", Hesapla);
            b.HorizontalAlignment = HorizontalAlignment.Left;
            var alt = new StackPanel();
            alt.Children.Add(_ozet);
            alt.Children.Add(NotMetni("Ocak–Aralık kümülatif gelir vergisi matrahıyla hesaplanır (2026 tarifesi: %15/%20/%27/%35/%40). SGK işçi payı %14, işsizlik %1, damga vergisi binde 7,59; " +
                "SGK tavanı 297.270 ₺. Her ay aynı tutar alınıyor varsayılır; ücret değişiyorsa o ay için ayrı hesaplayın. Engelli, AGİ ve özel indirimler dahil değildir."));
            Content = Duzen(g, b, _t, alt);
        }

        void Hesapla()
        {
            var tutar = Num(_tutar);
            if (tutar <= 0) { Ui.Warn(this, "Pozitif bir tutar girin."); return; }
            var net = (_yon.SelectedItem as ComboItem)?.Value is true;
            var ay = Bordro.YillikTablo(tutar, net, _ist.IsChecked == true);
            var rows = ay.Select(a =>
            {
                var r = new Row { ["ay"] = Aylar[a.No - 1] };
                r["brut"] = a.Brut; r["sgk"] = a.Sgk; r["iss"] = a.Issiz; r["gv"] = a.Gv; r["dv"] = a.Damga; r["net"] = a.Net; r["kum"] = a.KumMatrah;
                return r;
            }).ToList();
            var tot = new Row { ["ay"] = "YILLIK TOPLAM" };
            tot["brut"] = ay.Sum(a => a.Brut); tot["sgk"] = ay.Sum(a => a.Sgk); tot["iss"] = ay.Sum(a => a.Issiz);
            tot["gv"] = ay.Sum(a => a.Gv); tot["dv"] = ay.Sum(a => a.Damga); tot["net"] = ay.Sum(a => a.Net); tot["kum"] = ay[^1].KumMatrah;
            rows.Add(tot);
            _t.Load(rows);
            _ozet.Text = net
                ? $"Ocak: net {N2(ay[0].Net)} için brüt {N2(ay[0].Brut)} • Aralık brüt: {N2(ay[11].Brut)} (kümülatif dilim nedeniyle brüt yıl içinde artar)"
                : $"Ocak neti: {N2(ay[0].Net)} • Aralık neti: {N2(ay[11].Net)} • Yıllık net: {N2(ay.Sum(a => a.Net))}";
        }
    }
}
