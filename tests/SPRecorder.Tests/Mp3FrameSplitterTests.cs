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

    [Fact]
    public void SplitBySize_FileOverThreshold_ProducesMultipleChunks()
    {
        var path = TempPath("size-over");
        try
        {
            WriteSineMp3(path, TimeSpan.FromSeconds(60)); // ~720 KB at 96 kbps mono
            long threshold = 200 * 1024; // 200 KB

            var splitter = new Mp3FrameSplitter();
            var chunks = splitter.SplitBySize(path, threshold);

            Assert.True(chunks.Count >= 3, $"Expected >= 3 chunks, got {chunks.Count}");
            foreach (var c in chunks)
            {
                Assert.True(File.Exists(c), $"Chunk missing: {c}");
                Assert.True(new FileInfo(c).Length > 0, $"Chunk empty: {c}");
            }
        }
        finally
        {
            DeleteAll(path);
        }
    }

    [Fact]
    public void SplitBySize_ChunkFilenamesAreZeroPadded()
    {
        var path = TempPath("naming");
        try
        {
            WriteSineMp3(path, TimeSpan.FromSeconds(15));
            var chunks = new Mp3FrameSplitter().SplitBySize(path, 50 * 1024); // ~3-4 chunks

            Assert.True(chunks.Count >= 2);
            var stem = Path.GetFileNameWithoutExtension(path);
            for (int i = 0; i < chunks.Count; i++)
            {
                var expected = $"{stem}_{(i + 1):D3}.mp3";
                Assert.EndsWith(expected, chunks[i]);
            }
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
