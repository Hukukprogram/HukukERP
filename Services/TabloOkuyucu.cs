using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace HukukErp.Services;

/// <summary>.xlsx ve .csv dosyalarını satır/sütun listesi olarak okur (ilk satır başlık kabul edilir).</summary>
public static class TabloOkuyucu
{
    public static List<string[]> Oku(string yol) =>
        Path.GetExtension(yol).ToLowerInvariant() == ".xlsx" ? Xlsx(yol) : Csv(yol);

    static List<string[]> Csv(string yol)
    {
        var metin = File.ReadAllText(yol, Encoding.UTF8);
        var ilk = metin.Split('\n').FirstOrDefault() ?? "";
        char ayirici = ilk.Count(c => c == ';') >= ilk.Count(c => c == ',') ? ';' : ',';
        if (ilk.Contains('\t') && ilk.Count(c => c == '\t') > ilk.Count(c => c == ayirici)) ayirici = '\t';
        var satirlar = new List<string[]>();
        var alan = new StringBuilder();
        var satir = new List<string>();
        bool tirnak = false;
        for (int i = 0; i < metin.Length; i++)
        {
            var c = metin[i];
            if (tirnak)
            {
                if (c == '"' && i + 1 < metin.Length && metin[i + 1] == '"') { alan.Append('"'); i++; }
                else if (c == '"') tirnak = false;
                else alan.Append(c);
            }
            else if (c == '"') tirnak = true;
            else if (c == ayirici) { satir.Add(alan.ToString().Trim()); alan.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < metin.Length && metin[i + 1] == '\n') i++;
                satir.Add(alan.ToString().Trim()); alan.Clear();
                if (satir.Any(x => x.Length > 0)) satirlar.Add(satir.ToArray());
                satir = new List<string>();
            }
            else alan.Append(c);
        }
        if (alan.Length > 0 || satir.Count > 0)
        {
            satir.Add(alan.ToString().Trim());
            if (satir.Any(x => x.Length > 0)) satirlar.Add(satir.ToArray());
        }
        return satirlar;
    }

    static List<string[]> Xlsx(string yol)
    {
        var sonuc = new List<string[]>();
        using var doc = SpreadsheetDocument.Open(yol, false);
        var wb = doc.WorkbookPart!;
        var sst = wb.SharedStringTablePart?.SharedStringTable;
        var ws = wb.WorksheetParts.First();
        var veri = ws.Worksheet.GetFirstChild<SheetData>();
        if (veri == null) return sonuc;
        foreach (var row in veri.Elements<Row>())
        {
            var hucre = new Dictionary<int, string>();
            int maks = -1;
            foreach (var c in row.Elements<Cell>())
            {
                var kolon = KolonNo(c.CellReference?.Value ?? "");
                if (kolon < 0) continue;
                string deger = c.CellValue?.Text ?? "";
                if (c.DataType?.Value == CellValues.SharedString && sst != null && int.TryParse(deger, out var idx))
                    deger = sst.ElementAt(idx).InnerText;
                else if (c.DataType?.Value == CellValues.InlineString)
                    deger = c.InnerText;
                hucre[kolon] = deger.Trim();
                maks = Math.Max(maks, kolon);
            }
            if (maks < 0) continue;
            var dizi = new string[maks + 1];
            for (int i = 0; i <= maks; i++) dizi[i] = hucre.TryGetValue(i, out var v) ? v : "";
            if (dizi.Any(x => x.Length > 0)) sonuc.Add(dizi);
        }
        return sonuc;
    }

    static int KolonNo(string referans)
    {
        var m = Regex.Match(referans, "^[A-Za-z]+");
        if (!m.Success) return -1;
        int n = 0;
        foreach (var ch in m.Value.ToUpperInvariant()) n = n * 26 + (ch - 'A' + 1);
        return n - 1;
    }
}
