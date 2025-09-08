using System;
using System.Threading.Tasks;

namespace PamPocClient.Services;

public interface IAudioPlaybackService
{
    Task PlayAudioAsync(byte[] audioData);
}