using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PamPocApi.Models;
using PamPocApi.Services;

namespace PamPocApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SpeechController : ControllerBase
{
    private readonly ISpeechService _speechService;

    public SpeechController(ISpeechService speechService)
    {
        _speechService = speechService;
    }

    [HttpPost("stt")]
    public async Task<IActionResult> ConvertSpeechToText([FromForm] IFormFile file, [FromForm] string? language, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "BAD_INPUT", message = "file missing" });

        var (success, text, error) = await _speechService.ConvertSpeechToTextAsync(file, language, cancellationToken);
        
        return success 
            ? Ok(new { text, language })
            : Problem(detail: error, statusCode: 502);
    }

    [HttpPost("tts")]
    public async Task<IActionResult> ConvertTextToSpeech([FromBody] TtsRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "BAD_INPUT", message = "text required" });

        var (success, audioData, error) = await _speechService.ConvertTextToSpeechAsync(request.Text, request.Voice, cancellationToken);
        
        return success 
            ? File(audioData, "audio/wav")
            : Problem(detail: error, statusCode: 502);
    }
}