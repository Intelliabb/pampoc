using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PamPocApi.Models;
using PamPocApi.Services;
using System.Collections.Concurrent;

namespace PamPocApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConversationController : ControllerBase
{
    private readonly ISpeechService _speechService;
    private readonly IChatService _chatService;
    private readonly IPromptService _promptService;
    private readonly IHubContext<ConversationHub> _hubContext;
    private static readonly ConcurrentDictionary<string, ConversationContext> _conversations = new();

    public ConversationController(
        ISpeechService speechService, 
        IChatService chatService, 
        IPromptService promptService,
        IHubContext<ConversationHub> hubContext)
    {
        _speechService = speechService;
        _chatService = chatService;
        _promptService = promptService;
        _hubContext = hubContext;
    }

    [HttpPost("start")]
    public IActionResult StartConversation([FromBody] StartConversationRequest request)
    {
        var conversationId = Guid.NewGuid().ToString();
        var context = new ConversationContext(
            History: new List<(string role, string content)>(),
            State: ConversationState.Idle,
            CancellationTokenSource: new CancellationTokenSource()
        );
        
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            context.History.Add(("system", request.SystemPrompt));
        }
        
        _conversations[conversationId] = context;
        
        return Ok(new { conversationId, state = context.State.ToString() });
    }

    [HttpPost("{conversationId}/interrupt")]
    public async Task<IActionResult> InterruptConversation(string conversationId, [FromBody] InterruptRequest request)
    {
        if (!_conversations.TryGetValue(conversationId, out var context))
            return NotFound(new { error = "CONVERSATION_NOT_FOUND" });

        context.CancellationTokenSource.Cancel();
        var newContext = context with { State = ConversationState.Interrupted };
        _conversations[conversationId] = newContext;

        await _hubContext.Clients.Group(conversationId).SendAsync("ConversationInterrupted", new { 
            conversationId, 
            additionalPrompt = request.AdditionalPrompt,
            timestamp = DateTime.UtcNow
        });

        if (!string.IsNullOrWhiteSpace(request.AdditionalPrompt))
        {
            newContext.History.Add(("user", request.AdditionalPrompt));
        }

        return Ok(new { state = newContext.State.ToString(), message = "Conversation interrupted" });
    }

    [HttpPost("{conversationId}/continue")]
    public async Task<IActionResult> ContinueConversation(string conversationId, [FromForm] IFormFile? audioFile, [FromBody] ContinueConversationRequest? textRequest)
    {
        if (!_conversations.TryGetValue(conversationId, out var context))
            return NotFound(new { error = "CONVERSATION_NOT_FOUND" });

        var newCancellationTokenSource = new CancellationTokenSource();
        var updatedContext = context with { 
            State = ConversationState.Listening, 
            CancellationTokenSource = newCancellationTokenSource 
        };
        _conversations[conversationId] = updatedContext;

        await _hubContext.Clients.Group(conversationId).SendAsync("StateChanged", new { 
            conversationId, 
            state = ConversationState.Listening.ToString() 
        });

        try
        {
            string userInput;

            if (audioFile != null)
            {
                var (sttSuccess, transcript, sttError) = await _speechService.ConvertSpeechToTextAsync(
                    audioFile, null, newCancellationTokenSource.Token);
                
                if (!sttSuccess)
                    return Problem(detail: sttError, statusCode: 502);
                
                userInput = transcript;
            }
            else if (textRequest != null && !string.IsNullOrWhiteSpace(textRequest.Text))
            {
                userInput = textRequest.Text;
            }
            else
            {
                return BadRequest(new { error = "BAD_INPUT", message = "Either audio file or text required" });
            }

            updatedContext.History.Add(("user", userInput));
            
            updatedContext = updatedContext with { State = ConversationState.Thinking };
            _conversations[conversationId] = updatedContext;
            
            await _hubContext.Clients.Group(conversationId).SendAsync("StateChanged", new { 
                conversationId, 
                state = ConversationState.Thinking.ToString(),
                userInput
            });

            var systemPrompt = updatedContext.History.FirstOrDefault(h => h.role == "system").content ?? _promptService.GetSystemPrompt();
            var conversationHistory = string.Join("\n", updatedContext.History.Where(h => h.role != "system").Select(h => $"{h.role}: {h.content}"));

            var (llmSuccess, answer, usage, llmError) = await _chatService.SendChatAsync(
                conversationHistory, null, systemPrompt, cancellationToken: newCancellationTokenSource.Token);
            
            if (!llmSuccess)
            {
                if (newCancellationTokenSource.Token.IsCancellationRequested)
                    return Ok(new { state = ConversationState.Interrupted.ToString(), message = "LLM processing was interrupted" });
                return Problem(detail: llmError, statusCode: 502);
            }

            updatedContext.History.Add(("assistant", answer));
            updatedContext = updatedContext with { State = ConversationState.Speaking };
            _conversations[conversationId] = updatedContext;
            
            await _hubContext.Clients.Group(conversationId).SendAsync("StateChanged", new { 
                conversationId, 
                state = ConversationState.Speaking.ToString(),
                assistantResponse = answer
            });

            var (ttsSuccess, audioData, ttsError) = await _speechService.ConvertTextToSpeechAsync(
                answer, null, newCancellationTokenSource.Token);
            
            if (!ttsSuccess)
            {
                if (newCancellationTokenSource.Token.IsCancellationRequested)
                    return Ok(new { state = ConversationState.Interrupted.ToString(), message = "TTS processing was interrupted" });
                return Problem(detail: ttsError, statusCode: 502);
            }

            updatedContext = updatedContext with { State = ConversationState.Idle };
            _conversations[conversationId] = updatedContext;
            
            await _hubContext.Clients.Group(conversationId).SendAsync("StateChanged", new { 
                conversationId, 
                state = ConversationState.Idle.ToString()
            });

            return File(audioData, "audio/wav", enableRangeProcessing: false);
        }
        catch (OperationCanceledException)
        {
            return Ok(new { state = ConversationState.Interrupted.ToString(), message = "Operation was cancelled due to interrupt" });
        }
    }

    [HttpGet("{conversationId}/state")]
    public IActionResult GetConversationState(string conversationId)
    {
        if (!_conversations.TryGetValue(conversationId, out var context))
            return NotFound(new { error = "CONVERSATION_NOT_FOUND" });

        return Ok(new { 
            conversationId, 
            state = context.State.ToString(),
            historyCount = context.History.Count
        });
    }

    [HttpDelete("{conversationId}")]
    public IActionResult EndConversation(string conversationId)
    {
        if (_conversations.TryRemove(conversationId, out var context))
        {
            context.CancellationTokenSource.Cancel();
            context.CancellationTokenSource.Dispose();
            return Ok(new { message = "Conversation ended" });
        }
        
        return NotFound(new { error = "CONVERSATION_NOT_FOUND" });
    }
}

public record StartConversationRequest(string? SystemPrompt);
public record InterruptRequest(string? AdditionalPrompt);
public record ContinueConversationRequest(string Text);