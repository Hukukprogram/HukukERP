using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HukukErp.Core;

namespace HukukErp.Services;

public static class Documents
{
    static readonly Regex Placeholder = new(@"\{\{\s*(\w+)\s*\}\}");

    public static string RenderText(string template, IDictionary<string, string> values) =>
        Placeholder.Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    public static List<string> Placeholders(string template) =>
        Placeholder.Matches(template).Select(m => m.Groups[1].Value).Distinct().ToList();

    public static string MakeDocx(string text, string outPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        using var doc = WordprocessingDocument.Create(outPath, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var rp = new RunProperties(new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman" }, new FontSize { Val = "24" });
            bool first = i == 0 && lines[i].Trim().Length > 0;
            if (first) rp.Append(new Bold());
            var run = new Run(rp, new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
            var pp = new ParagraphProperties(new Justification { Val = first ? JustificationValues.Center : JustificationValues.Both });
            body.Append(new Paragraph(pp, run));
        }
        main.Document.Save();
        return outPath;
    }

    /// <summary>
    /// UYAP Doküman Editörü için .udf (zip: content.xml + documentproperties.xml) üretir.
    /// **...** = kalın + altı çizili etiket; etiketli satırlarda asılı girinti (159 pt).
    /// </summary>
    public static string MakeUdf(string text, string outPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        text = text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("]]>", "] ]>");
        var satirlar = new List<List<(string t, bool b)>>();
        foreach (var line in text.Split('\n'))
        {
            var parcalar = new List<(string, bool)>();
            var segs = line.Split("**");
            for (int i = 0; i < segs.Length; i++)
                if (segs[i].Length > 0) parcalar.Add((segs[i], i % 2 == 1));
            satirlar.Add(parcalar);
        }
        var body = new StringBuilder();
        foreach (var pr in satirlar) { foreach (var p in pr) body.Append(p.t); body.Append('\n'); }

        var elements = new StringBuilder();
        int offset = 0;
        const double ASILI = 159.0;
        string Run(string txt, bool kalin, int at)
        {
            var k = kalin ? "true" : "false";
            return $"<content bold=\"{k}\" italic=\"false\" underline=\"{k}\" family=\"Times New Roman\" size=\"12\" startOffset=\"{at}\" length=\"{txt.Length}\" />";
        }
        foreach (var pr0 in satirlar)
        {
            bool etiketli = pr0.Count > 0 && pr0[0].b;
            var pr = pr0.Count == 0 ? new List<(string t, bool b)> { ("", false) } : new List<(string t, bool b)>(pr0);
            pr[^1] = (pr[^1].t + "\n", pr[^1].b);
            var runs = new StringBuilder();
            foreach (var (txt, kalin) in pr)
            {
                runs.Append(Run(txt, kalin, offset));
                offset += txt.Length;
            }
            double sol = etiketli ? ASILI : 0.0, ilk = etiketli ? -ASILI : 0.0;
            elements.Append($"<paragraph Alignment=\"0\" LeftIndent=\"{sol.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}\" RightIndent=\"0.0\" LineSpacing=\"0.0\" " +
                            $"FirstLineIndent=\"{ilk.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}\">{runs}</paragraph>");
        }
        var xml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>\n" +
            "<template format_id=\"1.8\" >\n" +
            $"<content><![CDATA[{body}]]></content>\n" +
            "<properties><pageFormat mediaSizeName=\"1\" leftMargin=\"42.525\" rightMargin=\"42.525\" topMargin=\"42.525\" " +
            "bottomMargin=\"42.525\" paperOrientation=\"1\" headerFOffset=\"20.0\" footerFOffset=\"20.0\" /></properties>\n" +
            $"<elements resolver=\"hvl-default\" >{elements}</elements>\n" +
            "<styles><style name=\"default\" description=\"Geçerli\" family=\"Times New Roman\" size=\"12\" bold=\"false\" " +
            "italic=\"false\" foreground=\"-13421773\" FirstLineIndent=\"0.0\" />" +
            "<style name=\"hvl-default\" family=\"Times New Roman\" size=\"12\" description=\"Gövde\" /></styles>\n" +
            "</template>\n";
        var props =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n" +
            "<!DOCTYPE properties SYSTEM \"http://java.sun.com/dtd/properties.dtd\">\n" +
            "<properties><entry key=\"origin\">hukuk_erp</entry></properties>\n";

        if (File.Exists(outPath)) File.Delete(outPath);
        using var fs = new FileStream(outPath, FileMode.Create);
        using var z = new ZipArchive(fs, ZipArchiveMode.Create);
        void Add(string name, string content)
        {
            var e = z.CreateEntry(name, CompressionLevel.Optimal);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(content);
        }
        Add("content.xml", xml);
        Add("documentproperties.xml", props);
        return outPath;
    }

    public static string DesktopDir()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var real = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        foreach (var cand in new[] { real, Path.Combine(home, "Desktop"), Path.Combine(home, "OneDrive", "Desktop"),
                                     Path.Combine(home, "Masaüstü"), Path.Combine(home, "OneDrive", "Masaüstü") })
            if (!string.IsNullOrEmpty(cand) && Directory.Exists(cand)) return cand;
        return Path.Combine(home, "Desktop");
    }

    public static string TopluImzaKlasoru()
    {
        var d = Path.Combine(DesktopDir(), "UYAP_Toplu_Imza_Bekleyenler");
        Directory.CreateDirectory(d);
        return d;
    }

    public static string Uret(Row sablon, Dictionary<string, string> values, string outDir, string baseName, bool toSigning = false)
    {
        var vals = new Dictionary<string, string> { ["tarih"] = Utils.FmtDate(Utils.TodayIso()) };
        foreach (var kv in values) vals[kv.Key] = kv.Value;
        var text = RenderText(sablon.S("taslak_metin"), vals);
        var name = $"{Utils.SlugAd(baseName)}_{Utils.TodayIso()}";
        if (sablon.S("sablon_tipi") == "udf")
        {
            var path = MakeUdf(text, Path.Combine(outDir, name + ".udf"));
            if (toSigning) File.Copy(path, Path.Combine(TopluImzaKlasoru(), Path.GetFileName(path)), true);
            return path;
        }
        return MakeDocx(text, Path.Combine(outDir, name + ".docx"));
    }
}
