using System.Windows.Forms;

namespace SPRecorder.Hotkey;

[Flags]
public enum HotkeyModifiers : uint
{
    None  = 0x0000,
    Alt   = 0x0001,
    Ctrl  = 0x0002,
    Shift = 0x0004,
    Win   = 0x0008,
}

public readonly record struct ParsedHotkey(HotkeyModifiers Modifiers, Keys Key);

public static class HotkeyParser
{
    public static ParsedHotkey Parse(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            throw new ArgumentException("Hotkey spec is empty.", nameof(spec));

        var mods = HotkeyModifiers.None;
        var key  = Keys.None;

        foreach (var rawToken in spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = rawToken.ToUpperInvariant();
            switch (token)
            {
                case "CTRL":
                case "CONTROL": mods |= HotkeyModifiers.Ctrl;  break;
                case "ALT":     mods |= HotkeyModifiers.Alt;   break;
                case "SHIFT":   mods |= HotkeyModifiers.Shift; break;
                case "WIN":
                case "WINDOWS": mods |= HotkeyModifiers.Win;   break;
                default:
                    if (key != Keys.None)
                        throw new FormatException($"Hotkey '{spec}' has more than one non-modifier key.");
                    if (!Enum.TryParse<Keys>(token, ignoreCase: true, out var parsed))
                        throw new FormatException($"Unknown key '{rawToken}' in hotkey '{spec}'.");
                    key = parsed;
                    break;
            }
        }

        if (key == Keys.None)
            throw new FormatException($"Hotkey '{spec}' has no non-modifier key.");

        return new ParsedHotkey(mods, key);
    }
}
