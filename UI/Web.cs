using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HukukErp.UI;

/// <summary>Ortak WebView2 ortamı (UETS oturumu bu bilgisayarda kalıcıdır).</summary>
public static class WebEnv
{
    static Task<CoreWebView2Environment>? _env;

    public static string UserDataFolder
    {
        get
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HukukERP", "WebView2");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static Task<CoreWebView2Environment> Get() =>
        _env ??= CoreWebView2Environment.CreateAsync(null, UserDataFolder);
}

/// <summary>Önizleme: PDF ve resimler Edge/WebView2 ile, UDF metin olarak gösterilir.</summary>
public class PdfPreview : Grid
{
    readonly WebView2 _wv = new() { Visibility = Visibility.Collapsed };
    readonly TextBlock _msg = Ui.Muted("Dosya seçilmedi");
    bool _ready;
    bool _failed;
    string? _pending;

    public PdfPreview()
    {
        _msg.HorizontalAlignment = HorizontalAlignment.Center;
        _msg.VerticalAlignment = VerticalAlignment.Center;
        _msg.TextAlignment = TextAlignment.Center;
        Children.Add(_msg);
        Children.Add(_wv);
    }

    async Task EnsureAsync()
    {
        if (_ready || _failed) return;
        try
        {
            await _wv.EnsureCoreWebView2Async(await WebEnv.Get());
            _ready = true;
        }
        catch (Exception e)
        {
            _failed = true;
            _msg.Text = "Önizleme için Microsoft Edge WebView2 Runtime gerekli.\n" + e.Message;
            _msg.Visibility = Visibility.Visible;
            _wv.Visibility = Visibility.Collapsed;
        }
    }

    public async void Open(string? path)
    {
        _pending = path;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            _msg.Text = string.IsNullOrEmpty(path) ? "Dosya seçilmedi" : "Dosya bulunamadı";
            _msg.Visibility = Visibility.Visible;
            _wv.Visibility = Visibility.Collapsed;
            return;
        }
        var uz = Path.GetExtension(path).ToLowerInvariant();
        if (uz is ".tif" or ".tiff" or ".zip")
        {
            _msg.Text = "Bu dosya türü burada önizlenemiyor (\"Dosyayı Aç\" ile açın).";
            _msg.Visibility = Visibility.Visible;
            _wv.Visibility = Visibility.Collapsed;
            return;
        }
        await EnsureAsync();
        if (!_ready || _pending != path) return;
        _msg.Visibility = Visibility.Collapsed;
        _wv.Visibility = Visibility.Visible;
        try
        {
            if (uz == ".udf")
            {
                var metin = HukukErp.Services.TebligatDosya.UdfMetni(path);
                var html = "<html><head><meta charset='utf-8'><style>body{font:14px 'Times New Roman',serif;margin:24px 40px;line-height:1.5}" +
                           ".n{font:12px Segoe UI,sans-serif;color:#6b7280;margin-bottom:12px}pre{white-space:pre-wrap;font:inherit;margin:0}</style></head><body>" +
                           "<div class='n'>UDF önizleme (yalnızca metin; biçimlendirme için \"Dosyayı Aç\")</div><pre>" +
                           System.Net.WebUtility.HtmlEncode(metin.Length > 0 ? metin : "(UDF içinde metin bulunamadı)") + "</pre></body></html>";
                _wv.CoreWebView2.NavigateToString(html);
            }
            else _wv.CoreWebView2.Navigate(new Uri(path).AbsoluteUri);   // pdf ve resimleri Edge kendisi gösterir
        }
        catch (Exception e) { _msg.Text = "Önizlenemedi: " + e.Message; _msg.Visibility = Visibility.Visible; _wv.Visibility = Visibility.Collapsed; }
    }
}
