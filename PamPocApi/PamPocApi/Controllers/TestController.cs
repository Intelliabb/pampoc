using Microsoft.AspNetCore.Mvc;
using PamPocApi.Services;

namespace PamPocApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    private readonly IChatService _chatService;

    public TestController(IChatService chatService)
    {
        _chatService = chatService;
    }

    [HttpPost("slow-response")]
    public async Task<IActionResult> TestSlowResponse([FromBody] TestRequest request, CancellationToken cancellationToken)
    {
        try
        {
            // Simulate a slow LLM response that can be interrupted
            var prompt = request.Prompt ?? "Tell me a very detailed 500-word story about space exploration. Include lots of technical details.";
            
            var (success, response, usage, error) = await _chatService.SendChatAsync(
                prompt, 
                request.Model,
                "You are a verbose assistant that gives very detailed responses.",
                0.7,
                1000,
                cancellationToken);

            if (!success)
                return Problem(detail: error, statusCode: 502);

            return Ok(new { response, usage, interrupted = false });
        }
        catch (OperationCanceledException)
        {
            return Ok(new { response = "Operation was interrupted", interrupted = true });
        }
    }
}

public record TestRequest(string? Prompt, string? Model);