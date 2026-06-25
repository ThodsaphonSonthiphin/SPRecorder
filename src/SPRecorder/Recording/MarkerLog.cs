using System.Globalization;
using System.Text;

namespace SPRecorder.Recording;

public readonly record struct MarkerStamp(TimeSpan Elapsed, DateTime WallClock);

/// <summary>
/// Owns the sidecar Marker log for one Recording Session. Lines are appended and
/// flushed the instant a marker is dropped (crash-safe). The Markdown title block is
/// written once at Stop via the static FinalizeMarkdownTitle.
/// </summary>
public sealed class MarkerLog : IDisposable
{
    public const string CsvHeader = "Number,Elapsed,WallClock,Note";

    private readonly bool _csv;
    private StreamWriter? _writer;
    private int _count;

    public string Path { get; }
    public int Count => _count;
    public bool IsCsv => _csv;

    public MarkerLog(string path, string format)
    {
        Path = path;
        _csv = format.Equals("Csv", StringComparison.OrdinalIgnoreCase);
    }

    public int Append(MarkerStamp stamp, string? note)
    {
        if (_writer is null)
        {
            _writer = new StreamWriter(Path, append: true, Encoding.UTF8);
            if (_csv) { _writer.WriteLine(CsvHeader); }
        }
        _count++;
        _writer.WriteLine(_csv ? CsvRow(_count, stamp, note) : MarkdownLine(_count, stamp, note));
        _writer.Flush();
        return _count;
    }

    public void Close()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
    }

    public void Dispose() => Close();

    /// <remarks>
    /// Call <see cref="Close"/> on the owning MarkerLog instance before invoking this:
    /// it reads then rewrites the file and must not run while the append writer is open.
    /// </remarks>
    public static void FinalizeMarkdownTitle(string path, string? label, DateTime startedAt, int count)
    {
        if (!File.Exists(path)) return;
        var lines = File.ReadAllLines(path);
        var sb = new StringBuilder();
        sb.AppendLine(string.IsNullOrWhiteSpace(label) ? "# Markers" : $"# Markers — {label.Trim()}");
        sb.AppendLine($"{startedAt:yyyy-MM-dd HH:mm:ss} · {count} {(count == 1 ? "marker" : "markers")}");
        sb.AppendLine();
        foreach (var l in lines) sb.AppendLine(l);
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public static string MarkdownLine(int seq, MarkerStamp stamp, string? note)
    {
        var line = $"- **#{seq} · {FormatElapsed(stamp.Elapsed)}** _({FormatWall(stamp.WallClock)})_";
        return string.IsNullOrWhiteSpace(note) ? line : $"{line} — {note.Trim()}";
    }

    public static string CsvRow(int seq, MarkerStamp stamp, string? note) =>
        $"{seq},{FormatElapsed(stamp.Elapsed)},{FormatWall(stamp.WallClock)},{CsvEscape(note ?? "")}";

    public static string CsvEscape(string field)
    {
        if (field.Length == 0) return "";
        bool quote = field.Contains(',') || field.Contains('"')
                  || field.Contains('\n') || field.Contains('\r');
        return quote ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
    }

    private static string FormatElapsed(TimeSpan t)
    {
        int hours = (int)t.TotalHours;
        return $"{hours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }

    private static string FormatWall(DateTime dt) =>
        dt.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
}
