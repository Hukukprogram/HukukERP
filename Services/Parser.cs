using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HukukErp.Core;
using UglyToad.PdfPig;

namespace HukukErp.Services;

public class Ayrisan
{
    public string Tur { get; set; } = "Genel";
    public string Kurum { get; set; } = "";
    public string EsasNo { get; set; } = "";
    public string TebligTarihi { get; set; } = "";
    public string SonCevapTarihi { get; set; } = "";
    public List<string> TcListesi { get; set; } = new();
    public string Iban { get; set; } = "";
    public double Tutar { get; set; }
    public string Konu { get; set; } = "";
    public string Alacakli { get; set; } = "";
    public string AlacakliVekili { get; set; } = "";
    public string Muhatap { get; set; } = "";
    public bool MetinVar { get; set; } = true;

    /// <summary>Python sürümüyle aynı anahtar adlarıyla JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["tur"] = Tur, ["kurum"] = Kurum, ["esas_no"] = EsasNo, ["teblig_tarihi"] = TebligTarihi,
        ["son_cevap_tarihi"] = SonCevapTarihi, ["tc_listesi"] = TcListesi, ["iban"] = Iban, ["tutar"] = Tutar,
        ["konu"] = Konu, ["alacakli"] = Alacakli, ["alacakli_vekili"] = AlacakliVekili,
        ["muhatap"] = Muhatap, ["metin_var"] = MetinVar,
    }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}

/// <summary>PDF metin çıkarma + tebligat sınıflandırma/ayrıştırma.</summary>
public static class Parser
{
    static readonly (string Tur, string[] Keys)[] Turler =
    {
        ("Dava Dilekçesi", new[] { "dava dilekçesi", "dava dilekçesinin tebliği", "davalı sıfatıyla" }),
        ("Bilirkişi Raporu", new[] { "bilirkişi raporu", "bilirkişi ek raporu", "bilirkişi heyeti" }),
        ("Duruşma Tebligatı", new[] { "duruşma günü", "duruşma tutanağı", "duruşmaya gelmeniz" }),
        ("Cevap Dilekçesi", new[] { "cevap dilekçesi", "cevaba cevap" }),
        ("Ödeme Emri", new[] { "ödeme emri", "icra emri", "takip talebi" }),
        ("Karar / İlam", new[] { "gerekçeli karar", "hüküm", "ilam" }),
    };

    const RegexOptions CI = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    static readonly Regex ReEsas = new(@"(\d{4})\s*/\s*(\d{1,7})\s*(?:e\.?|esas|es\.|icra|talimat|d\.?\s*is)?", RegexOptions.CultureInvariant);
    static readonly Regex ReEsasLabel = new(@"(?:dosya\s*(?:no|numarasi)|esas\s*(?:no)?|dosya)\s*[:\-]?\s*(\d{4}\s*/\s*\d{1,7})", RegexOptions.CultureInvariant);
    static readonly Regex ReTc = new(@"(?<!\d)([1-9]\d{10})(?!\d)");
    static readonly Regex ReIban = new(@"\bTR\s?\d{2}(?:\s?\d{4}){5}\s?\d{2}\b", CI);
    static readonly Regex RePara = new(@"(\d{1,3}(?:\.\d{3})*,\d{2})\s*(?:TL|₺|TRY)", CI);
    static readonly Regex ReTarih = new(@"\b(\d{2}[./-]\d{2}[./-]\d{4})\b");
    static readonly Regex ReKurumGovde = new(
        @"(?:icra(?:\s+ve\s+iflas)?\s+(?:hukuk\s+|ceza\s+)?(?:dairesi|müdürlüğü|mahkemesi)(?:\s+müdürlüğü)?" +
        @"|(?:asliye|sulh|iş|aile|tüketici|ticaret|ağır\s+ceza|idare|vergi|kadastro|bölge\s+adliye)[\wçğıöşü ]{0,35}?\s+mahkemesi" +
        @"|noterliği|başsavcılığı|vergi\s+dairesi(?:\s+müdürlüğü)?)", RegexOptions.CultureInvariant);
    static readonly string[] KurumYasak = { "cumhuriyet", "tarih", "sayı", "konu", "dosya", "esas", "müvekkil", "borçlu", "alacaklı",
        "adalet", "sayın", "tebligat", "ihbarname", "müzekkere" };
    static readonly Regex ReTcEtiket = new(@"\bT\.?\s?C\.?(?=\s|$)", CI);

    /// <summary>Büyük/küçük ve i/ı/İ/I farklarını eşitler (uzunluk korunur) — Python'daki (?i) davranışı için.</summary>
    public static string Fold(string s) => Utils.TrLower(s).Replace('ı', 'i');

    static bool Full(string pattern, string s) => Regex.IsMatch(s, "^(?:" + pattern + @")\z");

    static string SehirSatiri(string oncekiMetin)
    {
        var lines = oncekiMetin.Split('\n').Select(x => x.TrimEnd('\r')).ToList();
        var last = lines.Skip(Math.Max(0, lines.Count - 5)).Reverse();
        foreach (var prev in last)
        {
            var pc = ReTcEtiket.Replace(prev, " ");
            pc = Regex.Replace(pc, @"\d{1,2}\s*[./-]\s*\d{1,2}\s*[./-]\s*\d{4}", " ").Trim();
            if (pc.Length == 0 || Full(@"[\d./\-\s:]+", pc)) continue;
            var pl = Utils.TrLower(pc);
            if (Full(@"[A-Za-zÇĞİÖŞÜçğıöşü ]{2,30}", pc) && pc.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length <= 3
                && !KurumYasak.Any(k => pl.Contains(k)))
                return pc;
            return "";
        }
        return "";
    }

    static string[] Words(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    public static string KurumBul(string text)
    {
        var low = Utils.TrLower(text);
        Match? m = null;
        foreach (Match cand in ReKurumGovde.Matches(low))
        {
            int b = low.LastIndexOf('\n', Math.Max(0, cand.Index - 1)) + 1;
            if (cand.Index == 0) b = 0;
            int e = low.IndexOf('\n', cand.Index + cand.Length);
            var satir = low.Substring(b, (e != -1 ? e : low.Length) - b);
            if (Regex.IsMatch(satir, @"\badres\b|mahalle|\bmah\.|sokak|\bcad\b|\btel\s*:|\bblok\b|\bkat\b|@")) continue;
            m = cand; break;
        }
        if (m == null) return "";
        int satirBas = m.Index == 0 ? 0 : low.LastIndexOf('\n', m.Index - 1) + 1;
        var govde = text.Substring(m.Index, m.Length);
        var onParca = Regex.Split(text.Substring(satirBas, m.Index - satirBas), "[:/]").Last();
        var on = ReTcEtiket.Replace(onParca, " ");
        on = string.Join(" ", Words(on).TakeLast(4));
        if (on.Trim().Length == 0 || Full(@"[\d.\s]+", on))
        {
            var sehir0 = SehirSatiri(text[..satirBas]);
            if (sehir0.Length > 0)
            {
                var sira = on.Trim();
                on = Full(@"\d{1,3}\.?", sira) ? $"{sehir0} {sira}".Trim() : sehir0;
            }
        }
        if (on.Trim().Length == 0 || Full(@"[\d.\s]+", on))
        {
            var onceki = text[..satirBas].Split('\n').Select(x => x.TrimEnd('\r')).ToList();
            int eklenen = 0;
            foreach (var prev in onceki.Skip(Math.Max(0, onceki.Count - 4)).Reverse())
            {
                var pc = ReTcEtiket.Replace(prev, " ").Trim();
                if (pc.Length == 0) { if (eklenen > 0) break; continue; }
                var pl = Utils.TrLower(pc);
                if (pc.Length > 40 || pc.Contains(':') || Regex.IsMatch(pc, @"\d{3,}") || KurumYasak.Any(k => pl.Contains(k))) break;
                on = $"{pc} {on}".Trim();
                eklenen++;
                if (eklenen == 2) break;
            }
        }
        else
        {
            var sehir = SehirSatiri(text[..satirBas]);
            if (sehir.Length > 0 && !Utils.TrLower(on).Contains(Utils.TrLower(sehir)))
                on = $"{sehir} {on}".Trim();
        }
        var kelimeler = new List<string>();
        foreach (var w in Words($"{on} {govde}"))
            if (kelimeler.Count == 0 || Utils.TrLower(kelimeler[^1]) != Utils.TrLower(w)) kelimeler.Add(w);
        return Utils.TrTitle(string.Join(" ", kelimeler));
    }

    static string FirstSplit(string pattern, string s) => Regex.Split(s, pattern)[0];

    public static (string alacakli, string vekil, string muhatap) TaraflarBul(string text)
    {
        var ma = Regex.Match(text, @"ALACAKLI\s*:\s*(.+)");
        string alacakli, vekil = "";
        int bas;
        const string cut = @",\s*(?=\d{6,})|,\s*(?:Vergi|TC|T\.C)";
        if (ma.Success)
        {
            bas = ma.Index;
            var ilk = ma.Groups[1].Value.Trim();
            alacakli = FirstSplit(cut, ilk).Trim(' ', ',');
            alacakli = Regex.Replace(alacakli, @",\s*\d{6,}.*$", "").Trim(' ', ',');
            var mv = Regex.Match(text[(ma.Index + ma.Length)..], @"VEK[İIiı]L[İIiı]\s*:\s*(?:Av\.?\s*)?([^\n(,]+)");
            if (mv.Success) vekil = Regex.Replace(mv.Groups[1].Value, @"\s+", " ").Trim(' ', '.', ':', '-');
        }
        else
        {
            var m2 = Regex.Match(text, @"ALACAKLI\s*\n\s*VEK[İIiı]L[İIiı]\s*\n(?:\s*\n)*\s*:\s*(.+)\n\s*:\s*(?:Av\.?\s*)?([^\n(,]+)");
            if (!m2.Success) return ("", "", "");
            bas = m2.Index;
            alacakli = FirstSplit(cut, m2.Groups[1].Value.Trim()).Trim(' ', ',');
            vekil = Regex.Replace(m2.Groups[2].Value, @"\s+", " ").Trim(' ', '.', ':', '-');
        }
        var onceki = text[..bas].Split('\n').Select(x => x.TrimEnd('\r')).ToList();
        var satirlar = new List<string>();
        for (int i = onceki.Count - 1; i >= 0; i--)
        {
            var ln = onceki[i].Trim();
            if (ln.Length == 0) { if (satirlar.Count > 0) break; continue; }
            var f = Fold(ln);
            if (Full(@"(?:tebli[ğg]|tebligat|m[üu]zekkere|ihbarname|yazi|maa[şs]\s+hacz[ií]|haciz\s+(?:yazisi|m[üu]zekkeresi))\s*:?", f))
            {
                if (satirlar.Count > 0) break;
                continue;
            }
            if (Regex.IsMatch(f, @"\be?sas\b|\d{2}[./]\d{2}[./]\d{4}|^t\.?\s?c\.?$|icra|mahkeme") && satirlar.Count == 0) break;
            if (Regex.IsMatch(f, @"\be?sas\b|\d{2}[./]\d{2}[./]\d{4}")) break;
            satirlar.Insert(0, ln);
        }
        var muhatap = Regex.Replace(string.Join(" ", satirlar).Trim(), @"\s*-\s*E$", "");
        return (alacakli, vekil, muhatap);
    }

    /// <summary>PDF'ten satır satır metin (kelimeleri konumlarına göre satırlara gruplar).</summary>
    public static string PdfText(string path, int maxPages = 15) =>
        string.Concat(PdfSayfalari(path, maxPages).Select(x => x + "\n"));

    /// <summary>PDF sayfalarının metni (sayfa başına bir öğe).</summary>
    public static List<string> PdfSayfalari(string path, int maxPages = int.MaxValue)
    {
        try
        {
            using var doc = PdfDocument.Open(path);
            var sonuc = new List<string>();
            int n = 0;
            foreach (var page in doc.GetPages())
            {
                if (n++ >= maxPages) break;
                var sb = new StringBuilder();
                var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
                // üstten alta (PDF'te Y yukarı doğru artar)
                var sorted = words.OrderByDescending(w => w.BoundingBox.Bottom).ToList();
                var lines = new List<List<UglyToad.PdfPig.Content.Word>>();
                foreach (var w in sorted)
                {
                    var line = lines.LastOrDefault();
                    double h = Math.Max(1.0, w.BoundingBox.Height);
                    if (line != null && Math.Abs(line[0].BoundingBox.Bottom - w.BoundingBox.Bottom) < h * 0.5) line.Add(w);
                    else lines.Add(new List<UglyToad.PdfPig.Content.Word> { w });
                }
                foreach (var line in lines)
                {
                    var ordered = line.OrderBy(w => w.BoundingBox.Left).ToList();
                    var ls = new StringBuilder();
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        if (i > 0)
                        {
                            double gap = ordered[i].BoundingBox.Left - ordered[i - 1].BoundingBox.Right;
                            double cw = Math.Max(1.0, ordered[i].BoundingBox.Height * 0.5);
                            ls.Append(gap > cw * 2 ? "  " : " ");
                        }
                        ls.Append(ordered[i].Text);
                    }
                    sb.Append(ls).Append('\n');
                }
                sonuc.Add(sb.ToString());
            }
            return sonuc;
        }
        catch (Exception) { return new List<string>(); }
    }

    public static string FileHash(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
    }

    public static string Siniflandir(string text)
    {
        var t = Utils.TrLower(text);
        string best = "Genel"; double bestScore = 0;
        foreach (var (tur, keys) in Turler)
        {
            var sc = keys.Max(k => Utils.PartialRatio(t, k));
            if (sc > bestScore) { best = tur; bestScore = sc; }
        }
        return bestScore >= 90 ? best : "Genel";
    }

    /// <param name="teslimIso">UETS sayfasında okunan "Teslim Tarihi" (yyyy-MM-dd). Verilirse tebliğ tarihi olarak bu kullanılır.</param>
    /// <param name="varsayilanIso">Evrakta ve UETS sayfasında Teslim Tarihi bulunamazsa kullanılacak tarih (örn. dosyanın indirilme günü). Evraktaki tarihler (dilekçe/karar tarihi) tebliğ tarihi sayılmaz.</param>
    public static Ayrisan Ayristir(string text, int cevapGun = 7, string? teslimIso = null, string? varsayilanIso = null)
    {
        var a = new Ayrisan { MetinVar = text.Trim().Length > 0 };
        if (!a.MetinVar) return a;
        a.Tur = Siniflandir(text);
        a.Kurum = KurumBul(text);
        var folded = Fold(text);
        var m = ReEsasLabel.Match(folded);
        if (m.Success) a.EsasNo = Regex.Replace(m.Groups[1].Value, @"\s+", "");
        else
        {
            var m2 = ReEsas.Match(folded);
            if (m2.Success) a.EsasNo = $"{m2.Groups[1].Value}/{m2.Groups[2].Value}";
        }

        var tarihler = ReTarih.Matches(text).Select(x => Utils.ParseDate(x.Groups[1].Value)).Where(d => d != null).Select(d => d!.Value).ToList();
        if (tarihler.Count > 0 || !string.IsNullOrEmpty(teslimIso) || !string.IsNullOrEmpty(varsayilanIso))
        {
            if (tarihler.Count > 0) a.TebligTarihi = Utils.Iso(tarihler.Max());
            foreach (var line in text.Split('\n'))
            {
                if (Utils.TrLower(line).Contains("tebli"))
                {
                    var dd = ReTarih.Match(line);
                    if (dd.Success && Utils.ParseDate(dd.Groups[1].Value) != null)
                    {
                        a.TebligTarihi = Utils.Iso(Utils.ParseDate(dd.Groups[1].Value)!.Value);
                        break;
                    }
                }
            }
            // UETS'te "Teslim Tarihi: 26.10.2026 15:00" yazan tarih, tebligatın UETS'e ulaştığı (tebliğ) tarihidir; en öncelikli kaynaktır.
            bool teslimVar = false;
            if (!string.IsNullOrEmpty(teslimIso)) { a.TebligTarihi = teslimIso; teslimVar = true; }
            var tm = Regex.Match(text, @"teslim\s*tarihi\s*[:\-]?\s*(\d{1,2}\s*[./]\s*\d{1,2}\s*[./]\s*\d{4})", CI);
            if (tm.Success && string.IsNullOrEmpty(teslimIso))
            {
                var td = Utils.ParseDate(Regex.Replace(tm.Groups[1].Value, @"\s+", "").Replace('/', '.'));
                if (td != null) { a.TebligTarihi = Utils.Iso(td.Value); teslimVar = true; }
            }
            if (!teslimVar && !string.IsNullOrEmpty(varsayilanIso)) { a.TebligTarihi = varsayilanIso; teslimVar = true; }
            var gm = Regex.Match(Utils.TrLower(text), @"(\d{1,2})\s*gün\s*içinde");
            // e-tebligat: UETS'e ulaşmadan sonraki 5. gün tebliğ edilmiş sayılır; cevap süresi bu günden işler.
            var bazIso = teslimVar ? Utils.AddDays(a.TebligTarihi, IsSureleri.ETebligatGunu) : a.TebligTarihi;
            // Metinde "N gün içinde" yoksa ve varsayılan süre verilmediyse tahmin yürütülmez (boş kalır; katalogdan hesaplanır).
            a.SonCevapTarihi = gm.Success ? Utils.AddDays(bazIso, int.Parse(gm.Groups[1].Value))
                : cevapGun > 0 ? Utils.AddDays(bazIso, cevapGun) : "";
        }

        a.TcListesi = ReTc.Matches(text).Select(x => x.Groups[1].Value).Distinct().ToList();
        var mi = ReIban.Match(text);
        if (mi.Success) a.Iban = Regex.Replace(mi.Value, @"\s+", "").ToUpperInvariant();
        var paralar = RePara.Matches(text).Select(x => double.Parse(x.Groups[1].Value.Replace(".", "").Replace(",", "."),
            System.Globalization.CultureInfo.InvariantCulture)).ToList();
        if (paralar.Count > 0) a.Tutar = paralar.Max();
        (a.Alacakli, a.AlacakliVekili, a.Muhatap) = TaraflarBul(text);
        a.Konu = a.Tur;
        return a;
    }
}
