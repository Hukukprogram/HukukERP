using System.Globalization;
using System.Text;

namespace HukukErp.Services;

/// <summary>
/// İnfaz yatar hesaplama (5275 s. CGTİHK m.105/A, 107, 108, Geçici 6, 7571 s.K. Geçici 10/6, 7550 s.K.,
/// Açık Ceza İnfaz Kurumlarına Ayrılma Yönetmeliği m.5–6). Arayüzden bağımsız; Python infaz.py'nin birebir karşılığı.
/// </summary>
public class Cat
{
    public string Id = "";
    public string L = "";
    public int[] R = { 1, 2 };
    public bool G6, G10, P14, P16, Child, Teror, Yg, Cinsel, Taksir;
    public int[]? Mk;
    public int Ak; // 0 = yok
}

public class Ilam
{
    public string Tur = "sureli";   // sureli | mueb | agir
    public DateTime? Sd;            // suç tarihi
    public string Tk = "0";         // tekerrür 0/1/2
    public int Y, M, D;
    public string CatId = "genel";
    // hesap sırasında doldurulur
    public Cat C = null!;
    public int[] Rate = { 1, 2 };
    public List<string> Why = new();
    public int KsYears;
}

public class Girdi
{
    public DateTime? Start;
    public int Mahsup;
    public DateTime? Birth;
    public string Ozel = "yok";       // yok | kadin06 | yas70 | agir
    public bool Ihlal;
    public string AcikMod = "auto";   // auto | dogrudan | kapali
    public List<Ilam> Ilamlar = new();
    public DateTime Bugun = DateTime.Today;
}

public class Kart
{
    public string Cls = "", K = "", D = "", S = "", Pill = "";
}

public class ZamanInfo
{
    public DateTime Start, End, KapaliEnd, Ds, Ks, Bugun;
    public DateTime? Acik;
}

public class Sonuc
{
    public bool Eksik;
    public List<Kart> Kartlar = new();
    public List<object[]> Tablo = new();
    public List<string> Adimlar = new();
    public List<string> Uyarilar = new();
    public string Ozet = "";
    public ZamanInfo? Zaman;
}

public static class Infaz
{
    static Cat C(string id, string l, int a, int b, bool g6 = false, bool g10 = false, bool p14 = false, bool p16 = false,
                 bool child = false, int[]? mk = null, bool teror = false, int ak = 0, bool yg = false, bool cinsel = false, bool taksir = false)
        => new() { Id = id, L = l, R = new[] { a, b }, G6 = g6, G10 = g10, P14 = p14, P16 = p16, Child = child, Mk = mk, Teror = teror, Ak = ak, Yg = yg, Cinsel = cinsel, Taksir = taksir };

    public static readonly List<Cat> Cats = new()
    {
        C("genel", "Genel kural – diğer kasıtlı suçlar (basit hırsızlık, dolandırıcılık, basit yaralama, tehdit vb.)", 1, 2),
        C("taksir", "Taksirli suçlar (taksirle öldürme, taksirle yaralama vb.)", 1, 2, taksir: true),
        C("deprem", "Depremde binanın yıkılması veya hasar görmesi sonucu öldürme", 1, 2, taksir: true, g10: true),
        C("h142", "Nitelikli hırsızlık (TCK 142)", 1, 2, ak: 5),
        C("yagma", "Yağma ve nitelikli yağma (TCK 148, 149)", 1, 2, ak: 5),
        C("aile", "Kasten yaralama – üstsoy, altsoy, kardeş veya kendini savunamayana karşı", 1, 2, g6: true),
        C("es86", "Kasten yaralama – eşe karşı (TCK 86/3-a)", 1, 2, g6: true, ak: 3),
        C("oldurme", "Kasten öldürme (TCK 81, 82/1 diğer bentler) – teşebbüs, olası kast, haksız tahrik dahil", 2, 3, g6: true),
        C("o82es", "Kasten öldürme – eşe karşı (TCK 82/1-d)", 2, 3, g6: true, g10: true, ak: 3),
        C("o82def", "Kasten öldürme – üstsoy, altsoy, kardeş, çocuk, savunamayan veya gebe kadına karşı (TCK 82/1-d, e, f)", 2, 3, g6: true, g10: true),
        C("ihmal83", "Kasten öldürmenin ihmali davranışla işlenmesi (TCK 83)", 2, 3, g6: true, p16: true),
        C("y87", "Neticesi sebebiyle ağırlaşmış yaralama – yüzde sabit iz (TCK 87/2-d)", 2, 3, g6: true, p16: true),
        C("iskence", "İşkence ve eziyet (TCK 94, 95, 96)", 2, 3, g6: true, p16: true),
        C("es96", "Eziyet – eşe karşı (TCK 96/2-b)", 2, 3, g6: true, p16: true, ak: 3),
        C("c102_1", "Basit cinsel saldırı (TCK 102/1)", 2, 3, g6: true, g10: true, child: true, cinsel: true, ak: 3),
        C("c102_2", "Nitelikli cinsel saldırı (TCK 102/2)", 3, 4, g6: true, g10: true, p14: true, child: true, cinsel: true, ak: 3),
        C("c103", "Çocuğun cinsel istismarı (TCK 103)", 3, 4, g6: true, g10: true, p14: true, child: true, cinsel: true, ak: 3),
        C("c104_1", "Reşit olmayanla cinsel ilişki (TCK 104/1)", 2, 3, g6: true, child: true, cinsel: true),
        C("c104_2", "Reşit olmayanla cinsel ilişki – nitelikli (TCK 104/2-3)", 3, 4, g6: true, g10: true, p14: true, child: true, cinsel: true),
        C("c105", "Cinsel taciz (TCK 105)", 2, 3, g6: true, child: true, cinsel: true),
        C("ozel", "Özel hayata ve hayatın gizli alanına karşı suçlar (TCK 132–138)", 2, 3, g6: true),
        C("u188", "Uyuşturucu veya uyarıcı madde imal ve ticareti (TCK 188)", 3, 4, g6: true, p14: true, child: true, ak: 5),
        C("u190", "Uyuşturucu kullanılmasını kolaylaştırma (TCK 190)", 1, 2, ak: 5),
        C("orgut", "Suç örgütü kurma, yönetme, üyelik ve örgüt faaliyeti çerçevesinde suçlar (TCK 220, yardım hariç)", 2, 3, g10: true, child: true, mk: new[] { 36, 30 }, yg: true),
        C("orgut188", "Örgüt faaliyeti çerçevesinde uyuşturucu ticareti veya 102/2, 103, 104/2-3 (m.108/9)", 3, 4, g6: true, g10: true, child: true, mk: new[] { 36, 30 }, yg: true, cinsel: true),
        C("devlet", "Millete ve devlete karşı suçlar (TCK 302–325)", 1, 2, g6: true),
        C("sir", "Devlet sırlarına karşı suçlar ve casusluk (TCK 326–339)", 2, 3, g6: true),
        C("mit", "Devlet İstihbarat Hizmetleri ve MİT Kanunu kapsamındaki suçlar", 2, 3, p16: true),
        C("teror", "Terör suçları (3713 sayılı Kanun kapsamı)", 3, 4, g6: true, g10: true, child: true, mk: new[] { 36, 30 }, teror: true, yg: true),
    };
    public static readonly Dictionary<string, Cat> CatMap = Cats.ToDictionary(c => c.Id);

    static readonly DateTime D2014 = new(2014, 6, 28), D2016 = new(2016, 7, 1), D2020 = new(2020, 3, 30), D2023 = new(2023, 7, 31), D2025 = new(2025, 6, 4);

    // ---------------------------------------------------------------- tarih yardımcıları
    public static string Fmt(DateTime d) => $"{d.Day:00}.{d.Month:00}.{d.Year}";
    public static DateTime AddDays(DateTime d, int n) => d.AddDays(n);
    public static DateTime AddYears(DateTime d, int n)
    {
        int y = d.Year + n;
        return new DateTime(y, d.Month, Math.Min(d.Day, DateTime.DaysInMonth(y, d.Month)));
    }
    public static int Diff(DateTime a, DateTime b) => (int)(b.Date - a.Date).TotalDays;

    public static string Ymd(DateTime a, DateTime b)
    {
        if (b < a) return "0 gün";
        int y = b.Year - a.Year, m = b.Month - a.Month, d = b.Day - a.Day;
        if (d < 0)
        {
            m -= 1;
            int pm = b.Month > 1 ? b.Month - 1 : 12, py = b.Month > 1 ? b.Year : b.Year - 1;
            d += DateTime.DaysInMonth(py, pm);
        }
        if (m < 0) { y -= 1; m += 12; }
        var o = new List<string>();
        if (y != 0) o.Add($"{y} yıl");
        if (m != 0) o.Add($"{m} ay");
        if (d != 0 || o.Count == 0) o.Add($"{d} gün");
        return string.Join(" ", o);
    }

    public static string GunTxt(int n) => n.ToString("N0", CultureInfo.InvariantCulture).Replace(",", ".") + " gün";
    public static string RTxt(int[] r) => $"{r[0]}/{r[1]}";

    public static int? AgeAt(DateTime? birth, DateTime d)
    {
        if (birth == null) return null;
        int a = d.Year - birth.Value.Year;
        if ((d.Month, d.Day).CompareTo((birth.Value.Month, birth.Value.Day)) < 0) a -= 1;
        return a;
    }

    public static (int[] r, List<string> why) RateFor(Cat c, DateTime sd, bool childAtCrime, string tk)
    {
        var r = (int[])c.R.Clone();
        var why = new List<string>();
        if (c.P14 && sd < D2014) { r = new[] { 2, 3 }; why.Add("28.06.2014 öncesi suç: 2/3"); }
        if (c.P16 && sd < D2016) { r = new[] { 1, 2 }; why.Add("01.07.2016 öncesi suç: 1/2"); }
        if (childAtCrime && c.Child && (double)r[0] / r[1] > 2.0 / 3) { r = new[] { 2, 3 }; why.Add("suç tarihinde çocuk: 2/3"); }
        if (tk == "1" && (double)r[0] / r[1] < 2.0 / 3) { r = new[] { 2, 3 }; why.Add("mükerrir: 2/3"); }
        if (tk == "2" && (double)r[0] / r[1] < 3.0 / 4) { r = new[] { 3, 4 }; why.Add("ikinci kez mükerrir: 3/4"); }
        return (r, why);
    }

    public static Sonuc Hesapla(Girdi g)
    {
        var warn = new List<string>();
        var steps = new List<string>();
        var start0 = g.Start;
        int mahsup = Math.Max(0, g.Mahsup);
        var birth = g.Birth;
        var ozel = g.Ozel;
        var ihlal = g.Ihlal;
        var rows = g.Ilamlar;
        if (start0 == null || rows.Count == 0 || rows.Any(r => r.Sd == null)) return new Sonuc { Eksik = true };
        var start = start0.Value;
        foreach (var r in rows) r.C = CatMap[r.CatId];
        var mueb = rows.Where(r => r.Tur != "sureli").ToList();
        if (mueb.Count > 1 || (mueb.Count > 0 && rows.Count > 1))
            warn.Add("Müebbet ile başka cezaların içtimaında m.107/3 azami süreleri uygulanır; bu durumda yalnızca ilk müebbet ilam hesaba katıldı, müddetnameyi kontrol edin.");
        var use = mueb.Count > 0 ? new List<Ilam> { mueb[0] } : rows.ToList();
        bool isMueb = mueb.Count > 0;

        int totalDays = 0, ksDays = 0;
        var table = new List<object[]>();
        var cur = start;
        for (int idx = 0; idx < use.Count; idx++)
        {
            var r = use[idx];
            var sd = r.Sd!.Value;
            bool childAt = birth != null && (AgeAt(birth, sd) ?? 0) < 18;
            if (birth != null && (AgeAt(birth, sd) ?? 0) < 12)
                warn.Add("Suç tarihinde 12 yaşından küçük kişinin ceza sorumluluğu yoktur (TCK 31/1).");
            var (rr, why) = RateFor(r.C, sd, childAt, r.Tk);
            r.Rate = rr; r.Why = why;
            if (isMueb)
            {
                var mk = r.C.Mk ?? new[] { 30, 24 };
                int yrs = r.Tur == "agir" ? mk[0] : mk[1];
                if (r.Tk != "0" && r.C.Mk == null) yrs = r.Tur == "agir" ? 39 : 33;
                r.KsYears = yrs;
                table.Add(new object[] { idx + 1, r.C.L, Fmt(sd), r.Tur == "agir" ? "Ağırlaştırılmış müebbet" : "Müebbet", "—", $"{yrs} yıl",
                    (r.Tk != "0" && r.C.Mk == null) ? $"tekerrür: {yrs} yıl" : "m.107/2" + (r.C.Mk != null ? "-4" : "") });
            }
            else
            {
                var end = AddDays(AddYears(cur, r.Y), r.M * 30 + r.D);
                int days = Diff(cur, end);
                cur = end;
                int k = (int)Math.Ceiling((double)days * rr[0] / rr[1]);
                totalDays += days;
                ksDays += k;
                var parts = new List<string>();
                if (r.Y != 0) parts.Add($"{r.Y} yıl");
                if (r.M != 0) parts.Add($"{r.M} ay");
                if (r.D != 0) parts.Add($"{r.D} gün");
                var sure = parts.Count > 0 ? string.Join(" ", parts) : "0";
                table.Add(new object[] { idx + 1, r.C.L, Fmt(sd), sure, GunTxt(days), $"{RTxt(rr)} → {GunTxt(k)}", why.Count > 0 ? string.Join("; ", why) : "temel oran" });
            }
        }

        DateTime? endDate = null;
        DateTime ksDate;
        int serveNeed;
        if (isMueb)
        {
            var r0 = use[0];
            ksDate = AddDays(AddYears(start, r0.KsYears), -mahsup);
            serveNeed = Diff(start, ksDate) + mahsup;
            steps.Add($"Koşullu salıverilme için kurumda geçirilecek süre {r0.KsYears} yıl; mahsup ({mahsup} gün) düşülerek {Fmt(ksDate)} bulunur. Müebbette denetim süresi 10 yıldır (m.107/6).");
        }
        else
        {
            endDate = AddDays(start, totalDays - mahsup);
            serveNeed = ksDays;
            int need = ksDays - mahsup;
            bool allPre = use.All(r => r.Sd!.Value < D2020);
            if (birth != null && (AgeAt(birth, start) ?? 0) < 18 && need > 0)
            {
                int acc = 0, n = 0;
                var d = start;
                while (acc < need && n < 20000)
                {
                    int a = AgeAt(birth, d) ?? 0;
                    int mult = allPre ? (a < 15 ? 3 : a < 18 ? 2 : 1) : (a < 15 ? 2 : 1);
                    acc += mult;
                    d = AddDays(d, 1);
                    n++;
                }
                ksDate = d;
                steps.Add("Çocuk hükümlü: kurumda " + (allPre ? "15 yaşına kadar 1 gün 3, 18 yaşına kadar 2 gün (Geçici 6)" : "15 yaşına kadar 1 gün 2 gün (m.107/5)") + " sayıldı.");
            }
            else ksDate = AddDays(start, Math.Max(0, need));
            steps.Add($"Toplam ceza {GunTxt(totalDays)}; koşullu salıverilme için kurumda geçirilecek süre {GunTxt(ksDays)}. Mahsup {mahsup} gün düşüldü.");
            steps.Add($"Hak ederek tahliye: {Fmt(start)} + {GunTxt(totalDays)} − {mahsup} gün mahsup = {Fmt(endDate.Value)}.");
        }

        // açık kuruma ayrılma
        var V = AddDays(start, -mahsup);
        bool anyTeror = use.Any(r => r.C.Teror);
        bool anyYg = use.Any(r => r.C.Yg);
        bool agirMueb = isMueb && use[0].Tur == "agir";
        bool anyCinsel = use.Any(r => r.C.Cinsel);
        bool ikinci = use.Any(r => r.Tk == "2");
        bool allTaksir = use.All(r => r.C.Taksir);
        bool buyuk = isMueb || totalDays >= Diff(start, AddYears(start, 10));
        int cezaGun = isMueb ? Diff(start, AddYears(start, use[0].KsYears)) : totalDays;
        bool dogrudanOk = !isMueb && !anyYg && !anyCinsel && !ikinci &&
            (totalDays <= Diff(start, AddYears(start, 3)) || (allTaksir && totalDays <= Diff(start, AddYears(start, 5))));
        DateTime? anorm = null;
        bool dogrudan = false;
        string acikWhy;
        if (g.AcikMod == "kapali")
            acikWhy = "Açık kuruma ayrılamaz olarak işaretlendi (ör. koşullu salıverilmesi geri alınan, birden fazla firar).";
        else if (g.AcikMod == "dogrudan" || (g.AcikMod == "auto" && dogrudanOk))
        {
            anorm = start; dogrudan = true;
            acikWhy = g.AcikMod == "dogrudan" ? "Doğrudan açık kurumda infaz olarak işaretlendi." :
                "Doğrudan açık (Yönetmelik m.5): " + ((allTaksir && totalDays > Diff(start, AddYears(start, 3))) ? "taksirli suçtan toplam 5 yıl veya daha az" : "kasıtlı suçtan toplam 3 yıl veya daha az") + " ceza.";
        }
        else
        {
            int kmin = buyuk ? (int)Math.Ceiling(cezaGun / 10.0) : 30, thr = 7;
            acikWhy = "Yönetmelik m.6/1-a: kapalıda " + (buyuk ? $"cezanın 1/10’u ({kmin} gün)" : "1 ay") + " ve koşullu salıverilmeye 7 yıl kala";
            if (isMueb && !agirMueb)
            {
                kmin = 30; thr = 5;
                acikWhy = "Yönetmelik m.6/1-b: müebbette koşullu salıverilmeye 5 yıl kala";
            }
            foreach (var r in use)
            {
                int ak = r.C.Ak;
                if (ak != 0 && ak < thr)
                {
                    thr = ak;
                    acikWhy = $"Yönetmelik m.6/2: {r.C.L.Split(" (")[0]} – kapalıda " + (buyuk ? "cezanın 1/10’u" : "1 ay") + $" ve koşullu salıverilmeye {ak} yıl kala";
                }
            }
            if (anyYg || agirMueb)
            {
                kmin = (int)Math.Ceiling(cezaGun / 3.0);
                if (anyYg)
                {
                    thr = 1;
                    acikWhy = $"Yönetmelik m.6/1-c ve 6/2-ç: yüksek güvenlikli kurumda toplam cezanın 1/3’ü ({kmin} gün) ve koşullu salıverilmeye 1 yıldan az kala (örgütten ayrıldığının tespiti şartıyla)";
                }
                else
                {
                    thr = Math.Min(thr, 3);
                    acikWhy = "Yönetmelik m.6/1-c: yüksek güvenlikli kurumda toplam cezanın 1/3’ü ve koşullu salıverilmeye 3 yıl kala";
                }
            }
            var a1 = AddDays(V, kmin);
            var a2 = AddYears(ksDate, -thr);
            anorm = a1 > a2 ? a1 : a2;
        }

        // denetimli serbestlik
        double baseYears = double.PositiveInfinity;
        string baseWhy = "";
        bool plus3 = !ihlal;
        bool need7550 = false;
        foreach (var r in use)
        {
            var sd = r.Sd!.Value;
            int b; string w;
            if (r.C.Teror) { b = 1; w = "terör suçu: koşullu salıverilmeye 1 yıl kala"; }
            else if (sd < D2020 && !r.C.G6)
            {
                if (ozel == "agir") { b = 99; w = "Geçici 6: ağır hasta/yaşlı, süre sınırı yok"; }
                else if (ozel == "kadin06" || ozel == "yas70") { b = 4; w = "Geçici 6: 4 yıl"; }
                else { b = 3; w = "Geçici 6 (30.03.2020 öncesi suç): 3 yıl"; }
            }
            else
            {
                if (ozel == "agir") { b = 3; w = "m.105/A: ağır hasta/engelli 3 yıl"; }
                else if (ozel == "kadin06") { b = 2; w = "m.105/A: 0–6 yaş çocuklu kadın 2 yıl"; }
                else { b = 1; w = "m.105/A: 1 yıl"; }
            }
            if (b < baseYears) { baseYears = b; baseWhy = w; }
            if (sd > D2023 || r.C.G10) plus3 = false;
            if (sd >= D2025) need7550 = true;
        }
        bool limitless = baseYears == 99;
        if (use.Count > 1 && use.Any(r => r.Sd!.Value > D2023) && use.Any(r => r.Sd!.Value <= D2023))
            steps.Add("31.07.2023 öncesi ve sonrası işlenmiş suçlar birlikte infaz edildiğinden Geçici 10/6 uygulanmadı.");
        DateTime? A = anorm;
        bool erken10 = false;
        if (plus3 && anorm != null && !dogrudan)
        {
            var e1 = AddDays(V, buyuk ? 90 : 30);
            var e2 = AddYears(anorm.Value, -3);
            A = e1 > e2 ? e1 : e2;
            if (A < anorm) erken10 = true;
        }
        if (A != null && A < start) A = start;
        if (A != null && A >= ksDate) A = null;
        string minKurum = "";
        bool needAcik = !limitless && use.Any(r => r.Sd!.Value >= D2020);
        DateTime dsDate;
        if (limitless) dsDate = start;
        else
        {
            var baseDate = AddYears(ksDate, -(int)baseYears);
            dsDate = baseDate;
            if (plus3 && A == null)
            {
                plus3 = false;
                steps.Add("Açık kuruma ayrılamadığı için Geçici 10/6’daki ek 3 yıl denetimli serbestlik kullanılamaz (3 ay açıkta kalma şartı).");
            }
            if (plus3)
            {
                var c3 = AddYears(baseDate, -3);
                var e3 = AddDays(A!.Value, 90);
                var cand = c3 < e3 ? e3 : c3;
                if (cand < dsDate) dsDate = cand;
                minKurum = dogrudan ? "3 ay açık" : (buyuk ? "3 ay kapalı + 3 ay açık" : "1 ay kapalı + 3 ay açık");
            }
            if (needAcik)
            {
                if (A != null && dsDate < A)
                {
                    dsDate = A.Value;
                    steps.Add($"30.03.2020 sonrası suç: denetimli serbestlik, açığa ayrılmaya hak kazanılan {Fmt(A.Value)} tarihinden önce başlayamaz.");
                }
                else if (A == null)
                {
                    dsDate = ksDate;
                    warn.Add("30.03.2020 sonrası suçlarda denetimli serbestlik için açık kurumda bulunma veya açığa ayrılmaya hak kazanma şartı aranır; açığa ayrılma tarihi koşullu salıverilmeden önce gelmediği için denetimli serbestlik hesaplanmadı.");
                }
            }
            if (need7550)
            {
                int min5 = Math.Max(5, (int)Math.Ceiling(serveNeed / 10.0));
                var e4 = AddDays(V, min5);
                if (dsDate < e4) dsDate = e4;
                steps.Add($"7550 sayılı Kanun: 04.06.2025 sonrası suç, denetimli serbestlik için koşullu salıverilme süresinin 1/10’u ({min5} gün) kurumda geçirilmeli.");
            }
            if (dsDate < start) dsDate = start;
        }
        if (dsDate > ksDate) dsDate = ksDate;
        var dsTotalTxt = limitless ? "süre sınırı yok" : $"{(int)baseYears}{(plus3 ? " + 3" : "")} yıl";
        steps.Insert(0, $"Denetimli serbestlik: {baseWhy}" + (plus3 ? $"; 7571 sayılı Kanunla değişik Geçici 10/6 gereği +3 yıl (asgari {minKurum})" : "") + ".");
        steps.Insert(0, $"Açık kuruma ayrılma: {acikWhy}" + (erken10 ? $" Geçici 10/6 gereği 3 yıl erken ({(buyuk ? "en az 3 ay" : "en az 1 ay")} kapalı)." : "") +
                     (A != null ? $" Ayrılma tarihi: {Fmt(A.Value)}." : " Koşullu salıverilmeden önce açığa ayrılma tarihi oluşmuyor."));
        if (anyTeror) steps.Add("Terör suçlarında Geçici 10/6 iyileştirmesi ve süre uzatmaları uygulanmaz.");
        if (!dogrudan && g.AcikMod == "auto" && !isMueb && totalDays <= Diff(start, AddYears(start, 3)) && (anyCinsel || ikinci || anyYg))
            steps.Add("Cinsel suç, terör/örgüt suçu veya ikinci kez tekerrür nedeniyle doğrudan açığa ayrılamaz (Yönetmelik m.5).");

        // dönemler
        bool acikGirdi = A != null && A < dsDate;
        var kapaliEnd = acikGirdi ? A!.Value : dsDate;
        int kapaliDays = Math.Max(0, Diff(start, kapaliEnd));
        int acikDays = acikGirdi ? Diff(A!.Value, dsDate) : 0;
        int netDays = kapaliDays + acikDays;
        int yatarDays = netDays + mahsup;
        if (!acikGirdi && A != null && A >= dsDate && dsDate > start && !needAcik)
            steps.Add("Denetimli serbestlik tarihi açığa ayrılma tarihinden önce geldiği için (30.03.2020 öncesi suç) hükümlü doğrudan kapalı kurumdan denetimli serbestliğe ayrılır.");
        (string, string)? gc = null;
        if (endDate != null && Diff(start, endDate.Value) <= 0)
            gc = ("Cezası mahsupla infaz edilmiş", "Teslim üzerine girdi-çıktı ile hak ederek tahliye");
        else if (Diff(start, ksDate) <= 0)
            gc = ("Yatarı yok", "Açık cezaevine girdi-çıktı · koşullu salıverilme");
        else if (netDays == 0)
            gc = ("Yatarı yok", "Açık cezaevine girdi-çıktı · denetimli serbestlik");
        if (gc != null)
        {
            steps.Add("Kurumda geçirilecek süre kalmadığından hükümlü teslim olduğu gün açık ceza infaz kurumuna alınır ve aynı gün " +
                      (gc.Value.Item2.Contains("denetimli") ? "infaz hâkimliği kararıyla denetimli serbestliğe" : gc.Value.Item2.Contains("koşullu") ? "koşullu salıverilme ile" : "hak ederek") + " ayrılır (girdi-çıktı).");
            if (anyYg)
                warn.Add("Terör ve örgüt suçu hükümlüleri açık kuruma doğrudan ayrılamaz (Yönetmelik m.6/1-c, 6/2-ç). Girdi-çıktı sonucunu bu suçlarda ayrıca kontrol edin.");
        }

        // kartlar
        var today = g.Bugun;
        string Status(DateTime d) { int n = Diff(today, d); return n <= 0 ? "Tarih geçti" : $"{GunTxt(n)} kaldı"; }
        string Dur(int n) => Ymd(start, AddDays(start, n));

        var k2 = new List<Kart>();
        if (gc != null)
            k2.Add(new Kart { Cls = "yatar", K = "Toplam yatar", D = gc.Value.Item1, S = gc.Value.Item2 + (mahsup != 0 ? $" · mahsup {GunTxt(mahsup)}" : ""), Pill = "Teslimde girdi-çıktı" });
        else
            k2.Add(new Kart { Cls = "yatar", K = "Toplam yatar (teslimden sonra)", D = Dur(netDays),
                S = GunTxt(netDays) + (mahsup != 0 ? $" · mahsupla birlikte {Dur(yatarDays)}" : ""), Pill = "" });
        k2.Add(new Kart
        {
            Cls = "kapali", K = "Kapalı cezaevi", D = kapaliDays != 0 ? Dur(kapaliDays) : "Kalmaz",
            S = (kapaliDays != 0 ? $"{Fmt(start)} – {Fmt(kapaliEnd)}" : ((dogrudan || (A != null && A <= start)) ? "Teslimde doğrudan açığa" : "—")) + (mahsup != 0 ? $" · ayrıca mahsup {GunTxt(mahsup)}" : ""),
            Pill = ""
        });
        k2.Add(new Kart
        {
            Cls = "acik", K = "Açık cezaevi",
            D = acikGirdi ? (acikDays != 0 ? Dur(acikDays) : "Girdi-çıktı") : (A != null ? "Açığa geçmez" : "Ayrılamaz"),
            S = acikGirdi ? $"{Fmt(A!.Value)} – {Fmt(dsDate)}" : (A != null ? "Kapalıdan doğrudan denetimli serbestliğe" : "Koşullu salıverilmeye kadar kapalıda"),
            Pill = acikGirdi ? Status(A!.Value) : ""
        });
        k2.Add(new Kart { Cls = "ds", K = "Denetimli serbestlik", D = Fmt(dsDate), S = $"{dsTotalTxt} · {GunTxt(Diff(dsDate, ksDate))}", Pill = Status(dsDate) });
        k2.Add(new Kart { Cls = "ks", K = "Koşullu salıverilme", D = Fmt(ksDate), S = isMueb ? $"{use[0].KsYears} yıl" : $"kurumda {GunTxt(serveNeed)}", Pill = Status(ksDate) });
        k2.Add(new Kart
        {
            Cls = "end", K = "Hak ederek tahliye", D = endDate != null ? Fmt(endDate.Value) : "—",
            S = endDate != null ? $"toplam {Ymd(start, AddDays(start, totalDays))}" : "müebbette denetim süresi 10 yıl",
            Pill = endDate != null ? Status(endDate.Value) : ""
        });

        var tEnd = endDate ?? AddYears(ksDate, 10);
        var zaman = new ZamanInfo { Start = start, End = tEnd, KapaliEnd = kapaliEnd, Acik = acikGirdi ? A : null, Ds = dsDate, Ks = ksDate, Bugun = today };

        var lines = new List<string>
        {
            $"İNFAZ HESABI ({Fmt(today)})",
            $"İnfaz başlangıcı: {Fmt(start)}" + (mahsup != 0 ? $" | Mahsup: {mahsup} gün" : "")
        };
        for (int i = 0; i < use.Count; i++)
        {
            var r = use[i];
            var ceza = isMueb ? (r.Tur == "agir" ? "Ağırlaştırılmış müebbet" : "Müebbet") : $"{r.Y} yıl {r.M} ay {r.D} gün";
            var oran = isMueb ? $"{r.KsYears} yıl" : RTxt(r.Rate);
            lines.Add($"{i + 1}) {r.C.L} | Suç tarihi: {Fmt(r.Sd!.Value)} | {ceza} | Oran: {oran}");
        }
        lines.Add("Kapalı cezaevi: " + (kapaliDays != 0 ? $"{Fmt(start)} – {Fmt(kapaliEnd)} ({Dur(kapaliDays)})" : "kalmaz") + (mahsup != 0 ? $" + mahsup {mahsup} gün" : ""));
        lines.Add("Açık cezaevi: " + (acikGirdi ? $"{Fmt(A!.Value)} – {Fmt(dsDate)} ({(acikDays != 0 ? Dur(acikDays) : "girdi-çıktı")})" : (A != null ? "açığa geçmeden denetimli serbestlik" : "açığa ayrılamaz")));
        lines.Add($"Denetimli serbestlik: {Fmt(dsDate)} – {Fmt(ksDate)} ({dsTotalTxt})");
        lines.Add($"Koşullu salıverilme tarihi: {Fmt(ksDate)}");
        if (endDate != null) lines.Add($"Hak ederek tahliye tarihi: {Fmt(endDate.Value)}");
        if (gc != null) lines.Add($"Fiilen yatar: {gc.Value.Item1} – {gc.Value.Item2} (teslim tarihinde açık ceza infaz kurumuna girdi-çıktı)");
        else lines.Add($"Toplam yatar: teslimden sonra {Dur(netDays)} ({netDays} gün)" + (mahsup != 0 ? $"; mahsup dahil {yatarDays} gün" : ""));
        lines.Add("Dayanak: 5275 s.K. m.105/A, 107, 108, Geçici 6, 7571 s.K. ile değişik Geçici 10/6 ve Açık Ceza İnfaz Kurumlarına Ayrılma Yönetmeliği m.5–6. Kesin tarih müddetnameye göredir.");

        return new Sonuc { Kartlar = k2, Tablo = table, Adimlar = steps, Uyarilar = warn, Ozet = string.Join("\n", lines), Zaman = zaman };
    }
}
