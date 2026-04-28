using Microsoft.Extensions.Configuration;

namespace SPRecorder.Configuration;

public sealed record AppConfig
{
    public string OutputDirectory { get; init; } = "%USERPROFILE%\\Documents\\SPRecorder";
    public string FileNamePattern { get; init; } = "{timestamp:yyyy-MM-dd_HH-mm-ss}_{track}.mp3";
    public string Hotkey { get; init; } = "Ctrl+Alt+R";
    public int Mp3BitrateKbps { get; init; } = 96;

    public string MicrophoneDeviceId  { get; init; } = "";
    public string SystemAudioDeviceId { get; init; } = "";

    public bool   MixedFileEnabled    { get; init; } = true;
    public string MixedFileFormat     { get; init; } = "Mono";
    public int    MixedFileSampleRate { get; init; } = 44100;

    public bool PromptForSessionName { get; init; } = false;

    public bool AutoDetectCallsEnabled { get; init; } = false;

    public static AppConfig Load(IConfiguration cfg)
    {
        var raw = cfg.Get<AppConfig>() ?? new AppConfig();
        return raw with { OutputDirectory = Environment.ExpandEnvironmentVariables(raw.OutputDirectory) };
    }
}
