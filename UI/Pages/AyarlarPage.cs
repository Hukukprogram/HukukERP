using System.IO;
using System.Windows;
using System.Windows.Controls;
using HukukErp.Core;
using HukukErp.Services;
using Microsoft.Win32;

namespace HukukErp.UI.Pages;

public class AyarlarPage : BasePage
{
    readonly TextBox _dbPath = new();
    readonly TextBox _smtpServer = new(), _smtpPort = new(), _sender = new();
    readonly PasswordBox _pwd = new(), _tgToken = new();
    readonly TextBox _tgKisisel = new();
    readonly DataTable _tU = new(("Hesap Adı", "hesap_adi", null), ("Kimlik No", "kimlik_no", null), ("Tür", "hesap_tipi", null), ("Durum", "durum", null));

    static List<Field> UFields() => new()
    {
        new("hesap_adi", "Hesap Adı", required: true), new("kimlik_no", "Kimlik / Vergi No"),
        new("hesap_tipi", "Hesap Türü", "combo", new List<(object?, string)> { ("Sahsi", "Bireysel (Avukat)") }, def: "Sahsi"),
        new("durum", "Durum", "combo", Field.Of("Aktif", "Pasif"), def: "Aktif"),
    };

    static UIElement Form(params (string Label, UIElement Input)[] rows)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        int r = 0;
        foreach (var (label, input) in rows)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = Ui.Muted(label);
            l.VerticalAlignment = VerticalAlignment.Center;
            l.Margin = new Thickness(0, 0, 12, 10);
            ((FrameworkElement)input).Margin = new Thickness(0, 0, 0, 10);
            Grid.SetRow(l, r); Grid.SetRow(input, r); Grid.SetColumn(input, 1);
            g.Children.Add(l); g.Children.Add(input);
            r++;
        }
        return g;
    }

    public AyarlarPage(AppCtx ctx) : base(ctx)
    {
        Top(Ui.PageHeader("Ayarlar"));
        var vp = new StackPanel { Margin = new Thickness(0, 12, 8, 0) };
        var v = new StackPanel { Margin = new Thickness(0, 12, 8, 0) };

        // --- veritabanı / tema
        var c = new Card();
        var dbRow = new Grid();
        dbRow.ColumnDefinitions.Add(new ColumnDefinition());
        dbRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var b1 = Ui.Btn("Gözat…", "", BrowseDb); b1.Margin = new Thickness(8, 0, 0, 0);
        var b2 = Ui.Btn("Yolu Kaydet", "Primary", SaveDb); b2.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(b1, 1);
        var btns = Ui.HRow(b1, b2);
        Grid.SetColumn(btns, 1);
        dbRow.Children.Add(_dbPath); dbRow.Children.Add(btns);
        c.Top(Ui.H2("Veritabanı & Görünüm"));
        c.Top(dbRow);
        c.Top(Ui.Muted("Ağ paylaşımı için UNC yolu girin (ör. \\\\ANA-BILGISAYAR\\HukukERP\\hukuk.db). " +
                       "Değişiklik uygulama yeniden başlatılınca geçerli olur. Dosyalar/ ve Yedekler/ klasörleri veritabanının yanında tutulur."));
        c.Fill(Ui.HRow(Ui.Btn("☾ Koyu Tema", "", () => ctx.SetTheme("dark")), Ui.Btn("☀ Açık Tema", "", () => ctx.SetTheme("light"))));
        vp.Children.Add(c);

        c = new Card { Margin = new Thickness(0, 14, 0, 0) };
        c.Top(Ui.H2("Program"));
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        c.Top(Ui.Muted($"Sürüm: {ver?.ToString(3) ?? "1.0.0"}"));
        c.Fill(Ui.Muted($"Veri klasörü: {Cfg.DataDir}\nYeni sürüm kontrolü yalnızca program açılışında yapılır; güncelleme varsa üstte bildirim çıkar."));
        vp.Children.Add(c);

        // --- yedekleme
        c = new Card { Margin = new Thickness(0, 14, 0, 0) };
        c.Top(Ui.H2("Yedekleme"));
        c.Top(_yedekBilgi);
        c.Top(Ui.HRow(Ui.Btn("Şimdi Yedekle", "Primary", YedekAl), Ui.Btn("Yedek Klasörünü Aç", "", () => Ui.OpenExternal(Yedekleme.Klasor(Cfg))),
                      Ui.Btn("İkinci Konum Seç…", "", IkinciKonum), Ui.Btn("Yedekten Geri Yükle…", "Danger", GeriYukle)));
        c.Fill(Ui.Muted("Program her gün ilk açılışta ve kapanırken yedek alır (son 30 yedek saklanır). İkinci konum (harici disk, bulut klasörü) seçerseniz her yedek oraya da kopyalanır. Geri yükleme program yeniden açılınca uygulanır; mevcut veritabanı 'oncesi_' adıyla saklanır."));
        vp.Children.Add(c);

        // --- güvenlik ve kayıtlar
        c = new Card { Margin = new Thickness(0, 14, 0, 0) };
        c.Top(Ui.H2("Güvenlik ve Kayıtlar"));
        c.Top(_kilitBilgi);
        c.Top(Ui.HRow(Ui.Btn("Program Parolası Belirle / Değiştir", "", ParolaBelirle), Ui.Btn("Parolayı Kaldır", "", ParolaKaldir),
                      Ui.Btn("Hata Kaydı Klasörünü Aç", "", () => Ui.OpenExternal(Cfg.Subdir("Loglar")))));
        c.Fill(Ui.Muted("Parola programın açılışını korur; veritabanı dosyasını şifrelemez. Dosya sahibi olmayanlardan korunmak için bilgisayarınızın Windows hesabını da parolalı tutun. Değişiklik günlüğü: Raporlar sayfası."));
        vp.Children.Add(c);

        // --- SMTP
        c = new Card { Margin = new Thickness(0, 14, 0, 0) };
        c.Top(Ui.H2("E-posta (SMTP)"), 12);
        c.Top(Form(("SMTP Sunucu", _smtpServer), ("Port (587=STARTTLS, 465=SSL)", _smtpPort), ("Gönderen E-posta", _sender),
                   ("Şifre / Uygulama Şifresi", _pwd)), 4);
        c.Fill(new StackPanel { Children = { Ui.Btn("Test Maili Gönder", "", TestMail) }, HorizontalAlignment = HorizontalAlignment.Left });
        v.Children.Add(c);

        // --- Telegram
        c = new Card { Margin = new Thickness(0, 14, 0, 0) };
        c.Top(Ui.H2("Telegram"), 12);
        c.Top(Form(("Bot Token", _tgToken),
                   ("Chat ID (tahsilat, görev, UETS bildirimleri)", _tgKisisel)), 4);
        c.Fill(Ui.Btn("Telegram Test Mesajı", "", () => TestTg(true)));
        v.Children.Add(c);

        var save = Ui.Btn("Ayarları Kaydet", "Primary", Save);
        save.Margin = new Thickness(0, 14, 0, 0);
        save.HorizontalAlignment = HorizontalAlignment.Left;
        v.Children.Add(save);

        // --- UETS hesapları
        c = new Card { Margin = new Thickness(0, 14, 0, 0) };
        c.Top(Ui.H2("UETS Hesapları"));
        _tU.MinHeight = 180; _tU.MaxHeight = 260;
        c.Top(_tU);
        c.Top(Ui.HRow(Ui.Btn("＋ Hesap", "Primary", AddU), Ui.Btn("Düzenle", "", EditU), Ui.Btn("Sil", "Danger", DelU)));
        c.Fill(Ui.Muted("Şifre saklanmaz: 'Bireysel UETS Takibi' sayfasındaki tarayıcı sekmesinde bir kez giriş yaparsınız (oturum bu bilgisayarda kalır)."));
        v.Children.Add(c);

        var tabs = new TabControl();
        tabs.Items.Add(Ui.Tab("Program", new ScrollViewer { Content = vp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }));
        tabs.Items.Add(Ui.Tab("Ayarlar", new ScrollViewer { Content = v, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }));
        Fill(tabs);
    }

    readonly TextBlock _yedekBilgi = Ui.Muted("");
    readonly TextBlock _kilitBilgi = Ui.Muted("");

    void YedekGoster()
    {
        var ik = Cfg.GetString("yedek_ikinci_konum");
        var son = Cfg.GetString("son_yedek");
        _yedekBilgi.Text = $"Son yedek: {(son.Length > 0 ? son : "henüz yok")}\nİkinci konum: {(ik.Length > 0 ? ik : "tanımlı değil")}";
        _kilitBilgi.Text = Kilit.Var(Cfg) ? "Program parolası: AÇIK" : "Program parolası: kapalı";
    }

    void YedekAl()
    {
        var y = Yedekleme.Yap(Db, Cfg);
        YedekGoster();
        if (y != null) Ui.Info(this, $"Yedek alındı:\n{y}");
        else Ui.Warn(this, "Yedek alınamadı.");
    }

    void IkinciKonum()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "İkinci yedek konumu (harici disk / bulut klasörü)" };
        if (dlg.ShowDialog() != true) return;
        Cfg.Set("yedek_ikinci_konum", dlg.FolderName);
        YedekGoster();
    }

    void GeriYukle()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Geri yüklenecek yedek", Filter = "Yedek (*.db)|*.db", InitialDirectory = Yedekleme.Klasor(Cfg) };
        if (dlg.ShowDialog() != true) return;
        if (!Ui.Confirm(this, $"'{Path.GetFileName(dlg.FileName)}' yedeği geri yüklenecek.\nProgramı kapatıp yeniden açtığınızda uygulanır; o ana kadarki veriler 'oncesi_' yedeğinde saklanır.\n\nDevam edilsin mi?")) return;
        try { Yedekleme.GeriYuklemeIste(Cfg, dlg.FileName); Ui.Info(this, "Hazır. Programı kapatıp yeniden açın."); }
        catch (Exception e) { Ui.Warn(this, "Yedek hazırlanamadı: " + e.Message); }
    }

    void ParolaBelirle()
    {
        var w = Window.GetWindow(this);
        if (Kilit.Var(Cfg))
        {
            var eski = ParolaDialog.Sor("Mevcut parola", "Mevcut parolanızı girin.", false, w);
            if (eski == null) return;
            if (!Kilit.Dogru(Cfg, eski)) { Ui.Warn(this, "Parola yanlış."); return; }
        }
        var yeni = ParolaDialog.Sor("Yeni parola", "Yeni program parolasını girin. Unutursanız programa giremezsiniz; config.json'daki kilit_hash/kilit_salt satırlarını silerek kaldırabilirsiniz.", true, w);
        if (yeni == null) return;
        Kilit.Ayarla(Cfg, yeni);
        YedekGoster();
        Ui.Info(this, "Parola belirlendi. Bir sonraki açılışta sorulur.");
    }

    void ParolaKaldir()
    {
        if (!Kilit.Var(Cfg)) return;
        var p = ParolaDialog.Sor("Parolayı kaldır", "Kaldırmak için mevcut parolayı girin.", false, Window.GetWindow(this));
        if (p == null) return;
        if (!Kilit.Dogru(Cfg, p)) { Ui.Warn(this, "Parola yanlış."); return; }
        Kilit.Kaldir(Cfg);
        YedekGoster();
    }

    public override void Refresh()
    {
        YedekGoster();
        var s = Db.GetSettings();
        _smtpServer.Text = s.S("smtp_server");
        _smtpPort.Text = (s.I("smtp_port") == 0 ? 587 : s.I("smtp_port")).ToString();
        _sender.Text = s.S("sender_email");
        _pwd.Password = s.S("sender_password");
        _tgToken.Password = s.S("telegram_bot_token");
        _tgKisisel.Text = s.S("telegram_kisisel_chat_id");
        _dbPath.Text = Cfg.GetString("db_path");
        _tU.Load(Db.Query("SELECT * FROM uets_hesaplari ORDER BY hesap_tipi, hesap_adi"));
    }

    Dictionary<string, object?> Collect() => new()
    {
        ["smtp_server"] = _smtpServer.Text.Trim(),
        ["smtp_port"] = int.TryParse(_smtpPort.Text.Trim(), out var p) && p > 0 ? p : 587,
        ["sender_email"] = _sender.Text.Trim(), ["sender_password"] = _pwd.Password,
        ["telegram_bot_token"] = _tgToken.Password.Trim(),
        ["telegram_kisisel_chat_id"] = _tgKisisel.Text.Trim(),
    };

    static Row AsRow(Dictionary<string, object?> d) { var r = new Row(); foreach (var kv in d) r[kv.Key] = kv.Value; return r; }

    void Save()
    {
        Db.SaveSettings(Collect());
        Ctx.Status("Ayarlar kaydedildi");
        Ui.Info(this, "Ayarlar kaydedildi.");
    }

    void TestMail()
    {
        var s = AsRow(Collect());
        var to = s.S("sender_email");
        Ui.RunAsyncVoid(() => Notify.SendMail(s, to, "Hukuk ERP test", "Test maili."),
            () => Ui.Info(this, "Test maili gönderildi."), m => Ui.Warn(this, m));
    }

    void TestTg(bool kisisel)
    {
        var s = AsRow(Collect());
        var chat = s.S("telegram_kisisel_chat_id");
        Ui.RunAsyncVoid(() => Notify.TelegramSend(s.S("telegram_bot_token"), chat, "Hukuk ERP test mesajı ✔"),
            () => Ui.Info(this, "Telegram test mesajı gönderildi."), m => Ui.Warn(this, m));
    }

    void BrowseDb()
    {
        var dlg = new SaveFileDialog { Filter = "SQLite (*.db)|*.db", FileName = _dbPath.Text, OverwritePrompt = false, CheckFileExists = false };
        if (dlg.ShowDialog() == true) _dbPath.Text = dlg.FileName;
    }

    void SaveDb()
    {
        Cfg.Set("db_path", _dbPath.Text.Trim());
        Ui.Info(this, "Yol config.json'a kaydedildi. Uygulamayı yeniden başlatın.");
    }

    void AddU()
    {
        var v = FormDialog.Ask(this, "Yeni UETS Hesabı", UFields());
        if (v != null) { Db.Insert("uets_hesaplari", v); Refresh(); }
    }

    void EditU()
    {
        var r = _tU.Current;
        if (r == null) return;
        var v = FormDialog.Ask(this, "UETS Hesabı", UFields(), r);
        if (v != null) { Db.Update("uets_hesaplari", r.L("id"), v); Refresh(); }
    }

    void DelU()
    {
        var r = _tU.Current;
        if (r != null && Ui.Confirm(this, "Hesap silinsin mi? (Arşivlenmiş tebligatlar silinmez)")) { Db.Delete("uets_hesaplari", r.L("id")); Refresh(); }
    }
}
