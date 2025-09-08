using Microsoft.Extensions.Logging;

namespace PamPocClient.Services;

public class AudioPlaybackService(ILogger<AudioPlaybackService> logger) : IAudioPlaybackService
{
    public async Task PlayAudioAsync(byte[] audioData)
    {
        try
        {
            logger.LogInformation($"Playing audio data of {audioData.Length} bytes");
            
            // Create a temporary file for audio playback
            var tempPath = Path.GetTempFileName();
            var audioPath = Path.ChangeExtension(tempPath, ".wav");
            
            await File.WriteAllBytesAsync(audioPath, audioData);
            
            // Platform-specific audio playback
#if MACCATALYST
            await PlayAudioMacCatalyst(audioPath);
#else
            _logger.LogWarning("Audio playback not implemented for this platform");
#endif
            
            // Clean up temp file
            try
            {
                File.Delete(audioPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Could not delete temp audio file: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Failed to play audio: {ex.Message}");
            throw;
        }
    }

#if MACCATALYST
    private async Task PlayAudioMacCatalyst(string audioPath)
    {
        try
        {
            var audioSession = AVFoundation.AVAudioSession.SharedInstance();
            audioSession.SetCategory(AVFoundation.AVAudioSessionCategory.Playback);
            audioSession.SetActive(true);
            
            var url = Foundation.NSUrl.FromFilename(audioPath);
            var player = new AVFoundation.AVAudioPlayer(url, null, out var error);
            
            if (error != null)
                throw new Exception($"MacCatalyst audio player error: {error.Description}");
            
            player.Play();
            
            // Wait for playback to complete
            while (player.Playing)
            {
                await Task.Delay(100);
            }
            
            player.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogError($"MacCatalyst audio playback failed: {ex.Message}");
            throw;
        }
    }
#endif
}