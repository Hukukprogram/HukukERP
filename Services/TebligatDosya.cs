using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;

namespace HukukErp.Services;

/// <summary>Tebligat ekleri: PDF, UDF (UYAP belge) ve resim dosyaları.</summary>
public static class TebligatDosya
{
    public static readonly string[] Uzantilar = { ".pdf", ".udf", ".jpg", ".jpeg", ".png" };

    public const string DialogFiltresi = "Tebligat dosyaları (*.pdf;*.udf;*.jpg;*.jpeg;*.png)|*.pdf;*.udf;*.jpg;*.jpeg;*.png";

    public static bool Destekli(string yol) => Uzantilar.Contains(Path.GetExtension(yol).ToLowerInvariant());
    public static bool Resim(string yol) => Path.GetExtension(yol).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png";

    /// <summary>Ayrıştırma için dosya metni. UDF: içindeki metin; PDF: sayfa metni; resim: boş (OCR yapılmaz).</summary>
    public static string Metin(string yol)
    {
        var uz = Path.GetExtension(yol).ToLowerInvariant();
        if (uz == ".pdf") return Parser.PdfText(yol);
        if (uz == ".udf") return UdfMetni(yol);
        return "";
    }

    /// <summary>UDF bir ZIP'tir; metin content.xml içinde &lt;content&gt;&lt;![CDATA[...]]&gt;&lt;/content&gt; olarak durur.</summary>
    public static string UdfMetni(string yol)
    {
        try
        {
            using var z = ZipFile.OpenRead(yol);
            var e = z.GetEntry("content.xml") ?? z.Entries.FirstOrDefault(x => x.Name.Equals("content.xml", StringComparison.OrdinalIgnoreCase));
            if (e == null) return "";
            using var sr = new StreamReader(e.Open(), System.Text.Encoding.UTF8);
            var xml = sr.ReadToEnd();
            var m = Regex.Match(xml, @"<content>\s*<!\[CDATA\[(.*?)\]\]>\s*</content>", RegexOptions.Singleline);
            if (m.Success) return m.Groups[1].Value.Replace("\r\n", "\n");
            m = Regex.Match(xml, @"<content>(.*?)</content>", RegexOptions.Singleline);
            return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : "";
        }
        catch (Exception) { return ""; }
    }
}
