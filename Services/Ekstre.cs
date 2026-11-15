using System.Text;
using HukukErp.Core;

namespace HukukErp.Services;

/// <summary>Müvekkil cari ekstresi ve tahsilat hatırlatma metni.</summary>
public static class Ekstre
{
    public static string Metin(Db db, Row muvekkil, string buro)
    {
        var id = muvekkil.L("id");
        var sb = new StringBuilder();
        sb.AppendLine("CARİ HESAP EKSTRESİ");
        sb.AppendLine();
        sb.AppendLine($"Müvekkil: {muvekkil.S("ad_soyad")}   T.C. No: {muvekkil.S("tc_no")}");
        sb.AppendLine($"Düzenleme tarihi: {DateTime.Today:dd.MM.yyyy}" + (buro.Length > 0 ? $"   Büro: {buro}" : ""));
        sb.AppendLine();
        double T = 0, A = 0, K = 0;
        foreach (var f in db.Query("SELECT f.*, TRIM(COALESCE(d.mahkeme_adi,'') || ' ' || COALESCE(d.esas_no,'')) AS dosya FROM finans_tahsilat f LEFT JOIN davalar d ON d.id=f.dava_id WHERE f.muvekkil_id=? ORDER BY f.id", id))
        {
            T += f.D("toplam_ucret"); A += f.D("alinan_tutar"); K += f.D("kalan_bakiye");
            sb.AppendLine($"Dosya: {(f.S("dosya").Length > 0 ? f.S("dosya") : "genel cari")} – {f.S("dosya_konusu")}");
            sb.AppendLine($"Toplam ücret: {Utils.FmtMoney(f.D("toplam_ucret"))}   Alınan: {Utils.FmtMoney(f.D("alinan_tutar"))}   Kalan: {Utils.FmtMoney(f.D("kalan_bakiye"))}");
            foreach (var h in db.Query("SELECT * FROM tahsilat_hareketleri WHERE finans_id=? ORDER BY tarih, id", f.L("id")))
                sb.AppendLine($"   {Utils.FmtDate(h.S("tarih"))}  {Utils.FmtMoney(h.D("tutar"))}  {h.S("aciklama")}");
            sb.AppendLine();
        }
        sb.AppendLine($"GENEL TOPLAM – Ücret: {Utils.FmtMoney(T)}   Alınan: {Utils.FmtMoney(A)}   Kalan: {Utils.FmtMoney(K)}");
        var masraflar = db.Query("SELECT x.*, TRIM(d.mahkeme_adi || ' ' || d.esas_no) AS dosya FROM dava_masraflari x JOIN davalar d ON d.id=x.dava_id WHERE d.muvekkil_id=? ORDER BY x.tarih", id);
        if (masraflar.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Dosya masrafları:");
            foreach (var m in masraflar) sb.AppendLine($"   {Utils.FmtDate(m.S("tarih"))}  {m.S("dosya")} – {m.S("masraf_kalemi")}: {Utils.FmtMoney(m.D("tutar"))}");
            sb.AppendLine($"   Toplam masraf: {Utils.FmtMoney(masraflar.Sum(x => x.D("tutar")))}");
        }
        return sb.ToString();
    }

    /// <summary>Müvekkile gönderilecek nazik tahsilat hatırlatma yazısı (WhatsApp/SMS/e-posta için).</summary>
    public static string Hatirlatma(Row muvekkil, double kalan, string vadeIso, string dosyaKonusu, string buro)
    {
        var vade = Utils.FmtDate(vadeIso);
        var konu = dosyaKonusu.Length > 0 ? $"{dosyaKonusu} dosyanız kapsamındaki " : "";
        return $"Sayın {muvekkil.S("ad_soyad")},\n\n{konu}vekalet ücreti bakiyeniz {Utils.FmtMoney(kalan)} olup " +
               (vade.Length > 0 ? $"{vade} tarihinde ödenmesi planlanmıştır. " : "ödeme planı için sizinle görüşmek isteriz. ") +
               "Ödemeyi yaptığınızda ya da farklı bir tarih önermek isterseniz lütfen bilgi veriniz.\n\nSaygılarımla" + (buro.Length > 0 ? $"\n{buro}" : "");
    }
}
