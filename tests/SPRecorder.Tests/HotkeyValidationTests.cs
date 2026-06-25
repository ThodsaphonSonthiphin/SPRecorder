using SPRecorder.Hotkey;

namespace SPRecorder.Tests;

public class HotkeyValidationTests
{
    [Fact]
    public void Validate_AllDistinct_ReturnsNull()
        => Assert.Null(HotkeyValidation.Validate(
            ("Start/stop", "Ctrl+Alt+R"),
            ("Quick-mark", "Ctrl+Alt+M"),
            ("Mark with note", "Ctrl+Alt+N")));

    [Fact]
    public void Validate_TwoIdentical_ReturnsError()
    {
        var msg = HotkeyValidation.Validate(
            ("Start/stop", "Ctrl+Alt+R"),
            ("Quick-mark", "Ctrl+Alt+M"),
            ("Mark with note", "Ctrl+Alt+M"));
        Assert.NotNull(msg);
        Assert.Contains("Quick-mark", msg);
        Assert.Contains("Mark with note", msg);
    }

    [Fact]
    public void Validate_InvalidSpec_ReturnsErrorWithLabel()
    {
        var msg = HotkeyValidation.Validate(
            ("Start/stop", "Ctrl+Alt+R"),
            ("Quick-mark", "NotAKey"));
        Assert.NotNull(msg);
        Assert.Contains("Quick-mark", msg);
    }
}
