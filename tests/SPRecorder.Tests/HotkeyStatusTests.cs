using SPRecorder.Hotkey;

namespace SPRecorder.Tests;

public class HotkeyStatusTests
{
    [Fact]
    public void AnyInactive_AllRegistered_False()
        => Assert.False(new HotkeyStatus(true, true, true).AnyInactive);

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void AnyInactive_OneFailed_True(bool a, bool b, bool c)
        => Assert.True(new HotkeyStatus(a, b, c).AnyInactive);

    [Fact]
    public void InactiveLabels_AllRegistered_Empty()
        => Assert.Empty(new HotkeyStatus(true, true, true).InactiveLabels());

    [Fact]
    public void InactiveLabels_QuickMarkOnly()
        => Assert.Equal(new[] { "Quick-mark" }, new HotkeyStatus(true, false, true).InactiveLabels());

    [Fact]
    public void InactiveLabels_AllInactive_InOrder()
        => Assert.Equal(new[] { "Start/stop", "Quick-mark", "Mark with note" },
                        new HotkeyStatus(false, false, false).InactiveLabels());
}
