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

    public string SplitMode        { get; init; } = "None";   // "None" | "Time" | "Size"
    public int    SplitTimeMinutes { get; init; } = 30;
    public int    SplitSizeMb      { get; init; } = 195;
    public bool   SplitSystemTrack { get; init; } = true;
    public bool   SplitMicTrack    { get; init; } = true;
    public bool   SplitMixedTrack  { get; init; } = true;

    public bool   ScreenRecordingEnabled  { get; init; } = false;
    public string ScreenMonitorDeviceName { get; init; } = "";        // "" = primary; else \\.\DISPLAYn
    public int    ScreenFrameRate         { get; init; } = 30;        // 15 | 25 | 30
    public string ScreenQuality           { get; init; } = "Medium";  // Low | Medium | High
    public bool   ShowMouseClicks         { get; init; } = true;
    public bool   ShowKeystrokes          { get; init; } = true;

    public static AppConfig Load(IConfiguration cfg)
    {
        var raw = cfg.Get<AppConfig>() ?? new AppConfig();
        return raw with
        {
            OutputDirectory = Environment.ExpandEnvironmentVariables(raw.OutputDirectory),
            SplitMode = raw.SplitMode is "Time" or "Size" ? raw.SplitMode : "None",
            SplitTimeMinutes = Math.Clamp(raw.SplitTimeMinutes, 1, 1440),
            SplitSizeMb      = Math.Clamp(raw.SplitSizeMb,      1, 10000),
            ScreenFrameRate = NearestFrameRate(raw.ScreenFrameRate),
            ScreenQuality   = raw.ScreenQuality is "Low" or "Medium" or "High" ? raw.ScreenQuality : "Medium",
        };
    }

    private static int NearestFrameRate(int fps)
    {
        int[] allowed = { 15, 25, 30 };
        int best = allowed[0];
        foreach (var a in allowed)
            if (Math.Abs(a - fps) < Math.Abs(best - fps)) best = a;
        return best;
    }
}
