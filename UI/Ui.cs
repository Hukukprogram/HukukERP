using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using HukukErp.Core;
using Microsoft.Win32;

namespace HukukErp.UI;

/// <summary>Sayfalar arası paylaşılan uygulama bağlamı.</summary>
public class AppCtx
{
    public Db Db { get; init; } = null!;
    public Config Cfg { get; init; } = null!;
    public Action<string> SetTheme { get; set; } = _ => { };
    public Action<string> Status { get; set; } = _ => { };
    public Action<string, string?> Navigate { get; set; } = (_, _) => { };
}

public abstract class BasePage : UserControl
{
    protected readonly AppCtx Ctx;
    protected Db Db => Ctx.Db;
    protected Config Cfg => Ctx.Cfg;
    protected readonly DockPanel Root = new() { LastChildFill = true };

    protected BasePage(AppCtx ctx, bool padded = true)
    {
        Ctx = ctx;
        Root.Margin = padded ? new Thickness(24, 20, 24, 20) : new Thickness(0);
        Content = Root;
    }

    /// <summary>Üste yerleşir (başlık, araç çubuğu…).</summary>
    protected void Top(UIElement el, double bottom = 12)
    {
        DockPanel.SetDock(el, Dock.Top);
        if (el is FrameworkElement fe && fe.Margin == new Thickness(0)) fe.Margin = new Thickness(0, 0, 0, bottom);
        Root.Children.Add(el);
    }

    /// <summary>Kalan alanı doldurur (en son eklenmeli).</summary>
    protected void Fill(UIElement el) => Root.Children.Add(el);

    public virtual void Refresh() { }
}

public static class Fmt
{
    public static string Date(object? v) => Utils.FmtDate(Convert.ToString(v, CultureInfo.InvariantCulture));
    public static string Money(object? v) => Utils.FmtMoney(v == null || v is DBNull ? 0 : Convert.ToDouble(v, CultureInfo.InvariantCulture));
    public static string YesNo(object? v) => v != null && v is not DBNull && Convert.ToInt64(v, CultureInfo.InvariantCulture) != 0 ? "Evet" : "";
}

// ------------------------------------------------------------------ genel yardımcılar
public static class Ui
{
    public static Db? Db { get; set; }

    static Window? Owner(DependencyObject? d) => d == null ? Application.Current?.MainWindow : Window.GetWindow(d) ?? Application.Current?.MainWindow;

    public static void Info(DependencyObject? p, string text, string title = "Bilgi") =>
        MessageBox.Show(Owner(p)!, text, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Warn(DependencyObject? p, string text, string title = "Uyarı") =>
        MessageBox.Show(Owner(p)!, text, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public static bool Confirm(DependencyObject? p, string text) =>
        MessageBox.Show(Owner(p)!, text, "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    public static void OpenExternal(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e) { Warn(null, $"Açılamadı:\n{path}\n\n{e.Message}"); }
    }

    public static void RevealInFolder(string path)
    {
        try
        {
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else Process.Start("explorer.exe", $"\"{path}\"");
        }
        catch (Exception)
        {
            var dir = File.Exists(path) ? Path.GetDirectoryName(path)! : path;
            OpenExternal(dir);
        }
    }

    /// <summary>Arka planda çalıştırır; sonuç arayüz thread'inde teslim edilir.</summary>
    public static void RunAsync<T>(Func<T> fn, Action<T>? onDone = null, Action<string>? onError = null)
    {
        var disp = Application.Current.Dispatcher;
        Task.Run(() =>
        {
            try
            {
                var r = fn();
                disp.BeginInvoke(() => onDone?.Invoke(r));
            }
            catch (Exception e)
            {
                var msg = $"{e.GetType().Name}: {e.Message}";
                disp.BeginInvoke(() => onError?.Invoke(msg));
            }
            finally { Db?.CloseThreadConn(); }
        });
    }

    public static void RunAsyncVoid(Action fn, Action? onDone = null, Action<string>? onError = null) =>
        RunAsync<int>(() => { fn(); return 0; }, _ => onDone?.Invoke(), onError);

    // ---- metin
    public static TextBlock Text(string text, double size = 13, bool bold = false, string brush = "text", bool wrap = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
        if (bold) t.FontWeight = FontWeights.SemiBold;
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public static TextBlock H1(string text) => Text(text, 22, true);
    public static TextBlock H2(string text) => Text(text, 15, true);
    public static TextBlock Muted(string text, bool wrap = true) => Text(text, 13, false, "muted", wrap);

    static readonly Regex BoldRx = new("<b>(.*?)</b>", RegexOptions.Singleline);

    /// <summary>'metin <b>kalın</b> metin' biçimini TextBlock'a çevirir.</summary>
    public static TextBlock Rich(string markup, double size = 13, string brush = "text", bool wrap = true)
    {
        var tb = Text("", size, false, brush, wrap);
        int pos = 0;
        foreach (Match m in BoldRx.Matches(markup))
        {
            if (m.Index > pos) tb.Inlines.Add(new System.Windows.Documents.Run(markup[pos..m.Index]));
            tb.Inlines.Add(new System.Windows.Documents.Run(m.Groups[1].Value) { FontWeight = FontWeights.Bold });
            pos = m.Index + m.Length;
        }
        if (pos < markup.Length) tb.Inlines.Add(new System.Windows.Documents.Run(markup[pos..]));
        return tb;
    }

    public static UIElement PageHeader(string title, string subtitle = "")
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        sp.Children.Add(H1(title));
        if (subtitle.Length > 0) { var m = Muted(subtitle); m.Margin = new Thickness(0, 2, 0, 0); sp.Children.Add(m); }
        return sp;
    }

    public static Button Btn(string text, string kind = "", Action? onClick = null)
    {
        var b = new Button { Content = text };
        if (kind.Length > 0 && Application.Current.TryFindResource(kind) is Style st) b.Style = st;
        if (onClick != null) b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>Düğme/öğe satırı: öğeler arasında 8 px boşluk.</summary>
    public static StackPanel HRow(params UIElement[] els)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < els.Length; i++)
        {
            if (els[i] is FrameworkElement fe)
            {
                if (i > 0) fe.Margin = new Thickness(8, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
                fe.VerticalAlignment = VerticalAlignment.Center;
            }
            sp.Children.Add(els[i]);
        }
        return sp;
    }

    /// <summary>Solda düğmeler, sağda tek öğe (ör. durum yazısı).</summary>
    public static DockPanel Bar(UIElement left, UIElement? right = null)
    {
        var d = new DockPanel { LastChildFill = true };
        if (right != null) { DockPanel.SetDock(right, Dock.Right); d.Children.Add(right); }
        d.Children.Add(left);
        return d;
    }

    public static StackPanel VStack(double gap, params UIElement[] els)
    {
        var sp = new StackPanel();
        for (int i = 0; i < els.Length; i++)
        {
            if (els[i] is FrameworkElement fe && i > 0) fe.Margin = new Thickness(fe.Margin.Left, gap, fe.Margin.Right, fe.Margin.Bottom);
            sp.Children.Add(els[i]);
        }
        return sp;
    }

    public static Grid HSplit(UIElement left, UIElement right, double l = 1, double r = 1)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(l, GridUnitType.Star), MinWidth = 200 });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(r, GridUnitType.Star), MinWidth = 200 });
        var sp = new GridSplitter { Width = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        Grid.SetColumn(left, 0); Grid.SetColumn(sp, 1); Grid.SetColumn(right, 2);
        g.Children.Add(left); g.Children.Add(sp); g.Children.Add(right);
        return g;
    }

    public static Grid VSplit(UIElement top, UIElement bottom, double t = 1, double b = 1)
    {
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(t, GridUnitType.Star), MinHeight = 120 });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(b, GridUnitType.Star), MinHeight = 120 });
        var sp = new GridSplitter { Height = 10, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        Grid.SetRow(top, 0); Grid.SetRow(sp, 1); Grid.SetRow(bottom, 2);
        g.Children.Add(top); g.Children.Add(sp); g.Children.Add(bottom);
        return g;
    }

    public static TabItem Tab(string header, UIElement content) => new() { Header = header, Content = content };

    public static string StatusKind(string? durum)
    {
        var d = (durum ?? "").ToLower(Utils.Tr);
        if (new[] { "aktif", "çalışıyor", "onay", "tamam", "ödendi", "işlendi" }.Any(d.Contains)) return "ok";
        if (new[] { "bekle", "kuyru", "yeni", "devam" }.Any(d.Contains)) return "warn";
        if (new[] { "red", "iptal", "gecik", "kapal", "ayrıldı" }.Any(d.Contains)) return "danger";
        return "info";
    }

    public static string DeadlineKind(string? iso)
    {
        var k = Utils.DaysLeft(iso);
        if (k == null) return "muted";
        return k < 0 ? "danger" : (k <= 3 ? "warn" : "ok");
    }
}

// ------------------------------------------------------------------ kartlar
public class Card : Border
{
    public DockPanel Lay { get; } = new() { LastChildFill = true };

    public Card()
    {
        SetResourceReference(BackgroundProperty, "surface");
        SetResourceReference(BorderBrushProperty, "border");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(16, 14, 16, 14);
        Child = Lay;
    }

    public Card Top(UIElement el, double bottom = 8)
    {
        DockPanel.SetDock(el, Dock.Top);
        if (el is FrameworkElement fe) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top, fe.Margin.Right, bottom);
        Lay.Children.Add(el);
        return this;
    }

    public Card Bottom(UIElement el, double top = 8)
    {
        DockPanel.SetDock(el, Dock.Bottom);
        if (el is FrameworkElement fe) fe.Margin = new Thickness(fe.Margin.Left, top, fe.Margin.Right, fe.Margin.Bottom);
        Lay.Children.Add(el);
        return this;
    }

    public Card Fill(UIElement el) { Lay.Children.Add(el); return this; }
}

/// <summary>İç kart (surface2): başlık + satırlar.</summary>
public class Inner : Border
{
    readonly StackPanel _box = new();
    public StackPanel Box => _box;
    public StackPanel Outer { get; } = new();

    public Inner(string title)
    {
        SetResourceReference(BackgroundProperty, "surface2");
        SetResourceReference(BorderBrushProperty, "border");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(16, 14, 16, 14);
        Child = Outer;
        var t = Ui.H2(title);
        t.Margin = new Thickness(0, 0, 0, 9);
        Outer.Children.Add(t);
        Outer.Children.Add(_box);
    }

    public void Clear() => _box.Children.Clear();

    public void AddRow(UIElement content, UIElement? pill = null, string? dotBrush = null)
    {
        var d = new DockPanel { Margin = new Thickness(0, 4, 0, 4), LastChildFill = true };
        if (pill != null) { DockPanel.SetDock(pill, Dock.Right); ((FrameworkElement)pill).Margin = new Thickness(8, 0, 0, 0); d.Children.Add(pill); }
        if (dotBrush != null)
        {
            var dot = Ui.Text("●", 13, false, dotBrush);
            dot.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(dot, Dock.Left);
            d.Children.Add(dot);
        }
        d.Children.Add(content);
        _box.Children.Add(d);
    }

    public void AddEmpty(string text = "Kayıt yok") => _box.Children.Add(Ui.Muted(text));
}

public class Pill : Border
{
    public Pill(string text, string kind = "muted")
    {
        string key = kind == "info" ? "accent" : kind;
        CornerRadius = new CornerRadius(11);
        Padding = new Thickness(11, 3, 11, 3);
        HorizontalAlignment = HorizontalAlignment.Left;
        var tb = Ui.Text(text, 12, true, key);
        Child = tb;
        if (kind == "muted")
        {
            SetResourceReference(BackgroundProperty, "surface");
            SetResourceReference(BorderBrushProperty, "border");
        }
        else
        {
            Background = Theme.Alpha(key, 46);
            SetResourceReference(BorderBrushProperty, key);
        }
        BorderThickness = new Thickness(1);
    }
}

public class StatCard : Card
{
    readonly TextBlock _v;
    public StatCard(string title, string value = "0")
    {
        var t = Ui.Muted(title);
        _v = Ui.Text(value, 26, true);
        Top(t, 2);
        Fill(_v);
    }
    public void SetValue(string v) => _v.Text = v;
}

// ------------------------------------------------------------------ tablo
public class RowView
{
    public Row Row { get; init; } = null!;
    public string[] Cells { get; init; } = Array.Empty<string>();
    public string[] Kinds { get; init; } = Array.Empty<string>();
}

sealed class KindBrushConverter : IValueConverter
{
    public static readonly KindBrushConverter I = new();
    public object Convert(object value, Type t, object p, CultureInfo c) => (value as string) switch
    {
        "ok" => Theme.B("ok"), "warn" => Theme.B("warn"), "danger" => Theme.B("danger"),
        "info" => Theme.B("accent"), "muted" => Theme.B("muted"), _ => DependencyProperty.UnsetValue,
    };
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

sealed class KindWeightConverter : IValueConverter
{
    public static readonly KindWeightConverter I = new();
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        string.IsNullOrEmpty(value as string) ? FontWeights.Normal : FontWeights.Bold;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Sütun tanımlı tablo. Sütun: (başlık, anahtar, biçimleyici|null).</summary>
public class DataTable : DataGrid
{
    readonly (string Title, string Key, Func<object?, string>? Fmt)[] _cols;
    public List<Row> Rows { get; private set; } = new();

    public DataTable(params (string Title, string Key, Func<object?, string>? Fmt)[] cols)
    {
        _cols = cols;
        SelectionMode = DataGridSelectionMode.Single;
        VirtualizingPanel.SetScrollUnit(this, ScrollUnit.Pixel);
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        CanUserResizeRows = false;
        for (int i = 0; i < cols.Length; i++)
        {
            var st = new Style(typeof(TextBlock));
            st.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            st.Setters.Add(new Setter(ToolTipProperty, new Binding($"Cells[{i}]")));
            st.Setters.Add(new Setter(TextBlock.ForegroundProperty, new Binding($"Kinds[{i}]") { Converter = KindBrushConverter.I }));
            st.Setters.Add(new Setter(TextBlock.FontWeightProperty, new Binding($"Kinds[{i}]") { Converter = KindWeightConverter.I }));
            var col = new DataGridTextColumn
            {
                Header = cols[i].Title,
                Binding = new Binding($"Cells[{i}]"),
                ElementStyle = st,
                MinWidth = 70,
                MaxWidth = 560,
            };
            if (i == cols.Length - 1) { col.MaxWidth = double.PositiveInfinity; col.Width = new DataGridLength(1, DataGridLengthUnitType.Star); }
            Columns.Add(col);
        }
    }

    public void Load(List<Row> rows, Dictionary<string, Func<Row, string>>? badge = null)
    {
        var sel = SelectedId;
        Rows = rows;
        var views = new List<RowView>(rows.Count);
        foreach (var row in rows)
        {
            var cells = new string[_cols.Length];
            var kinds = new string[_cols.Length];
            for (int c = 0; c < _cols.Length; c++)
            {
                row.TryGetValue(_cols[c].Key, out var val);
                cells[c] = _cols[c].Fmt != null ? _cols[c].Fmt!(val) : (val == null || val is DBNull ? "" : Convert.ToString(val, CultureInfo.InvariantCulture) ?? "");
                kinds[c] = badge != null && badge.TryGetValue(_cols[c].Key, out var fn) ? fn(row) : "";
            }
            views.Add(new RowView { Row = row, Cells = cells, Kinds = kinds });
        }
        ItemsSource = views;
        if (sel != null) SelectId(sel.Value);
    }

    public Row? Current => (SelectedItem as RowView)?.Row;
    public long? SelectedId => Current != null && Current.ContainsKey("id") ? Current.L("id") : null;

    public void SelectId(long id)
    {
        if (ItemsSource is not List<RowView> vs) return;
        var v = vs.FirstOrDefault(x => x.Row.ContainsKey("id") && x.Row.L("id") == id);
        if (v != null) { SelectedItem = v; ScrollIntoView(v); }
    }

    public void SelectFirst()
    {
        if (ItemsSource is List<RowView> vs && vs.Count > 0) SelectedItem = vs[0];
    }
}

// ------------------------------------------------------------------ tarih kutusu
/// <summary>GG.AA.YYYY biçiminde otomatik noktalı tarih kutusu. Boş bırakılabilir.</summary>
public class DateBox : TextBox
{
    bool _busy;

    public DateBox(string? iso = null)
    {
        MaxLength = 10;
        MaxWidth = 150;
        Text = string.IsNullOrEmpty(iso) ? "" : Utils.FmtDate(iso);
        PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        TextChanged += (_, _) => Reformat();
        ToolTip = "GG.AA.YYYY";
    }

    void Reformat()
    {
        if (_busy) return;
        var d = new string(Text.Where(char.IsDigit).Take(8).ToArray());
        var f = d.Length <= 2 ? d : d.Length <= 4 ? d[..2] + "." + d[2..] : d[..2] + "." + d[2..4] + "." + d[4..];
        if (f != Text)
        {
            _busy = true;
            Text = f;
            CaretIndex = f.Length;
            _busy = false;
        }
    }

    public string Digits => new(Text.Where(char.IsDigit).ToArray());

    /// <summary>Boş → ""; geçerli → 'yyyy-MM-dd'; geçersiz → null.</summary>
    public string? Iso()
    {
        if (Digits.Length == 0) return "";
        if (Text.Length != 10) return null;
        var d = Utils.ParseDate(Text);
        return d == null ? null : Utils.Iso(d.Value);
    }
}

// ------------------------------------------------------------------ form diyaloğu
public class Field
{
    public string Key, Label, Kind, Placeholder;
    public List<(object? Value, string Label)> Options;
    public bool Required;
    public object? Default;

    public Field(string key, string label, string kind = "text", IEnumerable<(object? Value, string Label)>? options = null,
                 bool required = false, object? def = null, string placeholder = "")
    {
        Key = key; Label = label; Kind = kind; Required = required; Default = def; Placeholder = placeholder;
        Options = options?.ToList() ?? new();
    }

    public static List<(object? Value, string Label)> Of(params string[] items) => items.Select(x => ((object?)x, x)).ToList();
}

public class FileFieldBox : Grid
{
    public TextBox Line { get; } = new();
    public FileFieldBox(string? v, string filter)
    {
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Line.Text = v ?? "";
        var b = Ui.Btn("Seç…", "", () =>
        {
            var dlg = new OpenFileDialog { Filter = filter };
            if (dlg.ShowDialog() == true) Line.Text = dlg.FileName;
        });
        b.Margin = new Thickness(8, 0, 0, 0);
        SetColumn(b, 1);
        Children.Add(Line);
        Children.Add(b);
    }
}

public class ComboItem
{
    public object? Value { get; init; }
    public string Label { get; init; } = "";
    public override string ToString() => Label;
}

public class FormDialog : Window
{
    readonly List<Field> _fields;
    readonly Dictionary<string, FrameworkElement> _w = new();
    public Dictionary<string, object?> Result { get; private set; } = new();

    static string K(object? v) => v == null || v is DBNull ? "∅" : Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";

    public FormDialog(string title, List<Field> fields, IDictionary<string, object?>? values, DependencyObject? owner, string fileFilter = "PDF (*.pdf)|*.pdf")
    {
        Title = title;
        _fields = fields;
        SetResourceReference(BackgroundProperty, "bg");
        SetResourceReference(ForegroundProperty, "text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Owner = owner == null ? Application.Current.MainWindow : Window.GetWindow(owner);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.Height;
        Width = 560;
        MinWidth = 460;
        MaxHeight = SystemParameters.WorkArea.Height * 0.92;
        ShowInTaskbar = false;

        var grid = new Grid { Margin = new Thickness(18, 16, 18, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        int r = 0;
        foreach (var f in fields)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            object? v = values != null && values.TryGetValue(f.Key, out var x) ? x : f.Default;
            var w = Make(f, v, fileFilter);
            w.Margin = new Thickness(0, 0, 0, 10);
            var lab = Ui.Muted(f.Label + (f.Required ? " *" : ""));
            lab.Margin = new Thickness(0, 7, 12, 0);
            lab.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRow(lab, r); Grid.SetRow(w, r); Grid.SetColumn(w, 1);
            grid.Children.Add(lab); grid.Children.Add(w);
            _w[f.Key] = w;
            r++;
        }
        var save = Ui.Btn("Kaydet", "Primary", OnSave);
        var cancel = Ui.Btn("Vazgeç", "", () => { DialogResult = false; });
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18, 4, 18, 16) };
        cancel.Margin = new Thickness(0, 0, 8, 0);
        bar.Children.Add(cancel); bar.Children.Add(save);
        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(bar);
        dock.Children.Add(new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = dock;
        Loaded += (_, _) =>
        {
            var first = _fields.Select(f => _w[f.Key]).OfType<TextBox>().FirstOrDefault(t => !t.IsReadOnly && t is not DateBox);
            first?.Focus();
        };
    }

    FrameworkElement Make(Field f, object? v, string filter)
    {
        switch (f.Kind)
        {
            case "multiline":
                return new TextBox
                {
                    Text = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                    MinHeight = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top,
                };
            case "combo":
                {
                    var cb = new ComboBox { DisplayMemberPath = "Label" };
                    foreach (var (val, lab) in f.Options) cb.Items.Add(new ComboItem { Value = val, Label = lab });
                    int idx = -1;
                    for (int i = 0; i < cb.Items.Count; i++) if (K(((ComboItem)cb.Items[i]).Value) == K(v)) { idx = i; break; }
                    cb.SelectedIndex = idx >= 0 ? idx : (cb.Items.Count > 0 ? 0 : -1);
                    return cb;
                }
            case "date":
                return new DateBox(v as string) { HorizontalAlignment = HorizontalAlignment.Left, Width = 150 };
            case "money":
                {
                    var tb = new TextBox { Text = (v == null || v is DBNull ? 0.0 : Convert.ToDouble(v, CultureInfo.InvariantCulture)).ToString("N2", Utils.Tr) };
                    tb.GotKeyboardFocus += (_, _) => tb.SelectAll();
                    tb.LostFocus += (_, _) => tb.Text = Utils.ParseMoney(tb.Text).ToString("N2", Utils.Tr);
                    return tb;
                }
            case "int":
                {
                    var tb = new TextBox { Text = (v == null || v is DBNull ? 0 : Convert.ToInt32(v, CultureInfo.InvariantCulture)).ToString(), HorizontalAlignment = HorizontalAlignment.Left, Width = 120 };
                    tb.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
                    return tb;
                }
            case "check":
                {
                    bool on = v switch { null => false, bool b => b, string s => s.Length > 0 && s != "0", _ => Convert.ToDouble(v, CultureInfo.InvariantCulture) != 0 };
                    return new CheckBox { IsChecked = on, Margin = new Thickness(0, 4, 0, 0) };
                }
            case "readonly":
                return new TextBox { Text = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "", IsReadOnly = true };
            case "file":
                return new FileFieldBox(Convert.ToString(v, CultureInfo.InvariantCulture), filter);
            default:
                {
                    var tb = new TextBox { Text = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" };
                    if (f.Placeholder.Length > 0) tb.ToolTip = f.Placeholder;
                    return tb;
                }
        }
    }

    object? ValueOf(Field f)
    {
        var w = _w[f.Key];
        return f.Kind switch
        {
            "multiline" => ((TextBox)w).Text.Trim(),
            "combo" => ((w as ComboBox)?.SelectedItem as ComboItem)?.Value,
            "date" => ((DateBox)w).Iso() ?? "",
            "money" => Math.Round(Utils.ParseMoney(((TextBox)w).Text), 2),
            "int" => int.TryParse(((TextBox)w).Text, out var n) ? n : 0,
            "check" => ((CheckBox)w).IsChecked == true ? 1 : 0,
            "file" => ((FileFieldBox)w).Line.Text.Trim(),
            _ => ((TextBox)w).Text.Trim(),
        };
    }

    static bool IsEmpty(object? v) => v switch
    {
        null => true,
        string s => s.Length == 0,
        int i => i == 0,
        long l => l == 0,
        double d => d == 0,
        _ => false,
    };

    void OnSave()
    {
        foreach (var f in _fields.Where(x => x.Kind == "date"))
            if (((DateBox)_w[f.Key]).Iso() == null)
            {
                Ui.Warn(this, $"'{f.Label}' geçerli bir tarih değil. Biçim: GG.AA.YYYY (örn. 05.03.2026)");
                return;
            }
        foreach (var f in _fields.Where(x => x.Required))
            if (IsEmpty(ValueOf(f)))
            {
                Ui.Warn(this, $"'{f.Label}' alanı zorunludur.");
                return;
            }
        Result = _fields.Where(x => x.Kind != "readonly").ToDictionary(x => x.Key, x => ValueOf(x));
        DialogResult = true;
    }

    public static Dictionary<string, object?>? Ask(DependencyObject? owner, string title, List<Field> fields, IDictionary<string, object?>? values = null)
    {
        var dlg = new FormDialog(title, fields, values, owner);
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }
}

/// <summary>Fare tekerleği açılır listenin değerini asla değiştirmez; kaydırma üst kaydırma alanına iletilir.</summary>
public static class WheelGuard
{
    public static void Install() =>
        EventManager.RegisterClassHandler(typeof(ComboBox), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel), true);

    static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var cb = (ComboBox)sender;
        if (cb.IsDropDownOpen) return; // açık listede kaydırma serbest
        e.Handled = true;
        var parent = VisualTreeHelper.GetParent(cb) as UIElement;
        if (parent == null) return;
        var ev = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = cb };
        parent.RaiseEvent(ev);
    }
}
