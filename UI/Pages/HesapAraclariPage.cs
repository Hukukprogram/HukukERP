using System.Windows;
using System.Windows.Controls;
using HukukErp.Core;
using HukukErp.Services;

namespace HukukErp.UI.Pages;

/// <summary>Faiz, harç ve vekalet ücreti (AAÜT) hesabı. Oran ve tutarlar hesap_parametreleri.json dosyasından gelir.</summary>
public sealed class HesapAraclariPage : BasePage
{
    HesapParametreleri _p;
    readonly TabControl _tab = new();

    // faiz
    readonly TextBox _fAna = new() { Text = "100.000", MinWidth = 160 };
    readonly TextBox _fBas = new() { MinWidth = 160 };
    readonly TextBox _fBit = new() { Text = DateTime.Today.ToString("dd.MM.yyyy"), MinWidth = 160 };
    readonly ComboBox _fTur = new() { MinWidth = 200 };
    readonly TextBox _fOran = new() { Text = "", MinWidth = 160 };
    readonly DataTable _fT = new(("Başlangıç", "bas", null), ("Bitiş", "bit", null), ("Gün", "gun", null), ("Yıllık %", "y", null), ("Faiz", "tutar", null));
    readonly TextBlock _fSonuc = Ui.Text("", 14, true, "text", true);

    // harç
    readonly TextBox _hDeger = new() { Text = "100.000", MinWidth = 160 };
    readonly ComboBox _hMah = new() { MinWidth = 200 };
    readonly TextBlock _hSonuc = Ui.Text("", 13, false, "text", true);

    // vekalet
    readonly TextBox _vDeger = new() { Text = "100.000", MinWidth = 160 };
    readonly ComboBox _vMah = new() { MinWidth = 280 };
    readonly TextBlock _vSonuc = Ui.Text("", 13, false, "text", true);

    public HesapAraclariPage(AppCtx ctx) : base(ctx)
    {
        _p = HesapParametreleri.Yukle(Ctx.Cfg);
        Top(Ui.PageHeader("Hesap Araçları", "Faiz • harç • vekalet ücreti (AAÜT). Değerler hesap_parametreleri.json dosyasından okunur; yılda bir güncelleyin."));
        _tab.Items.Add(Ui.Tab("Faiz", FaizUi()));
        _tab.Items.Add(Ui.Tab("Harç", HarcUi()));
        _tab.Items.Add(Ui.Tab("Vekalet Ücreti (AAÜT)", VekaletUi()));
        _tab.Items.Add(Ui.Tab("Parametreler", ParamUi()));
        Fill(_tab);
    }

    public override void Refresh()
    {
        _p = HesapParametreleri.Yukle(Ctx.Cfg);
        Doldur();
    }

    void Doldur()
    {
        if (_fTur.Items.Count == 0) { _fTur.Items.Add("Yasal faiz (3095)"); _fTur.Items.Add("Özel oran (aşağıya yazın)"); _fTur.SelectedIndex = 0; }
        var h = _hMah.SelectedIndex; _hMah.Items.Clear();
        _hMah.Items.Add("Sulh mahkemesi / icra tetkik mercii"); _hMah.Items.Add("Asliye mahkemesi / idare mahkemesi");
        _hMah.SelectedIndex = h < 0 ? 1 : h;
        var v = _vMah.SelectedIndex; _vMah.Items.Clear();
        foreach (var m in _p.Tarife.Maktular) _vMah.Items.Add($"{m.Ad} (maktu {Utils.FmtMoney(m.Tutar)})");
        _vMah.SelectedIndex = v < 0 ? 0 : v;
    }

    static StackPanel Form(params (string Etiket, FrameworkElement K)[] a)
    {
        var sp = new StackPanel();
        foreach (var (e, k) in a)
        {
            var l = Ui.Muted(e, false); l.Margin = new Thickness(0, 6, 0, 3);
            k.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(l); sp.Children.Add(k);
        }
        return sp;
    }

    static FrameworkElement Yan(UIElement sol, UIElement sag)
    {
        var g = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var c1 = new Card(); c1.Fill(sol); c1.Margin = new Thickness(0, 0, 12, 0);
        var c2 = new Card(); c2.Fill(sag); Grid.SetColumn(c2, 1);
        g.Children.Add(c1); g.Children.Add(c2);
        return g;
    }

    UIElement Uyari() => Ui.Muted($"⚠ Bilgi amaçlıdır; mahkeme/icra/bilirkişi hesabının yerini tutmaz. Değerlerin tarihi: {_p.GuncellemeTarihi}. Resmî kaynakla doğrulayın.", true);

    // ---------------------------------------------------------------- faiz
    UIElement FaizUi()
    {
        Doldur();
        var form = Form(("Anapara (₺)", _fAna), ("Başlangıç tarihi (gg.aa.yyyy)", _fBas), ("Bitiş tarihi", _fBit), ("Faiz türü", _fTur), ("Özel yıllık oran % (isteğe bağlı)", _fOran));
        form.Children.Add(new Border { Height = 10 });
        form.Children.Add(Ui.Btn("Hesapla", "Primary", FaizHesapla));
        var sag = new DockPanel();
        var ust = new StackPanel(); ust.Children.Add(_fSonuc); ust.Children.Add(Uyari());
        DockPanel.SetDock(ust, Dock.Bottom); sag.Children.Add(ust); sag.Children.Add(_fT);
        return Yan(form, sag);
    }

    void FaizHesapla()
    {
        var bas = Utils.ParseDate(_fBas.Text); var bit = Utils.ParseDate(_fBit.Text);
        if (bas == null || bit == null) { Ui.Warn(this, "Tarihleri gg.aa.yyyy biçiminde girin."); return; }
        double? ozel = _fTur.SelectedIndex == 1 ? Utils.ParseMoney(_fOran.Text) : null;
        if (ozel == 0) { if (_fTur.SelectedIndex == 1) { Ui.Warn(this, "Özel oran seçtiniz; oranı yazın."); return; } ozel = null; }
        var ana = Utils.ParseMoney(_fAna.Text);
        var s = _p.Faiz(ana, bas.Value, bit.Value, ozel);
        if (s.Count == 0) { Ui.Warn(this, "Bitiş tarihi başlangıçtan sonra olmalı."); return; }
        _fT.Load(s.Select(x => new Row { ["bas"] = x.Bas.ToString("dd.MM.yyyy"), ["bit"] = x.Bit.ToString("dd.MM.yyyy"), ["gun"] = x.Gun.ToString(), ["y"] = x.Yuzde.ToString("0.##"), ["tutar"] = Utils.FmtMoney(x.Tutar) }).ToList());
        var top = s.Sum(x => x.Tutar);
        _fSonuc.Text = $"Faiz: {Utils.FmtMoney(top)}   •   Anapara + faiz: {Utils.FmtMoney(ana + top)}   •   {s.Sum(x => x.Gun)} gün (basit faiz, yıl=365 gün)";
    }

    // ---------------------------------------------------------------- harç
    UIElement HarcUi()
    {
        Doldur();
        var form = Form(("Dava değeri / talep (₺)", _hDeger), ("Mahkeme", _hMah));
        form.Children.Add(new Border { Height = 10 });
        form.Children.Add(Ui.Btn("Hesapla", "Primary", HarcHesapla));
        var sp = new StackPanel(); sp.Children.Add(_hSonuc); sp.Children.Add(new Border { Height = 8 }); sp.Children.Add(Uyari());
        return Yan(form, sp);
    }

    void HarcHesapla()
    {
        var h = _p.Harclar; var d = Utils.ParseMoney(_hDeger.Text);
        var nispi = d * h.NispiBinde / 1000.0;
        var pesin = nispi * h.PesinOran;
        var bas = _hMah.SelectedIndex == 0 ? h.BasvurmaSulh : h.BasvurmaAsliye;
        _hSonuc.Text =
            $"Nispi karar ve ilam harcı (binde {h.NispiBinde:0.##}): {Utils.FmtMoney(nispi)}\n" +
            $"Peşin yatırılacak kısım (%{h.PesinOran * 100:0}): {Utils.FmtMoney(pesin)}\n" +
            $"Başvurma harcı: {Utils.FmtMoney(bas)}\n" +
            $"Dava açarken tahmini toplam (başvurma + peşin harç): {Utils.FmtMoney(bas + pesin)}\n" +
            $"Maktu karar ve ilam harcı (değeri belli olmayan davalar): {Utils.FmtMoney(h.MaktuKararIlam)}\n" +
            $"Vekalet harcı (dosyaya başına, tahmini): {Utils.FmtMoney(h.VekaletHarci)}\n\n" +
            "Not: Gider avansı, tebligat/posta, bilirkişi, keşif ve damga vergisi ayrıca hesaplanır. Dava türüne göre istisna ve muafiyetler olabilir.";
    }

    // ---------------------------------------------------------------- vekalet ücreti
    UIElement VekaletUi()
    {
        Doldur();
        var form = Form(("Kabul/ret edilen değer (₺)", _vDeger), ("Mahkeme / iş türü (maktu alt sınır)", _vMah));
        form.Children.Add(new Border { Height = 10 });
        form.Children.Add(Ui.Btn("Hesapla", "Primary", VekaletHesapla));
        var sp = new StackPanel(); sp.Children.Add(_vSonuc); sp.Children.Add(new Border { Height = 8 }); sp.Children.Add(Uyari());
        return Yan(form, sp);
    }

    void VekaletHesapla()
    {
        var d = Utils.ParseMoney(_vDeger.Text);
        var ay = new List<(double Tutar, double Yuzde, double Pay)>();
        var nispi = _p.NispiVekalet(d, ay);
        var i = Math.Max(0, _vMah.SelectedIndex);
        var maktu = i < _p.Tarife.Maktular.Count ? _p.Tarife.Maktular[i] : null;
        var sonuc = Math.Max(nispi, maktu?.Tutar ?? 0);
        if (sonuc > d && d > 0) sonuc = d;   // ücret, kabul/ret edilen değeri aşamaz
        var ayr = string.Join("\n", ay.Select(x => $"   {Utils.FmtMoney(x.Tutar)} × %{x.Yuzde:0.##} = {Utils.FmtMoney(x.Pay)}"));
        _vSonuc.Text =
            $"Nispi ücret (dilimli):\n{ayr}\nToplam nispi: {Utils.FmtMoney(nispi)}\n" +
            $"Maktu alt sınır ({maktu?.Ad}): {Utils.FmtMoney(maktu?.Tutar ?? 0)}\n\n" +
            $"Asgari vekalet ücreti: {Utils.FmtMoney(sonuc)}\n\n" +
            "Not: Sonuç, nispi ücretin maktu alt sınırdan az olamayacağı ve değeri aşamayacağı kuralına göre hesaplandı. Kısmi kabul/ret, birden fazla talep ve özel haller için tarifeyi kontrol edin.";
    }

    // ---------------------------------------------------------------- parametreler
    UIElement ParamUi()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        sp.Children.Add(Ui.Text(_p.Aciklama, 13, true, "warn", true));
        sp.Children.Add(Ui.Muted("Dosya: " + HesapParametreleri.Yol(Ctx.Cfg)));
        sp.Children.Add(Ui.Muted("Kaynaklar:\n • Yasal faiz: " + _p.YasalFaiz.LastOrDefault()?.Kaynak + "\n • Harç: " + _p.Harclar.Kaynak + "\n • AAÜT: " + _p.Tarife.Kaynak));
        var b = Ui.HRow(Ui.Btn("📝 Dosyayı aç (düzenle)", "", () => Ui.OpenExternal(HesapParametreleri.Yol(Ctx.Cfg))),
                        Ui.Btn("↻ Yeniden oku", "", () => { _p = HesapParametreleri.Yukle(Ctx.Cfg); Doldur(); Ui.Info(this, "Parametreler yeniden okundu."); }));
        b.Margin = new Thickness(0, 10, 0, 0);
        sp.Children.Add(b);
        sp.Children.Add(Ui.Muted("\nTicari/avans faiz oranı değişken olduğundan dahil edilmedi: faiz sekmesinde 'Özel oran' ile TCMB oranını yazabilirsiniz. Güncelleme dosyayı değiştirir; program güncellemesi dosyanızı ezmez.", true));
        return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
