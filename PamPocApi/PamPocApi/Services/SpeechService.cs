using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PamPocApi.Configuration;
using PamPocApi.Models;

namespace PamPocApi.Services;

public class SpeechService : ISpeechService
{
    private readonly HttpClient _httpClient;
    private readonly ServiceConfiguration _config;
    private readonly JsonSerializerOptions _jsonOptions;

    public SpeechService(HttpClient httpClient, IOptions<ServiceConfiguration> config, IOptionsMonitor<JsonSerializerOptions> jsonOptions)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _jsonOptions = jsonOptions.Get("Web");
    }

    public async Task<(bool Success, string Text, string? Error)> ConvertSpeechToTextAsync(
        IFormFile audioFile, 
        string? language, 
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        var stream = audioFile.OpenReadStream();
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(audioFile.ContentType ?? "audio/wav");
        form.Add(fileContent, "file", audioFile.FileName);
        
        if (!string.IsNullOrWhiteSpace(language)) 
            form.Add(new StringContent(language), "language");

        using var request = new HttpRequestMessage(HttpMethod.Post, _config.SttUrl) { Content = form };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
            return (false, string.Empty, $"STT upstream status {(int)response.StatusCode} {response.ReasonPhrase}");

        using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var sttResult = await JsonSerializer.DeserializeAsync<SttResult>(responseStream, _jsonOptions, cancellationToken);
        
        if (sttResult is null || string.IsNullOrWhiteSpace(sttResult.Text)) 
            return (false, string.Empty, "STT returned empty text");
            
        return (true, sttResult.Text, null);
    }

    public async Task<(bool Success, byte[] AudioData, string? Error)> ConvertTextToSpeechAsync(
        string text, 
        string? voice, 
        CancellationToken cancellationToken)
    {
        if (_config.TtsMode == "http")
        {
            return await ConvertTextToSpeechHttpAsync(text, voice, cancellationToken);
        }
        else
        {
            return await ConvertTextToSpeechCliAsync(text, cancellationToken);
        }
    }

    private async Task<(bool Success, byte[] AudioData, string? Error)> ConvertTextToSpeechHttpAsync(
        string text, 
        string? voice, 
        CancellationToken cancellationToken)
    {
        var payload = new { text, voice = voice ?? Path.GetFileNameWithoutExtension(_config.TtsVoicePath) };
        var jsonContent = JsonSerializer.Serialize(payload, _jsonOptions);
        
        using var request = new HttpRequestMessage(HttpMethod.Post, _config.TtsUrl)
        {
            Content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json")
        };
        
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return (false, Array.Empty<byte>(), $"TTS HTTP status {(int)response.StatusCode} {response.ReasonPhrase}");
            
        var audioData = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return (true, audioData, null);
    }

    private async Task<(bool Success, byte[] AudioData, string? Error)> ConvertTextToSpeechCliAsync(
        string text, 
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.TtsVoicePath))
            return (false, Array.Empty<byte>(), "TTS_VOICE_PATH not set for Piper CLI");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            ArgumentList =
            {
                "-lc",
                $"printf %s \"{text.Replace("\"", "\\\"")}\" | {_config.PiperBin} --model \"{_config.TtsVoicePath}\" --output_file -"
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        
        using var process = Process.Start(processStartInfo)!;
        using var memoryStream = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(memoryStream, cancellationToken);
        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 || memoryStream.Length == 0)
            return (false, Array.Empty<byte>(), $"Piper CLI failed (exit {process.ExitCode}). {stderr}");

        return (true, memoryStream.ToArray(), null);
    }
}