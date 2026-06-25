namespace SPRecorder.Hotkey;

/// <summary>Live registration status of the three global hotkeys (true = registered/active).</summary>
public sealed record HotkeyStatus(bool StartStop, bool QuickMark, bool MarkWithNote)
{
    public bool AnyInactive => !StartStop || !QuickMark || !MarkWithNote;

    /// <summary>Human-readable names of the inactive hotkeys, in display order.</summary>
    public IReadOnlyList<string> InactiveLabels()
    {
        var list = new List<string>(3);
        if (!StartStop)    list.Add("Start/stop");
        if (!QuickMark)    list.Add("Quick-mark");
        if (!MarkWithNote) list.Add("Mark with note");
        return list;
    }
}
