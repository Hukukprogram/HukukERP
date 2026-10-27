using System.Windows;
using System.Windows.Controls;
using HukukErp.Core;
using HukukErp.Services;

namespace HukukErp.UI.Pages;

public class DashboardPage : BasePage
{
    readonly string _mode;
    readonly BugunPage _bugun;
    readonly Inner _cUets = new("📦  Gelen Tebligatlar");
    readonly Inner _cDurusma = new("📅  Yaklaşan Duruşmalar");
    readonly Inner _cAjanda = new("🗓  Bugünün Ajandası");
    readonly Inner _cFinans = new("💰  Finans & Cari (Vekalet Ücreti)");
    readonly Inner _cIs = new("⏱  Süreli İşler (İş Listesi)");
    readonly Inner _cGorev = new("🚨  Kişisel Görevler (Sona Eren)");
    readonly TextBlock _lAlacak = Ui.Text("", 13, false, "text", true);
    readonly TextBlock _lSmm = Ui.Muted("");
    readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1000, Margin = new Thickness(0, 6, 0, 6) };

    public DashboardPage(AppCtx ctx, string mode) : base(ctx)
    {
        _mode = mode;
        // başlık + mod düğmeleri
        var title = new TextBlock { FontSize = 22 };
        title.SetResourceReference(TextBlock.ForegroundProperty, "text");
        title.Inlines.Add(new System.Windows.Documents.Run("Dashboard") { FontWeight = FontWeights.Bold });
        title.Inlines.Add(new System.Windows.Documents.Run(" (Gösterge Paneli)"));
        Top(Ui.Bar(title, new StackPanel()), 14);

        var left = Panel("📬  BİREYSEL UETS", false);
        var rightCard = Panel("⚖  HUKUK OFİSİ & DAVA ÖZETİ", false);
        var leftStack = new StackPanel(); var rightStack = new StackPanel();
        left.Fill(leftStack); rightCard.Fill(rightStack);

        var bCek = Ui.Btn("🌐  UETS'i Aç", "Primary", () => ctx.Navigate("avukat_uets", "ShowBrowser"));
        bCek.Height = 38;
        bCek.Margin = new Thickness(0, 0, 0, 8);
        var sub = Ui.H2("Gelen Bildirimler");
        sub.Margin = new Thickness(0, 0, 0, 4);
        _cUets.Outer.Children.Insert(1, bCek);
        _cUets.Outer.Children.Insert(2, sub);
        leftStack.Children.Add(Gap(_cUets));

        rightStack.Children.Add(_cAjanda);
        rightStack.Children.Add(Gap(_cDurusma));
        var bTah = Ui.Btn("💰  Tahsilat Girişi Ekle", "", () => ctx.Navigate("muvekkil", "TahsilatEkle"));
        bTah.HorizontalAlignment = HorizontalAlignment.Left;
        foreach (UIElement w in new UIElement[] { _lAlacak, _lSmm, _bar, bTah }) _cFinans.Outer.Children.Add(w);
        rightStack.Children.Add(Gap(_cFinans));
        rightStack.Children.Add(Gap(_cIs));
        rightStack.Children.Add(Gap(_cGorev));

        var cols = new Grid { Margin = new Thickness(0, 0, 6, 0) };
        cols.ColumnDefinitions.Add(new ColumnDefinition());
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        cols.ColumnDefinitions.Add(new ColumnDefinition());
        left.VerticalAlignment = VerticalAlignment.Top; rightCard.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(rightCard, 2);
        cols.Children.Add(left); cols.Children.Add(rightCard);
        // Üstte "Bugün" bölümü, altında genel özet
        _bugun = new BugunPage(ctx, true);
        cols.Margin = new Thickness(0, 14, 6, 0);
        var tum = new StackPanel();
        tum.Children.Add(_bugun);
        tum.Children.Add(cols);
        Fill(new ScrollViewer { Content = tum, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }

    static FrameworkElement Gap(FrameworkElement e) { e.Margin = new Thickness(0, 12, 0, 0); return e; }

    static Card Panel(string title, bool active)
    {
        var c = new Card();
        if (active) c.SetResourceReference(Border.BorderBrushProperty, "accent");
        c.Top(Ui.H2(title), 12);
        return c;
    }

    static UIElement GunPill(string? iso, bool yasal = false)
    {
        var k = Utils.DaysLeft(iso);
        if (k == null) return new Pill("Tarih yok", "muted");
        var ek = yasal ? " (Yasal Cevap)" : "";
        if (k < 0) return new Pill($"🚨 {-k} Gün Gecikti{ek}", "danger");
        if (k <= 1) return new Pill($"🚨 {(k == 0 ? "Bugün" : "1 Gün Kaldı")}{ek}", "danger");
        if (k <= 3) return new Pill($"⏰ {k} Gün Kaldı{ek}", "warn");
        return new Pill($"{k} Gün{ek}", "info");
    }

    static TextBlock BoldFirst(string bold, string rest)
    {
        var tb = Ui.Text("", 13, false, "text", true);
        tb.Inlines.Add(new System.Windows.Documents.Run(bold) { FontWeight = FontWeights.Bold });
        tb.Inlines.Add(new System.Windows.Documents.Run(rest));
        return tb;
    }

    public override void Refresh()
    {
        var d = Db;
        _bugun.Refresh();
        // Şirket UETS bildirimleri
        _cUets.Clear();
        var ts = d.Query("SELECT * FROM genel_tebligatlar WHERE hesap_tipi='Sahsi' ORDER BY id DESC LIMIT 5");
        if (ts.Count == 0) _cUets.AddEmpty("Henüz tebligat yok");
        foreach (var t in ts)
        {
            var kurum = t.S("gonderen_kurum").Length > 0 ? t.S("gonderen_kurum") : "Tebligat";
            var kind = t.S("durum") == Workflows.QAvukat ? "warn" : (t.S("durum") == "Onaylandı" ? "ok" : "muted");
            var pills = Ui.HRow(new Pill("Bireysel UETS", "ok"), new Pill(t.S("durum") == Workflows.QAvukat ? "İşlem Bekliyor" : t.S("durum"), kind));
            var text = Ui.Text($"🔔 {kurum} – {t.S("konu")}", 13, false, "text", true);
            _cUets.AddRow(text, pills);
        }

        // Duruşmalar
        _cDurusma.Clear();
        var ds = d.Query("SELECT * FROM davalar WHERE durusma_tarihi!='' AND durusma_tarihi>=date('now') ORDER BY durusma_tarihi LIMIT 4");
        if (ds.Count == 0) _cDurusma.AddEmpty("Yaklaşan duruşma yok");
        foreach (var r in ds)
        {
            var k = Utils.DaysLeft(r.S("durusma_tarihi"));
            var kind = k != null && k <= 1 ? "danger" : (k != null && k <= 7 ? "warn" : "ok");
            _cDurusma.AddRow(Ui.Text($"📅  {Utils.FmtDate(r.S("durusma_tarihi"))} - {r.S("mahkeme_adi")} - {r.S("esas_no")} Esas", 13, false, "text", true),
                new Pill(r.S("asama_durum"), kind));
        }

        // Bugünün ajandası
        _cAjanda.Clear();
        var bugun = Utils.TodayIso();
        var aj = d.Query("SELECT * FROM ajanda WHERE tarih=? ORDER BY saat", bugun);
        var dd = d.Query("SELECT * FROM davalar WHERE durusma_tarihi=? AND NOT EXISTS (SELECT 1 FROM ajanda x WHERE x.dava_id=davalar.id AND x.tarih=davalar.durusma_tarihi)", bugun);
        if (aj.Count + dd.Count == 0) _cAjanda.AddEmpty("Bugün için kayıt yok");
        foreach (var r in aj)
            _cAjanda.AddRow(Ui.Text($"{(r.S("saat").Length > 0 ? r.S("saat") + "  " : "")}{(r.S("baslik").Length > 0 ? r.S("baslik") : r.S("tur"))} — {r.S("mahkeme")} {r.S("esas_no")}".Trim(' ', '—'), 13, false, "text", true),
                new Pill(r.S("tur"), "info"));
        foreach (var r in dd)
            _cAjanda.AddRow(Ui.Text($"Duruşma — {r.S("mahkeme_adi")} {r.S("esas_no")}", 13, false, "text", true), new Pill("Duruşma", "info"));

        // Finans
        var alacak = d.ScalarD("SELECT SUM(kalan_bakiye) FROM finans_tahsilat");
        var toplam = d.ScalarD("SELECT SUM(toplam_ucret) FROM finans_tahsilat");
        var alinan = d.ScalarD("SELECT SUM(alinan_tutar) FROM finans_tahsilat");
        _lAlacak.Inlines.Clear();
        _lAlacak.Inlines.Add(new System.Windows.Documents.Run("Toplam Vekalet Alacağı: "));
        _lAlacak.Inlines.Add(new System.Windows.Documents.Run(Utils.FmtMoney(alacak)) { FontWeight = FontWeights.Bold });
        var smm = d.One("SELECT f.*, m.ad_soyad FROM finans_tahsilat f JOIN muvekkiller m ON m.id=f.muvekkil_id WHERE f.makbuz_kesildi=0 AND f.alinan_tutar>0 " +
                        "ORDER BY f.sonraki_tahsilat_tarihi='', f.sonraki_tahsilat_tarihi LIMIT 1");
        if (smm != null)
        {
            var vade = smm.S("sonraki_tahsilat_tarihi").Length > 0 ? $" (Vade: {Utils.FmtDate(smm.S("sonraki_tahsilat_tarihi"))})" : "";
            _lSmm.Text = $"SMM bekleyen: {smm.S("ad_soyad")}{vade}";
        }
        else _lSmm.Text = "SMM (makbuz) bekleyen kayıt yok";
        _bar.Value = toplam > 0 ? (int)(1000 * alinan / toplam) : 0;

        // Süreli işler
        _cIs.Clear();
        var isler = d.Query("SELECT * FROM sureler WHERE durum='Açık' ORDER BY son_tarih LIMIT 6");
        if (isler.Count == 0) _cIs.AddEmpty("Açık süreli iş yok");
        foreach (var sj in isler)
        {
            var kj = Utils.DaysLeft(sj.S("son_tarih"));
            var rj = Ui.DeadlineKind(sj.S("son_tarih"));
            UIElement? pill = kj == null ? null : kj < 0 ? new Pill($"{-kj} Gün Geçti", "danger")
                : kj == 0 ? new Pill("Bugün", "danger") : new Pill($"{kj} Gün", kj <= 3 ? "warn" : "muted");
            _cIs.AddRow(Ui.Text(sj.S("baslik"), 13, false, "text", true), pill, rj);
        }

        // Görevler
        _cGorev.Clear();
        var gs = d.Query("SELECT * FROM gorevler WHERE durum!='Tamamlandı' ORDER BY son_tarih='', son_tarih LIMIT 6");
        if (gs.Count == 0) _cGorev.AddEmpty("Açık görev yok");
        foreach (var g in gs)
        {
            var renk = g.S("oncelik") == "Yüksek" ? "danger" : g.S("oncelik") == "Orta" ? "warn" : "muted";
            var k = Utils.DaysLeft(g.S("son_tarih"));
            UIElement? pill = k == null ? null : k < 0 ? new Pill($"{-k} Gün Gecikti", "danger")
                : k <= 2 ? new Pill(k == 0 ? "Bugün" : $"Son {k} Gün", "danger") : new Pill($"{k} Gün", "muted");
            _cGorev.AddRow(Ui.Text(g.S("baslik"), 13, false, "text", true), pill, renk);
        }
    }
}
