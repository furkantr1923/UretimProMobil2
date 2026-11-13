# 📱 ÜretimPro Mobil · Android İstasyon Uygulaması

[ÜretimPro](https://github.com/furkantr1923/UretimPro) üretim izlenebilirlik ve kalite sisteminin Android uygulaması. Aynı iş kurallarıyla, **internet olmadan** çalışır; veritabanı telefonun içindedir.

> ⚠️ **Yasal bilgilendirme:** Bu proje, kişisel portfolyo amacıyla geliştirilmiş **bağımsız bir eğitim / demo çalışmasıdır**. Herhangi bir şirketin, kurumun veya markanın **resmî ürünü değildir**, onlarla bir bağı, ortaklığı ya da onayı yoktur. Hiçbir şirketin adı, logosu, kodu veya gerçek verisi kullanılmamıştır. Uygulamadaki tüm kişi, makine, ürün ve üretim verileri **kurgusaldır**. Ürünler genel metal parçalardır (flanş, mil, burç, kapak); proje silah veya askerî ürün içermez. Ticari amaç taşımaz ve ücretsiz olarak paylaşılmaktadır.
>
> 📲 APK, talep üzerine paylaşılmaktadır.

## ✨ Özellikler

- 📷 **Kamerayla QR / barkod okutma** (ZXing) ve elle seri no girişi
- 🛣️ **Rota kilidi**: adımlar sırayla yapılır, adım atlanamaz
- 👷 **Yetkinlik kontrolü**: belgesi olmayan operatör o makinede işlem yapamaz
- 📏 **Ölçüm girişi**: tolerans dışı değerde parça ve lot bloke, **otomatik NCR**, alarm sesi ve titreşim
- 📸 NCR'a **hata fotoğrafı**; kalite rolü için hurda / yeniden işleme / şartlı kabul kararı
- ⏸️ **Duruş ve arıza bildirimi**
- 📦 **Stok sayımı**: lot etiketini okut, farkı anında gör
- 📈 **Performansım**: 7 günlük işlem grafiği, ilk seferde doğru oranı, yetkinlikler
- 📤 **Çevrimdışı kuyruk**: her işlem kaydedilir, JSON aktarım dosyası olarak paylaşılır
- 🌙 Karanlık mod, açılış animasyonu, ses efektleri, konfeti

## 🛠️ Teknolojiler

| Katman | Teknoloji |
|---|---|
| Uygulama | C#, .NET MAUI Blazor Hybrid (.NET 10, Android) |
| Veri | SQLite (sqlite-net) |
| Kamera | ZXing.Net.Maui |
| Arayüz | Razor bileşenleri, özel CSS, WebAudio ses efektleri |

## 🚀 Derleme

```bash
dotnet publish -f net10.0-android -c Release -p:AndroidPackageFormat=apk
```

**Demo hesapları** (şifre: `Demo123!`): `operator`, `kalite`, `yonetici`, `depo`, `burak` (yetkinliği sınırlı), `admin`

## 👤 Geliştirici

**Furkan Baykal** · Yazılım Mühendisi · [@furkantr1923](https://github.com/furkantr1923)
