using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PamPocApi.Configuration;

namespace PamPocApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly HttpClient _httpClient;
    private readonly ServiceConfiguration _config;

    public HealthController(IHttpClientFactory httpClientFactory, IOptions<ServiceConfiguration> config)
    {
        _httpClient = httpClientFactory.CreateClient();
        _config = config.Value;
    }

    [HttpGet]
    public async Task<IActionResult> GetHealth(CancellationToken cancellationToken)
    {
        var status = new Dictionary<string, string>();

        try
        {
            using var _ = await _httpClient.GetAsync(_config.LlmUrl, cancellationToken);
            status["llm"] = "up";
        }
        catch
        {
            status["llm"] = "unknown";
        }

        try
        {
            using var _ = await _httpClient.GetAsync(_config.SttUrl, cancellationToken);
            status["stt"] = "up";
        }
        catch
        {
            status["stt"] = "unknown";
        }

        status["tts"] = _config.TtsMode == "http" ? "http" : "cli";

        return Ok(new { ok = true, services = status });
    }
}