using System.IO;
using System.Windows;
using System.Windows.Threading;
using Velopack;
using Velopack.Sources;

namespace HukukErp.Core;

/// <summary>
/// Güncelleme kaynağı sırası: config.json "guncelleme_url" → programla gelen guncelleme.txt (yayinla.bat yazar).
/// GitHub depo adresi, web adresi veya paylaşımlı klasör olabilir. Yalnızca program açılışında kontrol edilir.
/// </summary>
public static class Guncelleme
{
    static bool _soruldu;
    static UpdateManager? _mgr;
    static UpdateInfo? _hazir;
    static bool _uygulandi;

    /// <summary>İndirilmiş, kurulmayı bekleyen sürüm (yoksa null).</summary>
    public static string? HazirSurum => _hazir?.TargetFullRelease.Version.ToString();
    public static event Action? Degisti;

    /// <summary>Programı kapatıp güncellemeyi kurar ve yeniden başlatır.</summary>
    public static void Uygula()
    {
        if (_mgr == null || _hazir == null) return;
        _uygulandi = true;
        try
        {
            // Update.exe program kapanınca güncellemeyi kurar ve programı yeniden başlatır
            _mgr.WaitExitThenApplyUpdates(_hazir, false, true);
        }
        catch (Exception) { _uygulandi = false; return; }
        Application.Current.Shutdown();
        // kapanış bir nedenle takılırsa (arka plan iş parçacığı vb.) 4 sn sonra zorla kapat
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        t.Tick += (_, _) => Environment.Exit(0);
        t.Start();
    }

    static string Kaynak(Config cfg)
    {
        var k = cfg.GetString("guncelleme_url").Trim();
        if (k.Length > 0) return k;
        try
        {
            var f = Path.Combine(AppContext.BaseDirectory, "guncelleme.txt");
            if (File.Exists(f)) return File.ReadAllText(f).Trim();
        }
        catch (Exception) { }
        return "";
    }

    public static async Task KontrolEtAsync(Config cfg, Window owner)
    {
        await Denetle(cfg, owner); // yalnızca açılışta bir kez
    }

    static async Task Denetle(Config cfg, Window owner)
    {
        if (_soruldu) return;
        var kaynak = Kaynak(cfg);
        if (kaynak.Length == 0) return;
        try
        {
            _mgr ??= kaynak.Contains("github.com", StringComparison.OrdinalIgnoreCase)
                ? new UpdateManager(new GithubSource(kaynak, null, false))
                : new UpdateManager(kaynak);
            if (!_mgr.IsInstalled) return; // Setup ile kurulmamış (geliştirme) sürümde atla
            var info = await _mgr.CheckForUpdatesAsync();
            if (info == null) return;
            _soruldu = true;
            await _mgr.DownloadUpdatesAsync(info); // arka planda indir
            _hazir = info;
            // banner'a basılmazsa program kapanınca kurulur (yeniden başlatma isteği Uygula'da ayrıca verilir)
            Application.Current.Exit += (_, _) => { if (!_uygulandi) { try { _mgr.WaitExitThenApplyUpdates(info, false, false); } catch (Exception) { } } };
            Degisti?.Invoke();
        }
        catch (Exception) { /* çevrimdışı/erişilemez: sessizce geç, bir sonraki açılışta tekrar dener */ }
    }
}
