using System.Windows;
using System.Windows.Controls;
using HukukErp.Core;
using HukukErp.Services;

namespace HukukErp.UI.Pages;

/// <summary>Sözleşme & Evrak Motoru (şablonlar).</summary>
public class EvrakPage : BasePage
{
    const string YerTutucular = "Genel: {{tarih}} • Müvekkil: {{ad_soyad}} {{tc_no}} {{adres}} {{telefon}} {{eposta}} • " +
        "Dosya: {{dosya_konusu}} {{toplam_ucret}} {{odeme_plani}}";

    readonly ListBox _lst = new() { SelectionMode = SelectionMode.Single };
    readonly TextBlock _title = Ui.H2("Şablon seçin");
    readonly TextBox _ed = new()
    {
        AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalContentAlignment = VerticalAlignment.Top, FontFamily = new System.Windows.Media.FontFamily("Consolas"),
    };

    public EvrakPage(AppCtx ctx) : base(ctx)
    {
        Top(Ui.PageHeader("Sözleşme & Evrak Motoru", "Şablonu seçin, müvekkil bilgileriyle .docx / UYAP .udf üretin"));
        var left = new Card();
        left.Bottom(Ui.HRow(Ui.Btn("＋ Yeni", "Primary", New), Ui.Btn("Sil", "Danger", Delete)));
        left.Fill(_lst);

        var right = new Card();
        right.Top(_title);
        right.Bottom(Ui.HRow(Ui.Btn("Şablonu Kaydet", "", Save), Ui.Btn("Evrak Üret…", "Primary", Generate)));
        right.Bottom(Ui.Muted("Yer tutucular: " + YerTutucular));
        right.Fill(_ed);
        Fill(Ui.HSplit(left, right, 1, 3));
        _lst.SelectionChanged += (_, _) => Load();
    }

    public override void Refresh()
    {
        var cur = CurId();
        _lst.Items.Clear();
        foreach (var s in Db.Query("SELECT * FROM sablonlar ORDER BY sablon_adi"))
        {
            var it = new ListBoxItem { Tag = s.L("id"), Content = Ui.Text($"{s.S("sablon_adi")}  [{s.S("sablon_tipi").ToUpperInvariant()}]", 13, false, "text", true) };
            _lst.Items.Add(it);
            if (cur != null && s.L("id") == cur) _lst.SelectedItem = it;
        }
    }

    long? CurId() => (_lst.SelectedItem as ListBoxItem)?.Tag as long?;
    Row? Cur() { var i = CurId(); return i == null ? null : Db.One("SELECT * FROM sablonlar WHERE id=?", i); }

    void Load()
    {
        var s = Cur();
        _title.Text = s != null ? s.S("sablon_adi") : "Şablon seçin";
        _ed.Text = s != null ? s.S("taslak_metin") : "";
    }

    void New()
    {
        var v = FormDialog.Ask(this, "Yeni Şablon", new List<Field>
        {
            new("sablon_adi", "Şablon Adı", required: true),
            new("sablon_tipi", "Çıktı Türü", "combo", new List<(object?, string)> { ("docx", "Word (.docx)"), ("udf", "UYAP (.udf)") }),
        });
        if (v == null) return;
        v["taslak_metin"] = "BAŞLIK\n\n{{ad_soyad}} …";
        Db.Insert("sablonlar", v);
        Refresh();
    }

    void Save()
    {
        var i = CurId();
        if (i == null) return;
        Db.Update("sablonlar", i.Value, new Dictionary<string, object?> { ["taslak_metin"] = _ed.Text });
        Ctx.Status("Şablon kaydedildi");
    }

    void Delete()
    {
        var i = CurId();
        if (i != null && Ui.Confirm(this, "Şablon silinsin mi?")) { Db.Delete("sablonlar", i.Value); Refresh(); }
    }

    void Generate()
    {
        var s = Cur();
        if (s == null) { Ui.Info(this, "Önce şablon seçin."); return; }
        s["taslak_metin"] = _ed.Text;
        var ms = Db.Query("SELECT * FROM muvekkiller ORDER BY ad_soyad");
        bool udf = s.S("sablon_tipi") == "udf";
        var opts = new List<(object?, string)> { (null, "— seçilmedi —") };
        opts.AddRange(ms.Select(m => ((object?)m.L("id"), $"{m.S("ad_soyad")} ({m.S("tc_no")})")));
        var fields = new List<Field> { new("muvekkil_id", "Müvekkil", "combo", opts) };
        var keys = Documents.Placeholders(s.S("taslak_metin"));
        var ozel = new[] { "ad_soyad", "tc_no", "adres", "telefon", "eposta", "tarih" };
        var manual = keys.Where(k => !ozel.Contains(k)).ToList();
        foreach (var k in manual)
        {
            var lab = k.Replace("_", " ");
            lab = lab.Length > 0 ? char.ToUpper(lab[0], Utils.Tr) + lab[1..] : lab;
            fields.Add(new Field(k, lab, k == "toplam_ucret" ? "money" : "text"));
        }
        if (udf) fields.Add(new Field("imza", "UYAP Toplu İmza klasörüne de kopyala", "check", def: true));
        var v = FormDialog.Ask(this, $"Evrak Üret — {s.S("sablon_adi")}", fields);
        if (v == null) return;
        var mid = v.TryGetValue("muvekkil_id", out var x) && x != null ? Convert.ToInt64(x) : -1L;
        var m0 = ms.FirstOrDefault(m => m.L("id") == mid);
        var vals = new Dictionary<string, string>();
        foreach (var k in new[] { "ad_soyad", "tc_no", "adres", "telefon", "eposta" }) vals[k] = m0?.S(k) ?? "";
        foreach (var k in manual)
            if (v.TryGetValue(k, out var val))
                vals[k] = k == "toplam_ucret" ? Utils.FmtMoney(Convert.ToDouble(val)) : Convert.ToString(val) ?? "";
        bool imza = v.TryGetValue("imza", out var im) && im is int n && n != 0;
        try
        {
            var outp = Documents.Uret(s, vals, Cfg.Subdir("Dosyalar", "Evraklar"), $"{s.S("sablon_adi")}_{vals.GetValueOrDefault("ad_soyad", "")}", imza);
            Ui.Info(this, $"Evrak hazır:\n{outp}" + (imza ? "\n\nUYAP_Toplu_Imza_Bekleyenler klasörüne de kopyalandı." : ""));
            Ui.OpenExternal(outp);
        }
        catch (Exception e) { Ui.Warn(this, $"Evrak üretilemedi:\n{e.Message}"); }
    }
}
