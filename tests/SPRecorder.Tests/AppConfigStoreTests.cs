using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SPRecorder.Configuration;
using SPRecorder.Settings;

namespace SPRecorder.Tests;

public class AppConfigStoreTests
{
    [Fact]
    public void Save_WritesJson_ThatReloadsToSameValues()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sprec_cfg_{Guid.NewGuid():N}.json");
        try
        {
            var store = new AppConfigStore(path, new AppConfig());
            var updated = new AppConfig
            {
                OutputDirectory = "C:\\Recordings",
                Hotkey = "Ctrl+Shift+M",
                Mp3BitrateKbps = 128,
                MicrophoneDeviceId = "{0.0.1.00000000}.{some-guid}",
                MixedFileFormat = "Stereo",
                MixedFileSampleRate = 48000,
                PromptForSessionName = true,
                AutoDetectCallsEnabled = true,
            };

            store.Save(updated);

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            Assert.Equal("Ctrl+Shift+M", root.GetProperty("Hotkey").GetString());
            Assert.Equal("Stereo", root.GetProperty("MixedFileFormat").GetString());
            Assert.True(root.GetProperty("PromptForSessionName").GetBoolean());
            Assert.True(root.GetProperty("AutoDetectCallsEnabled").GetBoolean());
            Assert.Equal(128, store.Current.Mp3BitrateKbps);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Save_RaisesConfigChanged_WithOldAndNew()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sprec_cfg_{Guid.NewGuid():N}.json");
        try
        {
            var initial = new AppConfig { Hotkey = "Ctrl+Alt+R" };
            var store = new AppConfigStore(path, initial);

            AppConfig? raisedOld = null;
            AppConfig? raisedNew = null;
            store.ConfigChanged += (o, n) => { raisedOld = o; raisedNew = n; };

            var updated = initial with { Hotkey = "Ctrl+Alt+T" };
            store.Save(updated);

            Assert.NotNull(raisedOld);
            Assert.NotNull(raisedNew);
            Assert.Equal("Ctrl+Alt+R", raisedOld!.Hotkey);
            Assert.Equal("Ctrl+Alt+T", raisedNew!.Hotkey);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Save_OverwritesExistingFile_AtomicallyViaReplace()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sprec_cfg_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ \"OutputDirectory\": \"C:\\\\old\" }");
            var store = new AppConfigStore(path, new AppConfig());

            store.Save(new AppConfig { OutputDirectory = "C:\\new" });

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal("C:\\new", doc.RootElement.GetProperty("OutputDirectory").GetString());
            Assert.False(File.Exists(path + ".tmp"), "Temp file should be cleaned up after replace.");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        }
    }

    [Fact]
    public void Save_RoundtripsSplitFields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sprec_cfg_{Guid.NewGuid():N}.json");
        try
        {
            var store = new AppConfigStore(path, new AppConfig());
            var updated = new AppConfig
            {
                SplitMode = "Size",
                SplitTimeMinutes = 45,
                SplitSizeMb = 180,
                SplitSystemTrack = false,
                SplitMicTrack = true,
                SplitMixedTrack = true,
            };

            store.Save(updated);

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            Assert.Equal("Size", root.GetProperty("SplitMode").GetString());
            Assert.Equal(45,     root.GetProperty("SplitTimeMinutes").GetInt32());
            Assert.Equal(180,    root.GetProperty("SplitSizeMb").GetInt32());
            Assert.False(        root.GetProperty("SplitSystemTrack").GetBoolean());
            Assert.True(         root.GetProperty("SplitMicTrack").GetBoolean());
            Assert.True(         root.GetProperty("SplitMixedTrack").GetBoolean());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Load_ClampsAndFallsBackInvalidSplitFields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sprec_cfg_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
            {
              "SplitMode": "Garbage",
              "SplitTimeMinutes": 99999,
              "SplitSizeMb": 0
            }
            """);

            var builder = new ConfigurationBuilder().AddJsonFile(path);
            var loaded = AppConfig.Load(builder.Build());

            Assert.Equal("None", loaded.SplitMode);
            Assert.Equal(1440,   loaded.SplitTimeMinutes); // upper bound
            Assert.Equal(1,      loaded.SplitSizeMb);      // lower bound
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Save_RoundtripsScreenFields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sprec_cfg_{Guid.NewGuid():N}.json");
        try
        {
            var store = new AppConfigStore(path, new AppConfig());
            var updated = new AppConfig
            {
                ScreenRecordingEnabled = true,
                ScreenMonitorDeviceName = "\\\\.\\DISPLAY2",
                ScreenFrameRate = 25,
                ScreenQuality = "High",
                ShowMouseClicks = false,
                ShowKeystrokes = false,
            };

            store.Save(updated);

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            Assert.True(root.GetProperty("ScreenRecordingEnabled").GetBoolean());
            Assert.Equal("\\\\.\\DISPLAY2", root.GetProperty("ScreenMonitorDeviceName").GetString());
            Assert.Equal(25, root.GetProperty("ScreenFrameRate").GetInt32());
            Assert.Equal("High", root.GetProperty("ScreenQuality").GetString());
            Assert.False(root.GetProperty("ShowMouseClicks").GetBoolean());
            Assert.False(root.GetProperty("ShowKeystrokes").GetBoolean());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Load_ClampsScreenFrameRate_AndNormalizesQuality()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sprec_cfg_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
            {
              "ScreenFrameRate": 99,
              "ScreenQuality": "ultra"
            }
            """);

            var builder = new ConfigurationBuilder().AddJsonFile(path);
            var loaded = AppConfig.Load(builder.Build());

            Assert.Equal(30, loaded.ScreenFrameRate);     // snapped to nearest allowed {15,25,30}
            Assert.Equal("Medium", loaded.ScreenQuality); // unknown → Medium
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
