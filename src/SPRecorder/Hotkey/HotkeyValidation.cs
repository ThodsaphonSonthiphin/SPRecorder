namespace SPRecorder.Hotkey;

public static class HotkeyValidation
{
    /// <summary>
    /// Returns null if every spec parses and all are mutually distinct;
    /// otherwise a human-readable error naming the offending hotkey(s).
    /// </summary>
    public static string? Validate(params (string Label, string Spec)[] hotkeys)
    {
        var parsed = new List<(string Label, ParsedHotkey Key)>();
        foreach (var (label, spec) in hotkeys)
        {
            try { parsed.Add((label, HotkeyParser.Parse(spec))); }
            catch (Exception ex) { return $"{label} hotkey: {ex.Message}"; }
        }

        for (int i = 0; i < parsed.Count; i++)
            for (int j = i + 1; j < parsed.Count; j++)
                if (parsed[i].Key == parsed[j].Key)
                    return $"{parsed[i].Label} and {parsed[j].Label} use the same hotkey — they must be different.";

        return null;
    }
}
