using SPRecorder.Settings;

namespace SPRecorder.Tests;

public class SessionNameSanitizerTests
{
    [Theory]
    [InlineData("Q2 Planning",        "Q2 Planning")]
    [InlineData("  Daily standup  ",  "Daily standup")]
    [InlineData("foo/bar:baz?",       "foo_bar_baz_")]
    [InlineData("Bad<>|chars",        "Bad___chars")]
    [InlineData("trailing dots...",   "trailing dots")]
    public void Sanitize_ReplacesInvalid_AndTrims(string input, string expected)
    {
        Assert.Equal(expected, SessionNamePrompt.Sanitize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Sanitize_EmptyOrWhitespace_ReturnsEmpty(string? input)
    {
        Assert.Equal("", SessionNamePrompt.Sanitize(input ?? ""));
    }
}
