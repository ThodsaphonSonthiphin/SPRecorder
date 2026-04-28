using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SPRecorder.Audio;

public sealed class MicrophoneCapture : IDisposable
{
    private readonly WasapiCapture _capture;

    public WaveFormat WaveFormat => _capture.WaveFormat;
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public MicrophoneCapture(string deviceId = "")
    {
        var device = ResolveDevice(deviceId);
        _capture = device is null ? new WasapiCapture() : new WasapiCapture(device);
        _capture.DataAvailable    += (s, e) => DataAvailable?.Invoke(s, e);
        _capture.RecordingStopped += (s, e) => RecordingStopped?.Invoke(s, e);
    }

    private static MMDevice? ResolveDevice(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return null;
        using var enumerator = new MMDeviceEnumerator();
        try { return enumerator.GetDevice(deviceId); } catch { return null; }
    }

    public void Start() => _capture.StartRecording();
    public void Stop()  => _capture.StopRecording();
    public void Dispose() => _capture.Dispose();
}
