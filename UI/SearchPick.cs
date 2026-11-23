using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HukukErp.Core;

namespace HukukErp.UI;

/// <summary>
/// Binlerce kayıt içinden seçim için: düğme gibi görünür, tıklayınca arama kutulu bir liste penceresi açar.
/// Yazdıkça süzer (ad, T.C. no, telefon… listedeki metnin her yerinde arar), Enter ilk sonucu seçer.
/// ComboBox ile aynı üyeleri sunar (Items, SelectedIndex, SelectedItem, SelectionChanged), böylece sayfalarda kolayca yer değiştirir.
/// </summary>
public class SearchPick : Button
{
    public List<ComboItem> Items { get; } = new();
    string _ph = "Seçin…";
    public string Placeholder { get => _ph; set { _ph = value; Goster(); } }
    public string AramaIpucu { get; set; } = "Ad soyad ya da T.C. no yazın…";
    public event EventHandler? SelectionChanged;
    int _idx = -1;

    public SearchPick()
    {
        // türetilmiş sınıfa Button'ın örtük stili uygulanmaz: modern görünüm için elle atanır
        if (Application.Current?.TryFindResource(typeof(Button)) is Style st) Style = st;
        Height = 34;
        VerticalContentAlignment = VerticalAlignment.Center;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        MinWidth = 220;
        Padding = new Thickness(10, 6, 10, 6);
        Click += (_, _) => Ac();
        Goster();
    }

    public int SelectedIndex
    {
        get => _idx;
        set
        {
            var yeni = value >= 0 && value < Items.Count ? value : -1;
            var degisti = yeni != _idx;
            _idx = yeni;
            Goster();
            if (degisti) SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ComboItem? SelectedItem => _idx >= 0 && _idx < Items.Count ? Items[_idx] : null;

    void Goster()
    {
        var t = SelectedItem?.Label ?? Placeholder;
        var tb = new TextBlock { Text = t + "   ▾", TextTrimming = TextTrimming.CharacterEllipsis };
        Content = tb;
        ToolTip = Items.Count > 0 ? $"{Items.Count} kayıt • tıklayıp arayın" : null;
    }

    void Ac()
    {
        if (Items.Count == 0) return;
        var sec = SearchPickDialog.Sec(this, Items, _idx, AramaIpucu);
        if (sec >= 0) SelectedIndex = sec;
    }
}

/// <summary>Arama kutulu seçim penceresi. En fazla 200 sonuç gösterir; daha fazlası için aramayı daraltın.</summary>
public class SearchPickDialog : Window
{
    const int Sinir = 200;
    readonly List<ComboItem> _items;
    readonly List<string> _low;
    readonly TextBox _q = new() { Margin = new Thickness(0, 0, 0, 8) };
    readonly ListBox _lb = new();
    readonly TextBlock _info = Ui.Muted("", false);
    readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromMilliseconds(150) };
    readonly List<int> _gorunen = new();
    int _sonuc = -1;

    SearchPickDialog(DependencyObject owner, List<ComboItem> items, int mevcut, string ipucu)
    {
        _items = items;
        _low = items.Select(x => Utils.TrLower(x.Label)).ToList();
        Title = "Seçin";
        SetResourceReference(BackgroundProperty, "bg");
        SetResourceReference(ForegroundProperty, "text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Owner = Window.GetWindow(owner);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 520; Height = 520; MinWidth = 380; MinHeight = 320;
        ShowInTaskbar = false;

        _q.ToolTip = ipucu;
        var ipucuYazi = Ui.Muted(ipucu, false);
        ipucuYazi.Margin = new Thickness(0, 0, 0, 4);
        _info.Margin = new Thickness(0, 6, 0, 0);
        var dock = new DockPanel { Margin = new Thickness(16, 14, 16, 12) };
        var ust = new StackPanel();
        ust.Children.Add(ipucuYazi); ust.Children.Add(_q);
        DockPanel.SetDock(ust, Dock.Top);
        DockPanel.SetDock(_info, Dock.Bottom);
        dock.Children.Add(ust); dock.Children.Add(_info); dock.Children.Add(_lb);
        Content = dock;

        _t.Tick += (_, _) => { _t.Stop(); Doldur(mevcut: -1); };
        _q.TextChanged += (_, _) => { _t.Stop(); _t.Start(); };
        _q.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && _lb.Items.Count > 0)
            {
                _lb.Focus();
                if (_lb.SelectedIndex < 0) _lb.SelectedIndex = 0;
                (_lb.ItemContainerGenerator.ContainerFromIndex(Math.Max(0, _lb.SelectedIndex)) as ListBoxItem)?.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter) { _t.Stop(); Doldur(-1); Onayla(_lb.SelectedIndex >= 0 ? _lb.SelectedIndex : 0); e.Handled = true; }
        };
        _lb.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Onayla(_lb.SelectedIndex); e.Handled = true; } };
        _lb.MouseDoubleClick += (_, _) => Onayla(_lb.SelectedIndex);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
        Loaded += (_, _) => { Doldur(mevcut); _q.Focus(); };
    }

    void Doldur(int mevcut)
    {
        var tokenlar = Utils.TrLower(_q.Text.Trim()).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _gorunen.Clear();
        _lb.Items.Clear();
        int eslesen = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            bool ok = true;
            foreach (var t in tokenlar) if (!_low[i].Contains(t)) { ok = false; break; }
            if (!ok) continue;
            eslesen++;
            if (_gorunen.Count < Sinir) { _gorunen.Add(i); _lb.Items.Add(_items[i].Label); }
        }
        _info.Text = eslesen > Sinir ? $"{eslesen} sonuç; ilk {Sinir} tanesi gösteriliyor. Aramayı daraltın."
                   : $"{eslesen} sonuç" + (_items.Count > 0 ? $" / {_items.Count} kayıt" : "");
        var sec = mevcut >= 0 ? _gorunen.IndexOf(mevcut) : -1;
        _lb.SelectedIndex = sec >= 0 ? sec : (_lb.Items.Count > 0 ? 0 : -1);
        if (sec >= 0) _lb.ScrollIntoView(_lb.SelectedItem);
    }

    void Onayla(int satir)
    {
        if (satir < 0 || satir >= _gorunen.Count) return;
        _sonuc = _gorunen[satir];
        DialogResult = true;
    }

    /// <summary>Seçilen kaydın Items içindeki sırasını döndürür; vazgeçilirse -1.</summary>
    public static int Sec(DependencyObject owner, List<ComboItem> items, int mevcut, string ipucu)
    {
        var d = new SearchPickDialog(owner, items, mevcut, ipucu);
        return d.ShowDialog() == true ? d._sonuc : -1;
    }
}
