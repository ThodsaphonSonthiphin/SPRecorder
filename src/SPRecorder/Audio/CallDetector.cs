using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace SPRecorder.Audio;

/// <summary>
/// Polls Windows audio sessions every 2 s to determine whether some other process
/// is using both the microphone and a render device simultaneously (i.e. is in a call).
/// Wraps a <see cref="CallDetectionStateMachine"/> for the debounce logic.
/// </summary>
public sealed class CallDetector : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OnDebounce   = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OffDebounce  = TimeSpan.FromSeconds(15);

    private readonly System.Threading.Timer _timer;
    private readonly CallDetectionStateMachine _sm;
    private readonly int _ownPid;
    private bool _running;

    public event Action? CallStarted;
    public event Action? CallEnded;

    public CallDetector()
    {
        _ownPid = Process.GetCurrentProcess().Id;
        _sm = new CallDetectionStateMachine(OnDebounce, OffDebounce);
        _sm.Detected += () => CallStarted?.Invoke();
        _sm.Cleared  += () => CallEnded?.Invoke();
        _timer = new System.Threading.Timer(Tick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _timer.Change(TimeSpan.Zero, PollInterval);
    }

    public void Stop()
    {
        _running = false;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void Tick(object? _)
    {
        try
        {
            var inCall = DetectInCall();
            _sm.Update(inCall, DateTime.UtcNow);
        }
        catch
        {
            // Swallow — try again next tick. NAudio enumeration can throw transient COM errors.
        }
    }

    private bool DetectInCall()
    {
        using var enumerator = new MMDeviceEnumerator();
        var renderPids  = CollectActivePids(enumerator, DataFlow.Render);
        var capturePids = CollectActivePids(enumerator, DataFlow.Capture);

        renderPids.IntersectWith(capturePids);
        renderPids.Remove(_ownPid);
        renderPids.Remove(0); // System Sounds session uses PID 0
        return renderPids.Count > 0;
    }

    private static HashSet<int> CollectActivePids(MMDeviceEnumerator enumerator, DataFlow flow)
    {
        var result = new HashSet<int>();
        var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
        foreach (var device in devices)
        {
            try
            {
                var sessions = device.AudioSessionManager.Sessions;
                if (sessions == null) continue;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    if (session.State == AudioSessionState.AudioSessionStateActive)
                        result.Add((int)session.GetProcessID);
                }
            }
            catch
            {
                // Skip device on error
            }
            finally
            {
                device.Dispose();
            }
        }
        return result;
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
