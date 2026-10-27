using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HukukErp.Core;

/// <summary>SQLite satırı: sütun adı → değer. Python sürümündeki dict karşılığı.</summary>
public class Row : Dictionary<string, object?>
{
    public Row() : base(StringComparer.OrdinalIgnoreCase) { }

    public string S(string k)
    {
        if (TryGetValue(k, out var v) && v != null && v != DBNull.Value)
            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        return "";
    }
    public long L(string k)
    {
        if (!TryGetValue(k, out var v) || v == null || v == DBNull.Value) return 0;
        if (v is long l) return l;
        if (v is double d) return (long)d;
        return long.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out var r) ? r : 0;
    }
    public int I(string k) => (int)L(k);
    public double D(string k)
    {
        if (!TryGetValue(k, out var v) || v == null || v == DBNull.Value) return 0;
        if (v is double d) return d;
        if (v is long l) return l;
        return double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 0;
    }
}

public static class Utils
{
    public static readonly CultureInfo Tr = new("tr-TR");

    public static string TrLower(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            sb.Append(ch switch
            {
                'İ' => 'i', 'I' => 'ı', 'Ş' => 'ş', 'Ğ' => 'ğ', 'Ü' => 'ü', 'Ö' => 'ö', 'Ç' => 'ç',
                _ => char.ToLowerInvariant(ch)
            });
        }
        return sb.ToString();
    }

    /// <summary>Python'daki _tr_upper: i→İ, ı→I, sonra büyük harf.</summary>
    public static string TrUpper(string s) => s.Trim().Replace("i", "İ").Replace("ı", "I").ToUpperInvariant();

    public static string TrAscii(string s)
    {
        const string from = "çğıöşüÇĞİÖŞÜ", to = "cgiosuCGIOSU";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s) { var i = from.IndexOf(ch); sb.Append(i >= 0 ? to[i] : ch); }
        return sb.ToString();
    }

    public static string SlugAd(string s)
    {
        s = TrAscii((s ?? "").Trim());
        s = Regex.Replace(s, "[^A-Za-z0-9]+", "_").Trim('_');
        return s.Length == 0 ? "Isimsiz" : s;
    }

    public static string TodayIso() => DateTime.Today.ToString("yyyy-MM-dd");

    static readonly string[] DateFormats = { "yyyy-MM-dd", "dd.MM.yyyy", "dd/MM/yyyy", "dd-MM-yyyy" };

    public static DateTime? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (s.Length > 10) s = s[..10];
        return DateTime.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : null;
    }

    public static string FmtDate(string? s) => ParseDate(s)?.ToString("dd.MM.yyyy") ?? "";
    public static string Iso(DateTime d) => d.ToString("yyyy-MM-dd");

    public static string AddDays(string iso, int days) =>
        Iso((ParseDate(iso) ?? DateTime.Today).AddDays(days));

    public static int? DaysLeft(string? iso)
    {
        var d = ParseDate(iso);
        return d == null ? null : (int)(d.Value - DateTime.Today).TotalDays;
    }

    public static string FmtMoney(double v)
    {
        var s = v.ToString("N2", CultureInfo.InvariantCulture); // 1,234.56
        return s.Replace(",", "X").Replace(".", ",").Replace("X", ".") + " ₺";
    }

    public static double ParseMoney(string? s)
    {
        if (s == null) return 0;
        var t = Regex.Replace(s, @"[^\d,.\-]", "");
        if (t.Length == 0) return 0;
        if (t.Contains(',')) t = t.Replace(".", "").Replace(",", ".");
        else if (t.Count(c => c == '.') > 1) t = t.Replace(".", "");
        else if (Regex.IsMatch(t, @"^-?\d{1,3}\.\d{3}$")) t = t.Replace(".", "");
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 0;
    }

    public static bool ValidTc(string? tc)
    {
        if (tc == null || !Regex.IsMatch(tc, @"^[1-9]\d{10}$")) return false;
        var d = tc.Select(c => c - '0').ToArray();
        int odd = d[0] + d[2] + d[4] + d[6] + d[8];
        int even = d[1] + d[3] + d[5] + d[7];
        if ((((odd * 7) - even) % 10 + 10) % 10 != d[9]) return false;
        return d.Take(10).Sum() % 10 == d[10];
    }

    /// <summary>Dosya adında geçersiz karakterleri boşlukla değiştirir.</summary>
    public static string SafeFileName(string s)
    {
        var t = Regex.Replace(s ?? "", "[\\\\/:*?\"<>|]+", " ").Trim();
        return t;
    }

    public static string TrTitle(string s)
    {
        var parts = new List<string>();
        foreach (var w in TrLower(s).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (char.IsLetter(w[0]))
            {
                var first = w[0] == 'i' ? "İ" : w[0] == 'ı' ? "I" : char.ToUpperInvariant(w[0]).ToString();
                parts.Add(first + w[1..]);
            }
            else parts.Add(w);
        }
        return string.Join(" ", parts);
    }

    /// <summary>İki metin arası benzerlik (0-100) — en uzun ortak altdizi (Indel) tabanlı.</summary>
    public static double Ratio(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 100;
        if (a.Length == 0 || b.Length == 0) return 0;
        return 200.0 * Lcs(a, b) / (a.Length + b.Length);
    }

    static int Lcs(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
                cur[j] = a[i - 1] == b[j - 1] ? prev[j - 1] + 1 : Math.Max(prev[j], cur[j - 1]);
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>Kısa metnin uzun metinde en iyi pencere eşleşmesi (rapidfuzz partial_ratio benzeri).</summary>
    public static double PartialRatio(string a, string b)
    {
        var (sh, lg) = a.Length <= b.Length ? (a, b) : (b, a);
        if (sh.Length == 0) return 0;
        if (lg.Contains(sh, StringComparison.Ordinal)) return 100;
        int n = sh.Length;
        double best = 0;
        for (int i = 0; i + n <= lg.Length; i++)
        {
            // hızlı eleme: pencerenin ilk veya son harfi kısa metinde yoksa atla
            if (sh.IndexOf(lg[i]) < 0 && sh.IndexOf(lg[i + n - 1]) < 0) continue;
            var r = Ratio(lg.Substring(i, n), sh);
            if (r > best) { best = r; if (best >= 100) break; }
        }
        return best;
    }

    public static double WRatio(string a, string b)
    {
        var r = Ratio(a, b);
        static string Sort(string x) => string.Join(" ", x.Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderBy(w => w, StringComparer.Ordinal));
        var ts = Ratio(Sort(a), Sort(b)) * 0.95;
        return Math.Max(r, ts);
    }
}
