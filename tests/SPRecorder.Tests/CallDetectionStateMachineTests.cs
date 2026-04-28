using SPRecorder.Audio;

namespace SPRecorder.Tests;

public class CallDetectionStateMachineTests
{
    private static readonly TimeSpan OnDeb  = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OffDeb = TimeSpan.FromSeconds(15);
    private static readonly DateTime T0 = new(2026, 4, 28, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void StartsInNotDetected()
    {
        var sm = new CallDetectionStateMachine(OnDeb, OffDeb);
        Assert.Equal(CallDetectionStateMachine.State.NotDetected, sm.Current);
    }

    [Fact]
    public void Transitions_NotDetected_To_Detected_AfterOnDebounce()
    {
        var sm = new CallDetectionStateMachine(OnDeb, OffDeb);
        int detectedCount = 0;
        sm.Detected += () => detectedCount++;

        sm.Update(true, T0);                              // start of "in call"
        sm.Update(true, T0.AddSeconds(2));                // 2s in
        sm.Update(true, T0.AddSeconds(4));                // 4s in (still under 5)
        Assert.Equal(CallDetectionStateMachine.State.NotDetected, sm.Current);
        Assert.Equal(0, detectedCount);

        sm.Update(true, T0.AddSeconds(5));                // exactly 5s
        Assert.Equal(CallDetectionStateMachine.State.Detected, sm.Current);
        Assert.Equal(1, detectedCount);
    }

    [Fact]
    public void Flicker_Resets_OnDebounce()
    {
        var sm = new CallDetectionStateMachine(OnDeb, OffDeb);
        int detectedCount = 0;
        sm.Detected += () => detectedCount++;

        sm.Update(true,  T0);
        sm.Update(true,  T0.AddSeconds(3));
        sm.Update(false, T0.AddSeconds(4));               // dropped — reset
        sm.Update(true,  T0.AddSeconds(5));               // restart
        sm.Update(true,  T0.AddSeconds(9));               // 4s in
        Assert.Equal(CallDetectionStateMachine.State.NotDetected, sm.Current);
        Assert.Equal(0, detectedCount);

        sm.Update(true,  T0.AddSeconds(10));              // 5s in (from restart)
        Assert.Equal(CallDetectionStateMachine.State.Detected, sm.Current);
        Assert.Equal(1, detectedCount);
    }

    [Fact]
    public void Transitions_Detected_To_NotDetected_AfterOffDebounce()
    {
        var sm = new CallDetectionStateMachine(OnDeb, OffDeb);
        int clearedCount = 0;
        sm.Cleared += () => clearedCount++;

        // Move into Detected
        sm.Update(true, T0);
        sm.Update(true, T0.AddSeconds(5));
        Assert.Equal(CallDetectionStateMachine.State.Detected, sm.Current);

        // Now no call, but only for 14s — should still be Detected
        sm.Update(false, T0.AddSeconds(6));
        sm.Update(false, T0.AddSeconds(20));              // 14s of off
        Assert.Equal(CallDetectionStateMachine.State.Detected, sm.Current);
        Assert.Equal(0, clearedCount);

        sm.Update(false, T0.AddSeconds(21));              // 15s of off
        Assert.Equal(CallDetectionStateMachine.State.NotDetected, sm.Current);
        Assert.Equal(1, clearedCount);
    }

    [Fact]
    public void OffFlicker_Resets_OffDebounce()
    {
        var sm = new CallDetectionStateMachine(OnDeb, OffDeb);
        sm.Update(true, T0);
        sm.Update(true, T0.AddSeconds(5));
        Assert.Equal(CallDetectionStateMachine.State.Detected, sm.Current);

        sm.Update(false, T0.AddSeconds(6));               // off starts
        sm.Update(false, T0.AddSeconds(15));              // 9s of off
        sm.Update(true,  T0.AddSeconds(16));              // back on — reset
        sm.Update(false, T0.AddSeconds(17));              // off restarts
        sm.Update(false, T0.AddSeconds(31));              // 14s of off (from restart)
        Assert.Equal(CallDetectionStateMachine.State.Detected, sm.Current);

        sm.Update(false, T0.AddSeconds(32));              // 15s of off
        Assert.Equal(CallDetectionStateMachine.State.NotDetected, sm.Current);
    }

    [Fact]
    public void RepeatedSameStateUpdates_DoNotFireExtraEvents()
    {
        var sm = new CallDetectionStateMachine(OnDeb, OffDeb);
        int detected = 0, cleared = 0;
        sm.Detected += () => detected++;
        sm.Cleared  += () => cleared++;

        sm.Update(true, T0);
        sm.Update(true, T0.AddSeconds(5));
        sm.Update(true, T0.AddSeconds(10));
        sm.Update(true, T0.AddSeconds(20));
        Assert.Equal(1, detected);
        Assert.Equal(0, cleared);
    }
}
