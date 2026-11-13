using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace UretimProMobil.Services;

// Telefonun donanımı: titreşim, kamera ile QR okuma, hata fotoğrafı, paylaşım
public class CihazServisi
{
    public void Tik() { try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); } catch { } }
    public void Basari() { try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(60)); } catch { } }
    public void Alarm() { try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(700)); } catch { } }

    // ---------- Kamera ile QR / barkod ----------
    public async Task<string?> QrOkut()
    {
        try
        {
            var izin = await Permissions.RequestAsync<Permissions.Camera>();
            if (izin != PermissionStatus.Granted) return null;
            var sayfa = new TaramaSayfasi();
            var ana = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (ana == null) return null;
            await ana.Navigation.PushModalAsync(sayfa);
            return await sayfa.Sonuc;
        }
        catch { return null; }
    }

    // ---------- Hata fotoğrafı ----------
    public async Task<string?> FotoCek()
    {
        try
        {
            if (!MediaPicker.Default.IsCaptureSupported) return null;
            var izin = await Permissions.RequestAsync<Permissions.Camera>();
            if (izin != PermissionStatus.Granted) return null;
            var foto = await MediaPicker.Default.CapturePhotoAsync();
            if (foto == null) return null;
            var hedef = Path.Combine(FileSystem.AppDataDirectory, $"ncr-{DateTime.Now:yyyyMMdd-HHmmss}.jpg");
            await using var kaynak = await foto.OpenReadAsync();
            await using var yaz = File.Create(hedef);
            await kaynak.CopyToAsync(yaz);
            return hedef;
        }
        catch { return null; }
    }

    public async Task<string?> FotoVerisi(string? yol)
    {
        if (string.IsNullOrEmpty(yol) || !File.Exists(yol)) return null;
        var bayt = await File.ReadAllBytesAsync(yol);
        return "data:image/jpeg;base64," + Convert.ToBase64String(bayt);
    }

    public async Task DosyaPaylas(string yol, string baslik)
    {
        try { await Share.Default.RequestAsync(new ShareFileRequest { Title = baslik, File = new ShareFile(yol) }); } catch { }
    }
}

// Tam ekran kamera: QR / barkod görünce değeri döndürür
public class TaramaSayfasi : ContentPage
{
    private readonly TaskCompletionSource<string?> _sonuc = new();
    public Task<string?> Sonuc => _sonuc.Task;

    public TaramaSayfasi()
    {
        BackgroundColor = Colors.Black;
        var okuyucu = new CameraBarcodeReaderView
        {
            Options = new BarcodeReaderOptions
            {
                Formats = BarcodeFormat.QrCode | BarcodeFormat.Code128 | BarcodeFormat.DataMatrix,
                AutoRotate = true,
                Multiple = false
            }
        };
        okuyucu.BarcodesDetected += (_, e) =>
        {
            var deger = e.Results?.FirstOrDefault()?.Value;
            if (string.IsNullOrWhiteSpace(deger)) return;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (!_sonuc.TrySetResult(deger)) return;
                okuyucu.IsDetecting = false;
                try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(80)); } catch { }
                await Navigation.PopModalAsync();
            });
        };

        var cerceve = new Border
        {
            WidthRequest = 250, HeightRequest = 250, Stroke = Color.FromArgb("#D58E52"), StrokeThickness = 4,
            BackgroundColor = Colors.Transparent, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center
        };
        var bilgi = new Label
        {
            Text = "QR etiketini çerçeveye getirin", TextColor = Colors.White, FontSize = 16, HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 60, 0, 0)
        };
        var iptal = new Button
        {
            Text = "İptal", BackgroundColor = Color.FromArgb("#193326"), TextColor = Colors.White, CornerRadius = 14,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.End, Margin = new Thickness(0, 0, 0, 50), WidthRequest = 180
        };
        iptal.Clicked += async (_, _) => { _sonuc.TrySetResult(null); await Navigation.PopModalAsync(); };

        Content = new Grid { Children = { okuyucu, cerceve, bilgi, iptal } };
    }

    protected override bool OnBackButtonPressed()
    {
        _sonuc.TrySetResult(null);
        return base.OnBackButtonPressed();
    }
}
