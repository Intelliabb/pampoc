using PamPocApi.Models;

namespace PamPocApi.Services;

public interface IChatService
{
    Task<(bool Success, string Response, object? Usage, string? Error)> SendChatAsync(
        string prompt,
        string? model = null,
        string? systemPrompt = null,
        double? temperature = null,
        int? maxTokens = null,
        CancellationToken cancellationToken = default);
}