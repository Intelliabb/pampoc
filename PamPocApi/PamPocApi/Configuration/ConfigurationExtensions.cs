using Microsoft.Extensions.Options;

namespace PamPocApi.Configuration;

public static class ConfigurationExtensions
{
    public static IServiceCollection AddServiceConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ServiceConfiguration>()
            .Bind(configuration.GetSection(ServiceConfiguration.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart()
            .PostConfigure(options =>
            {
                // Environment variables take precedence (use app-specific prefix)
                options.SttUrl = Environment.GetEnvironmentVariable("PAMPOC__STT_URL") ?? options.SttUrl;
                options.LlmUrl = Environment.GetEnvironmentVariable("PAMPOC__LLM_URL") ?? options.LlmUrl;
                options.DefaultLlmModel = Environment.GetEnvironmentVariable("PAMPOC__DEFAULT_LLM_MODEL") ?? options.DefaultLlmModel;
                options.TtsMode = Environment.GetEnvironmentVariable("PAMPOC__TTS_MODE") ?? options.TtsMode;
                options.PiperBin = Environment.GetEnvironmentVariable("PAMPOC__PIPER_BIN") ?? options.PiperBin;
                options.TtsVoicePath = Environment.GetEnvironmentVariable("PAMPOC__TTS_VOICE_PATH") ?? options.TtsVoicePath;
                options.TtsUrl = Environment.GetEnvironmentVariable("PAMPOC__TTS_URL") ?? options.TtsUrl;

                // Local tool paths are configured as ~/... so one committed config works on any machine
                options.PiperBin = ExpandHome(options.PiperBin);
                options.TtsVoicePath = ExpandHome(options.TtsVoicePath);

                // Custom validation
                options.Validate();
            });

        return services;
    }

    private static string ExpandHome(string path) =>
        path.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..])
            : path;
}