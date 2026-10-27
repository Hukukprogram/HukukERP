using System.Windows;
using System.Windows.Controls;

namespace HukukErp.UI.Pages;

/// <summary>Kullanım kılavuzu, sorumluluk notu ve açık kaynak lisansları.</summary>
public class YardimPage : BasePage
{
    static readonly (string Baslik, string Metin)[] Bolumler =
    {
        ("Hızlı başlangıç",
         "1) Ayarlar'da Telegram bot bilgilerinizi ve UETS hesabınızı girin.\n" +
         "2) 'Müvekkil & Finans'te müvekkil ekleyin, 'Dava & Müvekkil Takibi'nde dava açın.\n" +
         "3) 'Bireysel UETS Takibi'nde UETS'i açın, giriş yapın, tebligatları UETS'in İNDİR özelliğiyle indirin. İndirilen PDF/UDF/resim otomatik işlenir.\n" +
         "4) Tebligat satırında '⏱ İş Listesine Ekle'ye basın, süreyi katalogdan seçin; son tarih hesaplanır ve süreli iş olarak kaydolur.\n" +
         "5) Her gün 'Ofis Dashboard' sayfasının üst bölümüne (bugünün işleri) bakın."),
        ("Klavye",
         "Ctrl+K: komut paleti. İstediğiniz sayfanın ya da işlemin adını yazıp Enter'a basın."),
        ("Süreler nasıl hesaplanır",
         "Kural dosyası tebligat_sureleri.json'dur. e-Tebligat, UETS'e ulaşmasından 5 gün sonra tebliğ edilmiş sayılır; gün süresinde son gün tebliğ + N gün, hafta süresinde tebliğ + 7×N gün, ay süresinde aynı tarihtir. " +
         "Son gün hafta sonu ya da resmî tatile denk gelirse ilk iş gününe kayar. Kayıtta adli tatil türü işaretliyse (HMK/CMK/İYUK) tatile denk gelen süre uzatılır. " +
         "Yeni süre eklemek ya da bir süreyi düzeltmek için veri klasöründeki (hukuk.db'nin yanı) tebligat_sureleri.json dosyasını düzenleyin; yoksa programla gelen kullanılır."),
        ("Dosya kartı",
         "Bir davanın her şeyi tek ekranda: süreler, tebligatlar, duruşma notları, müvekkil bilgilendirmeleri, zaman, tahsilat, masraf, emanet ve evrak. Evrakı pencereye sürükleyip bırakabilirsiniz. " +
         "'Duruşma Notu' sonraki duruşmayı günceller, kesin süre verildiyse süreli iş açar ve müvekkile gönderebileceğiniz bilgilendirme metnini panoya kopyalar. 'Dosyayı Kapat' kapanış kontrol listesini ve saklama süresini kaydeder."),
        ("Emanet hesabı",
         "Müvekkilden alınan harç/masraf avansı 'Giriş', yapılan harcama ya da iade 'Çıkış' olarak girilir. Bakiye müvekkile aittir; eksiye düşerse uyarılırsınız."),
        ("Yedekleme ve güvenlik",
         "Program günde bir kez ve her kapanışta yedek alır (Yedekler klasörü). Ayarlar'dan ikinci bir konum (harici disk, bulut klasörü) seçin. Program parolası açılışı korur ama veritabanını şifrelemez. " +
         "Hata kayıtları Loglar klasöründedir. Raporlar sayfasındaki 'Değişiklik Günlüğü' kayıtlardaki ekleme/değişiklik/silmeleri gösterir."),
        ("KVKK",
         "'Veri Aktarımı ve KVKK' sayfasından bir müvekkilin tüm verisini dışa aktarabilir, aydınlatma kaydı tutabilir ve gerektiğinde müvekkili ve dosyalarını silebilirsiniz. Saklama yükümlülüklerinizi silmeden önce kontrol edin."),
        ("Önemli not",
         "Bu program bir yardımcı araçtır. Süre, faiz, tazminat ve infaz hesapları mevzuatı ve hesaplama yöntemini basitleştirir; sonuçları, özellikle son günleri, kendiniz de doğrulayın. Hesap parametreleri (asgari ücret, tavanlar, faiz, harç) güncel olmayabilir."),
        ("Kullanılan açık kaynak bileşenler",
         "Microsoft.Data.Sqlite (MIT) • PdfPig (Apache-2.0) • MailKit (MIT) • DocumentFormat.OpenXml (MIT) • Velopack (MIT) • Microsoft Edge WebView2 (Microsoft lisansı). " +
         "Windows OCR ve PDF işleme Windows'un yerleşik bileşenlerini kullanır."),
    };

    public YardimPage(AppCtx ctx) : base(ctx)
    {
        Top(Ui.PageHeader("Kullanım Kılavuzu", "Kısa rehber, süre kuralları ve notlar"));
        var sp = new StackPanel { Margin = new Thickness(0, 10, 8, 0) };
        foreach (var (baslik, metin) in Bolumler)
        {
            var c = new Card { Margin = new Thickness(0, 0, 0, 12) };
            c.Top(Ui.H2(baslik));
            c.Fill(Ui.Text(metin, 13, false, "text", true));
            sp.Children.Add(c);
        }
        Fill(new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }
}
