using NAudio.Wave;
using SPRecorder.Audio;
using SPRecorder.Configuration;

namespace SPRecorder.Recording;

public sealed class RecordingSession : IDisposable
{
    public enum State { Idle, Recording }

    private readonly AppConfig _config;
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
    public event Action<string?>? MixingCompleted; // path on success, null on failure

    public RecordingSession(AppConfig config) => _config = config;

    public void Toggle()
    {
        if (CurrentState == State.Idle) Start();
        else Stop();
    }

    public void Start()
    {
        if (CurrentState != State.Idle) return;

        Directory.CreateDirectory(_config.OutputDirectory);
        _startedAt = DateTime.Now;
        SystemFilePath = Path.Combine(_config.OutputDirectory,
            FileNameBuilder.Build(_config.FileNamePattern, _startedAt, "system"));
        MicFilePath = Path.Combine(_config.OutputDirectory,
            FileNameBuilder.Build(_config.FileNamePattern, _startedAt, "mic"));
        MixedFilePath = Path.Combine(_config.OutputDirectory,
            FileNameBuilder.Build(_config.FileNamePattern, _startedAt, "mixed"));

        _systemCapture = new SystemAudioCapture();
        _micCapture = new MicrophoneCapture();
        _systemWriter = new Mp3StreamWriter(SystemFilePath, _systemCapture.WaveFormat, _config.Mp3BitrateKbps);
        _micWriter    = new Mp3StreamWriter(MicFilePath,    _micCapture.WaveFormat,    _config.Mp3BitrateKbps);

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

        StartMixingInBackground();
    }

    private void StartMixingInBackground()
    {
        var sysPath = SystemFilePath;
        var micPath = MicFilePath;
        var mixedPath = MixedFilePath;
        var bitrate = _config.Mp3BitrateKbps;
        if (sysPath is null || micPath is null || mixedPath is null) return;
        if (!File.Exists(sysPath) || !File.Exists(micPath)) return;

        MixingStarted?.Invoke();
        Task.Run(() =>
        {
            try
            {
                Mp3Mixer.MixToMono(sysPath, micPath, mixedPath, bitrate);
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
