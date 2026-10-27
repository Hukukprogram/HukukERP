using System.Windows;
using System.Windows.Controls;

namespace HukukErp.UI;

/// <summary>Parola soran küçük pencere. onay=true ise ikinci kutuda tekrar ister.</summary>
public class ParolaDialog : Window
{
    readonly PasswordBox _p1 = new() { Margin = new Thickness(0, 0, 0, 8) };
    readonly PasswordBox _p2 = new() { Margin = new Thickness(0, 0, 0, 8) };
    readonly bool _onay;
    public string? Sonuc { get; private set; }

    ParolaDialog(string baslik, string metin, bool onay, Window? owner)
    {
        _onay = onay;
        Title = baslik;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = owner != null ? false : true;
        Owner = owner;
        WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "bg");
        SetResourceReference(ForegroundProperty, "text");

        var sp = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };
        sp.Children.Add(new TextBlock { Text = metin, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        sp.Children.Add(_p1);
        if (onay) { sp.Children.Add(new TextBlock { Text = "Tekrar:", Margin = new Thickness(0, 0, 0, 4) }); sp.Children.Add(_p2); }
        var tamam = Ui.Btn("Tamam", "Primary", Kaydet);
        var iptal = Ui.Btn("Vazgeç", "", () => { DialogResult = false; });
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        bar.Children.Add(tamam); bar.Children.Add(iptal);
        sp.Children.Add(bar);
        Content = sp;
        Loaded += (_, _) => _p1.Focus();
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { Kaydet(); e.Handled = true; } };
    }

    void Kaydet()
    {
        if (_p1.Password.Length == 0) return;
        if (_onay && _p1.Password != _p2.Password) { Ui.Warn(this, "Parolalar aynı değil."); return; }
        Sonuc = _p1.Password;
        DialogResult = true;
    }

    public static string? Sor(string baslik, string metin, bool onay = false, Window? owner = null)
    {
        var d = new ParolaDialog(baslik, metin, onay, owner);
        return d.ShowDialog() == true ? d.Sonuc : null;
    }
}
