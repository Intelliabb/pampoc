using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PamPocApi.Configuration;
using PamPocApi.Models;

namespace PamPocApi.Services;

public class ChatService(
    HttpClient httpClient,
    IOptions<ServiceConfiguration> config,
    IOptionsMonitor<JsonSerializerOptions> jsonOptions)
    : IChatService
{
    private readonly ServiceConfiguration _config = config.Value;
    private readonly JsonSerializerOptions _jsonOptions = jsonOptions.Get("Web");

    public async Task<(bool Success, string Response, object? Usage, string? Error)> SendChatAsync(
        string prompt,
        string? model = null,
        string? systemPrompt = null,
        double? temperature = null,
        int? maxTokens = null,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>();
        
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new ChatMessage("system", systemPrompt));
            
        messages.Add(new ChatMessage("user", prompt));

        var requestObject = new
        {
            model = string.IsNullOrWhiteSpace(model) ? _config.DefaultLlmModel : model,
            messages,
            temperature = temperature ?? 0.7,
            max_tokens = maxTokens ?? 256,
            stream = false
        };

        var jsonContent = JsonSerializer.Serialize(requestObject, _jsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, _config.LlmUrl);
        request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return (false, string.Empty, null, $"LLM upstream status {(int)response.StatusCode} {response.ReasonPhrase}");

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);

        var root = document.RootElement;
        var text = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        object? usage = root.TryGetProperty("usage", out var usageElement)
            ? JsonSerializer.Deserialize<object>(usageElement.GetRawText(), _jsonOptions)
            : null;

        if (string.IsNullOrWhiteSpace(text)) 
            return (false, string.Empty, usage, "LLM returned empty content");
            
        return (true, text, usage, null);
    }
}