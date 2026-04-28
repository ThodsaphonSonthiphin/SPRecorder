namespace SPRecorder.Audio;

/// <summary>
/// Debounced two-state machine driven by a boolean "in call" signal.
/// Pure (no I/O, no timers) — caller polls <see cref="Update"/> with synthetic time
/// so this class is fully unit-testable.
/// </summary>
public sealed class CallDetectionStateMachine
{
    public enum State { NotDetected, Detected }

    private readonly TimeSpan _onDebounce;
    private readonly TimeSpan _offDebounce;

    private State _state = State.NotDetected;
    private DateTime? _conditionStartedAt;

    public State Current => _state;

    public event Action? Detected;
    public event Action? Cleared;

    public CallDetectionStateMachine(TimeSpan onDebounce, TimeSpan offDebounce)
    {
        _onDebounce  = onDebounce;
        _offDebounce = offDebounce;
    }

    public void Update(bool inCall, DateTime now)
    {
        if (_state == State.NotDetected)
        {
            if (inCall)
            {
                _conditionStartedAt ??= now;
                if (now - _conditionStartedAt.Value >= _onDebounce)
                {
                    _state = State.Detected;
                    _conditionStartedAt = null;
                    Detected?.Invoke();
                }
            }
            else
            {
                _conditionStartedAt = null;
            }
        }
        else // Detected
        {
            if (!inCall)
            {
                _conditionStartedAt ??= now;
                if (now - _conditionStartedAt.Value >= _offDebounce)
                {
                    _state = State.NotDetected;
                    _conditionStartedAt = null;
                    Cleared?.Invoke();
                }
            }
            else
            {
                _conditionStartedAt = null;
            }
        }
    }
}
