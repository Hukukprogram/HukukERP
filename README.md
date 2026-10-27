# HukukERP — C# (.NET 8 WPF) sürümü

Bireysel avukat büro yönetimi programı (WPF, SQLite). **Mevcut `config.json` ve `.db` dosyanızı okur;** yeni tablolar/sütunlar ilk açılışta eklenir, veriler silinmez (şema sürümü `PRAGMA user_version` ile izlenir).

## Setup ve otomatik güncelleme (bilgisayarınıza .NET kurmadan)
1. GitHub'da boş bir depo açın (public). Bu klasörün içeriğini (zip'in içindekileri) depoya yükleyin.
2. Depoda **Actions → "Setup olustur ve yayinla" → Run workflow**, sürüm numarasını yazın (ör. 1.0.0).
3. Birkaç dakika sonra deponun **Releases** sayfasında `HukukERP-Kurulum.exe` (sihirbazlı kurulum) oluşur. İndirip her bilgisayarda çalıştırın.
4. Güncelleme: kodu değiştirip aynı adımı yeni sürüm numarasıyla (1.0.1) tekrarlayın. Kurulu programlar açılışta yeni sürümü bulup kurulum önerir.

Veriler `%LOCALAPPDATA%\HukukERP\Veri` içindedir; güncellemede silinmez. Yerel yayın için alternatif: `yayinla.bat 1.0.1` (.NET 8 SDK ve GITHUB_TOKEN gerekir).

## Derleme (Windows)
1. .NET 8 SDK kurun. 2. Bu klasörde `build.bat` çalıştırın. 3. `dist\HukukERP.exe` oluşur.
   Geliştirme için: `dotnet run`.

Gerekenler: Windows 10/11, Microsoft Edge **WebView2 Runtime** (UETS tarayıcısı ve PDF önizleme için; Windows 11'de hazırdır).

## Mevcut verinizi kullanma
`HukukERP.exe`'yi eski `config.json` / `.db` ile aynı klasöre koyun (ya da config.json içindeki `db_path` yolunu kullanın).
Başlamadan önce klasörün yedeğini alın.

## Değişmeyen kurallar
- UETS: program hiçbir tebligatı kendiliğinden açmaz/tıklamaz; siz indirirsiniz, indirilen PDF/ZIP otomatik işlenir.
- Şahsi hesaplar → Avukat kuyruğu + kişisel Telegram; yalnızca bireysel (Şahsi) UETS takibi vardır.
- İnfaz hesabı: 5275 m.105/A, 107, 108, Geç.6, 7571 Geç.10/6, 7550, Açık Kurum Yönetmeliği m.5–6.

## Özellikler (özet)
Ofis Dashboard’da bugünün işleri • Dosya Kartı (süre, tebligat, duruşma notu, müvekkil bilgilendirme, zaman sayacı, evrak sürükle-bırak, kapanış/saklama) • İş Listesi + tebligat süresi kataloğu (`tebligat_sureleri.json`) • Bireysel UETS takibi (PDF/UDF/resim önizleme) • Emanet hesabı • Raporlar (CSV) • Genel arama • Emsal & Notlar • Excel/CSV içe aktarma • KVKK araçları • Yedekleme/geri yükleme, program parolası, hata ve değişiklik günlüğü • Komut paleti (Ctrl+K) • Hesaplamalar ve yardımcı programlar.

## Testler
`dotnet test Tests/HukukErp.Tests.csproj` (Windows). GitHub'da `Test` iş akışı her push'ta derleyip testleri çalıştırır; yayın akışından bağımsızdır.

## Kod imzalama (isteğe bağlı)
İmzasız Setup.exe'de Windows SmartScreen "bilinmeyen yayıncı" uyarısı gösterebilir. Programı başkalarına dağıtacaksanız bir kod imzalama sertifikası alıp `vpk pack` ve Inno Setup adımlarında imzalayın.

## Durum
Kod bu ortamda derlenip çalıştırılamadı (sandbox'ta .NET SDK yok). Derleme hatası çıkarsa çıktıyı gönderin, düzeltelim.
