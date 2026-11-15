using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using HukukErp.Core;

namespace HukukErp.UI.Pages;

/// <summary>Raporlar: aylık gelir/gider, müvekkil bakiyeleri, açık davalar, değişiklik günlüğü. Her tablo Excel'de açılabilen CSV olarak kaydedilir.</summary>
public class RaporPage : BasePage
{
    sealed record Sekme(string Ad, DataTable Tablo, (string Baslik, string Anahtar)[] Sutunlar);

    readonly List<Sekme> _sekmeler = new();
    readonly TabControl _tabs = new();
    readonly ComboBox _ay = new() { MinWidth = 140, Margin = new Thickness(8, 0, 0, 0) };
    bool _busy;

    public RaporPage(AppCtx ctx) : base(ctx)
    {
        Top(Ui.PageHeader("Raporlar", "Büronun mali ve dava durumu • her tablo CSV olarak kaydedilebilir"));
        Top(Ui.HRow(Ui.Btn("📄 Seçili Raporu CSV Kaydet…", "Primary", CsvKaydet), Ui.Btn("⟳ Yenile", "", Refresh), Ui.Muted("Aylık özet ayı:", false), _ay));
        for (int i = 0; i < 18; i++) _ay.Items.Add(DateTime.Today.AddMonths(-i).ToString("yyyy-MM"));
        _ay.SelectedIndex = 0;
        _ay.SelectionChanged += (_, _) => { if (!_busy) AyOzeti(); };

        Ekle("Aylık Tahsilat / Masraf", new[] { ("Ay", "ay", (Func<object?, string>?)null), ("Tahsilat", "tahsilat", Fmt.Money), ("Dosya Masrafı", "masraf", Fmt.Money), ("Net", "net", Fmt.Money) });
        Ekle("Müvekkil Bakiyeleri", new[] { ("Müvekkil", "ad_soyad", (Func<object?, string>?)null), ("Toplam Ücret", "toplam", Fmt.Money), ("Alınan", "alinan", Fmt.Money), ("Kalan", "kalan", Fmt.Money) });
        Ekle("Açık Davalar", new[] { ("Müvekkil", "ad_soyad", (Func<object?, string>?)null), ("Mahkeme", "mahkeme_adi", null), ("Esas", "esas_no", null), ("Aşama", "asama_durum", null), ("Duruşma", "durusma_tarihi", Fmt.Date), ("Açık Süre", "acik_sure", null) });
        Ekle("Değişiklik Günlüğü", new[] { ("Zaman", "zaman", (Func<object?, string>?)null), ("Kayıt Türü", "tablo", null), ("İşlem", "islem", null), ("Kayıt No", "kayit_id", null), ("Alanlar", "ozet", null) });
        Ekle("Aylık Özet", new[] { ("Kalem", "kalem", (Func<object?, string>?)null), ("Değer", "deger", null) });
        Fill(_tabs);
    }

    void AyOzeti()
    {
        var ay = _ay.SelectedItem as string ?? DateTime.Today.ToString("yyyy-MM");
        double tahsilat = Db.ScalarD("SELECT COALESCE(SUM(tutar),0) FROM tahsilat_hareketleri WHERE substr(tarih,1,7)=?", ay);
        double masraf = Db.ScalarD("SELECT COALESCE(SUM(tutar),0) FROM dava_masraflari WHERE substr(tarih,1,7)=?", ay);
        Row S(string k, string v) => new() { ["kalem"] = k, ["deger"] = v };
        T(4).Load(new List<Row>
        {
            S("Ay", ay),
            S("Tahsil edilen vekalet ücreti", Utils.FmtMoney(tahsilat)),
            S("Dosya masrafı", Utils.FmtMoney(masraf)),
            S("Net (tahsilat - masraf)", Utils.FmtMoney(tahsilat - masraf)),
            S("Yeni müvekkil", Db.ScalarL("SELECT COUNT(*) FROM muvekkiller WHERE substr(kayit_tarihi,1,7)=?", ay).ToString()),
            S("Kapanan dosya", Db.ScalarL("SELECT COUNT(*) FROM davalar WHERE substr(kapanis_tarihi,1,7)=?", ay).ToString()),
            S("Gelen tebligat", Db.ScalarL("SELECT COUNT(*) FROM genel_tebligatlar WHERE substr(teblig_tarihi,1,7)=?", ay).ToString()),
            S("Girilen duruşma notu", Db.ScalarL("SELECT COUNT(*) FROM durusma_notlari WHERE substr(tarih,1,7)=?", ay).ToString()),
            S("Şu an açık süre", Db.ScalarL("SELECT COUNT(*) FROM sureler WHERE durum='Açık'").ToString()),
            S("Toplam kalan alacak", Utils.FmtMoney(Db.ScalarD("SELECT COALESCE(SUM(kalan_bakiye),0) FROM finans_tahsilat"))),
        });
    }

    void Ekle(string ad, (string Baslik, string Anahtar, Func<object?, string>? Fmt)[] kolonlar)
    {
        var t = new DataTable(kolonlar.Select(k => (k.Baslik, k.Anahtar, k.Fmt)).ToArray());
        _sekmeler.Add(new Sekme(ad, t, kolonlar.Select(k => (k.Baslik, k.Anahtar)).ToArray()));
        _tabs.Items.Add(Ui.Tab(ad, t));
    }

    DataTable T(int i) => _sekmeler[i].Tablo;

    public override void Refresh()
    {
        T(0).Load(Db.Query(
            "SELECT ay, SUM(tahsilat) AS tahsilat, SUM(masraf) AS masraf, SUM(tahsilat) - SUM(masraf) AS net FROM (" +
            "SELECT substr(tarih,1,7) AS ay, tutar AS tahsilat, 0 AS masraf FROM tahsilat_hareketleri WHERE length(tarih)>=7 " +
            "UNION ALL SELECT substr(tarih,1,7), 0, tutar FROM dava_masraflari WHERE length(tarih)>=7) GROUP BY ay ORDER BY ay DESC"));
        T(1).Load(Db.Query(
            "SELECT m.ad_soyad, COALESCE(SUM(f.toplam_ucret),0) AS toplam, COALESCE(SUM(f.alinan_tutar),0) AS alinan, COALESCE(SUM(f.kalan_bakiye),0) AS kalan " +
            "FROM muvekkiller m LEFT JOIN finans_tahsilat f ON f.muvekkil_id=m.id GROUP BY m.id, m.ad_soyad ORDER BY kalan DESC"),
            new() { ["kalan"] = r => r.D("kalan") > 0 ? "warn" : "ok" });
        T(2).Load(Db.Query(
            "SELECT m.ad_soyad, d.mahkeme_adi, d.esas_no, d.asama_durum, d.durusma_tarihi, " +
            "(SELECT COUNT(*) FROM sureler s WHERE s.dava_id=d.id AND s.durum='Açık') AS acik_sure " +
            "FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id WHERE d.asama_durum!='Karara Çıktı' ORDER BY d.durusma_tarihi=''," + " d.durusma_tarihi"));
        T(3).Load(Db.Query("SELECT * FROM audit_log ORDER BY id DESC LIMIT 1000"));
        AyOzeti();
    }

    void CsvKaydet()
    {
        var s = _sekmeler[Math.Max(0, _tabs.SelectedIndex)];
        var dlg = new Microsoft.Win32.SaveFileDialog { Title = "Raporu kaydet", Filter = "CSV (*.csv)|*.csv", FileName = $"{s.Ad.Replace(' ', '_').Replace('/', '-')}_{DateTime.Today:yyyyMMdd}.csv" };
        if (dlg.ShowDialog() != true) return;
        var sb = new StringBuilder();
        string Q(string x) => "\"" + x.Replace("\"", "\"\"") + "\"";
        sb.AppendLine(string.Join(";", s.Sutunlar.Select(c => Q(c.Baslik))));
        foreach (var r in s.Tablo.Rows)
            sb.AppendLine(string.Join(";", s.Sutunlar.Select(c => Q(r.TryGetValue(c.Anahtar, out var v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "" : ""))));
        File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));  // BOM: Excel Türkçe karakterleri doğru açar
        Ui.Info(this, $"Kaydedildi:\n{dlg.FileName}");
    }
}
