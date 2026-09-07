using System.Text.Json.Serialization;

namespace PamPocWebClient.Models;

public class ChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    [JsonIgnore]
    public DateTime Timestamp { get; set; } = DateTime.Now;

    [JsonIgnore]
    public bool IsUser => Role == "user";

    [JsonIgnore]
    public bool IsAssistant => Role == "assistant";
}

public class ChatRequest
{
    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = new();

    [JsonPropertyName("model")]
    public string Model { get; set; } = "mistral:instruct";
}

public class ChatResponse
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

public class VoiceResponse
{
    [JsonPropertyName("transcript")]
    public string Transcript { get; set; } = string.Empty;

    [JsonPropertyName("assistantText")]
    public string AssistantText { get; set; } = string.Empty;

    [JsonPropertyName("audioFormat")]
    public string AudioFormat { get; set; } = string.Empty;

    [JsonPropertyName("audioBase64")]
    public string AudioBase64 { get; set; } = string.Empty;
}

public class HealthResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }
}

/// <summary>Result of the browser-side startRecording() call.</summary>
public record StartRecordingResult(bool Ok, string? Error);
