using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SPRecorder.Audio;

public sealed class MicrophoneCapture : IDisposable
{
    private readonly WasapiCapture _capture;

    public WaveFormat WaveFormat => _capture.WaveFormat;
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public MicrophoneCapture()
    {
        _capture = new WasapiCapture();
        _capture.DataAvailable    += (s, e) => DataAvailable?.Invoke(s, e);
        _capture.RecordingStopped += (s, e) => RecordingStopped?.Invoke(s, e);
    }

    public void Start() => _capture.StartRecording();
    public void Stop()  => _capture.StopRecording();
    public void Dispose() => _capture.Dispose();
}
