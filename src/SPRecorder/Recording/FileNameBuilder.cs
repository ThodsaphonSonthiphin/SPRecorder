using System.Globalization;
using System.Text.RegularExpressions;

namespace SPRecorder.Recording;

public static partial class FileNameBuilder
{
    [GeneratedRegex(@"\{timestamp(?::([^}]+))?\}")]
    private static partial Regex TimestampToken();

    public static string Build(string pattern, DateTime timestamp, string track)
    {
        string result = TimestampToken().Replace(pattern, m =>
        {
            string fmt = m.Groups[1].Success ? m.Groups[1].Value : "yyyy-MM-dd_HH-mm-ss";
            return timestamp.ToString(fmt, CultureInfo.InvariantCulture);
        });
        result = result.Replace("{track}", track);
        return Sanitize(result);
    }

    public static string BuildScreen(string pattern, DateTime timestamp)
    {
        var baseName = Build(pattern, timestamp, "screen");
        return Path.ChangeExtension(baseName, ".mp4");
    }

    private static string Sanitize(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var buf = new char[fileName.Length];
        for (int i = 0; i < fileName.Length; i++)
            buf[i] = Array.IndexOf(invalid, fileName[i]) >= 0 ? '_' : fileName[i];
        return new string(buf);
    }
}
