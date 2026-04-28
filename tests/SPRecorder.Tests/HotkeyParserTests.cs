using System.Windows.Forms;
using SPRecorder.Hotkey;

namespace SPRecorder.Tests;

public class HotkeyParserTests
{
    [Theory]
    [InlineData("Ctrl+Alt+R",   HotkeyModifiers.Ctrl | HotkeyModifiers.Alt,   Keys.R)]
    [InlineData("ctrl + alt + r", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Keys.R)]
    [InlineData("Win+Shift+F12", HotkeyModifiers.Win | HotkeyModifiers.Shift, Keys.F12)]
    [InlineData("Control+A",     HotkeyModifiers.Ctrl,                        Keys.A)]
    [InlineData("Alt+Space",     HotkeyModifiers.Alt,                         Keys.Space)]
    public void ParsesValidCombos(string spec, HotkeyModifiers mods, Keys key)
    {
        var parsed = HotkeyParser.Parse(spec);
        Assert.Equal(mods, parsed.Modifiers);
        Assert.Equal(key, parsed.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+Alt")]            // no non-modifier key
    [InlineData("Ctrl+R+T")]            // two non-modifier keys
    [InlineData("Ctrl+Banana")]         // unknown key
    public void RejectsInvalid(string spec) =>
        Assert.ThrowsAny<Exception>(() => HotkeyParser.Parse(spec));
}
