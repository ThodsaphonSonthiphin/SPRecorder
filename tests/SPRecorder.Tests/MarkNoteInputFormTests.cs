using SPRecorder.Tray;

namespace SPRecorder.Tests;

public class MarkNoteInputFormTests
{
    [Fact]
    public void Picker_PrefersANonRecordedMonitor()
        => Assert.Equal("B", MarkNoteInputForm.PickNoteMonitorDeviceName(new[] { "A", "B" }, "A"));

    [Fact]
    public void Picker_PrefersANonRecordedMonitor_OtherOrder()
        => Assert.Equal("A", MarkNoteInputForm.PickNoteMonitorDeviceName(new[] { "A", "B" }, "B"));

    [Fact]
    public void Picker_SingleMonitor_FallsBackToIt()
        => Assert.Equal("A", MarkNoteInputForm.PickNoteMonitorDeviceName(new[] { "A" }, "A"));

    [Fact]
    public void Picker_EmptyList_ReturnsEmpty()
        => Assert.Equal("", MarkNoteInputForm.PickNoteMonitorDeviceName(Array.Empty<string>(), "A"));
}
