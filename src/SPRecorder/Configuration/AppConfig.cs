using Microsoft.Extensions.Configuration;

namespace SPRecorder.Configuration;

public sealed record AppConfig
{
    public string OutputDirectory { get; init; } = "%USERPROFILE%\\Documents\\SPRecorder";
    public string FileNamePattern { get; init; } = "{timestamp:yyyy-MM-dd_HH-mm-ss}_{track}.mp3";
    public string Hotkey { get; init; } = "Ctrl+Alt+R";
    public int Mp3BitrateKbps { get; init; } = 96;

    public static AppConfig Load(IConfiguration cfg)
    {
        var raw = cfg.Get<AppConfig>() ?? new AppConfig();
        return raw with { OutputDirectory = Environment.ExpandEnvironmentVariables(raw.OutputDirectory) };
    }
}
