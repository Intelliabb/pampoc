using Microsoft.AspNetCore.Mvc;
using PamPocApi.Models;
using PamPocApi.Services;

namespace PamPocApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;

    public ChatController(IChatService chatService)
    {
        _chatService = chatService;
    }

    [HttpPost]
    public async Task<IActionResult> SendChat([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        if (request.Messages is null || request.Messages.Count == 0)
            return BadRequest(new { error = "BAD_INPUT", message = "messages required" });

        var systemPrompt = request.Messages.FirstOrDefault(m => m.Role == "system")?.Content;
        var userMessage = request.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? string.Empty;

        var (success, response, usage, error) = await _chatService.SendChatAsync(
            userMessage, 
            request.Model, 
            systemPrompt, 
            request.Temperature, 
            request.MaxTokens, 
            cancellationToken);

        return success 
            ? Ok(new { text = response, usage, provider = "ollama" })
            : Problem(detail: error, statusCode: 502);
    }
}