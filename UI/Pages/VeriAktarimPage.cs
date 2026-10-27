using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using HukukErp.Core;
using HukukErp.Services;

namespace HukukErp.UI.Pages;

/// <summary>Veri aktarımı: Excel/CSV'den müvekkil ve dava içe aktarma; KVKK (veri dışa aktarma, aydınlatma kaydı, silme); veritabanı sağlığı.</summary>
public class VeriAktarimPage : BasePage
{
    // ---- içe aktarma
    readonly ComboBox _tur = new() { Width = 160, Margin = new Thickness(0, 0, 8, 0) };
    readonly TextBlock _dosyaAd = Ui.Muted("Dosya seçilmedi", false);
    readonly TextBox _onizleme = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    List<string[]> _satirlar = new();

    // ---- KVKK
    readonly ComboBox _muv = new() { MinWidth = 320, Margin = new Thickness(0, 0, 8, 0) };
    readonly TextBlock _kvkkDurum = Ui.Muted("", true);

    // ---- sağlık
    readonly TextBlock _saglik = Ui.Text("", 13, false, "text", true);

    static readonly Dictionary<string, string[]> Muvekkil = new()
    {
        ["ad_soyad"] = new[] { "adsoyad", "adisoyadi", "ad", "isim", "muvekkil", "unvan", "adıSoyadı" },
        ["tc_no"] = new[] { "tc", "tcno", "tckimlik", "tckimlikno", "kimlikno", "vkn", "tckimlikvkn" },
        ["telefon"] = new[] { "telefon", "tel", "gsm", "cep", "ceptelefonu" },
        ["eposta"] = new[] { "eposta", "email", "mail" },
        ["adres"] = new[] { "adres" },
    };

    static readonly Dictionary<string, string[]> Dava = new()
    {
        ["mahkeme_adi"] = new[] { "mahkeme", "mahkemeadi", "mahkemesi" },
        ["esas_no"] = new[] { "esas", "esasno", "esasnumarasi" },
        ["dava_turu"] = new[] { "davaturu", "tur", "konu" },
        ["karsi_taraf"] = new[] { "karsitaraf", "davali", "karsi" },
        ["muvekkil"] = new[] { "muvekkil", "adsoyad", "tc", "tcno" },
        ["durusma_tarihi"] = new[] { "durusma", "durusmatarihi" },
    };

    public VeriAktarimPage(AppCtx ctx) : base(ctx)
    {
        Top(Ui.PageHeader("Veri Aktarımı ve KVKK", "Excel/CSV'den içe aktarma • müvekkil verisini dışa aktarma ve silme • veritabanı sağlığı"));
        var tabs = new TabControl();
        tabs.Items.Add(Ui.Tab("Excel / CSV İçe Aktar", IceAktarSekmesi()));
        tabs.Items.Add(Ui.Tab("KVKK", KvkkSekmesi()));
        tabs.Items.Add(Ui.Tab("Veritabanı Sağlığı", SaglikSekmesi()));
        Fill(tabs);
    }

    // ================================================================= içe aktarma
    UIElement IceAktarSekmesi()
    {
        _tur.Items.Add("Müvekkiller"); _tur.Items.Add("Davalar"); _tur.SelectedIndex = 0;
        _tur.SelectionChanged += (_, _) => Onizle();
        var bar = Ui.HRow(Ui.Muted("Ne aktarılacak:", false), _tur, Ui.Btn("Dosya Seç… (.xlsx / .csv)", "Primary", DosyaSec),
                          Ui.Btn("İçe Aktar", "Ok", Aktar), _dosyaAd);
        var not = Ui.Muted("İlk satır başlık olmalı. Müvekkil için: Ad Soyad, T.C. No, Telefon, E-posta, Adres. Dava için: Mahkeme, Esas No, Dava Türü, Karşı Taraf, Müvekkil (ad ya da T.C.), Duruşma. " +
                           "Başlık adları esnektir. Aynı T.C. (müvekkil) ya da aynı mahkeme+esas (dava) zaten varsa atlanır. İçe aktarmadan önce otomatik yedek alınır.");
        var dock = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(bar, Dock.Top); DockPanel.SetDock(not, Dock.Top);
        not.Margin = new Thickness(0, 8, 0, 8);
        dock.Children.Add(bar); dock.Children.Add(not); dock.Children.Add(_onizleme);
        return dock;
    }

    static string Norm(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in Utils.TrLower(s).Replace('ı', 'i').Replace('ş', 's').Replace('ğ', 'g').Replace('ü', 'u').Replace('ö', 'o').Replace('ç', 'c'))
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    Dictionary<string, string[]> Hedef => _tur.SelectedIndex == 1 ? Dava : Muvekkil;

    /// <summary>Başlık satırına göre alan → sütun indeksi eşlemesi.</summary>
    Dictionary<string, int> Esle()
    {
        var m = new Dictionary<string, int>();
        if (_satirlar.Count == 0) return m;
        var baslik = _satirlar[0].Select(Norm).ToArray();
        foreach (var (alan, esanlamlilar) in Hedef)
        {
            var syn = esanlamlilar.Select(Norm).ToHashSet();
            var i = Array.FindIndex(baslik, b => syn.Contains(b));
            if (i >= 0) m[alan] = i;
        }
        return m;
    }

    string Hucre(string[] satir, Dictionary<string, int> m, string alan) =>
        m.TryGetValue(alan, out var i) && i < satir.Length ? satir[i].Trim() : "";

    void DosyaSec()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Excel veya CSV dosyası", Filter = "Excel / CSV (*.xlsx;*.csv;*.txt)|*.xlsx;*.csv;*.txt" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _satirlar = TabloOkuyucu.Oku(dlg.FileName);
            _dosyaAd.Text = Path.GetFileName(dlg.FileName);
        }
        catch (Exception e) { Ui.Warn(this, "Dosya okunamadı:\n" + e.Message); _satirlar = new(); }
        Onizle();
    }

    void Onizle()
    {
        if (_satirlar.Count == 0) { _onizleme.Text = ""; return; }
        var m = Esle();
        var sb = new StringBuilder();
        sb.AppendLine($"{_satirlar.Count - 1} veri satırı okundu.");
        sb.AppendLine("Eşleşen sütunlar: " + (m.Count == 0 ? "YOK (başlık adlarını kontrol edin)" : string.Join(", ", m.Select(x => $"{x.Key} ← '{_satirlar[0][x.Value]}'"))));
        var eksik = Hedef.Keys.Where(k => !m.ContainsKey(k)).ToList();
        if (eksik.Count > 0) sb.AppendLine("Bulunamayan alanlar: " + string.Join(", ", eksik));
        sb.AppendLine();
        foreach (var s in _satirlar.Take(8)) sb.AppendLine(string.Join(" | ", s));
        _onizleme.Text = sb.ToString();
    }

    static string TarihIso(string s)
    {
        if (s.Length == 0) return "";
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var oa) && oa > 20000 && oa < 80000)
            return Utils.Iso(DateTime.FromOADate(oa));
        var d = Utils.ParseDate(s);
        return d == null ? "" : Utils.Iso(d.Value);
    }

    void Aktar()
    {
        if (_satirlar.Count < 2) { Ui.Info(this, "Önce içinde veri olan bir dosya seçin."); return; }
        var m = Esle();
        var gerekli = _tur.SelectedIndex == 1 ? new[] { "mahkeme_adi", "esas_no" } : new[] { "ad_soyad", "tc_no" };
        var yok = gerekli.Where(g => !m.ContainsKey(g)).ToList();
        if (yok.Count > 0) { Ui.Warn(this, "Şu sütunlar bulunamadı: " + string.Join(", ", yok)); return; }
        if (!Ui.Confirm(this, $"{_satirlar.Count - 1} satır içe aktarılacak. Önce otomatik yedek alınır. Devam edilsin mi?")) return;
        Yedekleme.Yap(Db, Cfg);

        int yeni = 0, atlanan = 0, hatali = 0, tcSorunlu = 0;
        foreach (var s in _satirlar.Skip(1))
        {
            try
            {
                if (_tur.SelectedIndex == 0)
                {
                    var ad = Hucre(s, m, "ad_soyad"); var tc = Hucre(s, m, "tc_no");
                    if (ad.Length == 0 || tc.Length == 0) { hatali++; continue; }
                    if (Db.One("SELECT id FROM muvekkiller WHERE tc_no=?", tc) != null) { atlanan++; continue; }
                    if (!Utils.ValidTc(tc)) tcSorunlu++;
                    Db.Insert("muvekkiller", new Dictionary<string, object?>
                    {
                        ["tc_no"] = tc, ["ad_soyad"] = ad, ["telefon"] = Hucre(s, m, "telefon"), ["eposta"] = Hucre(s, m, "eposta"), ["adres"] = Hucre(s, m, "adres"),
                    });
                    yeni++;
                }
                else
                {
                    var mah = Hucre(s, m, "mahkeme_adi"); var esas = Hucre(s, m, "esas_no");
                    if (mah.Length == 0 || esas.Length == 0) { hatali++; continue; }
                    if (Db.Query("SELECT mahkeme_adi, esas_no FROM davalar").Any(d => Utils.TrLower(d.S("mahkeme_adi")) == Utils.TrLower(mah) && DosyaEsleme.EsasNorm(d.S("esas_no")) == DosyaEsleme.EsasNorm(esas))) { atlanan++; continue; }
                    long? mid = null;
                    var mv = Hucre(s, m, "muvekkil");
                    if (mv.Length > 0)
                    {
                        var r = Db.One("SELECT id FROM muvekkiller WHERE tc_no=?", mv)
                                ?? Db.Query("SELECT id, ad_soyad FROM muvekkiller").FirstOrDefault(x => Utils.TrLower(x.S("ad_soyad")) == Utils.TrLower(mv));
                        if (r != null) mid = r.L("id");
                    }
                    Db.Insert("davalar", new Dictionary<string, object?>
                    {
                        ["mahkeme_adi"] = mah, ["esas_no"] = esas, ["dava_turu"] = Hucre(s, m, "dava_turu"), ["karsi_taraf"] = Hucre(s, m, "karsi_taraf"),
                        ["durusma_tarihi"] = TarihIso(Hucre(s, m, "durusma_tarihi")), ["muvekkil_id"] = mid,
                    });
                    yeni++;
                }
            }
            catch (Exception) { hatali++; }
        }
        Ui.Info(this, $"Aktarım bitti.\nEklenen: {yeni}\nZaten vardı (atlandı): {atlanan}\nEksik/hatalı satır: {hatali}" +
                      (tcSorunlu > 0 ? $"\nT.C. doğrulamasından geçmeyen (yine de eklendi): {tcSorunlu}" : ""));
    }

    // ================================================================= KVKK
    UIElement KvkkSekmesi()
    {
        _muv.SelectionChanged += (_, _) => KvkkYaz();
        var bar = Ui.HRow(Ui.Muted("Müvekkil:", false), _muv, Ui.Btn("Tüm Verisini Dışa Aktar (JSON)", "Primary", Disari),
                          Ui.Btn("Aydınlatma Metni Verildi (bugün)", "", Aydinlatma), Ui.Btn("Müvekkili ve Dosyalarını Sil…", "Danger", Sil));
        var not = Ui.Muted("Dışa aktarma: müvekkilin sizde kayıtlı tüm bilgileri tek dosyada (ilgili kişi başvurusu için). Silme geri alınamaz; vergi, avukatlık ve uyuşmazlık nedeniyle saklama yükümlülüğünüz olabilir, silmeden önce kontrol edin. " +
                           "Tebligat kayıtları silinmez, yalnızca davayla bağı kaldırılır. Dosyalar klasöründeki evrak kopyaları silinir; müvekkile ait vekaletname PDF'i ve UETS indirmeleri silinmez.");
        var dock = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(bar, Dock.Top);
        not.Margin = new Thickness(0, 10, 0, 10);
        DockPanel.SetDock(not, Dock.Top);
        dock.Children.Add(bar); dock.Children.Add(not); dock.Children.Add(_kvkkDurum);
        return dock;
    }

    long Muv => (_muv.SelectedItem as ComboItem)?.Value is { } v ? Convert.ToInt64(v) : 0;

    void KvkkYaz()
    {
        var m = Muv == 0 ? null : Db.One("SELECT * FROM muvekkiller WHERE id=?", Muv);
        if (m == null) { _kvkkDurum.Text = ""; return; }
        var davaSay = Db.ScalarL("SELECT COUNT(*) FROM davalar WHERE muvekkil_id=?", Muv);
        _kvkkDurum.Text = $"{m.S("ad_soyad")} • {davaSay} dosya • KVKK aydınlatma kaydı: {(m.S("kvkk_tarih").Length > 0 ? Utils.FmtDate(m.S("kvkk_tarih")) : "yok")}";
    }

    void Aydinlatma()
    {
        if (Muv == 0) { Ui.Info(this, "Önce müvekkil seçin."); return; }
        Db.Update("muvekkiller", Muv, new Dictionary<string, object?> { ["kvkk_tarih"] = Utils.TodayIso() });
        KvkkYaz();
    }

    void Disari()
    {
        var id = Muv;
        if (id == 0) { Ui.Info(this, "Önce müvekkil seçin."); return; }
        var m = Db.One("SELECT * FROM muvekkiller WHERE id=?", id);
        if (m == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog { Title = "Müvekkil verisi", Filter = "JSON (*.json)|*.json", FileName = $"{Utils.SlugAd(m.S("ad_soyad"))}_veri_{DateTime.Today:yyyyMMdd}.json" };
        if (dlg.ShowDialog() != true) return;
        var davalar = Db.Query("SELECT * FROM davalar WHERE muvekkil_id=?", id);
        var dIds = string.Join(",", davalar.Select(d => d.L("id")).DefaultIfEmpty(0));
        var paket = new Dictionary<string, object?>
        {
            ["olusturma"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ["muvekkil"] = m,
            ["davalar"] = davalar,
            ["finans"] = Db.Query("SELECT * FROM finans_tahsilat WHERE muvekkil_id=?", id),
            ["tahsilat_hareketleri"] = Db.Query("SELECT h.* FROM tahsilat_hareketleri h JOIN finans_tahsilat f ON f.id=h.finans_id WHERE f.muvekkil_id=?", id),
            ["emanet"] = Db.Query("SELECT * FROM emanet_hareketleri WHERE muvekkil_id=?", id),
            ["bilgilendirmeler"] = Db.Query("SELECT * FROM muvekkil_bilgilendirme WHERE muvekkil_id=?", id),
            ["vekaletnameler"] = Db.Query("SELECT * FROM vekaletnameler WHERE muvekkil_id=?", id),
            ["masraflar"] = Db.Query($"SELECT * FROM dava_masraflari WHERE dava_id IN ({dIds})"),
            ["sureler"] = Db.Query($"SELECT * FROM sureler WHERE dava_id IN ({dIds})"),
            ["durusma_notlari"] = Db.Query($"SELECT * FROM durusma_notlari WHERE dava_id IN ({dIds})"),
            ["zaman_kayitlari"] = Db.Query($"SELECT * FROM zaman_kayitlari WHERE dava_id IN ({dIds})"),
            ["evraklar"] = Db.Query($"SELECT * FROM dava_evraklari WHERE dava_id IN ({dIds})"),
            ["ajanda"] = Db.Query($"SELECT * FROM ajanda WHERE dava_id IN ({dIds})"),
            ["tebligatlar"] = Db.Query($"SELECT id, gonderen_kurum, konu, esas_no, teblig_tarihi, son_cevap_tarihi, durum FROM genel_tebligatlar WHERE dava_id IN ({dIds})"),
        };
        File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(paket, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), new UTF8Encoding(true));
        Ui.Info(this, $"Kaydedildi:\n{dlg.FileName}");
    }

    void Sil()
    {
        var id = Muv;
        var m = id == 0 ? null : Db.One("SELECT * FROM muvekkiller WHERE id=?", id);
        if (m == null) { Ui.Info(this, "Önce müvekkil seçin."); return; }
        var davalar = Db.Query("SELECT id FROM davalar WHERE muvekkil_id=?", id).Select(d => d.L("id")).ToList();
        if (!Ui.Confirm(this, $"'{m.S("ad_soyad")}' ve bağlı {davalar.Count} dosyası (süreler, ajanda, masraf, tahsilat, emanet, bilgilendirme kayıtları dahil) KALICI olarak silinecek.\n\nGeri alınamaz. Önce 'Tüm Verisini Dışa Aktar' ile kayıt almanız önerilir.\n\nDevam edilsin mi?")) return;
        if (!Ui.Confirm(this, "Emin misiniz? Bu işlem için yedek alınıp devam edilecek.")) return;
        Yedekleme.Yap(Db, Cfg);
        var dIds = string.Join(",", davalar.DefaultIfEmpty(0));
        Db.Execute($"DELETE FROM sureler WHERE dava_id IN ({dIds})");
        Db.Execute($"DELETE FROM ajanda WHERE dava_id IN ({dIds})");
        Db.Execute($"DELETE FROM emsal_notlari WHERE dava_id IN ({dIds})");
        Db.Execute($"UPDATE genel_tebligatlar SET dava_id=0 WHERE dava_id IN ({dIds})");
        Db.Execute($"DELETE FROM dava_masraflari WHERE dava_id IN ({dIds})");
        Db.Execute($"DELETE FROM durusma_notlari WHERE dava_id IN ({dIds})");
        Db.Execute($"DELETE FROM zaman_kayitlari WHERE dava_id IN ({dIds})");
        Db.Execute($"DELETE FROM dava_evraklari WHERE dava_id IN ({dIds})");
        Db.Execute("DELETE FROM muvekkil_bilgilendirme WHERE muvekkil_id=?", id);
        Db.Execute("DELETE FROM emanet_hareketleri WHERE muvekkil_id=?", id);
        Db.Execute("DELETE FROM tahsilat_hareketleri WHERE finans_id IN (SELECT id FROM finans_tahsilat WHERE muvekkil_id=?)", id);
        Db.Execute("DELETE FROM finans_tahsilat WHERE muvekkil_id=?", id);
        Db.Execute("DELETE FROM vekaletnameler WHERE muvekkil_id=?", id);
        Db.Execute($"DELETE FROM davalar WHERE id IN ({dIds})");
        Db.Execute("DELETE FROM muvekkiller WHERE id=?", id);
        foreach (var d in davalar)
            try { var k = Path.Combine(Cfg.DataDir, "Dosyalar", "Dava", d.ToString()); if (Directory.Exists(k)) Directory.Delete(k, true); } catch (Exception) { }
        Db.Execute("INSERT INTO audit_log (tablo, kayit_id, islem, ozet) VALUES ('muvekkiller', ?, 'KVKK silme', ?)", id, $"{davalar.Count} dosya ile birlikte silindi");
        Ui.Info(this, "Silindi.");
        Refresh();
    }

    // ================================================================= sağlık
    UIElement SaglikSekmesi()
    {
        var bar = Ui.HRow(Ui.Btn("Sağlık Denetimi Çalıştır", "Primary", SaglikCalistir), Ui.Btn("Veritabanını Sıkıştır (VACUUM)", "", Sikistir));
        var dock = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(bar, Dock.Top);
        _saglik.Margin = new Thickness(0, 12, 0, 0);
        dock.Children.Add(bar); dock.Children.Add(_saglik);
        return dock;
    }

    void SaglikCalistir()
    {
        var sonuc = Db.SaglikKontrolu();
        var sb = new StringBuilder();
        sb.AppendLine($"Bütünlük denetimi: {(sonuc.Equals("ok", StringComparison.OrdinalIgnoreCase) ? "SORUN YOK" : sonuc)}");
        sb.AppendLine($"Şema sürümü: {Db.ScalarL("PRAGMA user_version")} (program: {Db.SemaSurumu})");
        sb.AppendLine($"Dosya: {Db.Path} ({new FileInfo(Db.Path).Length / 1024.0 / 1024.0:0.0} MB)");
        foreach (var t in new[] { "muvekkiller", "davalar", "genel_tebligatlar", "sureler", "gorevler", "ajanda", "finans_tahsilat", "emanet_hareketleri", "dava_evraklari", "audit_log" })
            sb.AppendLine($"  {t}: {Db.ScalarL($"SELECT COUNT(*) FROM {t}")} kayıt");
        sb.AppendLine($"Son yedek: {(Cfg.GetString("son_yedek").Length > 0 ? Cfg.GetString("son_yedek") : "henüz yok")}");
        _saglik.Text = sb.ToString();
    }

    void Sikistir()
    {
        Yedekleme.Yap(Db, Cfg);
        Db.Execute("VACUUM");
        Ui.Info(this, "Veritabanı sıkıştırıldı (öncesinde yedek alındı).");
        SaglikCalistir();
    }

    public override void Refresh()
    {
        var onceki = Muv;
        _muv.Items.Clear();
        foreach (var m in Db.Query("SELECT id, ad_soyad, tc_no FROM muvekkiller ORDER BY ad_soyad"))
            _muv.Items.Add(new ComboItem { Value = m.L("id"), Label = $"{m.S("ad_soyad")} ({m.S("tc_no")})" });
        var i = _muv.Items.Cast<ComboItem>().ToList().FindIndex(x => Convert.ToInt64(x.Value) == onceki);
        _muv.SelectedIndex = i >= 0 ? i : (_muv.Items.Count > 0 ? 0 : -1);
        SaglikCalistir();
    }
}
