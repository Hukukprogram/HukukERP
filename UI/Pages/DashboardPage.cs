using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using HukukErp.Core;
using HukukErp.Services;

namespace HukukErp.UI.Pages;

public class DashboardPage : BasePage
{
    readonly string _mode;
    BugunPage _bugun = null!;
    readonly Inner _cUets = new("📦  Gelen Tebligatlar");
    readonly Inner _cDurusma = new("📅  Yaklaşan Duruşmalar");
    readonly Inner _cAjanda = new("🗓  Bugünün Ajandası");
    readonly Inner _cFinans = new("💰  Finans & Cari (Vekalet Ücreti)");
    readonly Inner _cIs = new("⏱  Süreli İşler (İş Listesi)");
    readonly Inner _cIcra = new("⚖  İcra Görevleri");
    readonly Inner _cGorev = new("🚨  Kişisel Görevler (Sona Eren)");
    readonly TextBlock _ozet = Ui.Muted("", false);
    readonly TextBlock _kSure = Ui.Text("0", 26, true), _kDurusma = Ui.Text("0", 26, true), _kTeb = Ui.Text("0", 26, true), _kTahsilat = Ui.Text("0", 26, true);
    readonly TextBlock _lAlacak = Ui.Text("", 13, false, "text", true);
    readonly TextBlock _lSmm = Ui.Muted("");
    readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1000, Margin = new Thickness(0, 6, 0, 6) };

    public DashboardPage(AppCtx ctx, string mode) : base(ctx)
    {
        _mode = mode;
        Top(Ui.Text("Ofis Dashboard", 22, true), 2);
        Top(_ozet, 12);

        // özet kutuları (tıklanınca ilgili sayfa açılır)
        var kpi = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 6, 0) };
        kpi.Children.Add(Kpi("Bugün biten / geciken süre", _kSure, () => ctx.Navigate("is_listesi", null)));
        kpi.Children.Add(Kpi("Bu hafta duruşma", _kDurusma, () => ctx.Navigate("ajanda", null)));
        kpi.Children.Add(Kpi("Bekleyen tebligat", _kTeb, () => ctx.Navigate("avukat_uets", null)));
        kpi.Children.Add(Kpi("Geciken tahsilat", _kTahsilat, () => ctx.Navigate("muvekkil", null)));

        var bCek = Ui.Btn("🌐  UETS'i Aç", "Primary", () => ctx.Navigate("avukat_uets", "ShowBrowser"));
        bCek.Height = 38;
        bCek.Margin = new Thickness(0, 0, 0, 8);
        var sub = Ui.H2("Gelen Bildirimler");
        sub.Margin = new Thickness(0, 0, 0, 4);
        _cUets.Outer.Children.Insert(1, bCek);
        _cUets.Outer.Children.Insert(2, sub);

        var bTah = Ui.Btn("💰  Tahsilat Girişi Ekle", "", () => ctx.Navigate("muvekkil", "TahsilatEkle"));
        bTah.HorizontalAlignment = HorizontalAlignment.Left;
        foreach (UIElement w in new UIElement[] { _lAlacak, _lSmm, _bar, bTah }) _cFinans.Outer.Children.Add(w);

        // sol: süreler (en üstte), görevler (2.), UETS • sağ: ajanda, duruşmalar, tebligat/tahsilat, finans
        _bugun = new BugunPage(ctx, true);
        var left = new StackPanel(); var right = new StackPanel();
        left.Children.Add(_cIs);
        left.Children.Add(Gap(_cIcra));
        left.Children.Add(Gap(_cGorev));
        left.Children.Add(Gap(_cUets));
        right.Children.Add(_cAjanda);
        right.Children.Add(Gap(_cDurusma));
        right.Children.Add(Gap(_bugun));
        right.Children.Add(Gap(_cFinans));

        var cols = new Grid { Margin = new Thickness(0, 14, 6, 0) };
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        left.VerticalAlignment = VerticalAlignment.Top; right.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(right, 2);
        cols.Children.Add(left); cols.Children.Add(right);

        var tum = new StackPanel();
        tum.Children.Add(kpi);
        tum.Children.Add(cols);
        Fill(new ScrollViewer { Content = tum, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }

    static Border Kpi(string etiket, TextBlock deger, Action git)
    {
        var b = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 10, 0),
                             BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
        b.SetResourceReference(Border.BackgroundProperty, "surface2");
        b.SetResourceReference(Border.BorderBrushProperty, "border");
        var sp = new StackPanel();
        sp.Children.Add(Ui.Muted(etiket, false));
        sp.Children.Add(deger);
        b.Child = sp;
        b.MouseLeftButtonUp += (_, _) => git();
        return b;
    }

    static void Boya(TextBlock t, string brush) => t.SetResourceReference(TextBlock.ForegroundProperty, brush);

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

        // özet kutuları
        var bugunIso = Utils.TodayIso();
        long nSure = d.ScalarL("SELECT COUNT(*) FROM sureler WHERE durum='Açık' AND son_tarih!='' AND son_tarih<=?", bugunIso);
        long nDur = d.ScalarL("SELECT COUNT(*) FROM davalar WHERE durusma_tarihi!='' AND durusma_tarihi>=? AND durusma_tarihi<=date('now','+7 day') AND asama_durum!='Karara Çıktı'", bugunIso);
        long nUetsYeni = UetsEsitleme.YeniSayisi(d);
        long nTeb = d.ScalarL("SELECT COUNT(*) FROM genel_tebligatlar WHERE durum=?", Workflows.QAvukat) + nUetsYeni;
        double gec = d.ScalarD("SELECT COALESCE(SUM(kalan_bakiye),0) FROM finans_tahsilat WHERE kalan_bakiye>0 AND sonraki_tahsilat_tarihi!='' AND sonraki_tahsilat_tarihi<=?", bugunIso);
        _kSure.Text = nSure.ToString(); Boya(_kSure, nSure > 0 ? "danger" : "text");
        _kDurusma.Text = nDur.ToString(); Boya(_kDurusma, "text");
        _kTeb.Text = nTeb.ToString(); Boya(_kTeb, nTeb > 0 ? "warn" : "text");
        _kTahsilat.Text = Utils.FmtMoney(gec); Boya(_kTahsilat, gec > 0 ? "warn" : "text");
        var parcalar = new List<string>();
        if (nSure > 0) parcalar.Add($"{nSure} süre bugün bitiyor ya da gecikti");
        if (nUetsYeni > 0) parcalar.Add($"UETS'te {nUetsYeni} yeni tebligat göründü");
        if (nTeb - nUetsYeni > 0) parcalar.Add($"{nTeb - nUetsYeni} bekleyen tebligat");
        _ozet.Text = DateTime.Today.ToString("dd MMMM yyyy, dddd", Utils.Tr) + (parcalar.Count > 0 ? "  •  " + string.Join(", ", parcalar) : "  •  acil bir iş görünmüyor");
        // Şirket UETS bildirimleri
        _cUets.Clear();
        var yeniler = d.Query("SELECT * FROM uets_liste WHERE hesap_tipi='Sahsi' AND durum='Yeni' ORDER BY id DESC LIMIT 5");
        foreach (var y in yeniler)
        {
            var baslik = (y.S("kurum").Length > 0 ? y.S("kurum") : "UETS tebligatı") + (y.S("esas_no").Length > 0 ? " – " + y.S("esas_no") : "");
            var tarih = y.S("tarih").Length > 0 ? Utils.FmtDate(y.S("tarih")) : "";
            _cUets.AddRow(Ui.Text($"🆕 {baslik}" + (tarih.Length > 0 ? $"  ({tarih})" : ""), 13, true, "text", true),
                          Ui.HRow(new Pill("UETS'te Yeni", "warn"), new Pill("Henüz açılmadı", "muted")));
        }
        var ts = d.Query("SELECT * FROM genel_tebligatlar WHERE hesap_tipi='Sahsi' ORDER BY id DESC LIMIT ?", Math.Max(0, 5 - yeniler.Count));
        if (ts.Count == 0 && yeniler.Count == 0) _cUets.AddEmpty("Henüz tebligat yok");
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
        var isler = d.Query("SELECT * FROM sureler WHERE durum='Açık' AND icra_id=0 ORDER BY son_tarih='', son_tarih LIMIT 10");
        if (isler.Count == 0) _cIs.AddEmpty("Açık süreli iş yok");
        foreach (var sj in isler)
        {
            var kj = Utils.DaysLeft(sj.S("son_tarih"));
            var rj = Ui.DeadlineKind(sj.S("son_tarih"));
            UIElement? pill = kj == null ? null : kj < 0 ? new Pill($"{-kj} Gün Geçti", "danger")
                : kj == 0 ? new Pill("Bugün", "danger") : new Pill($"{kj} Gün", kj <= 3 ? "warn" : "muted");
            _cIs.AddRow(Ui.Text(sj.S("baslik"), 13, false, "text", true), pill, rj);
        }

        // İcra görevleri
        _cIcra.Clear();
        var icg = d.Query("SELECT s.*, TRIM(COALESCE(i.icra_dairesi,'') || ' ' || COALESCE(i.esas_no,'')) AS dosya, COALESCE(i.borclu,'') AS borclu, COALESCE(m.ad_soyad,'') AS muvekkil " +
                          "FROM sureler s JOIN icra_takipleri i ON i.id=s.icra_id LEFT JOIN muvekkiller m ON m.id=i.muvekkil_id " +
                          "WHERE s.durum='Açık' AND s.icra_id>0 ORDER BY s.son_tarih='', s.son_tarih LIMIT 10");
        var hz = IcraHaciz.Uyarilar(d);
        foreach (var h in hz.Take(8))
        {
            var kh = Utils.DaysLeft(h.S("dusme_tarihi"));
            var ph = new Pill(IcraHaciz.Kalan(h), IcraHaciz.Renk(h) == "muted" ? "warn" : IcraHaciz.Renk(h));
            var alth = string.Join(" • ", new[] { h.S("muvekkil"), h.S("borclu"), $"{h.S("icra_dairesi")} {h.S("esas_no")}".Trim() }.Where(x => x.Trim().Length > 0));
            _cIcra.AddRow(Ui.Text($"🔒 {h.S("tur")} haczi düşüyor: {h.S("aciklama")}  —  {alth}", 13, false, "text", true), ph, IcraHaciz.Renk(h) == "muted" ? "warn" : IcraHaciz.Renk(h));
        }
        if (icg.Count == 0 && hz.Count == 0) _cIcra.AddEmpty("Açık icra görevi / haciz uyarısı yok");
        foreach (var ij in icg)
        {
            var ki = Utils.DaysLeft(ij.S("son_tarih"));
            var ri = Ui.DeadlineKind(ij.S("son_tarih"));
            UIElement? pill = ki == null ? null : ki < 0 ? new Pill($"{-ki} Gün Geçti", "danger")
                : ki == 0 ? new Pill("Bugün", "danger") : new Pill($"{ki} Gün", ki <= 3 ? "warn" : "muted");
            var alt = string.Join(" • ", new[] { ij.S("muvekkil"), ij.S("borclu"), ij.S("dosya") }.Where(x => x.Trim().Length > 0));
            _cIcra.AddRow(Ui.Text($"{ij.S("baslik")}" + (alt.Length > 0 ? $"  —  {alt}" : "") , 13, false, "text", true), pill, ri);
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
