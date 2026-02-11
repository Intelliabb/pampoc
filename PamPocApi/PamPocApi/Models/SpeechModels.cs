using System.Text.Json.Serialization;

namespace PamPocApi.Models;

public record SttResult([property: JsonPropertyName("text")] string Text);

public record TtsRequest(string Text, string? Voice);

public record VoiceJsonResponse(
    string Transcript,
    string AssistantText,
    string AudioFormat,
    string AudioBase64,
    object? Usage,
    object Models,
    object TimingsMs
);