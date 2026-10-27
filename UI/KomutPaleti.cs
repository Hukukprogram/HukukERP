using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HukukErp.Core;

namespace HukukErp.UI;

/// <summary>Ctrl+K komut paleti: sayfa ya da işlem adını yazıp Enter.</summary>
public class KomutPaleti : Window
{
    readonly TextBox _kutu = new() { Margin = new Thickness(0, 0, 0, 8), FontSize = 15, Padding = new Thickness(8, 6, 8, 6) };
    readonly ListBox _liste = new() { MaxHeight = 360, FontSize = 14 };
    readonly List<(string Ad, Action Calistir)> _hepsi;
    Action? _secilen;

    KomutPaleti(Window owner, List<(string Ad, Action Calistir)> komutlar)
    {
        _hepsi = komutlar;
        Owner = owner;
        Title = "Komut paleti";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = owner.Left + (owner.ActualWidth - 520) / 2;
        Top = owner.Top + 110;
        SetResourceReference(BackgroundProperty, "bg");
        SetResourceReference(ForegroundProperty, "text");
        var sp = new StackPanel { Margin = new Thickness(12) };
        sp.Children.Add(new TextBlock { Text = "Gitmek ya da yapmak istediğiniz şeyi yazın…", Margin = new Thickness(0, 0, 0, 6), Opacity = .7 });
        sp.Children.Add(_kutu);
        sp.Children.Add(_liste);
        Content = sp;
        _kutu.TextChanged += (_, _) => Filtrele();
        PreviewKeyDown += OnKey;
        Loaded += (_, _) => { _kutu.Focus(); Filtrele(); };
        _liste.MouseDoubleClick += (_, _) => Calistir();
    }

    void Filtrele()
    {
        var kelimeler = Utils.TrLower(_kutu.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _liste.Items.Clear();
        foreach (var k in _hepsi)
        {
            var ad = Utils.TrLower(k.Ad);
            if (kelimeler.All(x => ad.Contains(x))) _liste.Items.Add(new ListBoxItem { Content = k.Ad, Tag = k.Calistir });
        }
        if (_liste.Items.Count > 0) _liste.SelectedIndex = 0;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); e.Handled = true; break;
            case Key.Enter: Calistir(); e.Handled = true; break;
            case Key.Down: if (_liste.SelectedIndex < _liste.Items.Count - 1) _liste.SelectedIndex++; e.Handled = true; break;
            case Key.Up: if (_liste.SelectedIndex > 0) _liste.SelectedIndex--; e.Handled = true; break;
        }
    }

    void Calistir()
    {
        _secilen = (_liste.SelectedItem as ListBoxItem)?.Tag as Action;
        Close();
        if (_secilen != null) Application.Current.Dispatcher.BeginInvoke(_secilen);
    }

    public static void Ac(Window owner, List<(string Ad, Action Calistir)> komutlar) => new KomutPaleti(owner, komutlar).ShowDialog();
}
