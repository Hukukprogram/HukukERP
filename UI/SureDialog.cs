using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HukukErp.Core;
using HukukErp.Services;

namespace HukukErp.UI;

/// <summary>Modern "Tebligat Süresi" penceresi: tebliğ tarihi (gün / ay / yıl) + süre (sayı + gün/hafta/ay); son gün anında gösterilir.</summary>
public class SureDialog : Window
{
    public record Sonuc(DateTime Teblig, DateTime Son, int Sure, string Birim, string Aciklama);

    readonly ComboBox _g = new() { Width = 72, Height = 36 }, _a = new() { Width = 120, Height = 36 }, _y = new() { Width = 84, Height = 36 };
    readonly TextBox _n = new() { Width = 84, Height = 36, VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, FontSize = 15, ToolTip = "Örn. 30" };
    readonly ComboBox _b = new() { Width = 110, Height = 36 };
    readonly CheckBox _e = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _son = Ui.Text("", 18, true), _ac = Ui.Muted("", true);
    Sonuc? _sonuc;
    bool _katalog;
    bool _yukleniyor = true;

    SureDialog(DependencyObject owner, DateTime baslangic)
    {
        Title = "Tebligat Süresi";
        SetResourceReference(BackgroundProperty, "bg");
        SetResourceReference(ForegroundProperty, "text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Owner = Window.GetWindow(owner);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 540; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;

        for (int i = 1; i <= 31; i++) _g.Items.Add(i);
        foreach (var ay in Utils.Tr.DateTimeFormat.MonthNames.Take(12)) _a.Items.Add(ay);
        for (int y = DateTime.Today.Year - 3; y <= DateTime.Today.Year + 2; y++) _y.Items.Add(y);
        _b.Items.Add("Gün"); _b.Items.Add("Hafta"); _b.Items.Add("Ay");
        _g.SelectedItem = baslangic.Day; _a.SelectedIndex = baslangic.Month - 1; _y.SelectedItem = baslangic.Year; _b.SelectedIndex = 0;
        _e.IsChecked = true;
        _n.SetResourceReference(Control.BackgroundProperty, "surface2");
        _n.SetResourceReference(Control.ForegroundProperty, "text");
        _n.SetResourceReference(Control.BorderBrushProperty, "border");

        static TextBlock Et(string t) { var x = Ui.Text(t, 13, true); x.Margin = new Thickness(0, 0, 0, 6); return x; }
        static TextBlock Sl(string t) { var x = Ui.Muted(t, false); x.VerticalAlignment = VerticalAlignment.Center; x.Margin = new Thickness(8, 0, 8, 0); return x; }

        var tarih = new StackPanel { Orientation = Orientation.Horizontal };
        tarih.Children.Add(_g); tarih.Children.Add(Sl("/")); tarih.Children.Add(_a); tarih.Children.Add(Sl("/")); tarih.Children.Add(_y);

        var sure = new StackPanel { Orientation = Orientation.Horizontal };
        sure.Children.Add(_n); _b.Margin = new Thickness(8, 0, 0, 0); sure.Children.Add(_b);
        var ornek = Ui.Muted("Örn. 30", false); ornek.VerticalAlignment = VerticalAlignment.Center; ornek.Margin = new Thickness(10, 0, 0, 0); sure.Children.Add(ornek);

        var eRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        eRow.Children.Add(_e);
        var et = Ui.Muted($"UETS e-tebligatı (tebliğ günü = UETS'e ulaşma + {IsSureleri.ETebligatGunu} gün)", false); et.Margin = new Thickness(8, 0, 0, 0); et.VerticalAlignment = VerticalAlignment.Center;
        eRow.Children.Add(et);

        // sonuç kutusu
        var sp = new StackPanel();
        var s1 = Ui.Muted("Son gün", false); sp.Children.Add(s1); sp.Children.Add(_son); _ac.Margin = new Thickness(0, 4, 0, 0); sp.Children.Add(_ac);
        var kutu = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 16, 0, 0), Child = sp };
        kutu.SetResourceReference(Border.BackgroundProperty, "sel");
        kutu.SetResourceReference(Border.BorderBrushProperty, "accent");

        var kaydet = Ui.Btn("Kaydet", "Primary", Onayla);
        var vazgec = Ui.Btn("Vazgeç", "", () => { DialogResult = false; });
        var kat = Ui.Btn("📚 Katalogdan seç…", "", () => { _katalog = true; DialogResult = false; });
        var alt = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        var sag = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        vazgec.Margin = new Thickness(0, 0, 8, 0); sag.Children.Add(vazgec); sag.Children.Add(kaydet);
        DockPanel.SetDock(sag, Dock.Right);
        alt.Children.Add(sag); alt.Children.Add(kat);
        kat.HorizontalAlignment = HorizontalAlignment.Left;

        var kok = new StackPanel { Margin = new Thickness(24, 20, 24, 18) };
        kok.Children.Add(Ui.Text("Tebligat Süresi", 18, true));
        var alta = Ui.Muted("Tebliğ tarihini ve süreyi girin; son gün hemen hesaplanır.", false); alta.Margin = new Thickness(0, 2, 0, 16); kok.Children.Add(alta);
        kok.Children.Add(Et("Tebliğ Tarihi")); kok.Children.Add(tarih);
        var sEt = Et("Süre"); sEt.Margin = new Thickness(0, 16, 0, 6); kok.Children.Add(sEt); kok.Children.Add(sure);
        kok.Children.Add(eRow); kok.Children.Add(kutu); kok.Children.Add(alt);
        Content = kok;

        _yukleniyor = false;
        _g.SelectionChanged += (_, _) => Hesapla(); _a.SelectionChanged += (_, _) => Hesapla(); _y.SelectionChanged += (_, _) => Hesapla();
        _b.SelectionChanged += (_, _) => Hesapla(); _n.TextChanged += (_, _) => Hesapla();
        _e.Checked += (_, _) => Hesapla(); _e.Unchecked += (_, _) => Hesapla();
        _n.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
        Loaded += (_, _) => { _n.Focus(); Hesapla(); };
    }

    Sonuc? Oku()
    {
        if (_g.SelectedItem is not int g || _a.SelectedIndex < 0 || _y.SelectedItem is not int y) return null;
        if (!int.TryParse(_n.Text.Trim(), out var n) || n <= 0) return null;
        var gun = Math.Min(g, DateTime.DaysInMonth(y, _a.SelectedIndex + 1));
        var bas = new DateTime(y, _a.SelectedIndex + 1, gun);
        var eteb = _e.IsChecked == true;
        var teblig = eteb ? bas.AddDays(IsSureleri.ETebligatGunu) : bas;
        var birim = _b.SelectedIndex == 1 ? "hafta" : _b.SelectedIndex == 2 ? "ay" : "gun";
        var ham = birim == "hafta" ? teblig.AddDays(7 * n) : birim == "ay" ? teblig.AddMonths(n) : teblig.AddDays(n);
        var son = IsSureleri.IsGunuyaKaydir(ham);
        var bn = birim == "hafta" ? "hafta" : birim == "ay" ? "ay" : "gün";
        var ac = $"Tebliğ: {teblig:dd.MM.yyyy}" + (eteb ? $" (UETS'e ulaşma {bas:dd.MM.yyyy} + {IsSureleri.ETebligatGunu} gün)" : "") + $" • Süre: {n} {bn}" +
                 (son != ham ? " • son gün tatile denk geldiği için ilk iş gününe kaydı" : "");
        return new Sonuc(teblig, son, n, birim, ac);
    }

    void Hesapla()
    {
        if (_yukleniyor) return;
        _sonuc = Oku();
        if (_sonuc == null) { _son.Text = "—"; _ac.Text = "Süreyi (sayı olarak) girin."; return; }
        _son.Text = _sonuc.Son.ToString("dd.MM.yyyy dddd", Utils.Tr);
        _ac.Text = _sonuc.Aciklama + ". Adli tatil uzatması uygulanmaz; süreyi kendiniz de kontrol edin.";
    }

    void Onayla()
    {
        Hesapla();
        if (_sonuc == null) { Ui.Warn(this, "Süreyi (gün / hafta / ay sayısı) girin."); return; }
        DialogResult = true;
    }

    /// <summary>Sonuç döner; vazgeçilirse null. "Katalogdan seç" denirse katalog=true olur.</summary>
    public static Sonuc? Goster(DependencyObject owner, out bool katalog)
    {
        var d = new SureDialog(owner, DateTime.Today);
        var ok = d.ShowDialog() == true;
        katalog = d._katalog;
        return ok ? d._sonuc : null;
    }
}
