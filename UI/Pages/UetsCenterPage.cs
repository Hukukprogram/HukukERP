using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HukukErp.Core;
using HukukErp.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HukukErp.UI.Pages;

/// <summary>Bireysel (Avukat) UETS Merkezi (tip=Sahsi).</summary>
public class UetsCenterPage : BasePage
{
    readonly string _tip;
    readonly string _kuyrukDurum;
    readonly DataTable _tQ, _tA;
    readonly TextBlock _log = Ui.Muted("", false);
    readonly PdfPreview _preview = new();
    readonly TabControl _mainTabs = new();
    readonly UetsBrowser _browser;

    public UetsCenterPage(AppCtx ctx, string tip) : base(ctx)
    {
        _tip = tip;
        _kuyrukDurum = Workflows.QAvukat;
        const string baslik = "Bireysel UETS Takibi";
        const string alt = "Bireysel UETS hesaplarından gelen tebligatlar burada izlenir.";
        Top(Ui.PageHeader(baslik, alt));

        var bar = Ui.Bar(Ui.HRow(Ui.Btn("🌐 UETS'i Aç (Tarayıcı)", "Primary", ShowBrowser),
                                 Ui.Btn("Dosya İçe Aktar…", "", ImportPdf), Ui.Btn("Klasörden Tara…", "", ImportFolder)), _log);
        Top(bar);

        _tQ = new DataTable(("Tarih", "teblig_tarihi", Fmt.Date), ("Hesap", "uets_hesap_adi", null), ("Kurum", "gonderen_kurum", null),
                            ("Esas", "esas_no", null), ("Konu", "konu", null), ("Dava", "dava", null), ("İş Listesi", "is_durum", null));
        _tA = new DataTable(("Tarih", "teblig_tarihi", Fmt.Date), ("Hesap", "uets_hesap_adi", null), ("Kurum", "gonderen_kurum", null),
                            ("Konu", "konu", null), ("Esas", "esas_no", null), ("Durum", "durum", null), ("Dava", "dava", null), ("İş Listesi", "is_durum", null));

        // kuyruk sekmesi
        Button B(string metin, string ipucu, string stil, Action eylem) { var dugme = Ui.Btn(metin, stil, eylem); dugme.ToolTip = ipucu; return dugme; }
        var qb = Ui.FRow(B("✔ İşlendi", "İşlendi olarak işaretle", "Ok", Isle), B("→ Dava Aç", "Dava kaydı aç", "", DavaAc),
                         B("⏱ İş Listesi", "İş listesine süreli iş ekle", "Primary", () => IseEkle(_tQ.Current)), B("✉ Mail", "Mail gönder", "", () => MailGonder(_tQ.Current)),
                         B("📁 Bağla", "Davaya bağla", "", () => DavayaBagla(_tQ.Current)), B("📂 Dosya", "Dosyayı aç", "", () => OpenPdf(_tQ.Current)),
                         B("🌐 UETS", "UETS'te göster", "", () => UetsteGoster(_tQ.Current)), B("📅 Tarih", "Tebliğ (teslim) tarihini düzelt", "", () => TarihDuzelt(_tQ.Current)),
                         B("🗑 Sil", "Tebligatı sil", "Danger", () => SilTebligat(_tQ.Current)));
        var qDock = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(qb, Dock.Bottom); qb.Margin = new Thickness(0, 8, 0, 0);
        qDock.Children.Add(qb); qDock.Children.Add(_tQ);

        var aDock = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var ab = Ui.FRow(B("⏱ İş Listesi", "İş listesine süreli iş ekle", "", () => IseEkle(_tA.Current)), B("📂 Dosya", "Dosyayı aç", "", () => OpenPdf(_tA.Current)),
                         B("🌐 UETS", "UETS'te göster", "", () => UetsteGoster(_tA.Current)), B("✉ Mail", "Mail gönder", "", () => MailGonder(_tA.Current)),
                         B("📅 Tarih", "Tebliğ (teslim) tarihini düzelt", "", () => TarihDuzelt(_tA.Current)), B("🗑 Sil", "Tebligatı sil", "Danger", () => SilTebligat(_tA.Current)));
        DockPanel.SetDock(ab, Dock.Bottom); ab.Margin = new Thickness(0, 8, 0, 0);
        aDock.Children.Add(ab); aDock.Children.Add(_tA);

        var inner = new TabControl();
        inner.Items.Add(Ui.Tab("Dava Tebligatları Kuyruğu", qDock));
        inner.Items.Add(Ui.Tab("Tüm Tebligatlar (Arşiv)", aDock));

        var prev = new Card();
        prev.Top(Ui.H2("Canlı Önizleme"));
        prev.Fill(_preview);

        var split = Ui.HSplit(inner, prev, 7, 5);
        split.Margin = new Thickness(0, 8, 0, 0);

        _browser = new UetsBrowser(ctx, tip, Hesaplar, Refresh);
        _mainTabs.Items.Add(Ui.Tab("Kuyruk & Arşiv", split));
        _mainTabs.Items.Add(Ui.Tab("UETS (Tarayıcı)", _browser));
        _mainTabs.SelectionChanged += (s, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, _mainTabs)) return;
            if (_mainTabs.SelectedIndex == 1) _browser.Activate();
        };
        Fill(_mainTabs);

        _tQ.SelectionChanged += (_, _) => _preview.Open(Yol(_tQ.Current));
        _tA.SelectionChanged += (_, _) => _preview.Open(Yol(_tA.Current));
    }

    string? Yol(Row? r) => r != null && r.S("pdf_yolu").Length > 0 ? Workflows.DosyaYolu(Cfg, r.S("pdf_yolu")) : null;

    List<Row> Hesaplar() => Db.Query("SELECT * FROM uets_hesaplari WHERE hesap_tipi=? AND durum='Aktif' ORDER BY hesap_adi", _tip);

    public void ShowBrowser() => _mainTabs.SelectedIndex = 1;

    public override void Refresh()
    {
        try { Workflows.KurumlariYenidenOku(Db, Cfg); } catch (Exception) { }
        const string isDurum = "(SELECT CASE WHEN COUNT(*)=0 THEN '' ELSE '⏱ ' || MIN(son_tarih) END FROM sureler s " +
                               "WHERE s.tebligat_id=g.id AND s.durum='Açık') AS is_durum, " +
                               "(SELECT TRIM(COALESCE(d.mahkeme_adi,'') || ' ' || COALESCE(d.esas_no,'')) FROM davalar d WHERE d.id=g.dava_id) AS dava";
        _tQ.Load(Db.Query($"SELECT g.*, {isDurum} FROM genel_tebligatlar g WHERE g.hesap_tipi=? AND g.durum=? ORDER BY g.id DESC", _tip, _kuyrukDurum),
                 null);
        _tA.Load(Db.Query($"SELECT g.*, {isDurum} FROM genel_tebligatlar g WHERE g.hesap_tipi=? ORDER BY g.id DESC LIMIT 500", _tip),
                 new() { ["durum"] = r => Ui.StatusKind(r.S("durum")) });
    }

    void OpenPdf(Row? r)
    {
        var y = Yol(r);
        if (y != null) Ui.OpenExternal(y);
        else Ui.Info(this, "Bu kayda bağlı dosya yok.");
    }

    /// <summary>UETS'teki "Teslim Tarihi"ni elle girer; tebliğ sayılacağı gün e-tebligat kuralına göre gösterilir.</summary>
    void TarihDuzelt(Row? t)
    {
        if (t == null) { Ui.Info(this, "Önce bir tebligat seçin."); return; }
        var v = FormDialog.Ask(this, "Tebliğ (Teslim) Tarihini Düzelt", new List<Field>
        {
            new Field("tarih", "UETS'teki Teslim Tarihi (GG.AA.YYYY)", "date", required: true),
        }, new Dictionary<string, object?> { ["tarih"] = t.S("teblig_tarihi") });
        if (v == null) return;
        var yeni = Convert.ToString(v["tarih"]) ?? "";
        if (Utils.ParseDate(yeni) == null) { Ui.Warn(this, "Geçerli bir tarih girin."); return; }
        var yeniIso = Utils.Iso(Utils.ParseDate(yeni)!.Value);
        Db.Update("genel_tebligatlar", t.L("id"), new Dictionary<string, object?> { ["teblig_tarihi"] = yeniIso });
        Refresh();
        Ui.Info(this, $"Tebliğ tarihi {Utils.FmtDate(yeniIso)} olarak kaydedildi. Tebliğ sayılacağı gün: {Utils.FmtDate(Utils.AddDays(yeniIso, IsSureleri.ETebligatGunu))}.");
    }

    /// <summary>UETS tarayıcı sekmesine geçer ve aranacak tebligatı hatırlatır. Hiçbir tebligat açılmaz; açmayı siz yaparsınız.</summary>
    void UetsteGoster(Row? t)
    {
        if (t == null) { Ui.Info(this, "Önce bir tebligat seçin."); return; }
        _browser.ShowTebligat($"{t.S("gonderen_kurum")} {t.S("esas_no")} – {Utils.FmtDate(t.S("teblig_tarihi"))}".Trim());
        _mainTabs.SelectedIndex = 1;
    }

    // ------------------------------------------------------------------ mail
    void MailGonder(Row? t)
    {
        if (t == null) { Ui.Info(this, "Önce bir tebligat seçin."); return; }
        var ayar = Db.GetSettings();
        if (ayar.S("smtp_server").Length == 0 || ayar.S("sender_email").Length == 0)
        {
            Ui.Warn(this, "E-posta ayarları eksik. Ayarlar sayfasından SMTP sunucu ve gönderen e-posta bilgilerini girin.");
            return;
        }
        var ek = t.S("pdf_yolu");
        var kurum = t.S("gonderen_kurum").Length > 0 ? t.S("gonderen_kurum") : "Tebligat";
        var konu = string.Join(" – ", new[] { kurum, t.S("esas_no") }.Where(x => x.Length > 0));
        var v = FormDialog.Ask(this, "Mail Gönder", new List<Field>
        {
            new("to", "Alıcı e-posta (birden çok ise virgülle)", required: true),
            new("subject", "Konu", required: true, def: konu),
            new("body", "Mesaj", "multiline", def: $"Merhaba,\n\n{konu} ile ilgili tebligat ekte yer almaktadır.\n" +
                $"Tebliğ tarihi: {Utils.FmtDate(t.S("teblig_tarihi"))}\n\nİyi çalışmalar."),
            new("ek", "PDF ekle", "check", def: ek.Length > 0),
        });
        if (v == null) return;
        var ekler = new List<string>();
        if (Convert.ToInt32(v["ek"]) != 0 && ek.Length > 0) ekler.Add(Workflows.DosyaYolu(Cfg, ek));
        var to = string.Join(", ", ((string)v["to"]!).Replace(';', ',').Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));
        var subject = (string)v["subject"]!;
        var body = (string)v["body"]!;
        Ctx.Status("Mail gönderiliyor…");
        Ui.RunAsyncVoid(() => Notify.SendMail(ayar, to, subject, body, ekler),
            () => { Ctx.Status("Mail gönderildi"); Ui.Info(this, $"Mail gönderildi: {to}"); },
            m => Ui.Warn(this, $"Mail gönderilemedi:\n{m}"));
    }

    void SilTebligat(Row? t)
    {
        if (t == null) { Ui.Info(this, "Önce silinecek tebligatı seçin."); return; }
        var kurum = t.S("gonderen_kurum").Length > 0 ? t.S("gonderen_kurum") : "Tebligat";
        if (!Ui.Confirm(this, $"Bu tebligat silinsin mi?\n\n{kurum} — {t.S("esas_no")}\n(Kayıt kuyruk ve arşivden kalkar.)")) return;
        var pdfSil = t.S("pdf_yolu").Length > 0 &&
                     Ui.Confirm(this, "PDF dosyası da bilgisayardan silinsin mi?\n'Hayır' derseniz yalnızca kayıt silinir, PDF klasörde kalır.");
        Db.Delete("genel_tebligatlar", t.L("id"));
        if (pdfSil)
        {
            try
            {
                var yol = Workflows.DosyaYolu(Cfg, t.S("pdf_yolu"));
                if (File.Exists(yol)) File.Delete(yol);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        Refresh();
        Ctx.Status("Tebligat silindi");
    }

    // ------------------------------------------------------------------ içe aktarma
    Row? HesapSec()
    {
        var hs = Hesaplar();
        if (hs.Count == 0) { Ui.Info(this, "Önce Ayarlar > UETS Hesapları bölümünden bu türde bir hesap ekleyin."); return null; }
        if (hs.Count == 1) return hs[0];
        var v = FormDialog.Ask(this, "Hesap seç", new List<Field>
        {
            new("id", "UETS Hesabı", "combo", hs.Select(h => ((object?)h.L("id"), h.S("hesap_adi"))), def: hs[0].L("id")),
        });
        if (v == null) return null;
        var id = Convert.ToInt64(v["id"]);
        return hs.FirstOrDefault(h => h.L("id") == id);
    }

    void ImportPdf()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Tebligat dosyaları", Filter = TebligatDosya.DialogFiltresi, Multiselect = true };
        if (dlg.ShowDialog() == true) Import(dlg.FileNames);
    }

    void ImportFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Tebligat klasörü" };
        if (dlg.ShowDialog() == true) Import(Directory.GetFiles(dlg.FolderName).Where(TebligatDosya.Destekli).ToArray());
    }

    void Import(string[] files)
    {
        var h = HesapSec();
        if (h == null) return;
        _log.Text = "İçe aktarılıyor…";
        Ui.RunAsync(() => files.Count(f => Workflows.IngestPdf(Db, Cfg, f, h) != null),
            n => { _log.Text = $"{n} yeni, {files.Length - n} tekrar/atlanan"; Refresh(); },
            m => { _log.Text = ""; Ui.Warn(this, $"İşlem başarısız:\n{m}"); });
    }

    // ------------------------------------------------------------------ avukat
    void Isle()
    {
        var t = _tQ.Current;
        if (t != null)
        {
            Db.Update("genel_tebligatlar", t.L("id"), new Dictionary<string, object?> { ["durum"] = "İşlendi" });
            Refresh();
        }
    }

    void DavayaBagla(Row? t)
    {
        if (t == null) { Ui.Info(this, "Önce bir tebligat seçin."); return; }
        var v = FormDialog.Ask(this, "Tebligatı Davaya Bağla", new List<Field>
        {
            new("dava_id", "Dava", "combo", Secenek.Davalar(Db, "— bağlantıyı kaldır —"), def: t.L("dava_id")),
        });
        if (v == null) return;
        Db.Update("genel_tebligatlar", t.L("id"), new Dictionary<string, object?> { ["dava_id"] = v["dava_id"] ?? 0L });
        Refresh();
    }

    /// <summary>Seçili tebligattan süreli iş açar (son cevap tarihi hazır gelir); iş listesinde tebligata bağlı kalır.</summary>
    void IseEkle(Row? t)
    {
        if (t == null) { Ui.Info(this, "Önce bir tebligat seçin."); return; }
        var h = IsListesiPage.KatalogHazirla(this, Db, t);
        if (h == null) return;
        var v = IsListesiPage.Sor(this, Db, h);
        if (v == null) return;
        Db.Insert("sureler", v);
        Refresh();
        Ctx.Status("İş listesine eklendi.");
    }

    void DavaAc()
    {
        var t = _tQ.Current;
        if (t == null) return;
        var did = Db.Insert("davalar", new Dictionary<string, object?>
        {
            ["mahkeme_adi"] = t.S("gonderen_kurum"), ["esas_no"] = t.S("esas_no"), ["dava_turu"] = t.S("konu"),
            ["genel_notlar"] = $"UETS tebligatından açıldı ({Utils.FmtDate(t.S("teblig_tarihi"))})",
        });
        Db.Update("genel_tebligatlar", t.L("id"), new Dictionary<string, object?> { ["durum"] = "İşlendi" });
        Ui.Info(this, $"Dava kaydı #{did} açıldı. 'Dava & Müvekkil Takip' sayfasından tamamlayın.");
        Refresh();
    }
}

/// <summary>
/// Uygulamanın içine gömülü UETS tarayıcısı (WebView2). UETS bu pencerede açılır; siz giriş yapıp çalışırsınız.
/// Tarayıcıdan indirilen her PDF/UDF/resim (veya bunları içeren ZIP) seçili UETS hesabına işlenir. Program hiçbir düğmeye
/// kendiliğinden basmaz: okunmamış tebligatı açmak (tebliğ işlemi) tamamen sizin kontrolünüzdedir.
/// </summary>
public class UetsBrowser : Grid
{
    const string AdetJs = @"(() => {
  const m = (document.body.innerText || '').match(/Tebligat Adedi:\s*\d+\s*\/\s*(\d+)/);
  return m ? parseInt(m[1], 10) : -1;
})()";

    const string SatirOzetiJs = @"(() => {
  const re = /\d{2}\.\d{2}\.\d{4}/;
  const all = Array.from(document.querySelectorAll('a,button,span'));
  const g = all.find(e => (e.textContent || '').trim() === 'Görüntüle');
  if (!g) return 'Görüntüle düğmesi bulunamadı. URL: ' + location.href;
  let el = g;
  for (let i = 0; i < 12 && el.parentElement; i++) {
    el = el.parentElement;
    if (re.test(el.textContent || '') && el.querySelectorAll('button,a').length >= 2) break;
  }
  return el.outerHTML.slice(0, 6000);
})()";

    readonly AppCtx _ctx;
    readonly string _tip;
    readonly Func<List<Row>> _hesaplar;
    readonly Action _onImported;
    readonly WebView2 _view = new();
    readonly TextBox _url = new() { IsReadOnly = true };
    readonly ComboBox _combo = new() { MinWidth = 200 };
    readonly TextBox _spin = new() { Width = 56, TextAlignment = TextAlignment.Right };
    readonly TextBlock _nextLbl = Ui.Muted("", false);
    readonly TextBlock _status = Ui.Muted("UETS'e giriş yapın. Tebligatları UETS'in kendi İNDİR özelliğiyle indirin; indirilenler otomatik işlenir.");
    readonly DispatcherTimer _timer = new();
    bool _loaded, _oto, _ready, _failed;

    Db Db => _ctx.Db;
    Config Cfg => _ctx.Cfg;

    public UetsBrowser(AppCtx ctx, string tip, Func<List<Row>> hesaplar, Action onImported)
    {
        _ctx = ctx; _tip = tip; _hesaplar = hesaplar; _onImported = onImported;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());

        var lbl = Ui.Muted("İndirilenler şu hesaba işlensin:", false);
        lbl.Margin = new Thickness(10, 0, 8, 0);
        lbl.VerticalAlignment = VerticalAlignment.Center;
        var bar = new DockPanel { Margin = new Thickness(0, 8, 0, 6) };
        var nav = Ui.HRow(Ui.Btn("◀", "", () => { if (_ready && _view.CanGoBack) _view.GoBack(); }),
                          Ui.Btn("⟳", "", () => { if (_ready) _view.Reload(); }), Ui.Btn("Tebligatlarım", "", GoList));
        DockPanel.SetDock(nav, Dock.Left);
        var sag = Ui.HRow(lbl, _combo, Ui.Btn("Sayfa Yapısını Kaydet (Tanı)", "", DumpStructure));
        DockPanel.SetDock(sag, Dock.Right);
        _url.Margin = new Thickness(8, 0, 0, 0);
        bar.Children.Add(nav); bar.Children.Add(sag); bar.Children.Add(_url);
        SetRow(bar, 0); Children.Add(bar);

        var l2 = Ui.Muted("Otomatik yenile (0 = kapalı, dakika):", false);
        l2.VerticalAlignment = VerticalAlignment.Center;
        l2.Margin = new Thickness(0, 0, 8, 0);
        _nextLbl.Margin = new Thickness(10, 0, 0, 0);
        _nextLbl.VerticalAlignment = VerticalAlignment.Center;
        var bar2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        bar2.Children.Add(l2); bar2.Children.Add(_spin); bar2.Children.Add(_nextLbl);
        SetRow(bar2, 1); Children.Add(bar2);
        _spin.Text = Math.Max(0, Cfg.GetInt("uets_yenileme_dk", 0)).ToString();
        _spin.LostFocus += (_, _) => YenilemeDegisti();
        _spin.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) YenilemeDegisti(); };
        _timer.Tick += (_, _) => OtoYenile();

        _status.Margin = new Thickness(0, 0, 0, 6);
        SetRow(_status, 2); Children.Add(_status);
        SetRow(_view, 3); Children.Add(_view);
    }

    int Dk => int.TryParse(_spin.Text.Trim(), out var n) ? Math.Clamp(n, 0, 1440) : 0;

    // ---- yaşam döngüsü
    public async void Activate()
    {
        var cur = (_combo.SelectedItem as ComboItem)?.Value;
        _combo.Items.Clear();
        foreach (var h in _hesaplar()) _combo.Items.Add(new ComboItem { Value = h.L("id"), Label = h.S("hesap_adi") });
        var i = _combo.Items.Cast<ComboItem>().ToList().FindIndex(x => Equals(x.Value, cur));
        _combo.SelectedIndex = i >= 0 ? i : (_combo.Items.Count > 0 ? 0 : -1);
        if (_loaded) return;
        _loaded = true;
        try
        {
            await _view.EnsureCoreWebView2Async(await WebEnv.Get());
            _ready = true;
            _view.CoreWebView2.DownloadStarting += OnDownload;
            _view.CoreWebView2.SourceChanged += (_, _) => _url.Text = _view.Source?.ToString() ?? "";
            _view.CoreWebView2.NavigationCompleted += (_, _) => Yuklendi();
            _view.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; _view.CoreWebView2.Navigate(e.Uri); };
            _view.CoreWebView2.Navigate(Cfg.GetString("uets_url"));
            YenilemeDegisti();
        }
        catch (Exception e)
        {
            _failed = true;
            _status.Text = "Gömülü tarayıcı için Microsoft Edge WebView2 Runtime gerekli (Windows 11'de yüklüdür; Windows 10 için Microsoft'tan ücretsiz indirilir).\n" + e.Message;
        }
    }

    void GoList()
    {
        if (_ready) _view.CoreWebView2.Navigate(Cfg.GetString("uets_url"));
    }

    // ---- otomatik yenileme
    void YenilemeDegisti()
    {
        var dk = Dk;
        _spin.Text = dk.ToString();
        Cfg.Set("uets_yenileme_dk", dk);
        _timer.Stop();
        if (dk > 0)
        {
            _timer.Interval = TimeSpan.FromMinutes(dk);
            _timer.Start();
            _nextLbl.Text = $"Her {dk} dakikada bir yenilenir";
        }
        else _nextLbl.Text = "Otomatik yenileme kapalı";
    }

    void OtoYenile()
    {
        if (!_ready) return;
        _oto = true;
        _view.Reload();
    }

    async void Yuklendi()
    {
        if (!_oto) return;
        _oto = false;
        if ((_view.Source?.ToString() ?? "").ToLowerInvariant().Contains("login"))
        {
            _status.Text = "Yenileme sonrası oturum düşmüş görünüyor: lütfen yeniden giriş yapın.";
            return;
        }
        // Sayfa SPA: liste çizilene kadar kısa bekle, sonra toplam tebligat adedini oku
        await Task.Delay(5000);
        try
        {
            var res = await _view.CoreWebView2.ExecuteScriptAsync(AdetJs);
            Adet(int.TryParse(res, out var n) ? n : -1);
        }
        catch (Exception e) { _status.Text = "Adet okunamadı: " + e.Message; }
    }

    void Adet(int toplam)
    {
        if (toplam < 0)
        {
            _status.Text = "Yenilendi (bu sayfada tebligat adedi görünmüyor, yeni tebligat kontrolü yapılamadı).";
            return;
        }
        var anahtar = $"uets_son_adet_{_tip}";
        var onceki = Cfg.GetInt(anahtar, -1);
        Cfg.Set(anahtar, toplam);
        var zaman = DateTime.Now.ToString("HH:mm");
        if (onceki >= 0 && toplam > onceki)
        {
            var yeni = toplam - onceki;
            _status.Text = $"{zaman}: {yeni} yeni tebligat var! (toplam {toplam})";
            var msg = $"📬 UETS'te {yeni} yeni tebligat görünüyor (toplam {toplam}). Henüz açılmadı/indirilmedi.";
            var s = Db.GetSettings();
            Ui.RunAsyncVoid(() => Notify.SendPersonal(s, msg),
                null, m => _status.Text = $"Telegram gönderilemedi: {m}");
        }
        else _status.Text = $"{zaman}: yenilendi, yeni tebligat yok (toplam {toplam}).";
    }

    /// <summary>Tarayıcı sekmesinde hangi tebligatın aranacağını durum satırına yazar (tıklama yapmaz).</summary>
    public void ShowTebligat(string ozet) => _status.Text = $"Aradığınız tebligat: {ozet}. UETS listesinden kendiniz açın; indirdiğiniz dosya otomatik işlenir.";

    // ---- indirme yakalama
    void OnDownload(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        var hid = (_combo.SelectedItem as ComboItem)?.Value;
        var hesap = hid != null ? Db.One("SELECT * FROM uets_hesaplari WHERE id=?", Convert.ToInt64(hid)) : null;
        if (hesap == null)
        {
            e.Cancel = true;
            Ui.Info(this, "İndirme iptal edildi: önce Ayarlar > UETS Hesapları'nda bu türde bir hesap ekleyin ve yukarıdan seçin.");
            return;
        }
        var klasor = Cfg.Subdir("Dosyalar", "UETS_Indirilen");
        var ad = Path.GetFileName(e.ResultFilePath);
        if (string.IsNullOrWhiteSpace(ad)) ad = "tebligat.pdf";
        var yol = Path.Combine(klasor, ad);
        if (File.Exists(yol))
            yol = Path.Combine(klasor, $"{Path.GetFileNameWithoutExtension(ad)}_{DateTime.Now:HHmmss}{Path.GetExtension(ad)}");
        e.ResultFilePath = yol;
        e.Handled = true;  // varsayılan indirme penceresi açılmasın
        _status.Text = $"İndiriliyor: {Path.GetFileName(yol)}";
        var op = e.DownloadOperation;
        // Tebligatın açık olduğu UETS sayfasındaki "Teslim Tarihi" okunur (yalnızca okuma; tıklama yapılmaz).
        var teslimGorev = TeslimTarihiOku();
        op.StateChanged += async (_, _) =>
        {
            if (op.State == CoreWebView2DownloadState.Completed) Bitti(yol, hesap, await teslimGorev);
            else if (op.State == CoreWebView2DownloadState.Interrupted) _status.Text = $"İndirme yarım kaldı: {Path.GetFileName(yol)}";
        };
    }

    /// <summary>Açık UETS sayfasındaki tek bir "Teslim Tarihi: GG.AA.YYYY SS:DD" değerini okur; yoksa ya da birden çoksa boş döner.</summary>
    async Task<string?> TeslimTarihiOku()
    {
        try
        {
            var res = await _view.CoreWebView2.ExecuteScriptAsync("document.body ? document.body.innerText : ''");
            var metin = System.Text.Json.JsonSerializer.Deserialize<string>(res) ?? "";
            var ms = System.Text.RegularExpressions.Regex.Matches(metin, @"Teslim\s*Tarihi\s*[:\-]?\s*(\d{1,2}\.\d{1,2}\.\d{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var tarihler = ms.Select(m => Utils.ParseDate(m.Groups[1].Value)).Where(d => d != null).Select(d => Utils.Iso(d!.Value)).Distinct().ToList();
            return tarihler.Count == 1 ? tarihler[0] : null;   // birden çok farklı tarih varsa (liste sayfası) karıştırmamak için kullanılmaz
        }
        catch (Exception) { return null; }
    }

    void Bitti(string yol, Row hesap, string? teslimIso = null)
    {
        Ui.RunAsync(() => Ingest(yol, hesap, teslimIso),
            r => { _status.Text = $"İndirilen işlendi: {r.Yeni} yeni, {r.Toplam - r.Yeni} tekrar/atlanan (kuyruk sekmesine bakın)"; _onImported(); },
            m => _status.Text = $"İşlenemedi: {m}");
    }

    (int Yeni, int Toplam) Ingest(string yol, Row hesap, string? teslimIso = null)
    {
        var pdfler = new List<string>();
        var uz = Path.GetExtension(yol).ToLowerInvariant();
        if (uz == ".zip")
        {
            var ac = Path.Combine(Path.GetTempPath(), "uets_zip_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(ac);
            using var z = ZipFile.OpenRead(yol);
            foreach (var m in z.Entries)
            {
                if (!TebligatDosya.Destekli(m.FullName)) continue;
                var hedef = Path.Combine(ac, Path.GetFileName(m.FullName)); // zip-slip'e karşı yalnızca dosya adı
                m.ExtractToFile(hedef, true);
                pdfler.Add(hedef);
            }
        }
        else if (TebligatDosya.Destekli(yol)) pdfler.Add(yol);
        var yeni = pdfler.Count(p => Workflows.IngestPdf(Db, Cfg, p, hesap, true, pdfler.Count == 1 ? teslimIso : null) != null);
        return (yeni, pdfler.Count);
    }

    // ---- tanı
    async void DumpStructure()
    {
        if (!_ready) return;
        try
        {
            var res = await _view.CoreWebView2.ExecuteScriptAsync(SatirOzetiJs);
            var metin = System.Text.Json.JsonSerializer.Deserialize<string>(res) ?? res;
            var d = Cfg.Subdir("Debug");
            var f = Path.Combine(d, "uets_satir_ornek.txt");
            File.WriteAllText(f, metin);
            _status.Text = $"Kaydedildi: {f}";
        }
        catch (Exception e) { _status.Text = "Kaydedilemedi: " + e.Message; }
    }
}
