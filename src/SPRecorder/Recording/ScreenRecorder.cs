using ScreenRecorderLib;
using SPRecorder.Configuration;

namespace SPRecorder.Recording;

/// <summary>A single connected display the user can pick to record.</summary>
public readonly record struct DisplayInfo(string DeviceName, string FriendlyName);

/// <summary>
/// Wraps ScreenRecorderLib. Records one monitor (default primary) to an MP4 with
/// system+mic audio embedded and a built-in mouse-click highlight. The ONLY type
/// that references ScreenRecorderLib, so API drift is contained here.
/// </summary>
public sealed class ScreenRecorder : IDisposable
{
    private Recorder? _recorder;
    private readonly ManualResetEventSlim _completed = new(false);

    public event Action<string>? Failed;

    /// <summary>The \\.\DISPLAYn actually used (after primary fallback), for logging.</summary>
    public string? ResolvedDeviceName { get; private set; }

    /// <summary>Enumerate connected displays for the Settings picker.</summary>
    public static IReadOnlyList<DisplayInfo> GetDisplays()
    {
        try
        {
            return Recorder.GetDisplays()
                .Select(d => new DisplayInfo(d.DeviceName ?? "", d.FriendlyName ?? ""))
                .ToList();
        }
        catch
        {
            return Array.Empty<DisplayInfo>();
        }
    }

    public void Start(string filePath, AppConfig cfg)
    {
        // Defensive: a second Start() without an intervening Stop() must not leak
        // the previous native recorder.
        if (_recorder is not null)
            Stop();

        // Pick the configured monitor; fall back to primary if it is gone.
        DisplayRecordingSource source;
        var wanted = cfg.ScreenMonitorDeviceName;
        if (!string.IsNullOrEmpty(wanted) &&
            Recorder.GetDisplays().Any(d => d.DeviceName == wanted))
        {
            source = new DisplayRecordingSource(wanted);
        }
        else
        {
            if (!string.IsNullOrEmpty(wanted))
                Failed?.Invoke($"Monitor {wanted} not found; recording the primary monitor instead.");
            source = DisplayRecordingSource.MainMonitor
                     ?? new DisplayRecordingSource();
        }
        source.IsCursorCaptureEnabled = true;
        ResolvedDeviceName = source.DeviceName;

        // In 6.6.0 VideoEncoderOptions holds Framerate/Quality; BitrateMode lives on
        // the encoder object (H264VideoEncoder) not on VideoEncoderOptions directly.
        var encoder = new H264VideoEncoder
        {
            BitrateMode = H264BitrateControlMode.Quality,
        };

        var options = new RecorderOptions
        {
            SourceOptions = new SourceOptions
            {
                RecordingSources = new List<RecordingSourceBase> { source },
            },
            AudioOptions = new AudioOptions
            {
                IsAudioEnabled      = true,
                IsOutputDeviceEnabled = true, // system loopback
                IsInputDeviceEnabled  = true, // microphone
            },
            VideoEncoderOptions = new VideoEncoderOptions
            {
                Framerate = cfg.ScreenFrameRate,
                Quality   = QualityFor(cfg.ScreenQuality),
                Encoder   = encoder,
            },
            MouseOptions = new MouseOptions
            {
                IsMouseClicksDetected = cfg.ShowMouseClicks,
                IsMousePointerEnabled = true,
            },
        };

        _completed.Reset();
        _recorder = Recorder.CreateRecorder(options);
        _recorder.OnRecordingComplete += (_, _) => _completed.Set();
        _recorder.OnRecordingFailed   += (_, e) =>
        {
            Failed?.Invoke("Screen recording failed: " + e.Error);
            _completed.Set();
        };
        _recorder.Record(filePath);
    }

    public void Stop()
    {
        var rec = _recorder;
        if (rec is null) return;
        try
        {
            rec.Stop();
            // ScreenRecorderLib finalizes the MP4 asynchronously; wait briefly so the
            // file is closed before the caller renames/moves it.
            _completed.Wait(TimeSpan.FromSeconds(15));
        }
        catch (Exception ex)
        {
            Failed?.Invoke("Screen stop: " + ex.Message);
        }
        finally
        {
            rec.Dispose();
            _recorder = null;
        }
    }

    private static int QualityFor(string q) => q switch
    {
        "Low"  => 40,
        "High" => 90,
        _      => 70, // Medium
    };

    public void Dispose()
    {
        try { _recorder?.Dispose(); } catch { /* ignore */ }
        _recorder = null;
        _completed.Dispose();
    }
}
