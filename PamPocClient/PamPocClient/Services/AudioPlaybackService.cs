using Microsoft.Extensions.Logging;
using AVFoundation;
using Foundation;

namespace PamPocClient.Services;

public class AudioPlaybackService(ILogger<AudioPlaybackService> logger) : IAudioPlaybackService
{
    public async Task PlayAudioAsync(byte[] audioData)
    {
        try
        {
            logger.LogInformation($"Playing audio data of {audioData.Length} bytes");
            System.Diagnostics.Debug.WriteLine($"AudioPlaybackService: Playing {audioData.Length} bytes");

            // Save audio data to temporary file
            var tempPath = Path.Combine(Path.GetTempPath(), $"playback_{Guid.NewGuid()}.caf");
            await File.WriteAllBytesAsync(tempPath, audioData);

            System.Diagnostics.Debug.WriteLine($"AudioPlaybackService: Saved to: {tempPath}");

            // Play the audio file
            await PlayAudioFile(tempPath);

            // Clean up
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Could not delete temp audio file: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Failed to play audio: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"AudioPlaybackService: Error - {ex.Message}");
            throw;
        }
    }

    private async Task PlayAudioFile(string filePath)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"AudioPlaybackService: Playing file: {filePath}");

            if (!File.Exists(filePath))
            {
                throw new Exception($"Audio file not found: {filePath}");
            }

            var fileInfo = new FileInfo(filePath);
            System.Diagnostics.Debug.WriteLine($"AudioPlaybackService: File size: {fileInfo.Length} bytes");

            var url = NSUrl.FromFilename(filePath);
            NSError? error;
            var player = new AVAudioPlayer(url, null, out error);

            if (error != null)
            {
                System.Diagnostics.Debug.WriteLine($"AudioPlaybackService: Player error: {error.Description}");
                throw new Exception($"Failed to create audio player: {error.Description}");
            }

            if (player == null)
            {
                throw new Exception("Failed to create audio player - returned null");
            }

            System.Diagnostics.Debug.WriteLine($"AudioPlaybackService: Player created - Duration: {player.Duration}s, Channels: {player.NumberOfChannels}");

            if (player.Duration <= 0)
            {
                throw new Exception("Invalid audio duration - file may be corrupt or empty");
            }

            // Start playback
            if (!player.Play())
            {
                throw new Exception("Failed to start playback");
            }

            System.Diagnostics.Debug.WriteLine("AudioPlaybackService: Playback started");

            // Wait for playback to complete
            while (player.Playing)
            {
                await Task.Delay(100);
            }

            System.Diagnostics.Debug.WriteLine("AudioPlaybackService: Playback completed");
            player.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AudioPlaybackService: Playback failed - {ex.Message}");
            throw;
        }
    }
}
