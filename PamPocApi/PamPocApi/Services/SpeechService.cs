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
        Console.WriteLine($"Audio file received - Name: {audioFile.FileName}, ContentType: {audioFile.ContentType}, Length: {audioFile.Length} bytes");

        // Convert audio to whisper.cpp compatible format (16kHz, mono, 16-bit PCM WAV)
        byte[] convertedAudio;
        string outputFileName = "converted.wav";

        try
        {
            convertedAudio = await ConvertAudioToWavAsync(audioFile, cancellationToken);
            Console.WriteLine($"Audio converted to WAV - Length: {convertedAudio.Length} bytes");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Audio conversion failed: {ex.Message}");
            return (false, string.Empty, $"Audio conversion failed: {ex.Message}");
        }

        using var form = new MultipartFormDataContent();
        using var memoryStream = new MemoryStream(convertedAudio);

        var fileContent = new StreamContent(memoryStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(fileContent, "file", outputFileName);

        if (!string.IsNullOrWhiteSpace(language))
            form.Add(new StringContent(language), "language");

        using var request = new HttpRequestMessage(HttpMethod.Post, _config.SttUrl) { Content = form };
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return (false, string.Empty, $"STT upstream status {(int)response.StatusCode} {response.ReasonPhrase}");

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        Console.WriteLine($"STT Response: {responseText}");

        var sttResult = JsonSerializer.Deserialize<SttResult>(responseText, _jsonOptions);

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

    private async Task<byte[]> ConvertAudioToWavAsync(IFormFile audioFile, CancellationToken cancellationToken)
    {
        // Create temporary files for input and output
        var tempInputPath = Path.Combine(Path.GetTempPath(), $"input_{Guid.NewGuid()}{Path.GetExtension(audioFile.FileName)}");
        var tempOutputPath = Path.Combine(Path.GetTempPath(), $"output_{Guid.NewGuid()}.wav");

        try
        {
            // Save uploaded file to temp location
            await using (var fileStream = new FileStream(tempInputPath, FileMode.Create, FileAccess.Write))
            {
                await audioFile.CopyToAsync(fileStream, cancellationToken);
            }

            // Convert using ffmpeg: 16kHz sample rate, mono channel, 16-bit PCM WAV
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                ArgumentList =
                {
                    "-i", tempInputPath,
                    "-ar", "16000",        // 16kHz sample rate
                    "-ac", "1",            // mono (1 channel)
                    "-c:a", "pcm_s16le",   // 16-bit PCM little-endian
                    "-y",                  // overwrite output file
                    tempOutputPath
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
                throw new Exception("Failed to start ffmpeg process");

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
                throw new Exception($"ffmpeg conversion failed (exit code {process.ExitCode}): {stderr}");
            }

            // Read the converted file
            var convertedBytes = await File.ReadAllBytesAsync(tempOutputPath, cancellationToken);
            return convertedBytes;
        }
        finally
        {
            // Clean up temp files
            try
            {
                if (File.Exists(tempInputPath))
                    File.Delete(tempInputPath);
                if (File.Exists(tempOutputPath))
                    File.Delete(tempOutputPath);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}