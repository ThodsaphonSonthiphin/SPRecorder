using SPRecorder.Recording;

namespace SPRecorder.Tests;

public class MarkerLogTests
{
    private static MarkerStamp Stamp(int h, int m, int s, int wh, int wm, int ws) =>
        new(new TimeSpan(h, m, s), new DateTime(2026, 6, 25, wh, wm, ws));

    [Fact]
    public void MarkdownLine_WithNote()
    {
        var line = MarkerLog.MarkdownLine(1, Stamp(0, 12, 34, 14, 32, 39), "delay launch");
        Assert.Equal("- **#1 · 00:12:34** _(14:32:39)_ — delay launch", line);
    }

    [Fact]
    public void MarkdownLine_NoNote_OmitsDash()
    {
        var line = MarkerLog.MarkdownLine(2, Stamp(0, 25, 10, 14, 45, 15), null);
        Assert.Equal("- **#2 · 00:25:10** _(14:45:15)_", line);
    }

    [Fact]
    public void MarkdownLine_WhitespaceNote_TreatedAsNoNote()
    {
        var line = MarkerLog.MarkdownLine(3, Stamp(0, 0, 0, 9, 0, 0), "   ");
        Assert.Equal("- **#3 · 00:00:00** _(09:00:00)_", line);
    }

    [Fact]
    public void MarkdownLine_MultiHourElapsed()
    {
        var line = MarkerLog.MarkdownLine(1, Stamp(2, 5, 10, 16, 0, 0), null);
        Assert.Equal("- **#1 · 02:05:10** _(16:00:00)_", line);
    }

    [Fact]
    public void CsvRow_PlainNote()
    {
        var row = MarkerLog.CsvRow(1, Stamp(0, 12, 34, 14, 32, 39), "budget cut");
        Assert.Equal("1,00:12:34,14:32:39,budget cut", row);
    }

    [Fact]
    public void CsvRow_EscapesCommaAndQuotes()
    {
        var row = MarkerLog.CsvRow(2, Stamp(0, 41, 52, 15, 2, 0), "cut, \"again\"");
        Assert.Equal("2,00:41:52,15:02:00,\"cut, \"\"again\"\"\"", row);
    }

    [Fact]
    public void CsvRow_NullNote_TrailingEmptyField()
    {
        var row = MarkerLog.CsvRow(1, Stamp(0, 1, 0, 14, 32, 39), null);
        Assert.Equal("1,00:01:00,14:32:39,", row);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("a\nb", "\"a\nb\"")]
    public void CsvEscape_Cases(string input, string expected)
        => Assert.Equal(expected, MarkerLog.CsvEscape(input));

    [Fact]
    public void Append_Markdown_WritesLinesAndCounts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sprec_mk_{Guid.NewGuid():N}.md");
        try
        {
            using var log = new MarkerLog(path, "Markdown");
            Assert.Equal(1, log.Append(Stamp(0, 1, 0, 10, 1, 0), "first"));
            Assert.Equal(2, log.Append(Stamp(0, 2, 0, 10, 2, 0), null));
            log.Close();

            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            Assert.Equal("- **#1 · 00:01:00** _(10:01:00)_ — first", lines[0]);
            Assert.Equal("- **#2 · 00:02:00** _(10:02:00)_", lines[1]);
            Assert.Equal(2, log.Count);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Append_Csv_WritesHeaderThenRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sprec_mk_{Guid.NewGuid():N}.csv");
        try
        {
            using var log = new MarkerLog(path, "Csv");
            log.Append(Stamp(0, 1, 0, 10, 1, 0), "x");
            log.Close();

            var lines = File.ReadAllLines(path);
            Assert.Equal("Number,Elapsed,WallClock,Note", lines[0]);
            Assert.Equal("1,00:01:00,10:01:00,x", lines[1]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void FinalizeMarkdownTitle_PrependsTitleAndCount()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sprec_mk_{Guid.NewGuid():N}.md");
        try
        {
            File.WriteAllText(path, "- **#1 · 00:01:00** _(10:01:00)_\n");
            MarkerLog.FinalizeMarkdownTitle(path, "Q2 Planning",
                new DateTime(2026, 6, 25, 14, 20, 5), 1);

            var text = File.ReadAllText(path);
            Assert.StartsWith("# Markers — Q2 Planning", text);
            Assert.Contains("2026-06-25 14:20:05 · 1 marker", text);
            Assert.Contains("- **#1 · 00:01:00** _(10:01:00)_", text);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void FinalizeMarkdownTitle_NoLabel_OmitsDash()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sprec_mk_{Guid.NewGuid():N}.md");
        try
        {
            File.WriteAllText(path, "- **#1 · 00:01:00** _(10:01:00)_\n");
            MarkerLog.FinalizeMarkdownTitle(path, null,
                new DateTime(2026, 6, 25, 14, 20, 5), 2);
            var text = File.ReadAllText(path);
            Assert.StartsWith("# Markers\n", text.Replace("\r\n", "\n"));
            Assert.Contains("· 2 markers", text);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Append_AccumulatesEntries()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sprec_mk_{Guid.NewGuid():N}.md");
        try
        {
            using var log = new MarkerLog(path, "Markdown");
            log.Append(Stamp(0, 12, 34, 14, 32, 39), "decision");
            log.Append(Stamp(0, 25, 10, 14, 45, 15), null);

            Assert.Equal(2, log.Entries.Count);
            Assert.Equal(1, log.Entries[0].Seq);
            Assert.Equal(new TimeSpan(0, 12, 34), log.Entries[0].Elapsed);
            Assert.Equal("decision", log.Entries[0].Note);
            Assert.Equal(2, log.Entries[1].Seq);
            Assert.Null(log.Entries[1].Note);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
