namespace PamPocApi.Models;

public record ChatMessage(string Role, string Content);

public record ChatRequest(
    string? Model,
    List<ChatMessage> Messages,
    double? Temperature,
    int? MaxTokens,
    bool? Stream
);