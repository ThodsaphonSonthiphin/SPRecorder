using SPRecorder.Recording;

namespace SPRecorder.Tests;

public class FileNameBuilderTests
{
    private static readonly DateTime T = new(2026, 4, 27, 14, 30, 22);

    [Fact]
    public void DefaultPattern_ReplacesTokens()
    {
        var name = FileNameBuilder.Build("{timestamp:yyyy-MM-dd_HH-mm-ss}_{track}.mp3", T, "system");
        Assert.Equal("2026-04-27_14-30-22_system.mp3", name);
    }

    [Fact]
    public void TimestampWithoutFormat_UsesDefault()
    {
        var name = FileNameBuilder.Build("{timestamp}_{track}.mp3", T, "mic");
        Assert.Equal("2026-04-27_14-30-22_mic.mp3", name);
    }

    [Fact]
    public void Sanitizes_InvalidChars()
    {
        var name = FileNameBuilder.Build("a:b/c?{track}.mp3", T, "x");
        Assert.DoesNotContain(":", name);
        Assert.DoesNotContain("/", name);
        Assert.DoesNotContain("?", name);
    }

    [Fact]
    public void TrackToken_AppearsLiterally()
    {
        var name = FileNameBuilder.Build("rec_{track}.mp3", T, "system");
        Assert.Equal("rec_system.mp3", name);
    }

    [Fact]
    public void BuildScreen_ForcesMp4Extension()
    {
        var ts = new DateTime(2026, 6, 18, 14, 3, 22);
        var name = FileNameBuilder.BuildScreen("{timestamp:yyyy-MM-dd_HH-mm-ss}_{track}.mp3", ts);
        Assert.Equal("2026-06-18_14-03-22_screen.mp4", name);
    }

    [Fact]
    public void BuildScreen_AddsMp4_WhenPatternHasNoExtension()
    {
        var ts = new DateTime(2026, 6, 18, 14, 3, 22);
        var name = FileNameBuilder.BuildScreen("{timestamp:yyyy-MM-dd}_{track}", ts);
        Assert.Equal("2026-06-18_screen.mp4", name);
    }

    [Fact]
    public void BuildMarker_Markdown_ForcesMdExtension()
    {
        var name = FileNameBuilder.BuildMarker("{timestamp:yyyy-MM-dd_HH-mm-ss}_{track}.mp3", T, "Markdown");
        Assert.Equal("2026-04-27_14-30-22_markers.md", name);
    }

    [Fact]
    public void BuildMarker_Csv_ForcesCsvExtension()
    {
        var name = FileNameBuilder.BuildMarker("{timestamp:yyyy-MM-dd_HH-mm-ss}_{track}.mp3", T, "Csv");
        Assert.Equal("2026-04-27_14-30-22_markers.csv", name);
    }

    [Fact]
    public void BuildMarker_UnknownFormat_DefaultsToMd()
    {
        var name = FileNameBuilder.BuildMarker("{timestamp:yyyy-MM-dd_HH-mm-ss}_{track}.mp3", T, "weird");
        Assert.Equal("2026-04-27_14-30-22_markers.md", name);
    }
}
