using System.Text.Json.Serialization;

namespace PamPocClient.Models;

public class ChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool IsUser => Role == "user";
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

public class ChatChoice
{
    [JsonPropertyName("message")]
    public ChatMessage Message { get; set; } = new();
}

public class VoiceRequest
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
    
    [JsonPropertyName("voice")]
    public string Voice { get; set; } = "alloy";
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
    public byte[] Audio { get; set; } = [];
}

public class SpeechToTextResponse
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

public class HealthResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}