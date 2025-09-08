using System.Text;
using System.Text.Json;
using PamPocClient.Models;

namespace PamPocClient.Services;

public interface IVoiceService
{
    Task<string> GetChatCompletionAsync(List<ChatMessage> messages);
    Task<byte[]> GetVoiceAsync(string text, string voice = "alloy");
    Task<string> GetSpeechToTextAsync(byte[] audioData);
    Task<byte[]> GetTextToSpeechAsync(string text, string voice = "alloy");
    Task<string> ProcessVoiceAsync(byte[] audioData);
    Task<VoiceResponse> ProcessVoiceWithTextAsync(byte[] audioData);
    Task<bool> CheckHealthAsync();
}

public class VoiceService : IVoiceService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl = "http://localhost:5269";

    public VoiceService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(_baseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<string> GetChatCompletionAsync(List<ChatMessage> messages)
    {
        try
        {
            var request = new ChatRequest { Messages = messages };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/chat", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseJson);

            return chatResponse?.Text ?? "No response";
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to get chat completion: {ex.Message}");
        }
    }

    public async Task<byte[]> GetVoiceAsync(string text, string voice = "alloy")
    {
        try
        {
            var request = new VoiceRequest { Text = text, Voice = voice };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/voice", content);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to get voice audio: {ex.Message}");
        }
    }

    public async Task<string> GetSpeechToTextAsync(byte[] audioData)
    {
        try
        {
            using var formContent = new MultipartFormDataContent();
            using var audioContent = new ByteArrayContent(audioData);
            audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
            formContent.Add(audioContent, "audio", "audio.wav");

            var response = await _httpClient.PostAsync("/stt", formContent);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var sttResponse = JsonSerializer.Deserialize<SpeechToTextResponse>(responseJson);

            return sttResponse?.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to convert speech to text: {ex.Message}");
        }
    }

    public async Task<byte[]> GetTextToSpeechAsync(string text, string voice = "alloy")
    {
        try
        {
            var request = new VoiceRequest { Text = text, Voice = voice };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/tts", content);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to convert text to speech: {ex.Message}");
        }
    }

    public async Task<string> ProcessVoiceAsync(byte[] audioData)
    {
        try
        {
            using var formContent = new MultipartFormDataContent();
            using var audioContent = new ByteArrayContent(audioData);
            audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
            formContent.Add(audioContent, "file", "recording.wav");

            var response = await _httpClient.PostAsync("/voice", formContent);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            
            // Try to parse as JSON response first, fallback to plain text
            try
            {
                var jsonResponse = JsonSerializer.Deserialize<SpeechToTextResponse>(responseJson);
                return jsonResponse?.Text ?? responseJson;
            }
            catch
            {
                return responseJson;
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to process voice: {ex.Message}");
        }
    }
    
    public async Task<VoiceResponse> ProcessVoiceWithTextAsync(byte[] audioData)
    {
        try
        {
            // audioData = await GetSampleAudio();
            
            using var formContent = new MultipartFormDataContent();
            using var audioContent = new ByteArrayContent(audioData);
            audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
            formContent.Add(audioContent, "file", "recording.wav");

            var response = await _httpClient.PostAsync("/voice.json", formContent);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            
            // Try to parse as JSON response first, fallback to plain text
            try
            {
                var jsonResponse = JsonSerializer.Deserialize<VoiceResponse>(responseJson);
                return jsonResponse ?? new VoiceResponse();
            }
            catch
            {
                return new VoiceResponse();
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to process voice: {ex.Message}");
        }
    }

    private static async Task<byte[]> GetSampleAudio()
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync("sample.wav");
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        byte[] audioData = memoryStream.ToArray();
        return audioData;
    }

    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/health");
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var healthResponse = JsonSerializer.Deserialize<HealthResponse>(responseJson);

            return healthResponse?.Status?.Equals("healthy", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}