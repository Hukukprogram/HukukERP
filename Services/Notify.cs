using System.Net.Http;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using HukukErp.Core;

namespace HukukErp.Services;

public class NotifyError : Exception
{
    public NotifyError(string msg, Exception? inner = null) : base(msg, inner) { }
}

public static class Notify
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public static void TelegramSend(string token, string chatId, string text, string? document = null)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId))
            throw new NotifyError("Telegram bot token / chat id ayarlı değil.");
        var baseUrl = $"https://api.telegram.org/bot{token}";
        try
        {
            HttpResponseMessage r;
            if (!string.IsNullOrEmpty(document) && File.Exists(document))
            {
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent(chatId), "chat_id");
                form.Add(new StringContent(text.Length > 1000 ? text[..1000] : text), "caption");
                form.Add(new ByteArrayContent(File.ReadAllBytes(document)), "document", Path.GetFileName(document));
                r = Http.PostAsync($"{baseUrl}/sendDocument", form).GetAwaiter().GetResult();
            }
            else
            {
                using var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["chat_id"] = chatId, ["text"] = text.Length > 4000 ? text[..4000] : text,
                });
                r = Http.PostAsync($"{baseUrl}/sendMessage", form).GetAwaiter().GetResult();
            }
            if (!r.IsSuccessStatusCode)
            {
                var body = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                throw new NotifyError($"Telegram hatası {(int)r.StatusCode}: {(body.Length > 200 ? body[..200] : body)}");
            }
        }
        catch (HttpRequestException e) { throw new NotifyError($"Telegram bağlantı hatası: {e.Message}", e); }
        catch (TaskCanceledException e) { throw new NotifyError($"Telegram bağlantı hatası: {e.Message}", e); }
    }

    public static void SendPersonal(Row s, string text, string? document = null) =>
        TelegramSend(s.S("telegram_bot_token"), s.S("telegram_kisisel_chat_id"), text, document);


    public static void SendMail(Row s, string to, string subject, string body, IEnumerable<string>? attachments = null)
    {
        var server = s.S("smtp_server").Trim();
        int port = s.I("smtp_port") == 0 ? 587 : s.I("smtp_port");
        var sender = s.S("sender_email").Trim();
        var pwd = s.S("sender_password").Trim();
        if (server.Length == 0 || sender.Length == 0 || string.IsNullOrWhiteSpace(to))
            throw new NotifyError("SMTP sunucu / gönderen / alıcı bilgisi eksik.");

        var msg = new MimeMessage();
        msg.From.Add(MailboxAddress.Parse(sender));
        foreach (var addr in to.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            msg.To.Add(MailboxAddress.Parse(addr));
        msg.Subject = subject;
        var builder = new BodyBuilder { TextBody = body };
        foreach (var att in attachments ?? Array.Empty<string>())
            if (!string.IsNullOrEmpty(att) && File.Exists(att)) builder.Attachments.Add(att);
        msg.Body = builder.ToMessageBody();

        // Sunucu/port uyuşmazlığına dayanıklı: önce porta uygun yöntem, olmazsa diğerleri denenir
        var sirali = port == 465 ? new List<string> { "ssl", "starttls" } : new List<string> { "starttls", "ssl" };
        if (pwd.Length == 0) sirali.Add("plain");
        var hatalar = new List<string>();
        foreach (var mod in sirali)
        {
            try
            {
                using var c = new SmtpClient { Timeout = 30000 };
                var opt = mod == "ssl" ? SecureSocketOptions.SslOnConnect : mod == "starttls" ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
                c.Connect(server, port, opt);
                if (pwd.Length > 0) c.Authenticate(sender, pwd);
                c.Send(msg);
                c.Disconnect(true);
                return;
            }
            catch (AuthenticationException e)
            {
                throw new NotifyError($"SMTP kimlik doğrulama hatası: {e.Message}\nGmail/Outlook/Yandex gibi servislerde normal parola yerine 'uygulama şifresi' gerekir.", e);
            }
            catch (Exception e)
            {
                hatalar.Add($"{mod}: {e.Message}");
            }
        }
        throw new NotifyError($"SMTP hatası ({server}:{port}) — " + string.Join(" | ", hatalar) +
            "\nİpucu: Gmail smtp.gmail.com 587 (uygulama şifresi) • Outlook/Office365 smtp.office365.com 587 • " +
            "Yandex smtp.yandex.com.tr 465 • Hotmail smtp-mail.outlook.com 587.");
    }
}
