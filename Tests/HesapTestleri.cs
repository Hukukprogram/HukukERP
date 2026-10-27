using System.IO.Compression;
using HukukErp.Services;
using Xunit;

namespace HukukErp.Tests;

public class BordroTestleri
{
    [Fact]
    public void Ocak_AsgariUcret_NetYirmiSekizBinYetmisBesBuyukBeslik()
    {
        // 33.030 brüt: SGK %14 + işsizlik %1 = 4.954,50; asgari ücret istisnası GV ve damgayı sıfırlar.
        var ay = Bordro.BrutAy(1, 33030.00, 0, true);
        Assert.Equal(28075.50, ay.Net, 2);
        Assert.Equal(0, ay.Gv, 2);
        Assert.Equal(0, ay.Damga, 2);
    }

    [Fact]
    public void NetAy_BrutuGeriBulur()
    {
        var ay = Bordro.NetAy(1, 28075.50, 0, true);
        Assert.Equal(33030.00, ay.Brut, 1);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(190_000, 28_500)]
    [InlineData(400_000, 70_500)]   // 28.500 + 210.000 x %20
    public void GelirVergisi_Dilimler(double matrah, double beklenen)
    {
        Assert.Equal(beklenen, Bordro.GelirVergisi(matrah), 2);
    }
}

public class SureTestleri
{
    static IsSureleri.SureTanim Tanim(int sure, string birim, string adliTatil = "YOK") => new()
    {
        Kod = "TEST", Sure = sure, Birim = birim, AdliTatil = adliTatil,
    };

    [Fact]
    public void ETebligat_BesGunSonraTebligEdilir_IkiHaftaSonraBiter()
    {
        // 23.10.2026 UETS'e ulaştı → 28.10.2026 tebliğ → +14 gün = 11.11.2026 (Çarşamba)
        var s = IsSureleri.Hesapla(Tanim(2, "hafta", "HMK"), new DateTime(2026, 10, 23), eTebligat: true);
        Assert.Equal(new DateTime(2026, 10, 28), s.Tebligat);
        Assert.Equal(new DateTime(2026, 11, 11), s.Son);
        Assert.False(s.AdliTatilUzatti);
    }

    [Fact]
    public void SonGunHaftaSonuysaIlkIsGunu()
    {
        // 24.10.2026 Cumartesi + 7 gün = 31.10.2026 Cumartesi → 02.11.2026 Pazartesi
        var s = IsSureleri.Hesapla(Tanim(7, "gun"), new DateTime(2026, 10, 24), eTebligat: false);
        Assert.Equal(new DateTime(2026, 11, 2), s.Son);
    }

    [Fact]
    public void AdliTatil_HMK_YediEylule_UzatirYOK_Uzatmaz()
    {
        var baslangic = new DateTime(2026, 7, 10);   // +2 hafta = 24.07.2026 (adli tatilde)
        var hmk = IsSureleri.Hesapla(Tanim(2, "hafta", "HMK"), baslangic, eTebligat: false);
        Assert.True(hmk.AdliTatilUzatti);
        Assert.Equal(new DateTime(2026, 9, 7), hmk.Son);

        var yok = IsSureleri.Hesapla(Tanim(2, "hafta", "YOK"), baslangic, eTebligat: false);
        Assert.False(yok.AdliTatilUzatti);
        Assert.Equal(new DateTime(2026, 7, 24), yok.Son);
    }

    [Fact]
    public void AySuresi_AyinSonGunu()
    {
        // 31.01.2026 + 1 ay → 28.02.2026 (Cumartesi) → 02.03.2026 Pazartesi
        var s = IsSureleri.Hesapla(Tanim(1, "ay"), new DateTime(2026, 1, 31), eTebligat: false);
        Assert.Equal(new DateTime(2026, 3, 2), s.Son);
    }

    [Theory]
    [InlineData("2026/411 E.", "2026/411")]
    [InlineData("E:2026 / 411", "2026/411")]
    [InlineData("karar yok", "")]
    public void EsasNumarasiNormallesir(string girdi, string beklenen)
    {
        Assert.Equal(beklenen, DosyaEsleme.EsasNorm(girdi));
    }
}

public class DosyaOkumaTestleri
{
    [Fact]
    public void Udf_ContentXmlIcindenMetinOkunur()
    {
        var yol = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.udf");
        try
        {
            using (var z = ZipFile.Open(yol, ZipArchiveMode.Create))
            {
                var e = z.CreateEntry("content.xml");
                using var w = new StreamWriter(e.Open());
                w.Write("<?xml version=\"1.0\"?><template><content><![CDATA[Merhaba\nDünya]]></content><properties/></template>");
            }
            Assert.Equal("Merhaba\nDünya", TebligatDosya.UdfMetni(yol));
        }
        finally { if (File.Exists(yol)) File.Delete(yol); }
    }

    [Fact]
    public void Csv_NoktaliVirgulVeTirnakOkunur()
    {
        var yol = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(yol, "Ad Soyad;T.C.;Adres\n\"Ali, Veli\";10000000146;\"Ankara \"\"Çankaya\"\"\"\n", new System.Text.UTF8Encoding(true));
            var s = TabloOkuyucu.Oku(yol);
            Assert.Equal(2, s.Count);
            Assert.Equal("Ali, Veli", s[1][0]);
            Assert.Equal("Ankara \"Çankaya\"", s[1][2]);
        }
        finally { if (File.Exists(yol)) File.Delete(yol); }
    }
}
