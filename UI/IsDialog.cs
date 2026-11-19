using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HukukErp.Core;
using HukukErp.Services;
using HukukErp.UI.Pages;

namespace HukukErp.UI;

/// <summary>Süreli iş (yeni / düzenle) tek ekranı: başlık, tür, bağlı dava, tebliğ tarihi + süre (son gün anında hesaplanır), notlar.
/// "Tebligat süresi" artık ayrı bir pencere değil; aynı forma gömülüdür (katalogdan da doldurulabilir).</summary>
public class IsDialog : Window
{
    static readonly string[] Turler = { "Hukuk", "İcra", "Ceza", "İdari", "İş", "Yüksek Mahkeme", "Yasal Süre", "Mahkeme Verdiği Süre", "Tebligat Cevabı", "Diğer" };
    static readonly string[] Durumlar = { "Açık", "Tamamlandı" };

    readonly Db _db;
    readonly Dictionary<string, object?> _eski;
    readonly TextBox _baslik = new() { Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
    readonly ComboBox _tur = new() { Height = 36 }, _durum = new() { Height = 36 };
    readonly SearchPick _dava = new() { MinWidth = 200, Placeholder = "— Bağlı dava yok —" };
    readonly List<long> _davaIds = new();
    readonly TextBox _dayanak = new() { Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
    readonly ComboBox _g = new() { Width = 68, Height = 36 }, _a = new() { Width = 116, Height = 36 }, _y = new() { Width = 82, Height = 36 };
    readonly TextBox _n = new() { Width = 80, Height = 36, VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, FontSize = 15, ToolTip = "Örn. 30" };
    readonly ComboBox _b = new() { Width = 104, Height = 36 };
    readonly CheckBox _e = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBox _son = new() { Width = 150, Height = 36, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 15, FontWeight = FontWeights.Bold, ToolTip = "GG.AA.YYYY — süre yazınca otomatik dolar, isterseniz elle değiştirin" };
    readonly TextBlock _ac = Ui.Muted("", true);
    readonly TextBox _notlar = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 70, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(6) };
    Dictionary<string, object?>? _sonuc;
    bool _yukleniyor = true;
    string _baslangicIso = "";

    IsDialog(DependencyObject owner, Db db, IDictionary<string, object?>? values)
    {
        _db = db;
        _eski = values == null ? new() : new Dictionary<string, object?>(values);
        bool duzenle = _eski.ContainsKey("id");
        Title = duzenle ? "Süreli İşi Düzenle" : "Yeni Süreli İş";
        SetResourceReference(BackgroundProperty, "bg");
        SetResourceReference(ForegroundProperty, "text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Owner = Window.GetWindow(owner);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 600; SizeToContent = SizeToContent.Height; MaxHeight = SystemParameters.WorkArea.Height - 40;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;

        string S(string k) => _eski.TryGetValue(k, out var o) ? Convert.ToString(o) ?? "" : "";

        foreach (var t in Turler) _tur.Items.Add(t);
        foreach (var d in Durumlar) _durum.Items.Add(d);
        var tur = S("sure_turu"); _tur.SelectedIndex = Math.Max(0, Array.IndexOf(Turler, tur.Length > 0 ? tur : "Yasal Süre"));
        var du = S("durum"); _durum.SelectedIndex = Math.Max(0, Array.IndexOf(Durumlar, du.Length > 0 ? du : "Açık"));
        _baslik.Text = S("baslik"); _dayanak.Text = S("dayanak"); _notlar.Text = S("notlar");

        // bağlı dava
        _dava.Items.Add(new ComboItem { Value = 0L, Label = "— Bağlı dava yok —" }); _davaIds.Add(0);
        foreach (var d in db.Query("SELECT d.id, d.mahkeme_adi, d.esas_no, m.ad_soyad FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id ORDER BY d.id DESC"))
        {
            _dava.Items.Add(new ComboItem { Value = d.L("id"), Label = $"{d.S("mahkeme_adi")} {d.S("esas_no")} — {d.S("ad_soyad")}".Trim(' ', '—', ' ') });
            _davaIds.Add(d.L("id"));
        }
        long did = 0; if (_eski.TryGetValue("dava_id", out var dv) && dv != null) long.TryParse(Convert.ToString(dv), out did);
        _dava.SelectedIndex = Math.Max(0, _davaIds.IndexOf(did));

        // tebliğ tarihi + süre
        for (int i = 1; i <= 31; i++) _g.Items.Add(i);
        foreach (var ay in Utils.Tr.DateTimeFormat.MonthNames.Take(12)) _a.Items.Add(ay);
        var bas = Utils.ParseDate(S("baslangic")) ?? DateTime.Today;
        _baslangicIso = S("baslangic");
        for (int y = Math.Min(DateTime.Today.Year, bas.Year) - 2; y <= DateTime.Today.Year + 2; y++) _y.Items.Add(y);
        _g.SelectedItem = bas.Day; _a.SelectedIndex = bas.Month - 1; _y.SelectedItem = bas.Year;
        _b.Items.Add("Gün"); _b.Items.Add("Hafta"); _b.Items.Add("Ay"); _b.SelectedIndex = 0;
        _e.IsChecked = !duzenle;
        var sn = Utils.ParseDate(S("son_tarih"));
        _son.Text = sn == null ? "" : sn.Value.ToString("dd.MM.yyyy");
        _ac.Text = duzenle ? "" : "Süreyi yazınca son gün otomatik hesaplanır; isterseniz son günü doğrudan yazabilirsiniz.";

        // yerleşim
        static TextBlock Et(string t) { var x = Ui.Text(t, 12.5, true); x.Margin = new Thickness(0, 12, 0, 5); return x; }
        static TextBlock Sl(string t) { var x = Ui.Muted(t, false); x.VerticalAlignment = VerticalAlignment.Center; x.Margin = new Thickness(7, 0, 7, 0); return x; }

        var kok = new StackPanel { Margin = new Thickness(24, 20, 24, 8) };
        var baslikSatir = new DockPanel();
        var kat = Ui.Btn("📚 Katalogdan doldur", "", KatalogdanDoldur);
        DockPanel.SetDock(kat, Dock.Right);
        baslikSatir.Children.Add(kat); baslikSatir.Children.Add(Ui.Text(duzenle ? "Süreli İşi Düzenle" : "Yeni Süreli İş", 18, true));
        kok.Children.Add(baslikSatir);
        var alt0 = Ui.Muted("Tebligat süresi dahil tüm süreli işler buradan girilir.", false); alt0.Margin = new Thickness(0, 2, 0, 4); kok.Children.Add(alt0);

        kok.Children.Add(Et("İş / Süre Başlığı *")); kok.Children.Add(_baslik);

        var iki = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -10, 0) };
        StackPanel Kol(string et, UIElement c) { var sp = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; sp.Children.Add(Et(et)); sp.Children.Add(c); return sp; }
        iki.Children.Add(Kol("Süre türü", _tur)); iki.Children.Add(Kol("Durum", _durum)); iki.Children.Add(Kol("Bağlı dava", _dava));
        kok.Children.Add(iki);

        // tebligat süresi kartı
        var kart = new StackPanel();
        kart.Children.Add(Ui.Text("⚖ Tebliğ tarihi ve süre", 13, true));
        var t1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var tt = Ui.Muted("Tebliğ:", false); tt.VerticalAlignment = VerticalAlignment.Center; tt.Width = 60; t1.Children.Add(tt);
        t1.Children.Add(_g); t1.Children.Add(Sl("/")); t1.Children.Add(_a); t1.Children.Add(Sl("/")); t1.Children.Add(_y);
        kart.Children.Add(t1);
        var t2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var st = Ui.Muted("Süre:", false); st.VerticalAlignment = VerticalAlignment.Center; st.Width = 60; t2.Children.Add(st);
        t2.Children.Add(_n); _b.Margin = new Thickness(8, 0, 0, 0); t2.Children.Add(_b);
        var ornek = Ui.Muted("Örn. 30", false); ornek.VerticalAlignment = VerticalAlignment.Center; ornek.Margin = new Thickness(10, 0, 0, 0); t2.Children.Add(ornek);
        kart.Children.Add(t2);
        var t3 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        t3.Children.Add(_e);
        var et = Ui.Muted($"UETS e-tebligatı (tebliğ günü = UETS'e ulaşma + {IsSureleri.ETebligatGunu} gün)", false); et.Margin = new Thickness(8, 0, 0, 0); et.VerticalAlignment = VerticalAlignment.Center; t3.Children.Add(et);
        kart.Children.Add(t3);
        var t4 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var sg = Ui.Text("Son gün:", 13, true); sg.VerticalAlignment = VerticalAlignment.Center; sg.Width = 60; t4.Children.Add(sg);
        t4.Children.Add(_son);
        kart.Children.Add(t4);
        _ac.Margin = new Thickness(0, 6, 0, 0); kart.Children.Add(_ac);
        var kutu = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 14, 0, 0), Child = kart };
        kutu.SetResourceReference(Border.BackgroundProperty, "surface");
        kutu.SetResourceReference(Border.BorderBrushProperty, "border");
        kok.Children.Add(kutu);

        kok.Children.Add(Et("Dayanak (kanun maddesi / tebligat vb.)")); kok.Children.Add(_dayanak);
        kok.Children.Add(Et("Notlar")); kok.Children.Add(_notlar);

        var kaydet = Ui.Btn("Kaydet", "Primary", Onayla);
        var vazgec = Ui.Btn("Vazgeç", "", () => { DialogResult = false; });
        vazgec.Margin = new Thickness(0, 0, 8, 0);
        var alt = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 12) };
        alt.Children.Add(vazgec); alt.Children.Add(kaydet);
        kok.Children.Add(alt);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = kok };

        _notlar.SetResourceReference(Control.BackgroundProperty, "surface2");
        _notlar.SetResourceReference(Control.ForegroundProperty, "text");
        _notlar.SetResourceReference(Control.BorderBrushProperty, "border");

        _yukleniyor = false;
        _g.SelectionChanged += (_, _) => Hesapla(); _a.SelectionChanged += (_, _) => Hesapla(); _y.SelectionChanged += (_, _) => Hesapla();
        _b.SelectionChanged += (_, _) => Hesapla(); _n.TextChanged += (_, _) => Hesapla();
        _e.Checked += (_, _) => Hesapla(); _e.Unchecked += (_, _) => Hesapla();
        _n.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
        Loaded += (_, _) => _baslik.Focus();
    }

    DateTime? TebligTarihi()
    {
        if (_g.SelectedItem is not int g || _a.SelectedIndex < 0 || _y.SelectedItem is not int y) return null;
        return new DateTime(y, _a.SelectedIndex + 1, Math.Min(g, DateTime.DaysInMonth(y, _a.SelectedIndex + 1)));
    }

    /// <summary>Süre yazılmışsa son günü hesaplar ve kutuya yazar.</summary>
    void Hesapla()
    {
        if (_yukleniyor) return;
        if (!int.TryParse(_n.Text.Trim(), out var n) || n <= 0) return;
        var bas = TebligTarihi(); if (bas == null) return;
        var eteb = _e.IsChecked == true;
        var teblig = eteb ? bas.Value.AddDays(IsSureleri.ETebligatGunu) : bas.Value;
        var birim = _b.SelectedIndex == 1 ? "hafta" : _b.SelectedIndex == 2 ? "ay" : "gun";
        var ham = birim == "hafta" ? teblig.AddDays(7 * n) : birim == "ay" ? teblig.AddMonths(n) : teblig.AddDays(n);
        var son = IsSureleri.IsGunuyaKaydir(ham);
        _son.Text = son.ToString("dd.MM.yyyy");
        var bn = birim == "hafta" ? "hafta" : birim == "ay" ? "ay" : "gün";
        _ac.Text = $"Son gün {son.ToString("dd.MM.yyyy dddd", Utils.Tr)} • Tebliğ: {teblig:dd.MM.yyyy}" +
                   (eteb ? $" (UETS'e ulaşma {bas.Value:dd.MM.yyyy} + {IsSureleri.ETebligatGunu} gün)" : "") + $" • Süre: {n} {bn}" +
                   (son != ham ? " • tatile denk geldiği için ilk iş gününe kaydı" : "") +
                   ". Adli tatil uzatması uygulanmaz; süreyi kendiniz de kontrol edin.";
        _baslangicIso = Utils.Iso(teblig);
    }

    void KatalogdanDoldur()
    {
        var h = IsListesiPage.KatalogHazirla(this, _db, null, katalogZorunlu: true);
        if (h == null) return;
        _yukleniyor = true;
        try
        {
            string S(string k) => h.TryGetValue(k, out var o) ? Convert.ToString(o) ?? "" : "";
            if (S("baslik").Length > 0 && _baslik.Text.Trim().Length == 0) _baslik.Text = S("baslik");
            var t = S("sure_turu"); if (t.Length > 0) { var i = Array.IndexOf(Turler, t); if (i >= 0) _tur.SelectedIndex = i; }
            if (S("dayanak").Length > 0) _dayanak.Text = S("dayanak");
            var b = Utils.ParseDate(S("baslangic"));
            if (b != null)
            {
                _g.SelectedItem = b.Value.Day; _a.SelectedIndex = b.Value.Month - 1;
                if (!_y.Items.Contains(b.Value.Year)) _y.Items.Add(b.Value.Year);
                _y.SelectedItem = b.Value.Year; _e.IsChecked = false; _baslangicIso = Utils.Iso(b.Value);
            }
            var s = Utils.ParseDate(S("son_tarih")); if (s != null) _son.Text = s.Value.ToString("dd.MM.yyyy");
            if (S("notlar").Length > 0) _notlar.Text = S("notlar");
            _n.Text = "";
            _ac.Text = "Katalogdan dolduruldu; kontrol edin.";
        }
        finally { _yukleniyor = false; }
    }

    void Onayla()
    {
        if (_baslik.Text.Trim().Length == 0) { Ui.Warn(this, "İş / süre başlığını yazın."); _baslik.Focus(); return; }
        var son = Utils.ParseDate(_son.Text.Trim());
        if (son == null) { Ui.Warn(this, "Son günü GG.AA.YYYY biçiminde yazın ya da süreyi girip hesaplatın."); _son.Focus(); return; }
        var r = new Dictionary<string, object?>
        {
            ["baslik"] = _baslik.Text.Trim(),
            ["sure_turu"] = Turler[Math.Max(0, _tur.SelectedIndex)],
            ["dayanak"] = _dayanak.Text.Trim(),
            ["baslangic"] = _baslangicIso,
            ["son_tarih"] = Utils.Iso(son.Value),
            ["dava_id"] = _dava.SelectedIndex >= 0 && _dava.SelectedIndex < _davaIds.Count ? _davaIds[_dava.SelectedIndex] : 0L,
            ["notlar"] = _notlar.Text.Trim(),
            ["durum"] = Durumlar[Math.Max(0, _durum.SelectedIndex)],
        };
        if (_eski.TryGetValue("tebligat_id", out var tb) && tb != null) r["tebligat_id"] = tb;
        _sonuc = r;
        DialogResult = true;
    }

    public static Dictionary<string, object?>? Goster(DependencyObject owner, Db db, IDictionary<string, object?>? values)
    {
        var d = new IsDialog(owner, db, values);
        return d.ShowDialog() == true ? d._sonuc : null;
    }
}
