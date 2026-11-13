using SQLite;

namespace UretimProMobil.Models;

// Web sürümüyle aynı iş mantığı; veriler telefondaki SQLite dosyasında (uretimpro.db3)

public static class Rol
{
    public const string Admin = "Admin", Yonetici = "Yonetici", Kalite = "Kalite", Operator = "Operator", DepoBakim = "DepoBakim";
    public static string Ad(string r) => r switch { Yonetici => "Yönetici", Operator => "Operatör", DepoBakim => "Depo / Bakım", _ => r };
}

public static class Durum
{
    public const string Calisiyor = "Çalışıyor", Bekliyor = "Bekliyor", Arizali = "Arızalı";
    public const string Uretimde = "Üretimde", Tamamlandi = "Tamamlandı", Bloke = "Bloke", Hurda = "Hurda";
    public const string Acik = "Açık", KararVerildi = "Karar verildi", Kapali = "Kapalı";
}

public static class DurusNedeni
{
    public static readonly string[] Tumu = { "Arıza", "Ayar / set-up", "Malzeme bekleme", "Takım değişimi", "Operatör yok", "Kalite onayı bekleme" };
}

public class Kullanici
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Unique] public string KullaniciAdi { get; set; } = "";
    public string AdSoyad { get; set; } = "";
    public string Rol { get; set; } = "";
    public string SifreOzeti { get; set; } = "";
}

public class Makine
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string Kod { get; set; } = "";
    public string Ad { get; set; } = "";
    public string Durum { get; set; } = Models.Durum.Bekliyor;
    public int IdealCevrimSn { get; set; } = 240;
}

public class Urun
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string Kod { get; set; } = "";
    public string Ad { get; set; } = "";
}

public class RotaAdimi
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int UrunId { get; set; }
    public int Sira { get; set; }
    public string Ad { get; set; } = "";
    public int MakineId { get; set; }
}

public class Karakteristik
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int UrunId { get; set; }
    public int AdimSira { get; set; }
    public string Ad { get; set; } = "";
    public string Birim { get; set; } = "mm";
    public double Nominal { get; set; }
    public double Alt { get; set; }
    public double Ust { get; set; }
}

public class Yetkinlik
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int KullaniciId { get; set; }
    public int MakineId { get; set; }
    public DateTime Bitis { get; set; }
}

public class Emir
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string EmirNo { get; set; } = "";
    public string LotNo { get; set; } = "";
    public int UrunId { get; set; }
    public int Miktar { get; set; }
    public string Durum { get; set; } = Models.Durum.Uretimde;
}

public class Parca
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public string SeriNo { get; set; } = "";
    public int EmirId { get; set; }
    public int UrunId { get; set; }
    public string Durum { get; set; } = Models.Durum.Uretimde;
    public int TamamlananAdim { get; set; }
}

public class Islem
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int ParcaId { get; set; }
    public int AdimSira { get; set; }
    public int MakineId { get; set; }
    public int KullaniciId { get; set; }
    [Indexed] public DateTime Zaman { get; set; }
}

public class Olcum
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int ParcaId { get; set; }
    public int KarakteristikId { get; set; }
    public double Deger { get; set; }
    public bool Uygun { get; set; }
    public int KullaniciId { get; set; }
    public DateTime Zaman { get; set; }
}

public class Ncr
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string NcrNo { get; set; } = "";
    public int EmirId { get; set; }
    public int ParcaId { get; set; }
    public string Tanim { get; set; } = "";
    public string Durum { get; set; } = Models.Durum.Acik;
    public string? Karar { get; set; }
    public string? FotoYolu { get; set; }
    public string Acan { get; set; } = "";
    public DateTime Zaman { get; set; }
}

public class Durus
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int MakineId { get; set; }
    public string Neden { get; set; } = "";
    public string? Aciklama { get; set; }
    public DateTime Baslangic { get; set; }
    public DateTime? Bitis { get; set; }
    public string Bildiren { get; set; } = "";
}

public class HammaddeLotu
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string LotNo { get; set; } = "";
    public string Malzeme { get; set; } = "";
    public double Kalan { get; set; }
}

public class Sayim
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string LotNo { get; set; } = "";
    public string Malzeme { get; set; } = "";
    public double Sistem { get; set; }
    public double Sayilan { get; set; }
    public string Kisi { get; set; } = "";
    public DateTime Zaman { get; set; }
    [Ignore] public double Fark => Math.Round(Sayilan - Sistem, 3);
}

// Çevrimdışı kuyruk: her kayıt merkeze aktarılmak üzere burada bekler (JSON olarak dışa aktarılır)
public class KuyrukKaydi
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string Tur { get; set; } = "";
    public string Ozet { get; set; } = "";
    public string Veri { get; set; } = "{}";
    public DateTime Zaman { get; set; }
    public bool Aktarildi { get; set; }
}

// ---------- Ekran modelleri ----------

public record OkutmaSonucu(string Tur, string Baslik, string Mesaj, Parca? Parca = null, List<Karakteristik>? Olculecek = null, string? AdimAdi = null, int Ilerleme = 0, int Toplam = 0, int? NcrId = null);
public record PanelOzet(int BugunIslem, int BenimIslem, int AcikNcr, int DuranMakine, int CalisanMakine, int ToplamMakine, int Bekleyen, double Oee);
public record GunDegeri(string Etiket, int Deger);
