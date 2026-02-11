using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PamPocClient.Models;
using PamPocClient.Services;

namespace PamPocClient.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IVoiceService _voiceService;
    private readonly IAudioRecordingService _audioRecordingService;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private const int ContextWindowMessageCount = 10;

    [ObservableProperty]
    private string _messageText = string.Empty;

    [ObservableProperty]
    private bool _isBusy = false;

    [ObservableProperty]
    private bool _isRecording = false;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    public MainViewModel(IVoiceService voiceService, IAudioRecordingService audioRecordingService, IAudioPlaybackService audioPlaybackService)
    {
        _voiceService = voiceService;
        _audioRecordingService = audioRecordingService;
        _audioPlaybackService = audioPlaybackService;
        InitializeAsync();
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(MessageText) || IsBusy)
            return;

        var userMessage = MessageText.Trim();
        MessageText = string.Empty;

        try
        {
            IsBusy = true;
            StatusMessage = "Sending message...";

            // Add user message to collection
            var userChatMessage = new ChatMessage
            {
                Role = "user",
                Content = userMessage,
                Timestamp = DateTime.Now
            };
            Messages.Add(userChatMessage);

            // Get response from API
            var allMessages = Messages.Take(ContextWindowMessageCount).ToList();
            var response = await _voiceService.GetChatCompletionAsync(allMessages);

            // Add assistant response to collection
            var assistantMessage = new ChatMessage
            {
                Role = "assistant",
                Content = response.Trim(),
                Timestamp = DateTime.Now
            };
            Messages.Add(assistantMessage);

            StatusMessage = "Ready";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            
            // Add error message to chat
            var errorMessage = new ChatMessage
            {
                Role = "assistant",
                Content = $"Sorry, I encountered an error: {ex.Message}",
                Timestamp = DateTime.Now
            };
            Messages.Add(errorMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ToggleRecordingAsync()
    {
        if (IsBusy)
            return;

        try
        {
            if (IsRecording)
            {
                await StopRecordingAsync();
            }
            else
            {
                await StartRecordingWithPermissionCheckAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Recording error: {ex.Message}";
            IsRecording = false;
        }
    }

    private async Task StartRecordingWithPermissionCheckAsync()
    {
        var permissionStatus = await _audioRecordingService.CheckPermissionStatusAsync();
        
        switch (permissionStatus)
        {
            case PermissionStatus.Granted:
                // Permission already granted, start recording
                System.Diagnostics.Debug.WriteLine("MainViewModel: Permission granted, starting recording");
                await StartRecordingAsync();
                break;
                
            case PermissionStatus.Denied:
                System.Diagnostics.Debug.WriteLine("MainViewModel: Permission denied");
                if (DeviceInfo.Platform == DevicePlatform.iOS)
                {
                    // On iOS, once denied, user needs to go to settings
                    StatusMessage = "Microphone access denied. Please enable it in Settings > Privacy & Security > Microphone";
                    await ShowPermissionDeniedAlertAsync();
                }
                else
                {
                    // On Android, we can try requesting again
                    await RequestAndStartRecordingAsync();
                }
                break;
                
            case PermissionStatus.Unknown:
            case PermissionStatus.Disabled:
            case PermissionStatus.Restricted:
            case PermissionStatus.Limited:
            default:
                await RequestAndStartRecordingAsync();
                break;
        }
    }

    private async Task RequestAndStartRecordingAsync()
    {
        StatusMessage = "Requesting microphone permission...";
        
        var permissionGranted = await _audioRecordingService.RequestPermissionsAsync();
        
        if (permissionGranted)
        {
            await StartRecordingAsync();
        }
        else
        {
            StatusMessage = "Microphone permission required for voice recording";
            await ShowPermissionDeniedAlertAsync();
        }
    }

    private async Task ShowPermissionDeniedAlertAsync()
    {
        try
        {
            var mainPage = Application.Current?.Windows?.FirstOrDefault()?.Page;
            if (mainPage == null)
                return;

            var result = await mainPage.DisplayAlert(
                "Microphone Permission Required",
                "This app needs microphone access to record your voice. Would you like to open Settings to enable it?",
                "Open Settings",
                "Cancel");

            if (result)
            {
                if (DeviceInfo.Platform == DevicePlatform.iOS)
                {
                    // Open iOS Settings app
                    await Launcher.OpenAsync("app-settings:");
                }
                else if (DeviceInfo.Platform == DevicePlatform.Android)
                {
                    // Open Android app settings - use intent to open app settings
                    await Launcher.OpenAsync($"package:{AppInfo.PackageName}");
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open settings: {ex.Message}";
        }
    }

    private async Task StartRecordingAsync()
    {
        try
        {
            IsRecording = true;
            StatusMessage = "Recording... Will auto-stop after 3s of silence";
            
            var recordingTask = _audioRecordingService.StartRecordingWithSilenceDetectionAsync(3);
            
            // Don't await here - let it run in background until user stops it
            // The recording will complete when StopRecordingAsync is called
            var audioData = await recordingTask;
            
            System.Diagnostics.Debug.WriteLine($"MainViewModel: Recording complete, received {audioData.Length} bytes");

            IsRecording = false;
            StatusMessage = "Playing back recording...";

            // Play back what we recorded for verification
            try
            {
                //await _audioPlaybackService.PlayAudioAsync(audioData);
                StatusMessage = "Playback complete. Sending to API...";

                // Process the recorded audio through the voice service
                await ProcessRecordedAudioAsync(audioData);
            }
            catch (Exception playbackEx)
            {
                System.Diagnostics.Debug.WriteLine($"MainViewModel: Playback failed - {playbackEx.Message}");
                StatusMessage = $"Playback failed: {playbackEx.Message}";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MainViewModel: Recording failed - {ex.Message}");
            StatusMessage = $"Recording failed: {ex.Message}";
            IsRecording = false;
        }
    }

    private async Task StopRecordingAsync()
    {
        try
        {
            await _audioRecordingService.StopRecordingAsync();
            IsRecording = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Stop recording failed: {ex.Message}";
            IsRecording = false;
        }
    }

    private async Task ProcessRecordedAudioAsync(byte[] audioData)
    {
        IsRecording = false;
        IsBusy = true;
        StatusMessage = "Processing speech...";

        try
        {
            System.Diagnostics.Debug.WriteLine($"MainViewModel: ProcessRecordedAudioAsync - Received {audioData.Length} bytes");
            
            if (audioData.Length > 0)
            {
                // Check if we have meaningful audio data (not just WAV header)
                var hasNonZeroData = audioData.Skip(44).Any(b => b != 0); // Skip WAV header
                System.Diagnostics.Debug.WriteLine($"MainViewModel: Audio has non-zero data beyond header: {hasNonZeroData}");
                
                // Send the 16kHz WAV file to the /voice API endpoint
                System.Diagnostics.Debug.WriteLine("MainViewModel: Sending audio to /voice endpoint...");
                // var response = await _voiceService.ProcessVoiceAsync(audioData);
                var response = await _voiceService.ProcessVoiceWithTextAsync(audioData);
                
                Messages.Add(new ChatMessage {
                    Role = "user",
                    Content = response.Transcript,
                    Timestamp = DateTime.Now
                });
                System.Diagnostics.Debug.WriteLine($"MainViewModel: API response: Transcript:'{response.Transcript}' Response:'{response.AssistantText}'");
                
                if (!string.IsNullOrWhiteSpace(response.Transcript))
                {
                    // If we got a transcribed response, set it as the message text
                    Messages.Add(new ChatMessage {
                        Role = "assistant",
                        Content = response.AssistantText,
                        Timestamp = DateTime.Now
                    });
                    System.Diagnostics.Debug.WriteLine($"MainViewModel: Assistant response: '{response.AssistantText}'");
                    
                    if (response.Audio.Length > 0)
                    {
                        await PlayAudioAsync(response.Audio);
                    }
                }
                else
                {
                    StatusMessage = "No transcription received from API";
                }
            }
            else
            {
                StatusMessage = "No audio recorded";
            }

            StatusMessage = "Ready";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Speech processing error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PlayAudioAsync(byte[] audioData)
    {
        try
        {
            await _audioPlaybackService.PlayAudioAsync(audioData);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MainViewModel: Audio playback failed - {ex.Message}");
            StatusMessage = $"Audio playback failed: {ex.Message}";
        }
    }

    private async void InitializeAsync()
    {
        try
        {
            StatusMessage = "Checking connection...";
            var isHealthy = await _voiceService.CheckHealthAsync();
            
            if (isHealthy)
            {
                StatusMessage = "Connected - Ready to chat";
                
                var welcomeMessage = new ChatMessage
                {
                    Role = "assistant",
                    Content = "Hello! I'm Pam, your voice assistant. How can I help you today?",
                    Timestamp = DateTime.Now
                };
                Messages.Add(welcomeMessage);
            }
            else
            {
                StatusMessage = "Warning: API service not available";
            }
        }
        catch (Exception)
        {
            StatusMessage = "Warning: Could not connect to API service";
        }
    }
}