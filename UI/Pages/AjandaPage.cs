using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HukukErp.Core;

namespace HukukErp.UI.Pages;

public class AjandaItem
{
    public long Id;
    public string Kaynak = "ajanda"; // ajanda | dava | tatil
    public DateTime Tarih;
    public int? Bas;                 // gün içi başlangıç (dakika); null = tüm gün
    public string Baslik = "", Tur = "", Detay = "", Renk = "#0096E6";
    public string Etiket => (Bas != null ? $"{Bas.Value / 60}:{Bas.Value % 60:00} " : "") + Baslik;
}

/// <summary>Resmî tatiller (sabit + dini bayramlar). 2025-2026 dini bayramlar sabit tablo, diğer yıllar Hicrî takvimden hesaplanır.</summary>
static class TrTatil
{
    static readonly Dictionary<int, List<AjandaItem>> Onbellek = new();
    static readonly (int Ay, int Gun, string Ad)[] Sabit =
    {
        (1, 1, "Yılbaşı"), (4, 23, "Ulusal Egemenlik ve Çocuk Bayramı"), (5, 1, "Emek ve Dayanışma Günü"),
        (5, 19, "Atatürk'ü Anma, Gençlik ve Spor Bayramı"), (7, 15, "Demokrasi ve Milli Birlik Günü"),
        (8, 30, "Zafer Bayramı"), (10, 29, "Cumhuriyet Bayramı"),
    };
    static readonly Dictionary<int, (DateTime Ramazan, DateTime Kurban)> Bilinen = new()
    {
        [2025] = (new DateTime(2025, 3, 30), new DateTime(2025, 6, 6)),
        [2026] = (new DateTime(2026, 3, 20), new DateTime(2026, 5, 27)),
    };

    static void Ekle(List<AjandaItem> l, DateTime d, string ad, string detay = "") =>
        l.Add(new AjandaItem { Kaynak = "tatil", Tarih = d, Baslik = ad, Tur = "Resmi tatil", Detay = detay, Renk = "#107C41" });

    public static List<AjandaItem> Yil(int y)
    {
        if (Onbellek.TryGetValue(y, out var c)) return c;
        var l = new List<AjandaItem>();
        foreach (var (ay, gun, ad) in Sabit) Ekle(l, new DateTime(y, ay, gun), ad);
        Ekle(l, new DateTime(y, 10, 28), "Cumhuriyet Bayramı Arifesi (yarım gün)");
        var ramazan = new List<DateTime>();
        var kurban = new List<DateTime>();
        string not = "";
        if (Bilinen.TryGetValue(y, out var b)) { ramazan.Add(b.Ramazan); kurban.Add(b.Kurban); }
        else
        {
            not = "Dini bayram tarihi hesaplanmıştır; Diyanet takvimiyle 1 gün farklı olabilir.";
            try
            {
                var cal = new UmAlQuraCalendar();
                for (var d = new DateTime(y, 1, 1); d.Year == y; d = d.AddDays(1))
                {
                    if (cal.GetMonth(d) == 10 && cal.GetDayOfMonth(d) == 1) ramazan.Add(d);
                    if (cal.GetMonth(d) == 12 && cal.GetDayOfMonth(d) == 10) kurban.Add(d);
                }
            }
            catch (Exception) { }
        }
        foreach (var r in ramazan)
        {
            Ekle(l, r.AddDays(-1), "Ramazan Bayramı Arifesi (yarım gün)", not);
            for (int i = 0; i < 3; i++) Ekle(l, r.AddDays(i), $"Ramazan Bayramı ({i + 1}. gün)", not);
        }
        foreach (var k in kurban)
        {
            Ekle(l, k.AddDays(-1), "Kurban Bayramı Arifesi (yarım gün)", not);
            for (int i = 0; i < 4; i++) Ekle(l, k.AddDays(i), $"Kurban Bayramı ({i + 1}. gün)", not);
        }
        Onbellek[y] = l;
        return l;
    }
}

class TbBtn : Border
{
    readonly Action _click;

    public TbBtn(string glyph, string label, Action click)
    {
        _click = click;
        Width = 64; Height = 56; Background = Brushes.Transparent; Cursor = Cursors.Hand;
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var g = new TextBlock { Text = glyph, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center };
        g.SetResourceReference(TextBlock.ForegroundProperty, "text");
        var t = new TextBlock { Text = label, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "text");
        sp.Children.Add(g); sp.Children.Add(t);
        Child = sp;
        MouseLeftButtonUp += (_, _) => _click();
    }

    public void Aktif(bool on)
    {
        if (on) SetResourceReference(BackgroundProperty, "surface");
        else { ClearValue(BackgroundProperty); Background = Brushes.Transparent; }
    }
}

/// <summary>Ajanda: Gün / Hafta / Ay / Yıl / Liste görünümleri. Duruşma, keşif, toplantı kayıtları + resmî tatiller.</summary>
public class AjandaPage : BasePage
{
    enum Gor { Gun, Hafta, Ay, Yil, Liste }

    static readonly CultureInfo Tr = new("tr-TR");
    static readonly string[] GunAd = { "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar" };
    static readonly string[] Turler = { "Duruşma", "Keşif", "Toplantı", "Diğer" };
    const string TatilTur = "Resmi tatil";
    const string Kirmizi = "#EF5350";

    Gor _g = Gor.Ay, _onceki = Gor.Ay;
    DateTime _cur = DateTime.Today;
    string _ara = "";
    readonly HashSet<string> _gizli = new();
    List<AjandaItem> _listItems = new();
    readonly TextBlock _title = new() { FontFamily = new FontFamily("Segoe UI Light"), FontSize = 36, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    readonly TextBox _araBox = new() { Width = 260, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 16, 0) };
    readonly ContentControl _body = new();
    readonly Dictionary<Gor, TbBtn> _tb = new();
    TbBtn? _araBtn;
    DateTime? _secGun;      // ay görünümünde seçili (ilk tıklanan) gün
    Border? _secHucre;

    static SolidColorBrush Hex(string h) => new((Color)ColorConverter.ConvertFromString(h));

    static string Renk(string tur) => tur switch { "Toplantı" => "#D9822B", "Diğer" => "#6B7280", TatilTur => "#107C41", _ => "#0096E6" };

    public AjandaPage(AppCtx ctx) : base(ctx, false)
    {
        // sağ araç çubuğu
        var bar = new DockPanel { Width = 64 };
        var barBorder = new Border { Child = bar };
        barBorder.SetResourceReference(Border.BackgroundProperty, "sidebar");
        var ust = new StackPanel();
        _araBtn = new TbBtn("🔍", "Ara", AraAc);
        ust.Children.Add(_araBtn);
        foreach (var (g, glyph, ad) in new[] { (Gor.Gun, "▌", "Gün"), (Gor.Hafta, "▥", "Hafta"), (Gor.Ay, "▤", "Ay"), (Gor.Yil, "▦", "Yıl"), (Gor.Liste, "☰", "Liste") })
        {
            var gg = g;
            var b = new TbBtn(glyph, ad, () => { _ara = ""; _araBox.Text = ""; _araBox.Visibility = Visibility.Collapsed; _g = gg; Render(); });
            _tb[g] = b;
            ust.Children.Add(b);
        }
        ust.Children.Add(new TbBtn("📍", "Bugün", () => { _cur = DateTime.Today; Render(); }));
        DockPanel.SetDock(ust, Dock.Top);
        bar.Children.Add(ust);
        var alt = new TbBtn("🖨", "Yazdır", Yazdir);
        DockPanel.SetDock(alt, Dock.Bottom);
        bar.Children.Add(alt);
        DockPanel.SetDock(barBorder, Dock.Right);
        Root.Children.Add(barBorder);

        // üst başlık
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4), Height = 56 };
        Button Kare(string t, string tip, Action a)
        {
            var bt = Ui.Btn(t, "", a);
            bt.Width = 54; bt.Height = 40; bt.FontSize = 18; bt.ToolTip = tip; bt.Margin = new Thickness(0, 0, 4, 0);
            return bt;
        }
        var sag = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        sag.Children.Add(Kare("＋", "Yeni etkinlik", () => Yeni(_g == Gor.Gun ? _cur : DateTime.Today, "")));
        sag.Children.Add(Kare("☑", "Görevler", () => Ctx.Navigate("kanban", null)));
        sag.Children.Add(Kare("⏷", "Filtre", FiltreAc));
        sag.Children.Add(Kare("☰", "Menü", MenuAc));
        DockPanel.SetDock(sag, Dock.Right);
        head.Children.Add(sag);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var prev = Ui.Btn("‹", "", () => Git(-1)); var next = Ui.Btn("›", "", () => Git(1));
        foreach (var b in new[] { prev, next }) { b.Width = 28; b.Height = 36; b.FontSize = 18; b.Margin = new Thickness(0, 0, 2, 0); b.Padding = new Thickness(0); }
        nav.Children.Add(prev); nav.Children.Add(next); nav.Children.Add(_title);
        DockPanel.SetDock(nav, Dock.Left);
        head.Children.Add(nav);
        head.Children.Add(_araBox);
        _araBox.HorizontalAlignment = HorizontalAlignment.Left;
        _araBox.TextChanged += (_, _) => { _ara = _araBox.Text.Trim(); if (_ara.Length > 0) { _g = Gor.Liste; Render(); } };

        var content = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        content.Children.Add(head);
        content.Children.Add(_body);
        Fill(content);
        Render();
    }

    public override void Refresh() => Render();

    // ------------------------------------------------------------------ gezinme / araçlar
    void Git(int yon)
    {
        _cur = _g switch
        {
            Gor.Gun => _cur.AddDays(yon),
            Gor.Hafta => _cur.AddDays(7 * yon),
            Gor.Yil => _cur.AddYears(yon),
            _ => _cur.AddMonths(yon),
        };
        Render();
    }

    void AraAc()
    {
        if (_araBox.Visibility == Visibility.Visible)
        {
            _araBox.Text = ""; _ara = ""; _araBox.Visibility = Visibility.Collapsed;
            _g = _onceki; Render();
            return;
        }
        _onceki = _g == Gor.Liste ? Gor.Ay : _g;
        _araBox.Visibility = Visibility.Visible;
        _araBox.Focus();
    }

    void FiltreAc()
    {
        var m = new ContextMenu();
        foreach (var t in Turler.Concat(new[] { TatilTur }))
        {
            var tt = t;
            var mi = new MenuItem { Header = t, IsCheckable = true, IsChecked = !_gizli.Contains(t), StaysOpenOnClick = true };
            mi.Click += (_, _) => { if (mi.IsChecked) _gizli.Remove(tt); else _gizli.Add(tt); Render(); };
            m.Items.Add(mi);
        }
        m.IsOpen = true;
    }

    void MenuAc()
    {
        var m = new ContextMenu();
        void Ekle(string h, Action a) { var mi = new MenuItem { Header = h }; mi.Click += (_, _) => a(); m.Items.Add(mi); }
        Ekle("Bugüne git", () => { _cur = DateTime.Today; Render(); });
        Ekle("Yeni etkinlik…", () => Yeni(DateTime.Today, ""));
        Ekle("Yazdır…", Yazdir);
        Ekle("Dava & Müvekkil sayfasına git", () => Ctx.Navigate("dava", null));
        m.IsOpen = true;
    }

    void Yazdir()
    {
        try
        {
            var pd = new PrintDialog();
            if (pd.ShowDialog() == true && _body.Content is Visual v) pd.PrintVisual(v, "Ajanda — " + _title.Text);
        }
        catch (Exception e) { Ui.Warn(this, "Yazdırılamadı:\n" + e.Message); }
    }

    // ------------------------------------------------------------------ veri
    static DateTime? Iso(string s) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    static int? Dakika(string s)
    {
        var m = Regex.Match(s ?? "", @"^\s*(\d{1,2})[:.](\d{2})\s*$");
        if (!m.Success) return null;
        int h = int.Parse(m.Groups[1].Value), mi = int.Parse(m.Groups[2].Value);
        return h < 24 && mi < 60 ? h * 60 + mi : null;
    }

    AjandaItem Of(Row r)
    {
        var tur = r.S("tur").Length > 0 ? r.S("tur") : "Duruşma";
        var detay = string.Join("\n", new[] { r.S("mahkeme"), r.S("esas_no").Length > 0 ? r.S("esas_no") + " Esas" : "", r.S("notlar") }.Where(x => x.Length > 0));
        return new AjandaItem
        {
            Id = r.L("id"), Kaynak = "ajanda", Tarih = Iso(r.S("tarih")) ?? DateTime.Today, Bas = Dakika(r.S("saat")),
            Baslik = r.S("baslik").Length > 0 ? r.S("baslik") : tur, Tur = tur, Detay = detay, Renk = Renk(tur),
        };
    }

    List<AjandaItem> Yukle(DateTime a, DateTime b, bool tatil = true)
    {
        var l = new List<AjandaItem>();
        var ai = a.ToString("yyyy-MM-dd"); var bi = b.ToString("yyyy-MM-dd");
        foreach (var r in Db.Query("SELECT * FROM ajanda WHERE tarih>=? AND tarih<=? ORDER BY tarih, saat", ai, bi)) l.Add(Of(r));
        foreach (var r in Db.Query("SELECT d.* FROM davalar d WHERE d.durusma_tarihi!='' AND d.durusma_tarihi>=? AND d.durusma_tarihi<=? " +
                                   "AND NOT EXISTS (SELECT 1 FROM ajanda x WHERE x.dava_id=d.id AND x.tarih=d.durusma_tarihi)", ai, bi))
        {
            var d = Iso(r.S("durusma_tarihi"));
            if (d == null) continue;
            l.Add(new AjandaItem
            {
                Id = r.L("id"), Kaynak = "dava", Tarih = d.Value, Baslik = "Duruşma", Tur = "Duruşma", Renk = Renk("Duruşma"),
                Detay = $"{r.S("mahkeme_adi")} {r.S("esas_no")}".Trim(),
            });
        }
        foreach (var r in Db.Query("SELECT * FROM sureler WHERE durum='Açık' AND son_tarih>=? AND son_tarih<=?", ai, bi))
        {
            var d = Iso(r.S("son_tarih"));
            if (d == null) continue;
            l.Add(new AjandaItem
            {
                Id = r.L("id"), Kaynak = "is", Tarih = d.Value, Baslik = "⏱ " + r.S("baslik"), Tur = "Süreli İş", Renk = "#C42B1C",
                Detay = r.S("dayanak"),
            });
        }
        if (tatil)
            for (int y = a.Year; y <= b.Year; y++)
                foreach (var t in TrTatil.Yil(y))
                    if (t.Tarih >= a.Date && t.Tarih <= b.Date) l.Add(t);
        return l.Where(x => !_gizli.Contains(x.Tur))
                .OrderBy(x => x.Tarih).ThenBy(x => x.Kaynak == "tatil" ? 0 : 1).ThenBy(x => x.Bas ?? -1).ToList();
    }

    List<AjandaItem> Ara()
    {
        var q = Utils.TrLower(_ara);
        return Yukle(new DateTime(2000, 1, 1), new DateTime(2100, 12, 31), false)
            .Where(x => Utils.TrLower(x.Baslik + " " + x.Tur + " " + x.Detay).Contains(q)).ToList();
    }

    // ------------------------------------------------------------------ ekle / düzenle / sil
    List<Field> Alanlar()
    {
        var opts = new List<(object? Value, string Label)> { (0L, "— Bağlı dava yok —") };
        foreach (var d in Db.Query("SELECT id, mahkeme_adi, esas_no FROM davalar ORDER BY id DESC"))
            opts.Add((d.L("id"), $"{d.S("mahkeme_adi")} {d.S("esas_no")}".Trim()));
        return new List<Field>
        {
            new("tur", "Tür", "combo", Field.Of(Turler), def: "Duruşma"),
            new("tarih", "Tarih", "date", required: true),
            new("saat", "Saat (SS:DD — boş bırakılırsa tüm gün)", placeholder: "9:30"),
            new("baslik", "Başlık (boşsa tür yazılır)"),
            new("mahkeme", "Mahkeme / yer"),
            new("esas_no", "Esas No"),
            new("dava_id", "Bağlı dava", "combo", opts, def: 0L),
            new("notlar", "Notlar", "multiline"),
        };
    }

    void Yeni(DateTime gun, string saat)
    {
        var ilk = new Dictionary<string, object?> { ["tur"] = "Duruşma", ["tarih"] = gun.ToString("yyyy-MM-dd"), ["saat"] = saat, ["dava_id"] = 0L };
        Kaydet(null, "Yeni etkinlik", ilk);
    }

    void Duzenle(long id)
    {
        var r = Db.One("SELECT * FROM ajanda WHERE id=?", id);
        if (r == null) return;
        Kaydet(id, "Etkinliği düzenle", r);
    }

    void Kaydet(long? id, string baslik, IDictionary<string, object?> ilk)
    {
        var alanlar = Alanlar();
        var v = FormDialog.Ask(this, baslik, alanlar, ilk);
        while (v != null && (Convert.ToString(v["saat"]) is { Length: > 0 } sa && Dakika(sa) == null))
        {
            Ui.Warn(this, "Saat biçimi geçersiz. Örnek: 9:30 veya 14:05");
            v = FormDialog.Ask(this, baslik, alanlar, v);
        }
        if (v == null) return;
        var dk = Dakika(Convert.ToString(v["saat"]) ?? "");
        v["saat"] = dk == null ? "" : $"{dk.Value / 60:00}:{dk.Value % 60:00}";
        v["dava_id"] = Convert.ToInt64(v["dava_id"] ?? 0L);
        if (id == null) Db.Insert("ajanda", v); else Db.Update("ajanda", id.Value, v);
        // Duruşma bir davaya bağlıysa ve davanın duruşma tarihi boş/geçmişse güncelle
        var davaId = Convert.ToInt64(v["dava_id"]);
        var tarih = Convert.ToString(v["tarih"]) ?? "";
        if (davaId > 0 && Convert.ToString(v["tur"]) == "Duruşma" && string.CompareOrdinal(tarih, Utils.TodayIso()) >= 0)
        {
            var d = Db.One("SELECT durusma_tarihi FROM davalar WHERE id=?", davaId);
            var mevcut = d?.S("durusma_tarihi") ?? "";
            if (d != null && (mevcut.Length == 0 || string.CompareOrdinal(mevcut, Utils.TodayIso()) < 0))
                Db.Update("davalar", davaId, new Dictionary<string, object?> { ["durusma_tarihi"] = tarih });
        }
        Render();
    }

    void Sil(long id)
    {
        if (!Ui.Confirm(this, "Bu etkinlik silinsin mi?")) return;
        Db.Delete("ajanda", id);
        Render();
    }

    void Ac(AjandaItem it)
    {
        if (it.Kaynak == "ajanda") Duzenle(it.Id);
        else if (it.Kaynak == "dava") Ctx.Navigate("dava", null);
        else if (it.Kaynak == "is") Ctx.Navigate("is_listesi", null);
    }

    // ------------------------------------------------------------------ çizim
    string Baslik() => _ara.Length > 0 ? $"Arama: {_ara}" : _g switch
    {
        Gor.Gun => _cur.ToString("d MMMM yyyy, dddd", Tr),
        Gor.Hafta => HaftaBaslangic(_cur).ToString("d MMMM", Tr) + " – " + HaftaBaslangic(_cur).AddDays(6).ToString("d MMMM yyyy", Tr),
        Gor.Yil => _cur.Year.ToString(),
        Gor.Liste => "Liste",
        _ => _cur.ToString("MMMM yyyy", Tr),
    };

    static DateTime HaftaBaslangic(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    void Render()
    {
        _title.Text = Baslik();
        foreach (var kv in _tb) kv.Value.Aktif(kv.Key == _g && _ara.Length == 0);
        _araBtn?.Aktif(_ara.Length > 0 || _araBox.Visibility == Visibility.Visible);
        _body.Content = _g switch
        {
            Gor.Gun => ZamanGrid(new List<DateTime> { _cur.Date }),
            Gor.Hafta => ZamanGrid(Enumerable.Range(0, 7).Select(i => HaftaBaslangic(_cur).AddDays(i)).ToList()),
            Gor.Yil => YilGorunumu(),
            Gor.Liste => ListeGorunumu(),
            _ => AyGorunumu(),
        };
    }

    Border Pill(AjandaItem it)
    {
        var t = new TextBlock { Text = it.Etiket, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border
        {
            Background = Hex(it.Renk), CornerRadius = new CornerRadius(3), MinHeight = 22, Margin = new Thickness(0, 0, 0, 2),
            Padding = new Thickness(8, 0, 8, 0), Child = t, Cursor = it.Kaynak == "tatil" ? Cursors.Arrow : Cursors.Hand,
            ToolTip = it.Etiket + (it.Detay.Length > 0 ? "\n" + it.Detay : ""),
        };
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        if (it.Kaynak != "tatil")
        {
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; Ac(it); };
            var m = new ContextMenu();
            if (it.Kaynak == "ajanda")
            {
                var d = new MenuItem { Header = "Düzenle" }; d.Click += (_, _) => Duzenle(it.Id); m.Items.Add(d);
                var s = new MenuItem { Header = "Sil" }; s.Click += (_, _) => Sil(it.Id); m.Items.Add(s);
            }
            else
            {
                var g = new MenuItem { Header = "Davaya git" }; g.Click += (_, _) => Ac(it); m.Items.Add(g);
            }
            b.ContextMenu = m;
        }
        return b;
    }

    // ---- ay
    UIElement AyGorunumu()
    {
        var first = new DateTime(_cur.Year, _cur.Month, 1);
        var start = HaftaBaslangic(first);
        var last = first.AddMonths(1).AddDays(-1);
        var end = HaftaBaslangic(last).AddDays(6);
        int weeks = ((end - start).Days + 1) / 7;
        var items = Yukle(start, end);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        for (int i = 0; i < 7; i++) g.ColumnDefinitions.Add(new ColumnDefinition());
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int w = 0; w < weeks; w++) g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 60 });
        for (int i = 0; i < 7; i++)
        {
            var h = new TextBlock { Text = GunAd[i], FontSize = 16, Margin = new Thickness(4, 2, 0, 4) };
            h.SetResourceReference(TextBlock.ForegroundProperty, "text");
            Grid.SetColumn(h, i + 1);
            g.Children.Add(h);
        }
        for (int k = 0; k < weeks * 7; k++)
        {
            var d = start.AddDays(k);
            int w = k / 7, c = k % 7;
            if (c == 0)
            {
                var wn = new TextBlock
                {
                    Text = ISOWeek.GetWeekOfYear(d).ToString(), FontFamily = new FontFamily("Segoe UI Light"), FontSize = 30,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    LayoutTransform = new RotateTransform(-90),
                };
                wn.SetResourceReference(TextBlock.ForegroundProperty, "muted");
                Grid.SetRow(wn, w + 1);
                g.Children.Add(wn);
            }
            var cell = Hucre(d, items.Where(x => x.Tarih.Date == d).ToList(), d.Month == _cur.Month);
            Grid.SetRow(cell, w + 1); Grid.SetColumn(cell, c + 1);
            g.Children.Add(cell);
        }
        return g;
    }

    Border Hucre(DateTime d, List<AjandaItem> list, bool buAy)
    {
        var b = new Border { BorderThickness = new Thickness(0.5), ClipToBounds = true };
        bool secili = _secGun == d.Date;
        b.SetResourceReference(Border.BackgroundProperty, secili ? "sel" : "surface2");
        b.SetResourceReference(Border.BorderBrushProperty, secili ? "accent" : "border");
        if (secili) _secHucre = b;
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        FrameworkElement num;
        if (d.Date == DateTime.Today)
        {
            num = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Hex(Kirmizi),
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 3, 0, 1),
                Child = new TextBlock { Text = d.Day.ToString(), Foreground = Brushes.White, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
        }
        else
        {
            var t = new TextBlock { Text = d.Day.ToString(), FontSize = 15, Margin = new Thickness(6, 4, 0, 2) };
            t.SetResourceReference(TextBlock.ForegroundProperty, buAy ? "text" : "muted");
            num = t;
        }
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        Grid.SetRow(sp, 1);
        grid.Children.Add(num); grid.Children.Add(sp);
        b.Child = grid;
        void Doldur()
        {
            sp.Children.Clear();
            int cap = Math.Max(1, (int)((b.ActualHeight - 34) / 26));
            int goster = list.Count > cap ? Math.Max(0, cap - 1) : list.Count;
            for (int i = 0; i < goster; i++) sp.Children.Add(Pill(list[i]));
            if (list.Count > goster)
            {
                var more = new TextBlock { Text = $"+{list.Count - goster} daha", FontSize = 12, Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand };
                more.SetResourceReference(TextBlock.ForegroundProperty, "accent");
                more.MouseLeftButtonUp += (_, e) => { e.Handled = true; _cur = d; _g = Gor.Gun; Render(); };
                sp.Children.Add(more);
            }
        }
        b.SizeChanged += (_, _) => Doldur();
        // İlk tıklama günü seçer (renk değişir), aynı güne ikinci tıklama yeni kayıt penceresini açar.
        b.MouseLeftButtonDown += (_, e) =>
        {
            if (_secGun == d.Date)
            {
                _secGun = null;
                _secHucre = null;
                b.SetResourceReference(Border.BackgroundProperty, "surface2");
                b.SetResourceReference(Border.BorderBrushProperty, "border");
                Yeni(d, "");
                return;
            }
            if (_secHucre != null)
            {
                _secHucre.SetResourceReference(Border.BackgroundProperty, "surface2");
                _secHucre.SetResourceReference(Border.BorderBrushProperty, "border");
            }
            _secGun = d.Date;
            _secHucre = b;
            b.SetResourceReference(Border.BackgroundProperty, "sel");
            b.SetResourceReference(Border.BorderBrushProperty, "accent");
        };
        return b;
    }

    // ---- gün / hafta
    UIElement ZamanGrid(List<DateTime> gunler)
    {
        const double H = 56; const int h0 = 7, h1 = 21;
        var items = Yukle(gunler[0], gunler[^1]);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        void Kolonlar(Grid g)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            foreach (var _ in gunler) g.ColumnDefinitions.Add(new ColumnDefinition());
        }
        var head = new Grid(); Kolonlar(head);
        var tum = new Grid(); Kolonlar(tum);
        for (int i = 0; i < gunler.Count; i++)
        {
            var d = gunler[i];
            var hs = new StackPanel { Margin = new Thickness(6, 2, 0, 6) };
            var ad = new TextBlock { Text = GunAd[((int)d.DayOfWeek + 6) % 7], FontSize = 15 };
            ad.SetResourceReference(TextBlock.ForegroundProperty, "text");
            hs.Children.Add(ad);
            FrameworkElement n;
            if (d == DateTime.Today)
                n = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Hex(Kirmizi), HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock { Text = d.Day.ToString(), Foreground = Brushes.White, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            else
            {
                var t = new TextBlock { Text = d.Day.ToString(), FontSize = 20 };
                t.SetResourceReference(TextBlock.ForegroundProperty, "text");
                n = t;
            }
            hs.Children.Add(n);
            Grid.SetColumn(hs, i + 1); head.Children.Add(hs);
            var ts = new StackPanel { Margin = new Thickness(2, 0, 2, 4) };
            foreach (var it in items.Where(x => x.Tarih.Date == d && x.Bas == null)) ts.Children.Add(Pill(it));
            Grid.SetColumn(ts, i + 1); tum.Children.Add(ts);
        }
        Grid.SetRow(head, 0); Grid.SetRow(tum, 1);
        root.Children.Add(head); root.Children.Add(tum);

        var inner = new Grid { Height = (h1 - h0) * H };
        Kolonlar(inner);
        for (int h = h0; h <= h1; h++)
        {
            var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, (h - h0) * H, 0, 0) };
            line.SetResourceReference(Border.BackgroundProperty, "border");
            Grid.SetColumnSpan(line, gunler.Count + 1);
            inner.Children.Add(line);
            if (h < h1)
            {
                var lb = new TextBlock { Text = $"{h}:00", FontSize = 12, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, (h - h0) * H + 3, 0, 0) };
                lb.SetResourceReference(TextBlock.ForegroundProperty, "muted");
                inner.Children.Add(lb);
            }
        }
        for (int i = 0; i < gunler.Count; i++)
        {
            var d = gunler[i];
            var cv = new Canvas { ClipToBounds = true, Background = Brushes.Transparent };
            Grid.SetColumn(cv, i + 1);
            var sol = new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Left };
            sol.SetResourceReference(Border.BackgroundProperty, "border");
            Grid.SetColumn(sol, i + 1);
            inner.Children.Add(cv); inner.Children.Add(sol);
            var tl = items.Where(x => x.Tarih.Date == d && x.Bas != null).OrderBy(x => x.Bas).ToList();
            var laneEnd = new List<int>();
            var lane = new int[tl.Count];
            for (int j = 0; j < tl.Count; j++)
            {
                int s = tl[j].Bas!.Value, e = s + 40;
                int ln = laneEnd.FindIndex(x => x <= s);
                if (ln < 0) { laneEnd.Add(e); ln = laneEnd.Count - 1; } else laneEnd[ln] = e;
                lane[j] = ln;
            }
            int lanes = Math.Max(1, laneEnd.Count);
            var pills = new List<(Border P, int Lane)>();
            for (int j = 0; j < tl.Count; j++)
            {
                var p = Pill(tl[j]);
                p.Height = 26; p.Margin = new Thickness(0);
                double top = Math.Max(0, (tl[j].Bas!.Value - h0 * 60) * H / 60.0);
                Canvas.SetTop(p, top);
                cv.Children.Add(p);
                pills.Add((p, lane[j]));
            }
            cv.SizeChanged += (_, _) =>
            {
                double w = cv.ActualWidth / lanes;
                foreach (var (p, ln) in pills) { Canvas.SetLeft(p, ln * w + 2); p.Width = Math.Max(10, w - 4); }
            };
            var dd = d;
            cv.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount != 2) return;
                double y = e.GetPosition(cv).Y;
                int min = (int)(h0 * 60 + y / H * 60);
                min = min / 15 * 15;
                Yeni(dd, $"{min / 60}:{min % 60:00}");
            };
            if (d == DateTime.Today)
            {
                int nowMin = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
                if (nowMin >= h0 * 60 && nowMin <= h1 * 60)
                {
                    var nl = new Border { Height = 2, Background = Hex(Kirmizi), IsHitTestVisible = false };
                    Canvas.SetTop(nl, (nowMin - h0 * 60) * H / 60.0);
                    cv.Children.Add(nl);
                    cv.SizeChanged += (_, _) => nl.Width = cv.ActualWidth;
                }
            }
        }
        var sc = new ScrollViewer { Content = inner, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        sc.Loaded += (_, _) => sc.ScrollToVerticalOffset(H);
        Grid.SetRow(sc, 2);
        root.Children.Add(sc);
        return root;
    }

    // ---- yıl
    UIElement YilGorunumu()
    {
        int y = _cur.Year;
        var items = Yukle(new DateTime(y, 1, 1), new DateTime(y, 12, 31));
        var gunVar = items.Where(x => x.Kaynak != "tatil").Select(x => x.Tarih.Date).ToHashSet();
        var tatil = items.Where(x => x.Kaynak == "tatil").Select(x => x.Tarih.Date).ToHashSet();
        var ug = new UniformGrid { Columns = 4, Margin = new Thickness(8) };
        for (int m = 1; m <= 12; m++)
        {
            var ilk = new DateTime(y, m, 1);
            var kart = new StackPanel { Margin = new Thickness(10) };
            var bas = new TextBlock { Text = ilk.ToString("MMMM", Tr), FontSize = 17, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 4) };
            bas.SetResourceReference(TextBlock.ForegroundProperty, "accent");
            bas.MouseLeftButtonUp += (_, _) => { _cur = ilk; _g = Gor.Ay; Render(); };
            kart.Children.Add(bas);
            var gu = new UniformGrid { Columns = 7 };
            foreach (var h in new[] { "Pt", "Sa", "Ça", "Pe", "Cu", "Ct", "Pz" })
            {
                var ht = new TextBlock { Text = h, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
                ht.SetResourceReference(TextBlock.ForegroundProperty, "muted");
                gu.Children.Add(ht);
            }
            for (int i = 0; i < ((int)ilk.DayOfWeek + 6) % 7; i++) gu.Children.Add(new TextBlock());
            for (int dnum = 1; dnum <= DateTime.DaysInMonth(y, m); dnum++)
            {
                var d = new DateTime(y, m, dnum);
                var t = new TextBlock { Text = dnum.ToString(), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(0, 2, 0, 2), Cursor = Cursors.Hand };
                if (d == DateTime.Today) { t.Foreground = Brushes.White; t.Background = Hex(Kirmizi); }
                else if (gunVar.Contains(d)) { t.Foreground = Hex("#0096E6"); t.FontWeight = FontWeights.Bold; }
                else if (tatil.Contains(d)) { t.Foreground = Hex("#107C41"); }
                else t.SetResourceReference(TextBlock.ForegroundProperty, "text");
                var dd = d;
                t.MouseLeftButtonUp += (_, _) => { _cur = dd; _g = Gor.Gun; Render(); };
                gu.Children.Add(t);
            }
            kart.Children.Add(gu);
            ug.Children.Add(kart);
        }
        return new ScrollViewer { Content = ug, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    // ---- liste
    UIElement ListeGorunumu()
    {
        _listItems = _ara.Length > 0 ? Ara() : Yukle(_cur.Date, _cur.Date.AddDays(365));
        var t = new DataTable(("Tarih", "tarih", Fmt.Date), ("Saat", "saat", null), ("Tür", "tur", null), ("Başlık", "baslik", null), ("Ayrıntı", "detay", null));
        var rows = _listItems.Select((x, i) => new Row
        {
            ["tarih"] = x.Tarih.ToString("yyyy-MM-dd"), ["saat"] = x.Bas != null ? $"{x.Bas.Value / 60}:{x.Bas.Value % 60:00}" : "",
            ["tur"] = x.Tur, ["baslik"] = x.Baslik, ["detay"] = x.Detay.Replace("\n", " · "), ["_k"] = (long)i,
        }).ToList();
        t.Load(rows);
        t.MouseDoubleClick += (_, _) =>
        {
            var r = t.Current;
            if (r != null) Ac(_listItems[(int)r.L("_k")]);
        };
        t.Margin = new Thickness(8, 0, 0, 0);
        return t;
    }
}
