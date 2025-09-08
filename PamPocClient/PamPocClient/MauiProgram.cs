using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;
using PamPocClient.Services;
using PamPocClient.ViewModels;

namespace PamPocClient;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Register HttpClient
        builder.Services.AddHttpClient();

        // Register Services
        builder.Services.AddSingleton(AudioManager.Current);
        builder.Services.AddSingleton<IVoiceService, VoiceService>();
        builder.Services.AddSingleton<IAudioRecordingService, AudioRecordingService>();
        builder.Services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();

        // Register ViewModels
        builder.Services.AddTransient<MainViewModel>();

        // Register Pages
        builder.Services.AddTransient<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}