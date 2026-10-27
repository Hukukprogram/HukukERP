using System.Windows;
using System.Windows.Controls;
using HukukErp.Core;
using HukukErp.Services;

namespace HukukErp.UI.Pages;

/// <summary>"Bugün" ekranı: sadece bugün ve bu hafta yapılması gerekenler.</summary>
public class BugunPage : BasePage
{
    readonly Inner _cDurusma = new("⚖  Duruşmalar (bugün ve yarın)");
    readonly Inner _cSure = new("⏱  Süreler (geciken ve 7 gün içinde)");
    readonly Inner _cTeb = new("📬  Bekleyen tebligatlar");
    readonly Inner _cTahsilat = new("💰  Tahsilat (geciken ve 3 gün içinde)");
    readonly Inner _cGorev = new("✅  Görevler (bugün ve geciken)");
    readonly Inner _cAjanda = new("📅  Bugünün ajandası");
    readonly TextBlock _baslik = Ui.Text("", 22, true);

    /// <summary>gomulu=true: Ofis Dashboard'un içine yerleştirilir (kendi başlığı ve kaydırması olmaz).</summary>
    public BugunPage(AppCtx ctx, bool gomulu = false) : base(ctx, !gomulu)
    {
        if (!gomulu) Top(Ui.PageHeader("Bugün", "Bugün ve bu hafta yapılması gerekenler • kritik olanlar üstte"));
        Top(_baslik);
        var sol = new StackPanel(); var sag = new StackPanel();
        sol.Children.Add(_cSure); sol.Children.Add(Gap(_cDurusma)); sol.Children.Add(Gap(_cAjanda));
        sag.Children.Add(_cTeb); sag.Children.Add(Gap(_cTahsilat)); sag.Children.Add(Gap(_cGorev));
        var cols = new Grid { Margin = new Thickness(0, 10, 6, 0) };
        cols.ColumnDefinitions.Add(new ColumnDefinition());
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        cols.ColumnDefinitions.Add(new ColumnDefinition());
        sol.VerticalAlignment = VerticalAlignment.Top; sag.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(sag, 2);
        cols.Children.Add(sol); cols.Children.Add(sag);
        if (gomulu) Fill(cols);
        else Fill(new ScrollViewer { Content = cols, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }

    static FrameworkElement Gap(FrameworkElement e) { e.Margin = new Thickness(0, 12, 0, 0); return e; }

    static UIElement Pill_(int? k)
    {
        if (k == null) return new Pill("Tarih yok", "muted");
        if (k < 0) return new Pill($"{-k} gün geçti", "danger");
        if (k == 0) return new Pill("BUGÜN", "danger");
        return new Pill($"{k} gün", k <= 3 ? "warn" : "info");
    }

    static string Renk(int? k) => k == null ? "muted" : k < 0 ? "danger" : k <= 1 ? "danger" : k <= 3 ? "warn" : "muted";

    public override void Refresh()
    {
        _baslik.Text = DateTime.Today.ToString("dd MMMM yyyy, dddd", Utils.Tr);
        var bugun = Utils.TodayIso();

        _cSure.Clear();
        var sureler = Db.Query("SELECT sr.*, TRIM(COALESCE(d.mahkeme_adi,'') || ' ' || COALESCE(d.esas_no,'')) AS dava FROM sureler sr LEFT JOIN davalar d ON d.id=sr.dava_id " +
                               "WHERE sr.durum='Açık' AND sr.son_tarih<=date('now','+7 day') ORDER BY sr.son_tarih LIMIT 12");
        if (sureler.Count == 0) _cSure.AddEmpty("7 gün içinde biten süre yok");
        foreach (var r in sureler)
        {
            var k = Utils.DaysLeft(r.S("son_tarih"));
            var t = Ui.Text(r.S("baslik") + (r.S("dava").Length > 0 ? $"\n{r.S("dava")}" : "") + $"\nSon gün: {Utils.FmtDate(r.S("son_tarih"))}", 13, false, "text", true);
            _cSure.AddRow(t, Pill_(k), Renk(k));
        }

        _cDurusma.Clear();
        var dur = Db.Query("SELECT d.*, m.ad_soyad FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id " +
                           "WHERE d.durusma_tarihi>=? AND d.durusma_tarihi<=date('now','+1 day') AND d.asama_durum!='Karara Çıktı' ORDER BY d.durusma_tarihi", bugun);
        if (dur.Count == 0) _cDurusma.AddEmpty("Bugün ve yarın duruşma yok");
        foreach (var r in dur)
        {
            var k = Utils.DaysLeft(r.S("durusma_tarihi"));
            _cDurusma.AddRow(Ui.Text($"{r.S("mahkeme_adi")} {r.S("esas_no")}\n{r.S("ad_soyad")}", 13, false, "text", true), Pill_(k), Renk(k));
        }

        _cAjanda.Clear();
        var aj = Db.Query("SELECT * FROM ajanda WHERE tarih=? ORDER BY saat", bugun);
        if (aj.Count == 0) _cAjanda.AddEmpty("Bugün için ajanda kaydı yok");
        foreach (var r in aj)
            _cAjanda.AddRow(Ui.Text($"{r.S("saat")} {r.S("baslik")} {r.S("mahkeme")}".Trim(), 13, false, "text", true), new Pill(r.S("tur"), "info"), "muted");

        _cTeb.Clear();
        var tb = Db.Query("SELECT * FROM genel_tebligatlar WHERE durum=? ORDER BY id DESC LIMIT 10", Workflows.QAvukat);
        if (tb.Count == 0) _cTeb.AddEmpty("Bekleyen tebligat yok");
        foreach (var r in tb)
        {
            var t = Utils.ParseDate(r.S("teblig_tarihi"));
            int? k = t == null ? null : (t.Value.AddDays(IsSureleri.ETebligatGunu).Date - DateTime.Today).Days;
            var ek = t == null ? "" : $"\nOtomatik tebliğ: {t.Value.AddDays(IsSureleri.ETebligatGunu):dd.MM.yyyy}";
            _cTeb.AddRow(Ui.Text($"{r.S("gonderen_kurum")} {r.S("esas_no")}\n{r.S("konu")}{ek}", 13, false, "text", true), Pill_(k), Renk(k));
        }

        _cTahsilat.Clear();
        var th = Db.Query("SELECT f.*, m.ad_soyad FROM finans_tahsilat f JOIN muvekkiller m ON m.id=f.muvekkil_id " +
                          "WHERE f.kalan_bakiye>0 AND f.sonraki_tahsilat_tarihi!='' AND f.sonraki_tahsilat_tarihi<=date('now','+3 day') ORDER BY f.sonraki_tahsilat_tarihi LIMIT 10");
        if (th.Count == 0) _cTahsilat.AddEmpty("Vadesi gelen tahsilat yok");
        foreach (var r in th)
        {
            var k = Utils.DaysLeft(r.S("sonraki_tahsilat_tarihi"));
            _cTahsilat.AddRow(Ui.Text($"{r.S("ad_soyad")} – {r.S("dosya_konusu")}\nKalan: {Utils.FmtMoney(r.D("kalan_bakiye"))}", 13, false, "text", true), Pill_(k), Renk(k));
        }

        _cGorev.Clear();
        var gs = Db.Query("SELECT * FROM gorevler WHERE durum!='Tamamlandı' AND son_tarih!='' AND son_tarih<=? ORDER BY son_tarih LIMIT 10", bugun);
        if (gs.Count == 0) _cGorev.AddEmpty("Bugün biten ya da geciken görev yok");
        foreach (var r in gs)
        {
            var k = Utils.DaysLeft(r.S("son_tarih"));
            _cGorev.AddRow(Ui.Text(r.S("baslik"), 13, false, "text", true), Pill_(k), Renk(k));
        }
    }
}
