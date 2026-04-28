using NAudio.Wave;
using SPRecorder.Audio;
using SPRecorder.Configuration;
using SPRecorder.Settings;

namespace SPRecorder.Recording;

public sealed class RecordingSession : IDisposable
{
    public enum State { Idle, Recording }

    private readonly Func<AppConfig> _configGetter;
    private readonly Func<string?>? _sessionNamePrompt;
    private AppConfig _activeConfig = new();
    private SystemAudioCapture? _systemCapture;
    private MicrophoneCapture? _micCapture;
    private Mp3StreamWriter? _systemWriter;
    private Mp3StreamWriter? _micWriter;
    private DateTime _startedAt;

    public State CurrentState { get; private set; } = State.Idle;
    public DateTime StartedAt => _startedAt;
    public string? SystemFilePath { get; private set; }
    public string? MicFilePath { get; private set; }
    public string? MixedFilePath { get; private set; }

    public event Action<State>? StateChanged;
    public event Action<string>? Warning;
    public event Action? MixingStarted;
    public event Action<string?>? MixingCompleted;

    public RecordingSession(Func<AppConfig> configGetter, Func<string?>? sessionNamePrompt = null)
    {
        _configGetter = configGetter;
        _sessionNamePrompt = sessionNamePrompt;
    }

    public void Toggle()
    {
        if (CurrentState == State.Idle) Start();
        else Stop();
    }

    public void Start()
    {
        if (CurrentState != State.Idle) return;

        _activeConfig = _configGetter();
        Directory.CreateDirectory(_activeConfig.OutputDirectory);
        _startedAt = DateTime.Now;

        SystemFilePath = Path.Combine(_activeConfig.OutputDirectory,
            FileNameBuilder.Build(_activeConfig.FileNamePattern, _startedAt, "system"));
        MicFilePath = Path.Combine(_activeConfig.OutputDirectory,
            FileNameBuilder.Build(_activeConfig.FileNamePattern, _startedAt, "mic"));
        MixedFilePath = Path.Combine(_activeConfig.OutputDirectory,
            FileNameBuilder.Build(_activeConfig.FileNamePattern, _startedAt, "mixed"));

        _systemCapture = new SystemAudioCapture(_activeConfig.SystemAudioDeviceId);
        _micCapture = new MicrophoneCapture(_activeConfig.MicrophoneDeviceId);
        _systemWriter = new Mp3StreamWriter(SystemFilePath, _systemCapture.WaveFormat, _activeConfig.Mp3BitrateKbps);
        _micWriter    = new Mp3StreamWriter(MicFilePath,    _micCapture.WaveFormat,    _activeConfig.Mp3BitrateKbps);

        _systemCapture.DataAvailable    += OnSystemData;
        _micCapture.DataAvailable       += OnMicData;
        _systemCapture.RecordingStopped += (_, e) => OnCaptureStopped("System audio", e);
        _micCapture.RecordingStopped    += (_, e) => OnCaptureStopped("Microphone", e);

        _systemCapture.Start();
        _micCapture.Start();

        CurrentState = State.Recording;
        StateChanged?.Invoke(CurrentState);
    }

    private void OnSystemData(object? _, WaveInEventArgs e) => _systemWriter?.Write(e.Buffer, 0, e.BytesRecorded);
    private void OnMicData(object? _, WaveInEventArgs e)    => _micWriter?.Write(e.Buffer, 0, e.BytesRecorded);

    private void OnCaptureStopped(string source, StoppedEventArgs e)
    {
        if (e.Exception != null)
            Warning?.Invoke($"{source} stopped unexpectedly: {e.Exception.Message}");
    }

    public void Stop()
    {
        if (CurrentState != State.Recording) return;

        try { _systemCapture?.Stop(); } catch (Exception ex) { Warning?.Invoke("System stop: " + ex.Message); }
        try { _micCapture?.Stop();    } catch (Exception ex) { Warning?.Invoke("Mic stop: " + ex.Message); }

        _systemWriter?.Dispose();
        _micWriter?.Dispose();
        _systemCapture?.Dispose();
        _micCapture?.Dispose();
        _systemWriter = null;
        _micWriter = null;
        _systemCapture = null;
        _micCapture = null;

        CurrentState = State.Idle;
        StateChanged?.Invoke(CurrentState);

        if (_activeConfig.PromptForSessionName)
            TryRenameToSessionFolder();

        if (_activeConfig.MixedFileEnabled)
            StartMixingInBackground();
    }

    private void TryRenameToSessionFolder()
    {
        if (_sessionNamePrompt is null) return;
        if (SystemFilePath is null || MicFilePath is null || MixedFilePath is null) return;

        var rawName = _sessionNamePrompt();
        var clean = SessionNamePrompt.Sanitize(rawName ?? "");
        if (string.IsNullOrEmpty(clean)) return;

        var folderName = $"{clean}_{_startedAt:yyyy-MM-dd_HH-mm-ss}";
        var folder = Path.Combine(_activeConfig.OutputDirectory, folderName);
        try
        {
            Directory.CreateDirectory(folder);

            var newSystem = Path.Combine(folder, $"{folderName}_system.mp3");
            var newMic    = Path.Combine(folder, $"{folderName}_mic.mp3");
            var newMixed  = Path.Combine(folder, $"{folderName}_mixed.mp3");

            if (File.Exists(SystemFilePath)) File.Move(SystemFilePath, newSystem);
            if (File.Exists(MicFilePath))    File.Move(MicFilePath,    newMic);

            SystemFilePath = newSystem;
            MicFilePath    = newMic;
            MixedFilePath  = newMixed;
        }
        catch (Exception ex)
        {
            Warning?.Invoke("Could not rename to session folder: " + ex.Message);
        }
    }

    private void StartMixingInBackground()
    {
        var sysPath = SystemFilePath;
        var micPath = MicFilePath;
        var mixedPath = MixedFilePath;
        var bitrate = _activeConfig.Mp3BitrateKbps;
        var sampleRate = _activeConfig.MixedFileSampleRate;
        var stereo = _activeConfig.MixedFileFormat.Equals("Stereo", StringComparison.OrdinalIgnoreCase);

        if (sysPath is null || micPath is null || mixedPath is null) return;
        if (!File.Exists(sysPath) || !File.Exists(micPath)) return;

        MixingStarted?.Invoke();
        Task.Run(() =>
        {
            try
            {
                if (stereo)
                    Mp3Mixer.MixToStereo(sysPath, micPath, mixedPath, bitrate, sampleRate);
                else
                    Mp3Mixer.MixToMono(sysPath, micPath, mixedPath, bitrate, sampleRate);
                MixingCompleted?.Invoke(mixedPath);
            }
            catch (Exception ex)
            {
                Warning?.Invoke("Mixing failed: " + ex.Message);
                MixingCompleted?.Invoke(null);
            }
        });
    }

    public TimeSpan? Elapsed => CurrentState == State.Recording ? DateTime.Now - _startedAt : null;

    public void Dispose() { if (CurrentState == State.Recording) Stop(); }
}
