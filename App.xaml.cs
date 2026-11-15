using System.IO;
using System.Windows;
using HukukErp.Core;
using HukukErp.Services;
using HukukErp.UI;

namespace HukukErp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--ekran-goruntusu")) { EkranModu(e.Args); return; }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;   // parola penceresi kapanınca program kapanmasın
        Config.EskiVeriyiAktar();
        var cfg = new Config();
        var logPath = Path.Combine(cfg.Subdir("Loglar"), "hata.log");
        void Yaz(string kaynak, object? hata)
        {
            try { File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({kaynak}) {hata}\n\n"); } catch { }
        }
        DispatcherUnhandledException += (_, ex) =>
        {
            Yaz("arayüz", ex.Exception);
            MessageBox.Show(ex.Exception.Message + "\n\nAyrıntı: " + logPath, "Beklenmeyen hata", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Yaz("kritik", ex.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, ex) => { Yaz("arka plan", ex.Exception); ex.SetObserved(); };

        Theme.Apply(cfg.GetString("theme", "dark"));
        WheelGuard.Install(); // kaydırırken açılır listelerin değeri değişmesin

        if (Kilit.Var(cfg))
        {
            bool acildi = false;
            for (int i = 0; i < 3 && !acildi; i++)
            {
                var pw = ParolaDialog.Sor("HukukERP", i == 0 ? "Programı açmak için parolanızı girin." : $"Parola yanlış. Kalan hak: {3 - i}");
                if (pw == null) break;
                acildi = Kilit.Dogru(cfg, pw);
            }
            if (!acildi) { Shutdown(0); return; }
        }
        if (Yedekleme.BekleyeniUygula(cfg))
            MessageBox.Show("Seçtiğiniz yedek geri yüklendi. Önceki veritabanı Yedekler klasöründe 'oncesi_' adıyla saklandı.", "Geri yükleme tamam");

        Db db;
        try { db = new Db(cfg.DbPath); }
        catch (Exception ex)
        {
            MessageBox.Show($"{cfg.DbPath}\n\n{ex.Message}\n\nconfig.json içindeki db_path değerini kontrol edin.", "Veritabanı açılamadı",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        var saglik = db.SaglikKontrolu();
        if (!saglik.Equals("ok", StringComparison.OrdinalIgnoreCase))
        {
            Yaz("bütünlük", saglik);
            var sonYedek = Directory.Exists(Yedekleme.Klasor(cfg))
                ? Directory.GetFiles(Yedekleme.Klasor(cfg), "hukuk_*.db").OrderByDescending(x => x, StringComparer.Ordinal).FirstOrDefault() : null;
            var soru = "Veritabanı sağlık denetimi sorun buldu:\n" + (saglik.Length > 200 ? saglik[..200] : saglik) + "\n\n" +
                       (sonYedek != null ? $"Son yedek ({Path.GetFileName(sonYedek)}) geri yüklensin mi?\n(Evet: program kapanır, yeniden açınca yedek uygulanır. Hayır: bu veritabanıyla devam.)" : "Yedek bulunamadı.");
            if (sonYedek != null && MessageBox.Show(soru, "Veritabanı sorunu", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try { Yedekleme.GeriYuklemeIste(cfg, sonYedek); } catch (Exception ex) { Yaz("geri yükleme", ex); }
                Shutdown(0);
                return;
            }
            if (sonYedek == null) MessageBox.Show(soru, "Veritabanı sorunu", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Ui.Db = db;
        try { Yedekleme.GunlukYedek(db, cfg); } catch (Exception ex) { Yaz("yedek", ex); }
        var win = new MainWindow(db, cfg);
        MainWindow = win;
        win.Show();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        _ = Guncelleme.KontrolEtAsync(cfg, win);
    }

    /// <summary>GitHub Actions için: geçici, boş bir veri klasörüyle (kullanıcı verisine dokunmadan) örnek verili ekranların PNG'lerini üretir.</summary>
    async void EkranModu(string[] args)
    {
        try
        {
            var i = Array.IndexOf(args, "--ekran-goruntusu");
            var cikis = Path.GetFullPath(i + 1 < args.Length ? args[i + 1] : "ekran");
            Directory.CreateDirectory(cikis);
            var dir = Path.Combine(Path.GetTempPath(), "hukukerp_ekran_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            var cfg = new Config(Path.Combine(dir, "config.json"));
            cfg.Set("db_path", Path.Combine(dir, "hukuk.db"));
            Theme.Apply(args.Contains("--acik") ? "light" : "dark");
            var db = new Db(cfg.DbPath);
            Ui.Db = db;
            try
            {
                var m = db.Insert("muvekkiller", new Dictionary<string, object?> { ["ad_soyad"] = "Örnek Müvekkil", ["tc_no"] = "10000000146", ["telefon"] = "0555 000 00 00" });
                var d = db.Insert("davalar", new Dictionary<string, object?> { ["mahkeme_adi"] = "Ankara 5. Asliye Hukuk Mahkemesi", ["esas_no"] = "2026/123 E.", ["karsi_taraf"] = "Örnek A.Ş.", ["durusma_tarihi"] = Utils.AddDays(Utils.TodayIso(), 6), ["muvekkil_id"] = m });
                db.Insert("sureler", new Dictionary<string, object?> { ["baslik"] = "Cevap dilekçesi süresi (örnek)", ["son_tarih"] = Utils.AddDays(Utils.TodayIso(), 3), ["durum"] = "Açık", ["dava_id"] = d });
                db.Insert("gorevler", new Dictionary<string, object?> { ["baslik"] = "Bilirkişi raporunu incele (örnek)", ["durum"] = "Yapılacak", ["son_tarih"] = Utils.AddDays(Utils.TodayIso(), 1) });
            }
            catch (Exception) { /* örnek veri eklenemese de boş ekranlar alınır */ }
            var win = new MainWindow(db, cfg) { Width = 1500, Height = 900 };
            MainWindow = win;
            win.Show();
            await win.EkranGoruntuleriAl(cikis);
        }
        catch (Exception ex) { try { File.WriteAllText("ekran_hata.txt", ex.ToString()); } catch { } }
        Environment.Exit(0);
    }
}
