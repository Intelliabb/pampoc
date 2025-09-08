using System.ComponentModel.DataAnnotations;

namespace PamPocApi.Configuration;

public class ServiceConfiguration
{
    public const string SectionName = "ServiceConfiguration";

    [Required, Url] public string SttUrl { get; set; } = string.Empty;

    [Required, Url]
    public string LlmUrl { get; set; } = string.Empty;

    [Required]
    public string DefaultLlmModel { get; set; } = string.Empty;

    [Required]
    public string TtsMode { get; set; } = string.Empty;

    public string PiperBin { get; set; } = string.Empty;

    public string TtsVoicePath { get; set; } = string.Empty;

    [Url]
    public string? TtsUrl { get; set; }

    public void Validate()
    {
        if (TtsMode == "http" && string.IsNullOrWhiteSpace(TtsUrl))
            throw new InvalidOperationException("TtsUrl is required when TtsMode is 'http'");

        if (TtsMode == "cli" && string.IsNullOrWhiteSpace(TtsVoicePath))
            throw new InvalidOperationException("TtsVoicePath is required when TtsMode is 'cli'");
    }
}