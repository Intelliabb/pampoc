using System.Net;
using System.Text;
using System.Text.Json;
using PamPocWebClient.Models;

namespace PamPocWebClient.Services;

public interface IVoiceService
{
    Task<string> GetChatCompletionAsync(List<ChatMessage> messages, CancellationToken cancellationToken = default);
    Task<VoiceResponse> ProcessVoiceAsync(byte[] audioData, string fileName, string contentType, CancellationToken cancellationToken = default);
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Talks to the PamPocApi voice gateway. Mirrors PamPocClient's VoiceService.
/// </summary>
public class VoiceService : IVoiceService
{
    private readonly HttpClient _httpClient;

    public VoiceService(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<string> GetChatCompletionAsync(List<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new ChatRequest { Messages = messages };
            var json = JsonSerializer.Serialize(request);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.PostAsync("/api/chat", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseJson);

            return chatResponse?.Text ?? "No response";
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to get chat completion: {ex.Message}");
        }
    }

    public async Task<VoiceResponse> ProcessVoiceAsync(byte[] audioData, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        try
        {
            using var formContent = new MultipartFormDataContent();
            using var audioContent = new ByteArrayContent(audioData);
            audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            formContent.Add(audioContent, "file", fileName);

            using var response = await _httpClient.PostAsync("/api/voice/json", formContent, cancellationToken);

            // The gateway answers 502 when whisper heard no speech at all. For a
            // recording that captured only room noise that is an ordinary outcome,
            // not an error worth showing as one — hand back an empty response and
            // let the caller ask the user to try again.
            if (response.StatusCode == HttpStatusCode.BadGateway)
            {
                var problem = await response.Content.ReadAsStringAsync(cancellationToken);
                if (problem.Contains("no speech", StringComparison.OrdinalIgnoreCase))
                    return new VoiceResponse();
            }

            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<VoiceResponse>(responseJson) ?? new VoiceResponse();
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to process voice: {ex.Message}");
        }
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("/api/health", cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<HealthResponse>(responseJson)?.Ok == true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
