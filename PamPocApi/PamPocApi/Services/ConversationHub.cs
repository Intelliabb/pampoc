using Microsoft.AspNetCore.SignalR;

namespace PamPocApi.Services;

public class ConversationHub : Hub
{
    public async Task JoinConversation(string conversationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, conversationId);
        await Clients.Caller.SendAsync("JoinedConversation", conversationId);
    }

    public async Task LeaveConversation(string conversationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, conversationId);
        await Clients.Caller.SendAsync("LeftConversation", conversationId);
    }
}