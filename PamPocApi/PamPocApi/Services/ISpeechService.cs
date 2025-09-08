using PamPocApi.Models;

namespace PamPocApi.Services;

public interface ISpeechService
{
    Task<(bool Success, string Text, string? Error)> ConvertSpeechToTextAsync(
        IFormFile audioFile, 
        string? language, 
        CancellationToken cancellationToken);
    
    Task<(bool Success, byte[] AudioData, string? Error)> ConvertTextToSpeechAsync(
        string text, 
        string? voice, 
        CancellationToken cancellationToken);
}