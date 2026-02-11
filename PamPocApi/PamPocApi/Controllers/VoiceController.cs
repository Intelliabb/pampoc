using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PamPocApi.Configuration;
using PamPocApi.Models;
using PamPocApi.Services;

namespace PamPocApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VoiceController : ControllerBase
{
    private readonly ISpeechService _speechService;
    private readonly IChatService _chatService;
    private readonly ServiceConfiguration _config;
    private readonly IPromptService _promptService;

    public VoiceController(ISpeechService speechService, IChatService chatService, IOptions<ServiceConfiguration> config, IPromptService promptService)
    {
        _speechService = speechService;
        _chatService = chatService;
        _promptService = promptService;
        _config = config.Value;
    }

    [HttpPost]
    public async Task<IActionResult> ProcessVoice([FromForm] IFormFile file, 
        [FromForm] string? language,
        [FromForm] string? llm_model,
        [FromForm] string? system_prompt,
        [FromForm] double? temperature,
        [FromForm] int? max_tokens,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "BAD_INPUT", message = "file missing" });

        var totalStopwatch = Stopwatch.StartNew();

        var sttStopwatch = Stopwatch.StartNew();
        var (sttSuccess, transcript, sttError) = await _speechService.ConvertSpeechToTextAsync(file, language, cancellationToken);
        sttStopwatch.Stop();
        
        if (!sttSuccess)
            return Problem(detail: sttError, statusCode: 502);
        
        System.Diagnostics.Debug.WriteLine($"[{ToString()}]: Voice: Transcript:'{transcript}'");


        var llmStopwatch = Stopwatch.StartNew();
        var (llmSuccess, answer, usage, llmError) = await _chatService.SendChatAsync(
            transcript, llm_model, system_prompt ?? _promptService.GetSystemPrompt(), temperature, max_tokens, cancellationToken);
        llmStopwatch.Stop();
        
        if (!llmSuccess)
            return Problem(detail: llmError, statusCode: 502);

        var ttsStopwatch = Stopwatch.StartNew();
        var (ttsSuccess, audioData, ttsError) = await _speechService.ConvertTextToSpeechAsync(answer, null, cancellationToken);
        ttsStopwatch.Stop();
        
        if (!ttsSuccess)
            return Problem(detail: ttsError, statusCode: 502);

        totalStopwatch.Stop();

        return File(audioData, "audio/wav", enableRangeProcessing: false);
    }

    [HttpPost("json")]
    public async Task<IActionResult> ProcessVoiceJson([FromForm] IFormFile file,
        [FromForm] string? language,
        [FromForm] string? llm_model,
        [FromForm] string? system_prompt,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "BAD_INPUT", message = "file missing" });

        var totalStopwatch = Stopwatch.StartNew();

        var sttStopwatch = Stopwatch.StartNew();
        var (sttSuccess, transcript, sttError) = await _speechService.ConvertSpeechToTextAsync(file, language, cancellationToken);
        sttStopwatch.Stop();
        
        if (!sttSuccess)
            return Problem(detail: sttError, statusCode: 502);

        System.Diagnostics.Debug.WriteLine($"[{ToString()}]: Voice: Transcript:'{transcript}'");
        var llmStopwatch = Stopwatch.StartNew();
        var (llmSuccess, answer, usage, llmError) = await _chatService.SendChatAsync(
            transcript, llm_model, system_prompt, cancellationToken: cancellationToken);
        llmStopwatch.Stop();
        
        if (!llmSuccess)
            return Problem(detail: llmError, statusCode: 502);

        var ttsStopwatch = Stopwatch.StartNew();
        var (ttsSuccess, audioData, ttsError) = await _speechService.ConvertTextToSpeechAsync(answer, null, cancellationToken);
        ttsStopwatch.Stop();
        
        if (!ttsSuccess)
            return Problem(detail: ttsError, statusCode: 502);

        totalStopwatch.Stop();

        var response = new VoiceJsonResponse(
            Transcript: transcript,
            AssistantText: answer,
            AudioFormat: "wav",
            AudioBase64: Convert.ToBase64String(audioData),
            Usage: usage,
            Models: new { llm = llm_model ?? _config.DefaultLlmModel, stt = "whisper", tts = _config.TtsMode == "http" ? "piper-http" : "piper-cli" },
            TimingsMs: new { stt = sttStopwatch.ElapsedMilliseconds, llm = llmStopwatch.ElapsedMilliseconds, tts = ttsStopwatch.ElapsedMilliseconds, total = totalStopwatch.ElapsedMilliseconds }
        );

        return Ok(response);
    }
}