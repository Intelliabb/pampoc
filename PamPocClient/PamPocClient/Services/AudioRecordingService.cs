using AVFoundation;
using Foundation;
using AudioToolbox;

namespace PamPocClient.Services;

public interface IAudioRecordingService
{
    Task<PermissionStatus> CheckPermissionStatusAsync();
    Task<bool> RequestPermissionsAsync();
    Task<byte[]> StartRecordingWithSilenceDetectionAsync(int silenceTimeoutSeconds = 3);
    Task StopRecordingAsync();
    bool IsRecording { get; }
}

public class AudioRecordingService : IAudioRecordingService, IDisposable
{
    private AVAudioRecorder? _recorder;
    private CancellationTokenSource? _cancellationTokenSource;
    private string? _recordingFilePath;

    public bool IsRecording { get; private set; }

    public async Task<PermissionStatus> CheckPermissionStatusAsync()
    {
        try
        {
            var authStatus = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Audio);
            return authStatus switch
            {
                AVAuthorizationStatus.Authorized => PermissionStatus.Granted,
                AVAuthorizationStatus.Denied => PermissionStatus.Denied,
                AVAuthorizationStatus.Restricted => PermissionStatus.Restricted,
                _ => PermissionStatus.Unknown
            };
        }
        catch (Exception)
        {
            return PermissionStatus.Unknown;
        }
    }

    public async Task<bool> RequestPermissionsAsync()
    {
        try
        {
            var currentStatus = await CheckPermissionStatusAsync();

            if (currentStatus == PermissionStatus.Granted)
                return true;

            if (currentStatus == PermissionStatus.Denied)
            {
                // Already denied, user needs to go to System Settings
                return false;
            }

            // Request permission
            var tcs = new TaskCompletionSource<bool>();
            AVCaptureDevice.RequestAccessForMediaType(AVAuthorizationMediaType.Audio, (granted) =>
            {
                tcs.SetResult(granted);
            });

            return await tcs.Task;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AudioRecordingService: Permission request failed - {ex.Message}");
            return false;
        }
    }

    public async Task<byte[]> StartRecordingWithSilenceDetectionAsync(int silenceTimeoutSeconds = 3)
    {
        if (IsRecording)
            throw new InvalidOperationException("Recording is already in progress");

        // Check permission
        var permissionStatus = await CheckPermissionStatusAsync();
        System.Diagnostics.Debug.WriteLine($"AudioRecordingService: Permission status: {permissionStatus}");

        if (permissionStatus != PermissionStatus.Granted)
        {
            if (permissionStatus == PermissionStatus.Denied || permissionStatus == PermissionStatus.Restricted)
            {
                throw new UnauthorizedAccessException("Microphone access denied. Please enable it in System Settings > Privacy & Security > Microphone");
            }

            // Try to request permission
            var granted = await RequestPermissionsAsync();
            if (!granted)
            {
                throw new UnauthorizedAccessException("Microphone access denied");
            }
        }

        System.Diagnostics.Debug.WriteLine("AudioRecordingService: Starting recording...");

        return await RecordAudioAsync(silenceTimeoutSeconds);
    }

    private async Task<byte[]> RecordAudioAsync(int silenceTimeoutSeconds)
    {
        try
        {
            _cancellationTokenSource = new CancellationTokenSource();

            // Create recording file path in Documents directory
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _recordingFilePath = Path.Combine(documentsPath, $"recording_{Guid.NewGuid()}.caf");

            System.Diagnostics.Debug.WriteLine($"AudioRecordingService: Recording to: {_recordingFilePath}");

            var url = NSUrl.FromFilename(_recordingFilePath);

            // Configure recording settings - using Apple Lossless for compatibility
            var settings = new NSDictionary(
                AVAudioSettings.AVFormatIDKey, NSNumber.FromInt32((int)AudioFormatType.AppleLossless),
                AVAudioSettings.AVSampleRateKey, NSNumber.FromFloat(16000.0f),
                AVAudioSettings.AVNumberOfChannelsKey, NSNumber.FromInt32(1),
                AVAudioSettings.AVEncoderAudioQualityKey, NSNumber.FromInt32((int)AVAudioQuality.High)
            );

            // Create the recorder
            NSError? error;
            _recorder = AVAudioRecorder.Create(url, new AudioSettings(settings), out error);

            if (error != null)
            {
                System.Diagnostics.Debug.WriteLine($"AudioRecordingService: Failed to create recorder - {error.Description}");
                throw new Exception($"Failed to create audio recorder: {error.Description}");
            }

            if (_recorder == null)
            {
                throw new Exception("Failed to create audio recorder - returned null");
            }

            System.Diagnostics.Debug.WriteLine("AudioRecordingService: Recorder created successfully");

            // Prepare to record
            if (!_recorder.PrepareToRecord())
            {
                throw new Exception("Failed to prepare recorder");
            }

            // Enable metering for silence detection
            _recorder.MeteringEnabled = true;

            // Start recording
            if (!_recorder.Record())
            {
                System.Diagnostics.Debug.WriteLine("AudioRecordingService: Record() returned false");
                throw new Exception("Failed to start recording. Please check System Settings > Privacy & Security > Microphone and ensure this app has access.");
            }

            IsRecording = true;
            System.Diagnostics.Debug.WriteLine($"AudioRecordingService: Recording started, monitoring for {silenceTimeoutSeconds}s of silence...");

            // Monitor for silence
            await MonitorSilenceAsync(silenceTimeoutSeconds, _cancellationTokenSource.Token);

            // Stop recording
            System.Diagnostics.Debug.WriteLine("AudioRecordingService: Stopping recording...");
            _recorder.Stop();
            IsRecording = false;

            System.Diagnostics.Debug.WriteLine($"AudioRecordingService: Recording duration: {_recorder.CurrentTime}s");

            // Read the recorded file
            if (!File.Exists(_recordingFilePath))
            {
                throw new Exception("Recorded file not found");
            }

            var fileInfo = new FileInfo(_recordingFilePath);
            System.Diagnostics.Debug.WriteLine($"AudioRecordingService: Recorded file size: {fileInfo.Length} bytes");

            var audioBytes = await File.ReadAllBytesAsync(_recordingFilePath);

            // Verify we have audio data
            if (audioBytes.Length > 100)
            {
                var hasData = audioBytes.Skip(100).Take(1000).Any(b => b != 0);
                System.Diagnostics.Debug.WriteLine($"AudioRecordingService: Has non-zero audio data: {hasData}");
            }

            // Clean up
            _recorder.Dispose();
            _recorder = null;

            return audioBytes;
        }
        catch (Exception ex)
        {
            IsRecording = false;
            _recorder?.Dispose();
            _recorder = null;
            System.Diagnostics.Debug.WriteLine($"AudioRecordingService: Recording failed - {ex.Message}");
            throw;
        }
    }

    private async Task MonitorSilenceAsync(int silenceThresholdSeconds, CancellationToken cancellationToken)
    {
        var recordingStart = DateTime.Now;
        var lastAudioActivity = DateTime.Now;
        const int maxRecordingSeconds = 20;
        const float silenceThresholdDb = -30.0f;

        System.Diagnostics.Debug.WriteLine($"AudioRecordingService: Monitoring - will stop after {silenceThresholdSeconds}s silence or {maxRecordingSeconds}s total");

        try
        {
            while (!cancellationToken.IsCancellationRequested && IsRecording)
            {
                await Task.Delay(200, cancellationToken);

                var now = DateTime.Now;
                var recordingDuration = now - recordingStart;
                bool audioDetected = false;

                if (_recorder != null && _recorder.Recording)
                {
                    // Grace period - assume activity for first 3 seconds
                    if (recordingDuration.TotalSeconds < 3.0)
                    {
                        audioDetected = true;
                        lastAudioActivity = now;
                    }
                    else if (_recorder.MeteringEnabled)
                    {
                        _recorder.UpdateMeters();
                        var peakPower = _recorder.PeakPower(0);

                        if (peakPower > silenceThresholdDb)
                        {
                            audioDetected = true;
                            lastAudioActivity = now;
                            System.Diagnostics.Debug.WriteLine($"AudioRecordingService: Audio detected - peak: {peakPower:F1}dB");
                        }
                    }
                }

                var silenceDuration = now - lastAudioActivity;

                // Check if silence threshold reached
                if (silenceDuration.TotalSeconds >= silenceThresholdSeconds && recordingDuration.TotalSeconds > 3)
                {
                    System.Diagnostics.Debug.WriteLine($"AudioRecordingService: Silence threshold reached ({silenceThresholdSeconds}s)");
                    break;
                }

                // Check max duration
                if (recordingDuration.TotalSeconds > maxRecordingSeconds)
                {
                    System.Diagnostics.Debug.WriteLine("AudioRecordingService: Max recording duration reached");
                    break;
                }
            }
        }
        catch (TaskCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("AudioRecordingService: Monitoring cancelled");
        }
    }

    public async Task StopRecordingAsync()
    {
        if (!IsRecording)
            return;

        _cancellationTokenSource?.Cancel();

        if (_recorder != null)
        {
            _recorder.Stop();
            _recorder.Dispose();
            _recorder = null;
        }

        IsRecording = false;

        await Task.CompletedTask;
    }

    public void Dispose()
    {
        if (IsRecording)
        {
            try
            {
                StopRecordingAsync().Wait();
            }
            catch { }
        }

        _recorder?.Dispose();
        _recorder = null;
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        // Clean up recording file if it exists
        if (_recordingFilePath != null && File.Exists(_recordingFilePath))
        {
            try
            {
                File.Delete(_recordingFilePath);
            }
            catch { }
        }
    }
}
