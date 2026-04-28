using System.Text.Json;
using SPRecorder.Configuration;

namespace SPRecorder.Settings;

public sealed class AppConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    private readonly string _path;
    private AppConfig _current;

    public AppConfig Current => _current;
    public event Action<AppConfig, AppConfig>? ConfigChanged;

    public AppConfigStore(string path, AppConfig initial)
    {
        _path = path;
        _current = initial;
    }

    public void Save(AppConfig newConfig)
    {
        var oldConfig = _current;
        var temp = _path + ".tmp";

        var json = JsonSerializer.Serialize(newConfig, JsonOptions);
        File.WriteAllText(temp, json);

        if (File.Exists(_path))
            File.Replace(temp, _path, destinationBackupFileName: null);
        else
            File.Move(temp, _path);

        _current = newConfig with { OutputDirectory = Environment.ExpandEnvironmentVariables(newConfig.OutputDirectory) };
        ConfigChanged?.Invoke(oldConfig, _current);
    }
}
