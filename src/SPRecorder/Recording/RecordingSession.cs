using NAudio.Wave;
using SPRecorder.Audio;
using SPRecorder.Configuration;
using SPRecorder.Overlay;
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
    private ScreenRecorder? _screenRecorder;
    private InputHighlightOverlay? _overlay;

    public State CurrentState { get; private set; } = State.Idle;
    public DateTime StartedAt => _startedAt;
    public string? SystemFilePath { get; private set; }
    public string? MicFilePath { get; private set; }
    public string? MixedFilePath { get; private set; }
    public string? ScreenFilePath { get; private set; }

    public event Action<State>? StateChanged;
    public event Action<string>? Warning;
    public event Action? MixingStarted;
    public event Action<string?>? MixingCompleted;
    public event Action<int>? SplitCompleted;  // arg = total chunks across all tracks

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

        if (_activeConfig.ScreenRecordingEnabled)
            StartScreenTrack();

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

    private void StartScreenTrack()
    {
        ScreenFilePath = Path.Combine(_activeConfig.OutputDirectory,
            FileNameBuilder.BuildScreen(_activeConfig.FileNamePattern, _startedAt));

        if (_activeConfig.ShowKeystrokes)
        {
            try
            {
                var monitor = MonitorForDevice(_activeConfig.ScreenMonitorDeviceName);
                _overlay = new InputHighlightOverlay();
                _overlay.Show(monitor);
            }
            catch (Exception ex)
            {
                Warning?.Invoke("Keystroke overlay unavailable; recording screen without it. " + ex.Message);
                try { _overlay?.HideAndDispose(); } catch { /* ignore */ }
                _overlay = null;
            }
        }

        try
        {
            _screenRecorder = new ScreenRecorder();
            _screenRecorder.Failed += msg => Warning?.Invoke(msg);
            _screenRecorder.Start(ScreenFilePath, _activeConfig);
        }
        catch (Exception ex)
        {
            Warning?.Invoke("Screen recording could not start; continuing with audio only. " + ex.Message);
            _screenRecorder = null;
            ScreenFilePath = null;
        }
    }

    private static System.Windows.Forms.Screen MonitorForDevice(string deviceName)
    {
        if (!string.IsNullOrEmpty(deviceName))
        {
            foreach (var s in System.Windows.Forms.Screen.AllScreens)
                if (s.DeviceName == deviceName) return s;
        }
        return System.Windows.Forms.Screen.PrimaryScreen!;
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

        try { _screenRecorder?.Stop(); } catch (Exception ex) { Warning?.Invoke("Screen stop: " + ex.Message); }
        try { _overlay?.HideAndDispose(); } catch { /* ignore */ }
        _screenRecorder?.Dispose();
        _screenRecorder = null;
        _overlay = null;

        CurrentState = State.Idle;
        StateChanged?.Invoke(CurrentState);

        if (_activeConfig.PromptForSessionName)
            TryRenameToSessionFolder();

        var willMix   = _activeConfig.MixedFileEnabled;
        var willSplit = !_activeConfig.SplitMode.Equals("None", StringComparison.OrdinalIgnoreCase);

        if (willMix || willSplit)
            StartPostProcessingInBackground(willMix, willSplit);
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

            if (ScreenFilePath is not null && File.Exists(ScreenFilePath))
            {
                var newScreen = Path.Combine(folder, $"{folderName}_screen.mp4");
                File.Move(ScreenFilePath, newScreen);
                ScreenFilePath = newScreen;
            }
        }
        catch (Exception ex)
        {
            Warning?.Invoke("Could not rename to session folder: " + ex.Message);
        }
    }

    private void StartPostProcessingInBackground(bool willMix, bool willSplit)
    {
        var sysPath   = SystemFilePath;
        var micPath   = MicFilePath;
        var mixedPath = MixedFilePath;
        var cfg       = _activeConfig;
        var bitrate   = cfg.Mp3BitrateKbps;
        var sampleRate = cfg.MixedFileSampleRate;
        var stereo    = cfg.MixedFileFormat.Equals("Stereo", StringComparison.OrdinalIgnoreCase);

        if (sysPath is null || micPath is null || mixedPath is null) return;
        if (!File.Exists(sysPath) || !File.Exists(micPath)) return;

        MixingStarted?.Invoke();
        Task.Run(() =>
        {
            string? finalMixedPath = null;
            if (willMix)
            {
                try
                {
                    if (stereo)
                        Mp3Mixer.MixToStereo(sysPath, micPath, mixedPath, bitrate, sampleRate);
                    else
                        Mp3Mixer.MixToMono(sysPath, micPath, mixedPath, bitrate, sampleRate);
                    finalMixedPath = mixedPath;
                }
                catch (Exception ex)
                {
                    Warning?.Invoke("Mixing failed: " + ex.Message);
                }
            }

            int totalChunks = 0;
            if (willSplit)
            {
                var splitter = new Mp3FrameSplitter();
                if (cfg.SplitSystemTrack)            totalChunks += SplitTrack(splitter, sysPath,   cfg);
                if (cfg.SplitMicTrack)               totalChunks += SplitTrack(splitter, micPath,   cfg);
                if (cfg.SplitMixedTrack && finalMixedPath is not null)
                                             totalChunks += SplitTrack(splitter, finalMixedPath, cfg);
            }

            MixingCompleted?.Invoke(finalMixedPath);
            if (willSplit) SplitCompleted?.Invoke(totalChunks);
        });
    }

    private int SplitTrack(IMp3Splitter splitter, string path, AppConfig cfg)
    {
        if (!File.Exists(path)) return 0;
        try
        {
            var chunks = cfg.SplitMode.Equals("Time", StringComparison.OrdinalIgnoreCase)
                ? splitter.SplitByTime(path, TimeSpan.FromMinutes(cfg.SplitTimeMinutes))
                : splitter.SplitBySize(path, (long)cfg.SplitSizeMb * 1024L * 1024L);

            if (chunks.Count > 1)
            {
                File.Delete(path);
                return chunks.Count;
            }
            return 0;
        }
        catch (Exception ex)
        {
            Warning?.Invoke($"Split failed for {Path.GetFileName(path)}: {ex.Message}");
            return 0;
        }
    }

    public TimeSpan? Elapsed => CurrentState == State.Recording ? DateTime.Now - _startedAt : null;

    public void Dispose() { if (CurrentState == State.Recording) Stop(); }
}
