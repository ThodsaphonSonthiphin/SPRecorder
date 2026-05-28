using NAudio.Lame;
using NAudio.Wave;
using SPRecorder.Audio;

namespace SPRecorder.Tests;

public class Mp3FrameSplitterTests
{
    [Fact]
    public void SplitBySize_FileUnderThreshold_ReturnsInputUnchanged()
    {
        var path = TempPath("size-under");
        try
        {
            WriteSineMp3(path, TimeSpan.FromSeconds(2));
            var originalBytes = File.ReadAllBytes(path);

            var splitter = new Mp3FrameSplitter();
            var result = splitter.SplitBySize(path, 1_000_000); // 1 MB, way over file size

            Assert.Single(result);
            Assert.Equal(path, result[0]);
            Assert.Equal(originalBytes, File.ReadAllBytes(path)); // untouched
        }
        finally
        {
            DeleteAll(path);
        }
    }

    // --- helpers ---

    private static string TempPath(string label) =>
        Path.Combine(Path.GetTempPath(), $"sprec_split_{label}_{Guid.NewGuid():N}.mp3");

    private static void WriteSineMp3(string path, TimeSpan duration, int sampleRate = 44100, int bitrateKbps = 96)
    {
        var fmt = new WaveFormat(sampleRate, 16, 1);
        using var writer = new LameMP3FileWriter(path, fmt, bitrateKbps);
        int totalSamples = (int)(sampleRate * duration.TotalSeconds);
        var buf = new byte[totalSamples * 2];
        for (int i = 0; i < totalSamples; i++)
        {
            short v = (short)(MathF.Sin(2 * MathF.PI * 440 * i / sampleRate) * 16000);
            buf[i * 2]     = (byte)(v & 0xFF);
            buf[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        writer.Write(buf, 0, buf.Length);
    }

    private static void DeleteAll(params string[] paths)
    {
        foreach (var p in paths)
            if (File.Exists(p)) File.Delete(p);
        // also clean any numbered chunks next to each input
        foreach (var p in paths)
        {
            var dir = Path.GetDirectoryName(p) ?? "";
            var stem = Path.GetFileNameWithoutExtension(p);
            foreach (var f in Directory.GetFiles(dir, $"{stem}_*.mp3"))
                File.Delete(f);
        }
    }
}
