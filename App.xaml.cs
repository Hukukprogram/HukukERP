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
}
