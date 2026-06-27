using SPRecorder.Recording;

namespace SPRecorder.Tests;

public class MarkerReviewPageTests
{
    private static readonly DateTime Start = new(2026, 6, 25, 14, 20, 5);

    private static MarkerEntry E(int seq, int mm, int ss, string? note) =>
        new(seq, new TimeSpan(0, mm, ss), new DateTime(2026, 6, 25, 14, 32, 39), note);

    private static readonly ReviewMedia Video = new("video", "sess_screen.mp4");
    private static readonly ReviewMedia Audio = new("audio", "sess_mixed.mp3");

    [Fact]
    public void HtmlEscape_EscapesEntities()
        => Assert.Equal("&amp;&lt;&gt;&quot;", MarkerReviewPage.HtmlEscape("&<>\""));

    [Fact]
    public void Render_IncludesTitleAndCount()
    {
        var html = MarkerReviewPage.Render("Q2 Planning", Start,
            new[] { E(1, 12, 34, "x") }, new[] { Video });
        Assert.Contains("Q2 Planning", html);
        Assert.Contains("1 marker", html);
    }

    [Fact]
    public void Render_EmbedsMediaFilename()
    {
        var html = MarkerReviewPage.Render("t", Start,
            new[] { E(1, 0, 5, null) }, new[] { Video });
        Assert.Contains("sess_screen.mp4", html);
    }

    [Fact]
    public void Render_EmbedsElapsedInSeconds()
    {
        // 00:12:34 == 754 seconds in the JS marker array
        var html = MarkerReviewPage.Render("t", Start,
            new[] { E(1, 12, 34, "note") }, new[] { Audio });
        Assert.Contains("754", html);
    }

    [Fact]
    public void Render_DefaultsToVideoWhenPresent()
    {
        var html = MarkerReviewPage.Render("t", Start,
            new[] { E(1, 0, 1, null) }, new[] { Audio, Video });
        Assert.Contains("let activeKind = \"video\"", html);
    }

    [Fact]
    public void Render_AudioOnly_DefaultsToAudio_NoVideoTag()
    {
        var html = MarkerReviewPage.Render("t", Start,
            new[] { E(1, 0, 1, null) }, new[] { Audio });
        Assert.Contains("let activeKind = \"audio\"", html);
        Assert.DoesNotContain("<video", html);
    }

    [Fact]
    public void Render_NoteIsNotInjectedRaw()
    {
        var html = MarkerReviewPage.Render("t", Start,
            new[] { E(1, 0, 1, "</script><XSS>") }, new[] { Audio });
        Assert.DoesNotContain("<XSS>", html);   // JSON-encoded, never raw markup
    }
}
