using System.Text.Json;
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
}
