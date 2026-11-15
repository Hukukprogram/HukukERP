using System.Text;
using HukukErp.Core;

namespace HukukErp.Services;

/// <summary>Ajanda, duruşma ve süreleri telefon/Outlook/Google takvimine aktarılabilen .ics dosyasına çevirir.</summary>
public static class Takvim
{
    static string Kac(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");

    public static string Ics(Db db)
    {
        var sb = new StringBuilder();
        void S(string l) => sb.Append(l).Append("\r\n");
        S("BEGIN:VCALENDAR"); S("VERSION:2.0"); S("PRODID:-//HukukERP//TR"); S("CALSCALE:GREGORIAN"); S("X-WR-CALNAME:HukukERP");
        var damga = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");

        void Olay(string uid, string tarihIso, string saat, string baslik, string aciklama)
        {
            var t = Utils.ParseDate(tarihIso);
            if (t == null) return;
            S("BEGIN:VEVENT");
            S($"UID:{uid}@hukukerp");
            S($"DTSTAMP:{damga}");
            if (TimeSpan.TryParse(saat, out var ts))
            {
                var bas = t.Value.Date + ts;
                S($"DTSTART:{bas:yyyyMMdd'T'HHmmss}");
                S($"DTEND:{bas.AddHours(1):yyyyMMdd'T'HHmmss}");
            }
            else
            {
                S($"DTSTART;VALUE=DATE:{t.Value:yyyyMMdd}");
                S($"DTEND;VALUE=DATE:{t.Value.AddDays(1):yyyyMMdd}");
            }
            S("SUMMARY:" + Kac(baslik));
            if (aciklama.Length > 0) S("DESCRIPTION:" + Kac(aciklama));
            S("END:VEVENT");
        }

        foreach (var r in db.Query("SELECT * FROM ajanda"))
            Olay("aj" + r.L("id"), r.S("tarih"), r.S("saat"),
                 (r.S("baslik").Length > 0 ? r.S("baslik") : r.S("tur")) + (r.S("mahkeme").Length > 0 ? " – " + r.S("mahkeme") : ""),
                 $"{r.S("esas_no")} {r.S("notlar")}".Trim());
        foreach (var r in db.Query("SELECT d.*, m.ad_soyad FROM davalar d LEFT JOIN muvekkiller m ON m.id=d.muvekkil_id WHERE d.durusma_tarihi!='' " +
                                   "AND NOT EXISTS (SELECT 1 FROM ajanda x WHERE x.dava_id=d.id AND x.tarih=d.durusma_tarihi)"))
            Olay("dr" + r.L("id"), r.S("durusma_tarihi"), "", "Duruşma – " + $"{r.S("mahkeme_adi")} {r.S("esas_no")}".Trim(), r.S("ad_soyad"));
        foreach (var r in db.Query("SELECT * FROM sureler WHERE durum='Açık' AND son_tarih!=''"))
            Olay("sr" + r.L("id"), r.S("son_tarih"), "", "SÜRE: " + r.S("baslik"), r.S("dayanak"));
        S("END:VCALENDAR");
        return sb.ToString();
    }
}
