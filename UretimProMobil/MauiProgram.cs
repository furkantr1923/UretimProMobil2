using System.Globalization;
using Microsoft.Extensions.Logging;
using UretimProMobil.Services;
using ZXing.Net.Maui.Controls;

namespace UretimProMobil;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var kultur = new CultureInfo("tr-TR");
        CultureInfo.DefaultThreadCurrentCulture = kultur;
        CultureInfo.DefaultThreadCurrentUICulture = kultur;
        CultureInfo.CurrentCulture = kultur;
        CultureInfo.CurrentUICulture = kultur;

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseBarcodeReader();   // kamera ile QR / barkod okuma

        builder.Services.AddMauiBlazorWebView();

        builder.Services.AddSingleton<VeriServisi>();
        builder.Services.AddSingleton<OturumServisi>();
        builder.Services.AddSingleton<CihazServisi>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
