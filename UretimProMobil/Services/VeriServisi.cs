using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SQLite;
using UretimProMobil.Models;

namespace UretimProMobil.Services;

// Veritabanı ve iş kuralları: rota sırası, yetkinlik, tolerans, otomatik NCR, duruş, sayım, çevrimdışı kuyruk
public class VeriServisi
{
    public const string DemoSifre = "Demo123!";
    private SQLiteAsyncConnection? _db;
    private readonly SemaphoreSlim _kilit = new(1, 1);

    public static string SifreOzeti(string kullaniciAdi, string sifre) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"UretimPro|{kullaniciAdi.ToLowerInvariant()}|{sifre}")));

    private async Task<SQLiteAsyncConnection> Db()
    {
        if (_db != null) return _db;
        await _kilit.WaitAsync();
        try
        {
            if (_db == null)
            {
                var b = new SQLiteAsyncConnection(Path.Combine(FileSystem.AppDataDirectory, "uretimpro.db3"));
                await b.CreateTablesAsync(CreateFlags.None, typeof(Kullanici), typeof(Makine), typeof(Urun), typeof(RotaAdimi), typeof(Karakteristik),
                    typeof(Yetkinlik), typeof(Emir), typeof(Parca), typeof(Islem), typeof(Olcum), typeof(Ncr), typeof(Durus),
                    typeof(HammaddeLotu), typeof(Sayim), typeof(KuyrukKaydi));
                if (await b.Table<Kullanici>().CountAsync() == 0) await OrnekVeri(b);
                _db = b;
            }
            return _db;
        }
        finally { _kilit.Release(); }
    }

    // ---------- Giriş ----------

    public async Task<Kullanici?> GirisAsync(string kullaniciAdi, string sifre)
    {
        var db = await Db();
        var kAdi = (kullaniciAdi ?? "").Trim().ToLowerInvariant();
        var k = await db.Table<Kullanici>().Where(x => x.KullaniciAdi == kAdi).FirstOrDefaultAsync();
        return k != null && k.SifreOzeti == SifreOzeti(kAdi, sifre ?? "") ? k : null;
    }

    // ---------- Okuma ----------

    public async Task<List<Makine>> MakinelerAsync() => await (await Db()).Table<Makine>().OrderBy(m => m.Kod).ToListAsync();
    public async Task<Makine?> MakineAsync(int id) => await (await Db()).Table<Makine>().Where(m => m.Id == id).FirstOrDefaultAsync();
    public async Task<List<Urun>> UrunlerAsync() => await (await Db()).Table<Urun>().ToListAsync();
    public async Task<List<Emir>> EmirlerAsync() => await (await Db()).Table<Emir>().ToListAsync();

    public async Task<HashSet<int>> YetkiliMakinelerAsync(Kullanici k)
    {
        var db = await Db();
        if (k.Rol == Rol.Admin) return (await MakinelerAsync()).Select(m => m.Id).ToHashSet();
        var bugun = DateTime.Today;
        return (await db.Table<Yetkinlik>().Where(y => y.KullaniciId == k.Id).ToListAsync())
            .Where(y => y.Bitis >= bugun).Select(y => y.MakineId).ToHashSet();
    }

    public async Task<List<(Makine Makine, DateTime Bitis)>> YetkinliklerAsync(int kullaniciId)
    {
        var db = await Db();
        var makineler = (await MakinelerAsync()).ToDictionary(m => m.Id);
        return (await db.Table<Yetkinlik>().Where(y => y.KullaniciId == kullaniciId).ToListAsync())
            .Where(y => makineler.ContainsKey(y.MakineId))
            .Select(y => (makineler[y.MakineId], y.Bitis)).OrderBy(x => x.Item1.Kod).ToList();
    }

    // Bu makinede sırası gelen parçalar (rota sırasına göre)
    public async Task<List<string>> SiradakiParcalarAsync(int makineId, int adet = 6)
    {
        var db = await Db();
        var adimlar = await db.Table<RotaAdimi>().ToListAsync();
        var emirler = (await db.Table<Emir>().Where(e => e.Durum == Durum.Uretimde).ToListAsync()).Select(e => e.Id).ToHashSet();
        var parcalar = await db.Table<Parca>().Where(p => p.Durum == Durum.Uretimde).ToListAsync();
        return parcalar.Where(p => emirler.Contains(p.EmirId))
            .Where(p => adimlar.Any(a => a.UrunId == p.UrunId && a.Sira == p.TamamlananAdim + 1 && a.MakineId == makineId))
            .OrderBy(p => p.SeriNo).Take(adet).Select(p => p.SeriNo).ToList();
    }

    // ---------- İstasyon: okutma ----------

    public async Task<OkutmaSonucu> OkutAsync(int makineId, string seriNo, Kullanici k)
    {
        var db = await Db();
        var kontrol = await KontrolAsync(db, makineId, seriNo, k);
        if (kontrol.Sonuc != null) return kontrol.Sonuc;
        var (parca, adim, adimlar) = (kontrol.Parca!, kontrol.Adim!, kontrol.Adimlar!);

        var olculecek = await db.Table<Karakteristik>().Where(x => x.UrunId == parca.UrunId && x.AdimSira == adim.Sira).ToListAsync();
        if (olculecek.Count > 0)
            return new OkutmaSonucu("olcum", "Ölçüm gerekli", $"{adim.Ad}: {olculecek.Count} karakteristik ölçülecek.", parca, olculecek, adim.Ad, adim.Sira, adimlar.Count);

        return await AdimiTamamlaAsync(db, parca, adim, adimlar, makineId, k);
    }

    public async Task<OkutmaSonucu> OlcumKaydetAsync(int makineId, string seriNo, Kullanici k, Dictionary<int, string> girdiler)
    {
        var db = await Db();
        var kontrol = await KontrolAsync(db, makineId, seriNo, k);
        if (kontrol.Sonuc != null) return kontrol.Sonuc;
        var (parca, adim, adimlar) = (kontrol.Parca!, kontrol.Adim!, kontrol.Adimlar!);

        var karlar = await db.Table<Karakteristik>().Where(x => x.UrunId == parca.UrunId && x.AdimSira == adim.Sira).ToListAsync();
        var simdi = DateTime.Now;
        var olcumler = new List<Olcum>();
        foreach (var kr in karlar)
        {
            if (!girdiler.TryGetValue(kr.Id, out var metin) || !SayiCoz(metin, out var deger))
                return new OkutmaSonucu("hata", "Eksik ölçüm", $"{kr.Ad} için geçerli bir değer girin.", parca);
            olcumler.Add(new Olcum { ParcaId = parca.Id, KarakteristikId = kr.Id, Deger = deger, Uygun = deger >= kr.Alt && deger <= kr.Ust, KullaniciId = k.Id, Zaman = simdi });
        }
        await db.InsertAllAsync(olcumler);

        var hatali = olcumler.Where(o => !o.Uygun).ToList();
        if (hatali.Count == 0)
            return await AdimiTamamlaAsync(db, parca, adim, adimlar, makineId, k);

        // Tolerans dışı: parça ve lot bloke, otomatik NCR
        var emir = await db.Table<Emir>().Where(e => e.Id == parca.EmirId).FirstAsync();
        parca.Durum = Durum.Bloke;
        emir.Durum = Durum.Bloke;
        await db.UpdateAsync(parca);
        await db.UpdateAsync(emir);
        var detay = string.Join(", ", hatali.Select(o =>
        {
            var kr = karlar.First(x => x.Id == o.KarakteristikId);
            return $"{kr.Ad} {o.Deger:0.###} {kr.Birim} (tolerans {kr.Alt:0.###}–{kr.Ust:0.###})";
        }));
        var ncr = new Ncr
        {
            NcrNo = await SiradakiNcrNoAsync(db), EmirId = emir.Id, ParcaId = parca.Id, Acan = k.AdSoyad, Zaman = simdi,
            Tanim = $"{parca.SeriNo} tolerans dışı: {detay}"
        };
        await db.InsertAsync(ncr);
        await KuyrugaEkleAsync("NCR", $"{ncr.NcrNo} · {parca.SeriNo} tolerans dışı", ncr);
        return new OkutmaSonucu("red", "Tolerans dışı · Lot bloke", $"{ncr.NcrNo} otomatik açıldı. {detay}", parca, null, adim.Ad, adim.Sira, adimlar.Count, ncr.Id);
    }

    private record Kontrol(OkutmaSonucu? Sonuc, Parca? Parca = null, RotaAdimi? Adim = null, List<RotaAdimi>? Adimlar = null);

    private async Task<Kontrol> KontrolAsync(SQLiteAsyncConnection db, int makineId, string seriNo, Kullanici k)
    {
        var no = (seriNo ?? "").Trim().ToUpperInvariant();
        var makine = await db.Table<Makine>().Where(m => m.Id == makineId).FirstOrDefaultAsync();
        if (makine == null) return new(new("hata", "Makine bulunamadı", "İstasyonu yeniden seçin."));
        if (makine.Durum == Durum.Arizali) return new(new("hata", $"{makine.Kod} arızalı", "Arızalı makinede üretim yapılamaz. Bakım ekibine haber verin."));

        var parca = await db.Table<Parca>().Where(p => p.SeriNo == no).FirstOrDefaultAsync();
        if (parca == null) return new(new("hata", "Seri no bulunamadı", $"\"{no}\" sistemde kayıtlı değil."));
        if (parca.Durum == Durum.Tamamlandi) return new(new("hata", "Parça zaten tamamlandı", "Bu parça tüm rotayı bitirdi.", parca));
        if (parca.Durum == Durum.Hurda) return new(new("red", "Hurda parça", "Bu parça hurdaya ayrıldı, işlem yapılamaz.", parca));

        var emir = await db.Table<Emir>().Where(e => e.Id == parca.EmirId).FirstAsync();
        if (emir.Durum == Durum.Bloke || parca.Durum == Durum.Bloke)
            return new(new("red", "Lot bloke", $"Lot {emir.LotNo} açık NCR nedeniyle bloke. Kalite kararı bekleniyor.", parca));

        var adimlar = (await db.Table<RotaAdimi>().Where(a => a.UrunId == parca.UrunId).ToListAsync()).OrderBy(a => a.Sira).ToList();
        var adim = adimlar.ElementAtOrDefault(parca.TamamlananAdim);
        if (adim == null) return new(new("hata", "Rota tamam", "Parçanın yapılacak adımı yok.", parca));
        if (adim.MakineId != makineId)
        {
            var dogru = await db.Table<Makine>().Where(m => m.Id == adim.MakineId).FirstAsync();
            return new(new("hata", "Rota sırası", $"Sıradaki adım: {adim.Sira}. {adim.Ad} ({dogru.Kod}). Adım atlanamaz.", parca));
        }

        if (k.Rol != Rol.Admin)
        {
            var bugun = DateTime.Today;
            var y = await db.Table<Yetkinlik>().Where(x => x.KullaniciId == k.Id && x.MakineId == makineId).FirstOrDefaultAsync();
            if (y == null) return new(new("hata", "Yetkinlik yok", $"{makine.Kod} için yetkinlik belgen yok. Ustabaşına başvur.", parca));
            if (y.Bitis < bugun) return new(new("hata", "Yetkinlik süresi doldu", $"{makine.Kod} yetkinliğin {y.Bitis:dd.MM.yyyy} tarihinde bitti.", parca));
        }
        return new(null, parca, adim, adimlar);
    }

    private async Task<OkutmaSonucu> AdimiTamamlaAsync(SQLiteAsyncConnection db, Parca parca, RotaAdimi adim, List<RotaAdimi> adimlar, int makineId, Kullanici k)
    {
        var simdi = DateTime.Now;
        await db.InsertAsync(new Islem { ParcaId = parca.Id, AdimSira = adim.Sira, MakineId = makineId, KullaniciId = k.Id, Zaman = simdi });
        parca.TamamlananAdim = adim.Sira;
        var son = adim.Sira >= adimlar.Count;
        if (son) parca.Durum = Durum.Tamamlandi;
        await db.UpdateAsync(parca);

        var makine = await db.Table<Makine>().Where(m => m.Id == makineId).FirstAsync();
        makine.Durum = Durum.Calisiyor;
        await db.UpdateAsync(makine);

        var emirBitti = false;
        if (son)
        {
            var kalan = await db.Table<Parca>().Where(p => p.EmirId == parca.EmirId && p.Durum != Durum.Tamamlandi && p.Durum != Durum.Hurda).CountAsync();
            if (kalan == 0)
            {
                var emir = await db.Table<Emir>().Where(e => e.Id == parca.EmirId).FirstAsync();
                emir.Durum = Durum.Tamamlandi;
                await db.UpdateAsync(emir);
                emirBitti = true;
            }
        }
        await KuyrugaEkleAsync("İşlem", $"{parca.SeriNo} · {adim.Ad} · {makine.Kod}", new { parca.SeriNo, adim = adim.Ad, makine = makine.Kod, kullanici = k.KullaniciAdi, zaman = simdi });

        var sonraki = adimlar.ElementAtOrDefault(adim.Sira);
        return new OkutmaSonucu(emirBitti ? "emir" : "tamam",
            emirBitti ? "Üretim emri tamamlandı! 🎉" : son ? "Parça tamamlandı" : "Adım kaydedildi",
            son ? $"{parca.SeriNo} tüm rotayı tamamladı." : $"{adim.Ad} tamam. Sıradaki: {sonraki?.Ad}.",
            parca, null, adim.Ad, adim.Sira, adimlar.Count);
    }

    public static bool SayiCoz(string? metin, out double deger)
    {
        deger = 0;
        if (string.IsNullOrWhiteSpace(metin)) return false;
        var t = metin.Trim().Replace(" ", "").Replace(',', '.');
        return t.Count(c => c == '.') <= 1 && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out deger);
    }

    private static async Task<string> SiradakiNcrNoAsync(SQLiteAsyncConnection db)
    {
        var nolar = await db.Table<Ncr>().ToListAsync();
        var enBuyuk = nolar.Select(n => int.TryParse(n.NcrNo.Split('-').Last(), out var x) ? x : 0).DefaultIfEmpty(0).Max();
        return $"NCR-{enBuyuk + 1:0000}";
    }

    // ---------- NCR ----------

    public async Task<List<Ncr>> NcrlerAsync() => (await (await Db()).Table<Ncr>().ToListAsync()).OrderByDescending(n => n.Zaman).ToList();
    public async Task<Ncr?> NcrAsync(int id) => await (await Db()).Table<Ncr>().Where(n => n.Id == id).FirstOrDefaultAsync();
    public async Task<Parca?> ParcaAsync(int id) => await (await Db()).Table<Parca>().Where(p => p.Id == id).FirstOrDefaultAsync();
    public async Task<Emir?> EmirAsync(int id) => await (await Db()).Table<Emir>().Where(e => e.Id == id).FirstOrDefaultAsync();

    public async Task NcrFotoAsync(Ncr ncr, string yol)
    {
        ncr.FotoYolu = yol;
        await (await Db()).UpdateAsync(ncr);
        await KuyrugaEkleAsync("Fotoğraf", $"{ncr.NcrNo} hata fotoğrafı eklendi", new { ncr.NcrNo, dosya = Path.GetFileName(yol) });
    }

    public async Task<string?> NcrKararAsync(int ncrId, string karar, Kullanici k)
    {
        var db = await Db();
        var ncr = await db.Table<Ncr>().Where(n => n.Id == ncrId).FirstOrDefaultAsync();
        if (ncr == null) return "NCR bulunamadı.";
        if (ncr.Durum != Durum.Acik) return "Bu NCR için karar verilmiş.";
        if (k.Rol is not (Rol.Kalite or Rol.Yonetici or Rol.Admin)) return "Karar yetkiniz yok (Kalite / Yönetici).";

        ncr.Karar = karar;
        ncr.Durum = Durum.KararVerildi;
        await db.UpdateAsync(ncr);

        var parca = await db.Table<Parca>().Where(p => p.Id == ncr.ParcaId).FirstOrDefaultAsync();
        if (parca != null)
        {
            var adimSayisi = await db.Table<RotaAdimi>().Where(a => a.UrunId == parca.UrunId).CountAsync();
            if (karar == "Hurda") parca.Durum = Durum.Hurda;
            else if (karar == "Yeniden işleme") parca.Durum = Durum.Uretimde;
            else
            {
                parca.TamamlananAdim++;
                parca.Durum = parca.TamamlananAdim >= adimSayisi ? Durum.Tamamlandi : Durum.Uretimde;
            }
            await db.UpdateAsync(parca);
        }

        var acikVar = await db.Table<Ncr>().Where(n => n.EmirId == ncr.EmirId && n.Durum == Durum.Acik).CountAsync() > 0;
        if (!acikVar)
        {
            var emir = await db.Table<Emir>().Where(e => e.Id == ncr.EmirId).FirstAsync();
            if (emir.Durum == Durum.Bloke) { emir.Durum = Durum.Uretimde; await db.UpdateAsync(emir); }
        }
        await KuyrugaEkleAsync("NCR kararı", $"{ncr.NcrNo}: {karar}", new { ncr.NcrNo, karar, veren = k.KullaniciAdi });
        return null;
    }

    // ---------- Duruş ----------

    public async Task<List<Durus>> DuruslarAsync() => (await (await Db()).Table<Durus>().ToListAsync()).OrderByDescending(d => d.Baslangic).Take(40).ToList();

    public async Task<string?> DurusBaslatAsync(int makineId, string neden, string? aciklama, Kullanici k)
    {
        var db = await Db();
        var makine = await db.Table<Makine>().Where(m => m.Id == makineId).FirstOrDefaultAsync();
        if (makine == null) return "Makine seçin.";
        if (await db.Table<Durus>().Where(d => d.MakineId == makineId && d.Bitis == null).CountAsync() > 0) return $"{makine.Kod} için açık duruş var.";
        await db.InsertAsync(new Durus { MakineId = makineId, Neden = neden, Aciklama = aciklama, Baslangic = DateTime.Now, Bildiren = k.AdSoyad });
        makine.Durum = neden == "Arıza" ? Durum.Arizali : Durum.Bekliyor;
        await db.UpdateAsync(makine);
        await KuyrugaEkleAsync("Duruş", $"{makine.Kod} durdu · {neden}", new { makine = makine.Kod, neden, aciklama, kisi = k.KullaniciAdi });
        return null;
    }

    public async Task DurusBitirAsync(int durusId)
    {
        var db = await Db();
        var d = await db.Table<Durus>().Where(x => x.Id == durusId).FirstOrDefaultAsync();
        if (d == null || d.Bitis != null) return;
        d.Bitis = DateTime.Now;
        await db.UpdateAsync(d);
        var makine = await db.Table<Makine>().Where(m => m.Id == d.MakineId).FirstAsync();
        makine.Durum = Durum.Bekliyor;
        await db.UpdateAsync(makine);
        await KuyrugaEkleAsync("Duruş bitti", $"{makine.Kod} · {(int)(d.Bitis.Value - d.Baslangic).TotalMinutes} dk", new { makine = makine.Kod, d.Neden, dakika = (d.Bitis.Value - d.Baslangic).TotalMinutes });
    }

    // ---------- Stok sayımı ----------

    public async Task<List<HammaddeLotu>> LotlarAsync() => (await (await Db()).Table<HammaddeLotu>().ToListAsync()).OrderBy(l => l.LotNo).ToList();
    public async Task<List<Sayim>> SayimlarAsync() => (await (await Db()).Table<Sayim>().ToListAsync()).OrderByDescending(s => s.Zaman).ToList();

    public async Task<Sayim> SayimKaydetAsync(HammaddeLotu lot, double sayilan, Kullanici k)
    {
        var db = await Db();
        var s = new Sayim { LotNo = lot.LotNo, Malzeme = lot.Malzeme, Sistem = lot.Kalan, Sayilan = Math.Round(sayilan, 3), Kisi = k.AdSoyad, Zaman = DateTime.Now };
        await db.InsertAsync(s);
        await KuyrugaEkleAsync("Sayım", $"{lot.LotNo}: sistem {s.Sistem:N1} / sayılan {s.Sayilan:N1} kg", s);
        return s;
    }

    // ---------- Panel ve performans ----------

    public async Task<PanelOzet> PanelAsync(Kullanici k)
    {
        var db = await Db();
        var bugun = DateTime.Today;
        var islemler = await db.Table<Islem>().Where(i => i.Zaman >= bugun).ToListAsync();
        var makineler = await MakinelerAsync();
        var redler = await db.Table<Olcum>().Where(o => o.Zaman >= bugun && !o.Uygun).CountAsync();

        // Tahmini OEE: çalışan makinelerin ideal süreye göre verimi × kalite
        var oranlar = islemler.GroupBy(i => i.MakineId).Select(g =>
        {
            var m = makineler.FirstOrDefault(x => x.Id == g.Key);
            var pencere = Math.Max(30, (g.Max(i => i.Zaman) - g.Min(i => i.Zaman)).TotalMinutes + 10);
            return Math.Min(1, g.Count() * (m?.IdealCevrimSn ?? 240) / 60.0 / pencere);
        }).ToList();
        var kalite = islemler.Count == 0 ? 1 : (double)islemler.Count / (islemler.Count + redler);
        var oee = oranlar.Count == 0 ? 0 : oranlar.Average() * kalite * 0.92;

        return new PanelOzet(islemler.Count, islemler.Count(i => i.KullaniciId == k.Id),
            await db.Table<Ncr>().Where(n => n.Durum == Durum.Acik).CountAsync(),
            makineler.Count(m => m.Durum == Durum.Arizali), makineler.Count(m => m.Durum == Durum.Calisiyor), makineler.Count,
            await db.Table<KuyrukKaydi>().Where(q => !q.Aktarildi).CountAsync(), oee);
    }

    public async Task<(List<GunDegeri> Hafta, int Bugun, int Toplam, int Red, double Ortalama)> PerformansAsync(int kullaniciId)
    {
        var db = await Db();
        var bas = DateTime.Today.AddDays(-6);
        var islemler = await db.Table<Islem>().Where(i => i.KullaniciId == kullaniciId && i.Zaman >= bas).ToListAsync();
        var red = await db.Table<Olcum>().Where(o => o.KullaniciId == kullaniciId && o.Zaman >= bas && !o.Uygun).CountAsync();
        var hafta = Enumerable.Range(0, 7).Select(i => bas.AddDays(i))
            .Select(g => new GunDegeri(g.ToString("ddd", new CultureInfo("tr-TR")), islemler.Count(x => x.Zaman.Date == g))).ToList();
        var calisilan = hafta.Count(h => h.Deger > 0);
        return (hafta, hafta.Last().Deger, islemler.Count, red, calisilan == 0 ? 0 : (double)islemler.Count / calisilan);
    }

    // ---------- Çevrimdışı kuyruk ----------

    private async Task KuyrugaEkleAsync(string tur, string ozet, object veri)
    {
        var db = await Db();
        await db.InsertAsync(new KuyrukKaydi { Tur = tur, Ozet = ozet, Veri = JsonSerializer.Serialize(veri), Zaman = DateTime.Now });
    }

    public async Task<List<KuyrukKaydi>> KuyrukAsync() => (await (await Db()).Table<KuyrukKaydi>().ToListAsync()).OrderByDescending(q => q.Zaman).Take(100).ToList();

    // Bekleyen kayıtları merkez sisteme aktarım dosyası (JSON) olarak yazar
    public async Task<(string Yol, int Adet)> KuyrukDisaAktarAsync(Kullanici k)
    {
        var db = await Db();
        var bekleyen = await db.Table<KuyrukKaydi>().Where(q => !q.Aktarildi).ToListAsync();
        var paket = new
        {
            kaynak = "UretimPro Mobil",
            cihaz = DeviceInfo.Current.Model,
            kullanici = k.KullaniciAdi,
            olusturma = DateTime.Now,
            kayitlar = bekleyen.OrderBy(q => q.Zaman).Select(q => new { q.Tur, q.Ozet, q.Zaman, veri = JsonDocument.Parse(q.Veri).RootElement })
        };
        var yol = Path.Combine(FileSystem.CacheDirectory, $"uretimpro-aktarim-{DateTime.Now:yyyyMMdd-HHmm}.json");
        await File.WriteAllTextAsync(yol, JsonSerializer.Serialize(paket, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var q in bekleyen) q.Aktarildi = true;
        await db.UpdateAllAsync(bekleyen);
        return (yol, bekleyen.Count);
    }

    // ---------- Örnek veri ----------

    private static async Task OrnekVeri(SQLiteAsyncConnection db)
    {
        var rnd = new Random(2026);
        var bugun = DateTime.Today;
        var simdi = DateTime.Now;

        Kullanici K(string kAdi, string ad, string rol) => new() { KullaniciAdi = kAdi, AdSoyad = ad, Rol = rol, SifreOzeti = SifreOzeti(kAdi, DemoSifre) };
        var kullanicilar = new[]
        {
            K("admin", "Furkan Baykal", Rol.Admin), K("yonetici", "Selin Aksoy", Rol.Yonetici), K("kalite", "Murat Demir", Rol.Kalite),
            K("operator", "Ali Yılmaz", Rol.Operator), K("emre", "Emre Kaya", Rol.Operator), K("zeynep", "Zeynep Şahin", Rol.Operator),
            K("burak", "Burak Çelik", Rol.Operator), K("depo", "Hakan Öztürk", Rol.DepoBakim)
        };
        await db.InsertAllAsync(kullanicilar);
        Kullanici U(string kAdi) => kullanicilar.First(x => x.KullaniciAdi == kAdi);

        Makine M(string kod, string ad, int ideal) => new() { Kod = kod, Ad = ad, IdealCevrimSn = ideal };
        var makineler = new[]
        {
            M("KSM-1", "Şerit testere", 180), M("CNC-1", "Dik işleme merkezi", 240), M("CNC-2", "Dik işleme merkezi", 240),
            M("CNC-3", "Torna-freze merkezi", 240), M("TRN-1", "CNC torna", 240), M("TRN-2", "CNC torna", 240), M("PRS-1", "Hidrolik pres", 200),
            M("TSL-1", "Silindirik taşlama", 300), M("KLT-1", "Ölçüm istasyonu", 180), M("PKT-1", "Paketleme hattı", 150)
        };
        await db.InsertAllAsync(makineler);
        Makine Mk(string kod) => makineler.First(x => x.Kod == kod);

        var urunler = new[] { new Urun { Kod = "FL120", Ad = "Flanş FL-120" }, new Urun { Kod = "FL90", Ad = "Flanş FL-90" }, new Urun { Kod = "M40", Ad = "Mil M-40" },
                              new Urun { Kod = "B25", Ad = "Burç B-25" }, new Urun { Kod = "K60", Ad = "Kapak K-60" } };
        await db.InsertAllAsync(urunler);
        Urun Ur(string kod) => urunler.First(x => x.Kod == kod);

        var rotalar = new Dictionary<string, string[]>
        {
            ["FL120"] = new[] { "Kesim:KSM-1", "CNC işleme:CNC-3", "Son kontrol:KLT-1", "Paketleme:PKT-1" },
            ["FL90"] = new[] { "Kesim:KSM-1", "CNC işleme:CNC-1", "Son kontrol:KLT-1", "Paketleme:PKT-1" },
            ["M40"] = new[] { "Kesim:KSM-1", "Tornalama:TRN-2", "Taşlama:TSL-1", "Paketleme:PKT-1" },
            ["B25"] = new[] { "Kesim:KSM-1", "Tornalama:TRN-1", "Son kontrol:KLT-1", "Paketleme:PKT-1" },
            ["K60"] = new[] { "Kesim:KSM-1", "Presleme:PRS-1", "Delik delme:CNC-2", "Paketleme:PKT-1" }
        };
        var adimlar = new List<RotaAdimi>();
        foreach (var (kod, liste) in rotalar)
            adimlar.AddRange(liste.Select((x, i) => new RotaAdimi { UrunId = Ur(kod).Id, Sira = i + 1, Ad = x.Split(':')[0], MakineId = Mk(x.Split(':')[1]).Id }));
        await db.InsertAllAsync(adimlar);

        Karakteristik Kr(string u, int sira, string ad, string birim, double n, double a, double ust) =>
            new() { UrunId = Ur(u).Id, AdimSira = sira, Ad = ad, Birim = birim, Nominal = n, Alt = a, Ust = ust };
        var karlar = new[]
        {
            Kr("FL120", 2, "Dış çap", "mm", 120, 119.95, 120.05), Kr("FL120", 3, "Kalınlık", "mm", 12, 11.9, 12.1),
            Kr("FL90", 2, "Dış çap", "mm", 90, 89.95, 90.05), Kr("FL90", 3, "Kalınlık", "mm", 10, 9.9, 10.1),
            Kr("M40", 2, "Çap", "mm", 40, 39.98, 40.02), Kr("B25", 2, "İç çap", "mm", 25, 24.97, 25.03),
            Kr("B25", 3, "Dış çap", "mm", 32, 31.95, 32.05), Kr("K60", 3, "Delik çapı", "mm", 8, 7.95, 8.05)
        };
        await db.InsertAllAsync(karlar);

        var yetkinlik = new List<Yetkinlik>();
        void Y(string k, int gun, params string[] kodlar) => yetkinlik.AddRange(kodlar.Select(m => new Yetkinlik { KullaniciId = U(k).Id, MakineId = Mk(m).Id, Bitis = bugun.AddDays(gun) }));
        Y("operator", 210, "KSM-1", "CNC-3", "CNC-1", "PKT-1");
        Y("emre", 180, "KSM-1", "TRN-1", "TRN-2", "TSL-1", "PKT-1");
        Y("zeynep", 150, "KSM-1", "CNC-1", "PRS-1", "PKT-1");
        Y("zeynep", -1, "CNC-2");
        Y("burak", 240, "KSM-1", "PKT-1", "TRN-1", "PRS-1");
        Y("kalite", 365, "KLT-1", "CNC-3");
        await db.InsertAllAsync(yetkinlik);

        await db.InsertAllAsync(new[]
        {
            new HammaddeLotu { LotNo = "HM-2026-118", Malzeme = "Çelik S355", Kalan = 412.6 }, new HammaddeLotu { LotNo = "HM-2026-121", Malzeme = "Çelik S355", Kalan = 900 },
            new HammaddeLotu { LotNo = "HM-2026-119", Malzeme = "Çelik C45", Kalan = 286.4 }, new HammaddeLotu { LotNo = "HM-2026-120", Malzeme = "Pirinç CuZn39", Kalan = 97.8 },
            new HammaddeLotu { LotNo = "HM-2026-117", Malzeme = "Alüminyum 6061", Kalan = 188.0 }
        });

        // Emirler: son 6 gün tamamlanan + bugün üretimde
        var emirSira = 471;
        var seri = new Dictionary<string, int> { ["FL120"] = 640, ["FL90"] = 380, ["M40"] = 140, ["B25"] = 1520, ["K60"] = 420 };
        var islemler = new List<Islem>();
        var olcumler = new List<Olcum>();
        var operatorlar = new[] { "operator", "emre", "zeynep", "burak", "kalite" };

        async Task<(Emir, List<Parca>)> EmirOlustur(string urunKod, int miktar, DateTime gun, string durum)
        {
            var e = new Emir { EmirNo = $"UE-{emirSira++:0000}", LotNo = $"UL-{gun:yyMM}-{emirSira - 470:00}", UrunId = Ur(urunKod).Id, Miktar = miktar, Durum = durum };
            await db.InsertAsync(e);
            var parcalar = Enumerable.Range(0, miktar).Select(_ => new Parca { SeriNo = $"{urunKod}-{++seri[urunKod]:00000}", EmirId = e.Id, UrunId = e.UrunId }).ToList();
            await db.InsertAllAsync(parcalar);
            return (e, parcalar);
        }

        void Isle(Parca p, string urunKod, int adimSayisi, DateTime bas)
        {
            var rota = adimlar.Where(a => a.UrunId == p.UrunId).OrderBy(a => a.Sira).ToList();
            for (var s = 0; s < adimSayisi; s++)
            {
                var a = rota[s];
                var adaylar = yetkinlik.Where(y => y.MakineId == a.MakineId && y.Bitis >= bugun).Select(y => y.KullaniciId).ToList();
                var op = adaylar.Count > 0 ? adaylar[rnd.Next(adaylar.Count)] : U("operator").Id;
                var t = bas.AddMinutes(s * 35 + rnd.Next(0, 4));
                if (t > simdi) break;
                islemler.Add(new Islem { ParcaId = p.Id, AdimSira = a.Sira, MakineId = a.MakineId, KullaniciId = op, Zaman = t });
                foreach (var kr in karlar.Where(x => x.UrunId == p.UrunId && x.AdimSira == a.Sira))
                {
                    var deger = Math.Round(kr.Nominal + (rnd.NextDouble() - 0.5) * (kr.Ust - kr.Alt) * 0.6, 3);
                    olcumler.Add(new Olcum { ParcaId = p.Id, KarakteristikId = kr.Id, Deger = deger, Uygun = true, KullaniciId = op, Zaman = t });
                }
                p.TamamlananAdim = a.Sira;
            }
            p.Durum = p.TamamlananAdim >= rota.Count ? Durum.Tamamlandi : Durum.Uretimde;
        }

        var gecmisUrunler = new[] { "B25", "FL120", "K60", "FL90", "B25", "FL120" };
        for (var d = 6; d >= 1; d--)
        {
            var gun = bugun.AddDays(-d);
            var urunKod = gecmisUrunler[d - 1];
            var (e, parcalar) = await EmirOlustur(urunKod, 30, gun, Durum.Tamamlandi);
            for (var i = 0; i < parcalar.Count; i++) Isle(parcalar[i], urunKod, 4, gun.AddHours(8).AddMinutes(i * 9));
            await db.UpdateAllAsync(parcalar);
        }

        // Bugün: FL120 (80) ve B25 (60) üretimde; parçalar farklı adımlarda
        var basla = simdi.AddHours(-4) < bugun.AddHours(8) ? simdi.AddHours(-4) : bugun.AddHours(8);
        foreach (var (kod, miktar, ofset) in new[] { ("FL120", 80, 0), ("B25", 60, 60) })
        {
            var (_, parcalar) = await EmirOlustur(kod, miktar, bugun, Durum.Uretimde);
            for (var i = 0; i < parcalar.Count; i++)
            {
                var adim = i < 25 ? 4 : i < 38 ? 3 : i < 50 ? 2 : i < 58 ? 1 : 0;
                Isle(parcalar[i], kod, adim, basla.AddMinutes(ofset + i * 2));
            }
            await db.UpdateAllAsync(parcalar);
        }

        // Açık NCR: FL90 lotu bloke
        var (blokeEmir, blokeParcalar) = await EmirOlustur("FL90", 20, bugun.AddDays(-1), Durum.Bloke);
        for (var i = 0; i < 6; i++) Isle(blokeParcalar[i], "FL90", 2, bugun.AddDays(-1).AddHours(13).AddMinutes(i * 8));
        var hataliParca = blokeParcalar[6];
        Isle(hataliParca, "FL90", 2, bugun.AddDays(-1).AddHours(14));
        hataliParca.Durum = Durum.Bloke;
        await db.UpdateAllAsync(blokeParcalar);
        var kalinlik = karlar.First(x => x.UrunId == Ur("FL90").Id && x.Ad == "Kalınlık");
        olcumler.Add(new Olcum { ParcaId = hataliParca.Id, KarakteristikId = kalinlik.Id, Deger = 10.142, Uygun = false, KullaniciId = U("zeynep").Id, Zaman = bugun.AddDays(-1).AddHours(14).AddMinutes(40) });
        await db.InsertAsync(new Ncr
        {
            NcrNo = "NCR-0031", EmirId = blokeEmir.Id, ParcaId = hataliParca.Id, Acan = "Zeynep Şahin", Zaman = bugun.AddDays(-1).AddHours(14).AddMinutes(40),
            Tanim = $"{hataliParca.SeriNo} tolerans dışı: Kalınlık 10,142 mm (tolerans 9,9–10,1)"
        });
        await db.InsertAsync(new Ncr
        {
            NcrNo = "NCR-0029", EmirId = blokeEmir.Id - 3, ParcaId = 0, Acan = "Murat Demir", Zaman = bugun.AddDays(-4).AddHours(11),
            Tanim = "K60 delik çapı tolerans dışı (8,067 mm)", Durum = Durum.KararVerildi, Karar = "Şartlı kabul"
        });

        await db.InsertAllAsync(islemler);
        await db.InsertAllAsync(olcumler);

        // Makine durumları ve açık arıza
        var sonIslem = islemler.Where(i => i.Zaman > simdi.AddMinutes(-90)).Select(i => i.MakineId).ToHashSet();
        foreach (var m in makineler) m.Durum = sonIslem.Contains(m.Id) ? Durum.Calisiyor : Durum.Bekliyor;
        Mk("TSL-1").Durum = Durum.Arizali;
        await db.UpdateAllAsync(makineler);
        await db.InsertAllAsync(new[]
        {
            new Durus { MakineId = Mk("TSL-1").Id, Neden = "Arıza", Aciklama = "İş mili titreşimi", Baslangic = simdi.AddHours(-3), Bildiren = "Emre Kaya" },
            new Durus { MakineId = Mk("CNC-3").Id, Neden = "Ayar / set-up", Aciklama = "İlk parça onayı", Baslangic = simdi.AddHours(-5), Bitis = simdi.AddHours(-5).AddMinutes(25), Bildiren = "Ali Yılmaz" },
            new Durus { MakineId = Mk("KSM-1").Id, Neden = "Takım değişimi", Aciklama = "Testere lamı değişti", Baslangic = simdi.AddDays(-1).AddHours(-2), Bitis = simdi.AddDays(-1).AddHours(-2).AddMinutes(12), Bildiren = "Burak Çelik" }
        });
    }
}
