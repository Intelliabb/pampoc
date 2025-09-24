namespace PamPocApi.Models;

public enum ConversationState
{
    Idle,
    Listening,
    Thinking,
    Speaking,
    Interrupted
}

record ConversationContext(List<(string role, string content)> History, ConversationState State, CancellationTokenSource CancellationTokenSource);